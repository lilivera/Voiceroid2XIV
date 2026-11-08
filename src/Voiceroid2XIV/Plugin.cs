using Dalamud.Game.Chat;
using System.Diagnostics;
using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Voiceroid2XIV.Core;
using Voiceroid2XIV.Runtime;

namespace Voiceroid2XIV;

public sealed class Plugin : IDalamudPlugin
{
    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static ICommandManager Commands { get; private set; } = null!;
    [PluginService] internal static IChatGui Chat { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;

    private readonly WindowSystem windows = new("Voiceroid2XIV");
    private readonly SettingsWindow settings;
    private readonly CancellationTokenSource shutdown = new();
    private readonly Task monitor;
    private Task operation = Task.CompletedTask;
    private TalkHttpServer? http;
    private volatile string notice = string.Empty;
    private bool disposed;

    internal Configuration Configuration { get; }
    internal BridgeClient Bridge { get; }
    internal bool Busy => !operation.IsCompleted;
    internal bool HttpRunning => http?.IsRunning == true;
    internal int HttpPort => http?.Port ?? Configuration.HttpPort;
    internal string Notice => notice;

    public Plugin()
    {
        Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        Configuration.Channels ??= [];
        Configuration.EditorPath ??= string.Empty;
        Configuration.HttpPort = Math.Clamp(Configuration.HttpPort, 1024, 65535);
        Bridge = new BridgeClient(Path.Combine(PluginInterface.AssemblyLocation.Directory!.FullName,
            "bridge", "Voiceroid2XIV.Bridge.exe"));
        settings = new SettingsWindow(this);
        windows.AddWindow(settings);
        Commands.AddHandler("/v2xiv", new CommandInfo(OnCommand)
        {
            HelpMessage = "設定画面を開きます。say 文章 / connect / disconnect / clear / stop / pause / resume",
        });
        PluginInterface.UiBuilder.Draw += windows.Draw;
        PluginInterface.UiBuilder.OpenConfigUi += OpenSettings;
        PluginInterface.UiBuilder.OpenMainUi += OpenSettings;
        Chat.ChatMessageUnhandled += OnChat;
        monitor = Task.Run(MonitorAsync);
        if (Configuration.AutoConnect) Connect();
    }

    internal void Save() => PluginInterface.SavePluginConfig(Configuration);
    private void OpenSettings() => settings.IsOpen = true;

    internal void Connect()
    {
        var options = new BridgeOptions { EditorPath = Configuration.EditorPath, Interrupt = Configuration.Interrupt };
        var enableHttp = Configuration.EnableHttp;
        var port = Configuration.HttpPort;
        var runAsAdministrator = Configuration.RunBridgeAsAdministrator;
        RunOperation(async () =>
        {
            await StopHttpAsync().ConfigureAwait(false);
            await Bridge.StopAsync().ConfigureAwait(false);
            shutdown.Token.ThrowIfCancellationRequested();
            var proxies = Process.GetProcessesByName("SimpleVoiceroid2Proxy");
            var proxyRunning = proxies.Length > 0;
            foreach (var proxy in proxies) proxy.Dispose();
            if (proxyRunning)
                throw new InvalidOperationException("既存のSimpleVoiceroid2Proxyを終了してから接続してください。");
            await Bridge.StartAsync(options, runAsAdministrator).ConfigureAwait(false);
            if (enableHttp)
            {
                var server = new TalkHttpServer(port, text => Bridge.TalkAsync(text));
                try { await server.StartAsync().ConfigureAwait(false); }
                catch { await server.DisposeAsync().ConfigureAwait(false); throw; }
                http = server;
            }
            notice = "接続しました。";
        });
    }

    internal void Disconnect() => RunOperation(async () =>
    {
        await StopHttpAsync().ConfigureAwait(false);
        await Bridge.StopAsync().ConfigureAwait(false);
        notice = "切断しました。";
    });

    internal void Speak(string text, bool interpretCommands = true)
    {
        if (disposed) return;
        // TalkAsync内で同時要求数を制限し、ゲームの描画スレッドでは待機しない。
        _ = SpeakAsync(text, interpretCommands);
    }

    private async Task SpeakAsync(string text, bool interpretCommands)
    {
        try
        {
            var response = await Bridge.TalkAsync(text, interpretCommands).ConfigureAwait(false);
            if (!response.Success) notice = response.Message;
        }
        catch (OperationCanceledException) { }
        catch (Exception exception) { notice = exception.Message; }
    }

    private void RunOperation(Func<Task> action)
    {
        if (disposed || Busy) return;
        notice = string.Empty;
        operation = Task.Run(async () =>
        {
            try { await action().ConfigureAwait(false); }
            catch (OperationCanceledException) { }
            catch (Exception exception)
            {
                notice = exception.Message;
                Log.Warning("VOICEROID2の操作に失敗しました: {Message}", exception.Message);
            }
        });
    }

    private async Task MonitorAsync()
    {
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
            while (await timer.WaitForNextTickAsync(shutdown.Token).ConfigureAwait(false))
                if (Bridge.IsConnected) await Bridge.RefreshAsync().ConfigureAwait(false);
        }
        catch (OperationCanceledException) { }
    }

    private void OnChat(IChatMessage message)
    {
        if (!Configuration.ReadChat || !Bridge.IsConnected || !Configuration.Channels.Contains(message.LogKind)) return;
        var text = message.Message.TextValue;
        if (string.IsNullOrWhiteSpace(text)) return;
        if (Configuration.ReadSender && !string.IsNullOrWhiteSpace(message.Sender.TextValue))
            text = $"{message.Sender.TextValue}。{text}";
        Speak(text, false);
    }

    private void OnCommand(string command, string arguments)
    {
        var parts = arguments.Trim().Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) { OpenSettings(); return; }
        switch (parts[0].ToLowerInvariant())
        {
            case "say" when parts.Length == 2: Speak(parts[1]); break;
            case "connect": Connect(); break;
            case "disconnect": Disconnect(); break;
            case "clear": case "stop": case "pause": case "resume": Speak($"<{parts[0].ToLowerInvariant()}>"); break;
            default: OpenSettings(); break;
        }
    }

    private async Task StopHttpAsync()
    {
        var server = http;
        http = null;
        if (server != null) await server.DisposeAsync().ConfigureAwait(false);
    }

    public void Dispose()
    {
        disposed = true;
        Chat.ChatMessageUnhandled -= OnChat;
        PluginInterface.UiBuilder.Draw -= windows.Draw;
        PluginInterface.UiBuilder.OpenConfigUi -= OpenSettings;
        PluginInterface.UiBuilder.OpenMainUi -= OpenSettings;
        Commands.RemoveHandler("/v2xiv");
        windows.RemoveAllWindows();
        DisposeRuntimeAsync().GetAwaiter().GetResult();
        shutdown.Dispose();
    }

    private async Task DisposeRuntimeAsync()
    {
        await shutdown.CancelAsync().ConfigureAwait(false);
        // 初期化中のIPC待機も解除してから、設定変更タスクの終了を待つ。
        await Bridge.DisposeAsync().ConfigureAwait(false);
        await operation.ConfigureAwait(false);
        await StopHttpAsync().ConfigureAwait(false);
        await monitor.ConfigureAwait(false);
    }
}
