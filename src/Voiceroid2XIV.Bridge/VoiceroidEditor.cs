using System.Diagnostics;
using System.Windows.Controls;
using Codeer.Friendly.Windows;
using Codeer.Friendly.Windows.Grasp;
using Microsoft.Win32;
using RM.Friendly.WPFStandardControls;

namespace Voiceroid2XIV.Bridge;

/// <summary>SimpleVoiceroid2Proxyの接続方式を基にした32bit専用の操作層。</summary>
internal sealed class VoiceroidEditor : IDisposable
{
    private readonly Process process;
    private readonly WindowsAppFriend app;
    private readonly WPFTextBox textBox = null!;
    private readonly WPFButtonBase play = null!;
    private readonly WPFButtonBase stop = null!;
    private readonly WPFButtonBase beginning = null!;

    public VoiceroidEditor(string configuredPath)
    {
        process = FindOrStart(configuredPath);
        try
        {
            app = new WindowsAppFriend(process);
            try
            {
                var timeout = Stopwatch.StartNew();
                while (timeout.Elapsed < TimeSpan.FromSeconds(60))
                {
                    if (process.HasExited)
                        throw new InvalidOperationException("VOICEROID2が終了しました。");
                    var view = app.FromZTop().GetFromTypeFullName("AI.Talk.Editor.TextEditView")
                        .FirstOrDefault();
                    if (view != null)
                    {
                        var tree = view.LogicalTree();
                        textBox = new WPFTextBox(tree.ByType<TextBox>().Single());
                        play = new WPFButtonBase(tree.ByBinding("PlayCommand").Single());
                        stop = new WPFButtonBase(tree.ByBinding("StopCommand").Single());
                        var move = tree.ByBinding("MoveToBeginningCommand").FirstOrDefault();
                        // 一部のVOICEROID2ではBindingから取得できないため、参照実装の位置を使用する。
                        beginning = new WPFButtonBase(move ?? tree[15]);
                        return;
                    }
                    Thread.Sleep(100);
                }
                throw new TimeoutException("VOICEROID2の編集画面が60秒以内に見つかりませんでした。");
            }
            catch
            {
                app.Dispose();
                throw;
            }
        }
        catch
        {
            process.Dispose();
            throw;
        }
    }

    public bool IsSpeaking => !beginning.IsEnabled;
    public bool IsAlive => !process.HasExited;

    public void Speak(string text)
    {
        stop.EmulateClick();
        textBox.EmulateChangeText(text);
        beginning.EmulateClick();
        play.EmulateClick();
    }

    public void Stop() => stop.EmulateClick();

    public void Dispose()
    {
        app.Dispose();
        // 利用者の編集内容を守るため、VOICEROID2本体は終了させない。
        process.Dispose();
    }

    private static Process FindOrStart(string configuredPath)
    {
        var running = Process.GetProcessesByName("VoiceroidEditor");
        if (running.Length > 0)
        {
            foreach (var other in running.Skip(1)) other.Dispose();
            return running[0];
        }

        var path = configuredPath;
        if (string.IsNullOrWhiteSpace(path))
        {
            using var assemblies = Registry.ClassesRoot.OpenSubKey(@"Installer\Assemblies");
            path = assemblies?.GetSubKeyNames()
                .FirstOrDefault(name => name.EndsWith("VoiceroidEditor.exe", StringComparison.OrdinalIgnoreCase))
                ?.Replace('|', '\\') ?? string.Empty;
        }
        if (string.IsNullOrWhiteSpace(path))
            path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                "AHS", "VOICEROID2", "VoiceroidEditor.exe");
        if (!File.Exists(path) || !string.Equals(Path.GetFileName(path), "VoiceroidEditor.exe", StringComparison.OrdinalIgnoreCase))
            throw new FileNotFoundException("設定画面でVoiceroidEditor.exeの場所を指定してください。");

        return Process.Start(new ProcessStartInfo
        {
            FileName = path,
            WorkingDirectory = Path.GetDirectoryName(path),
            UseShellExecute = false,
            WindowStyle = ProcessWindowStyle.Hidden,
        }) ?? throw new InvalidOperationException("VOICEROID2を起動できませんでした。");
    }
}
