using System.Diagnostics;
using Newtonsoft.Json;
using Voiceroid2XIV.Core;

namespace Voiceroid2XIV.Bridge;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        System.IO.Pipes.NamedPipeClientStream? pipe;
        try { pipe = BridgeConsole.Open(args); }
        catch { return 2; }
        using var connection = pipe;
        var initializing = true;
        try
        {
            var request = JsonConvert.DeserializeObject<BridgeRequest>(Console.ReadLine() ?? "");
            if (request?.Operation != "initialize" || request.Options == null)
                throw new InvalidOperationException("初期化要求がありません。");

            var queue = new SpeechQueue(request.Options.Interrupt);
            using var shutdown = new CancellationTokenSource();
            // 初期化が応答しない場合でも、親の切断を別スレッドで受け取れるようにする。
            _ = Task.Run(() => ReadRequests(queue, shutdown));
            using var editor = new VoiceroidEditor(request.Options.EditorPath);
            initializing = false;
            Write(queue.Status());
            var clock = Stopwatch.StartNew();
            while (!shutdown.IsCancellationRequested)
            {
                if (!editor.IsAlive)
                    throw new IOException("VOICEROID2が終了しました。設定画面から再接続してください。");
                if (queue.ConsumeStopRequest())
                    editor.Stop();
                if (queue.Status().QueueCount > 0 &&
                    queue.TryTake(editor.IsSpeaking, clock.ElapsedMilliseconds, out var text))
                    editor.Speak(text!);
                shutdown.Token.WaitHandle.WaitOne(50);
            }
            return 0;
        }
        catch (Exception exception)
        {
            // 読み上げ本文は診断ログに残さない。
            var message = $"VOICEROID2との接続に失敗しました: {(initializing ? exception.ToString() : exception.Message)}";
            if (pipe == null) Console.Error.WriteLine(message);
            else
            {
                try { Write(new BridgeResponse { Message = message }); }
                catch (IOException) { }
            }
            return 1;
        }
    }

    private static void ReadRequests(SpeechQueue queue, CancellationTokenSource shutdown)
    {
        try
        {
            string? line;
            while ((line = Console.ReadLine()) != null)
            {
                var request = JsonConvert.DeserializeObject<BridgeRequest>(line);
                BridgeResponse response;
                switch (request?.Operation)
                {
                    case "talk": response = queue.Submit(request.Text, request.InterpretCommands); break;
                    case "status": response = queue.Status(); break;
                    case "shutdown":
                        Write(queue.Status());
                        return;
                    default: response = queue.Status(false, "不明な操作です。"); break;
                }
                Write(response);
            }
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"プラグインとの通信を終了します: {exception.Message}");
        }
        finally
        {
            try { shutdown.Cancel(); } catch (ObjectDisposedException) { }
            // 通常権限の親は管理者権限の子を強制終了できないため、切断後は子が自分で終了する。
            BridgeConsole.ExitAfterDisconnect();
        }
    }

    private static void Write(BridgeResponse response)
    {
        Console.WriteLine(JsonConvert.SerializeObject(response));
        Console.Out.Flush();
    }
}
