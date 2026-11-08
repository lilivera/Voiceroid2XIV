using System.ComponentModel;
using System.Diagnostics;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using Newtonsoft.Json;
using Voiceroid2XIV.Core;

namespace Voiceroid2XIV.Runtime;

public sealed class BridgeClient(string executablePath, Func<ProcessStartInfo, Process?>? processStarter = null) : IAsyncDisposable
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly CancellationTokenSource lifetime = new();
    private readonly Func<ProcessStartInfo, Process?> startProcess = processStarter ?? Process.Start;
    private Process? process;
    private NamedPipeServerStream? pipe;
    private StreamReader? reader;
    private StreamWriter? writer;
    private bool elevated;
    private int outstanding;
    private volatile BridgeResponse status = new() { Message = "未接続" };
    private volatile bool connected;

    public BridgeResponse Status => status;
    public bool IsConnected => connected;

    public async Task StartAsync(BridgeOptions options, bool runAsAdministrator = false)
    {
        await gate.WaitAsync(lifetime.Token).ConfigureAwait(false);
        try
        {
            if (connected) return;
            lifetime.Token.ThrowIfCancellationRequested();
            if (!File.Exists(executablePath))
                throw new FileNotFoundException("bridgeフォルダーがありません。配布物をフォルダーごと配置してください。", executablePath);

            var pipeName = $"Voiceroid2XIV-{Guid.NewGuid():N}";
            pipe = CreatePipe(pipeName);
            var startInfo = new ProcessStartInfo
            {
                FileName = executablePath,
                WorkingDirectory = Path.GetDirectoryName(executablePath),
                UseShellExecute = runAsAdministrator,
                Verb = runAsAdministrator ? "runas" : string.Empty,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
            };
            startInfo.ArgumentList.Add("--pipe");
            startInfo.ArgumentList.Add(pipeName);
            startInfo.ArgumentList.Add(Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));
            elevated = runAsAdministrator;
            status = new BridgeResponse { Message = runAsAdministrator ? "Windowsの管理者権限確認を待っています…" : "VOICEROID2に接続しています…" };
            try { process = startProcess(startInfo) ?? throw new IOException("ヘルパーを起動できませんでした。"); }
            catch (Win32Exception exception) when (exception.NativeErrorCode == 1223)
            {
                throw new IOException("管理者権限での起動がキャンセルされました。接続を押すと再試行できます。", exception);
            }

            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token))
            {
                timeout.CancelAfter(TimeSpan.FromSeconds(15));
                await pipe.WaitForConnectionAsync(timeout.Token).ConfigureAwait(false);
            }
            PipePeerIdentity.ValidateClient(pipe.SafePipeHandle, process.Id);
            reader = new StreamReader(pipe, new UTF8Encoding(false), false, 4096, true);
            writer = new StreamWriter(pipe, new UTF8Encoding(false), 4096, true);
            status = new BridgeResponse { Message = "VOICEROID2に接続しています…" };
            status = await ExchangeAsync(new BridgeRequest { Operation = "initialize", Options = options },
                TimeSpan.FromSeconds(75)).ConfigureAwait(false);
            if (!status.Success) throw new IOException(status.Message);
            connected = true;
        }
        catch (Exception exception)
        {
            status = new BridgeResponse { Message = exception.Message };
            await CloseProcessAsync().ConfigureAwait(false);
            throw;
        }
        finally { gate.Release(); }
    }

    private static NamedPipeServerStream CreatePipe(string name)
    {
        // CurrentUserOnlyは昇格レベルも照合するため使用せず、利用者のSIDを明示する。
        using var identity = WindowsIdentity.GetCurrent();
        var security = new PipeSecurity();
        security.SetAccessRuleProtection(true, false);
        security.AddAccessRule(new PipeAccessRule(identity.User!, PipeAccessRights.FullControl, AccessControlType.Allow));
        return NamedPipeServerStreamAcl.Create(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.FirstPipeInstance, 4096, 4096, security);
    }

    public Task<BridgeResponse> TalkAsync(string text, bool interpretCommands = true) =>
        string.IsNullOrWhiteSpace(text) || text.Length > SpeechQueue.MaximumTextLength
            ? Task.FromResult(new BridgeResponse { Message = $"文章は1〜{SpeechQueue.MaximumTextLength}文字で指定してください。" })
            : SendAsync(new BridgeRequest { Operation = "talk", Text = text, InterpretCommands = interpretCommands });

    public Task<BridgeResponse> RefreshAsync() => SendAsync(new BridgeRequest());

    private async Task<BridgeResponse> SendAsync(BridgeRequest request)
    {
        if (Interlocked.Increment(ref outstanding) > 64)
        {
            Interlocked.Decrement(ref outstanding);
            return new BridgeResponse { Message = "通信が混雑しています。少し待って再試行してください。" };
        }
        try
        {
            await gate.WaitAsync(lifetime.Token).ConfigureAwait(false);
            try
            {
                if (!connected || process == null)
                    return new BridgeResponse { Message = "設定画面でVOICEROID2に接続してください。" };
                status = await ExchangeAsync(request, TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                return status;
            }
            catch (Exception exception)
            {
                status = new BridgeResponse { Message = exception.Message };
                await CloseProcessAsync().ConfigureAwait(false);
                return status;
            }
            finally { gate.Release(); }
        }
        finally { Interlocked.Decrement(ref outstanding); }
    }

    private async Task<BridgeResponse> ExchangeAsync(BridgeRequest request, TimeSpan timeout)
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        cancellation.CancelAfter(timeout);
        if (writer == null || reader == null) throw new IOException("ヘルパーが接続されていません。");
        await writer.WriteLineAsync(JsonConvert.SerializeObject(request).AsMemory(), cancellation.Token).ConfigureAwait(false);
        await writer.FlushAsync(cancellation.Token).ConfigureAwait(false);
        var line = await reader.ReadLineAsync(cancellation.Token).ConfigureAwait(false);
        if (line == null) throw new IOException("ヘルパーとの接続が切れました。再接続してください。");
        return JsonConvert.DeserializeObject<BridgeResponse>(line) ?? throw new IOException("ヘルパーの応答を読み取れません。");
    }

    public async Task StopAsync()
    {
        await gate.WaitAsync().ConfigureAwait(false);
        try
        {
            await CloseProcessAsync().ConfigureAwait(false);
            status = new BridgeResponse { Message = "未接続" };
        }
        finally { gate.Release(); }
    }

    private async Task CloseProcessAsync()
    {
        connected = false;
        pipe?.Dispose();
        pipe = null;
        reader?.Dispose();
        reader = null;
        try { writer?.Dispose(); } catch (IOException) { } catch (ObjectDisposedException) { }
        writer = null;
        var child = process;
        process = null;
        if (child == null) return;
        try
        {
            if (!child.HasExited)
            {
                try { await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(3)).ConfigureAwait(false); }
                catch (TimeoutException) when (!elevated)
                {
                    // 自分で起動したヘルパーだけを終了し、VOICEROID2本体には触れない。
                    child.Kill();
                    await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
                }
                catch (TimeoutException)
                {
                    // 昇格したヘルパーはパイプ切断を検出して自分で終了する。
                }
            }
        }
        finally { child.Dispose(); }
    }

    public async ValueTask DisposeAsync()
    {
        await lifetime.CancelAsync().ConfigureAwait(false);
        await StopAsync().ConfigureAwait(false);
        // 待機中の呼び出しがキャンセルを観測できるよう、同期オブジェクトはここで破棄しない。
    }
}
