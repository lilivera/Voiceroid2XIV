using System.Globalization;
using System.IO.Pipes;
using System.Security.Principal;
using System.Text;
using Voiceroid2XIV.Core;

namespace Voiceroid2XIV.Bridge;

public static class BridgeConsole
{
    public static void ExitAfterDisconnect()
    {
        // 接続先が応答しなくても、管理者権限のヘルパーを常駐させない。
        _ = Task.Run(async () => { await Task.Delay(2000).ConfigureAwait(false); Environment.Exit(0); });
    }

    public static NamedPipeClientStream? Open(string[] args)
    {
        var encoding = new UTF8Encoding(false);
        if (args.Length == 1 && args[0] == "--stdio")
        {
            // WinExeにはコンソールがないため、親から継承したパイプを直接使う。
            Console.SetIn(new StreamReader(Console.OpenStandardInput(), encoding));
            Console.SetOut(new StreamWriter(Console.OpenStandardOutput(), encoding) { AutoFlush = true });
            Console.SetError(new StreamWriter(Console.OpenStandardError(), encoding) { AutoFlush = true });
            return null;
        }

        const string prefix = "Voiceroid2XIV-";
        if (args.Length != 3 || args[0] != "--pipe" || !args[1].StartsWith(prefix, StringComparison.Ordinal) ||
            !Guid.TryParseExact(args[1].Substring(prefix.Length), "N", out _) ||
            !int.TryParse(args[2], NumberStyles.None, CultureInfo.InvariantCulture, out var parentId) || parentId <= 0)
            throw new ArgumentException("ヘルパーの起動引数が正しくありません。");

        // 管理者権限のトークンを接続元に偽装させず、同じPCのパイプだけに接続する。
        var pipe = new NamedPipeClientStream(".", args[1], PipeDirection.InOut, PipeOptions.Asynchronous,
            TokenImpersonationLevel.Identification);
        try
        {
            pipe.Connect(10000);
            PipePeerIdentity.ValidateServer(pipe.SafePipeHandle, parentId);
            Console.SetIn(new StreamReader(pipe, encoding, false, 4096, true));
            Console.SetOut(new StreamWriter(pipe, encoding, 4096, true) { AutoFlush = true });
            Console.SetError(TextWriter.Null);
            return pipe;
        }
        catch { pipe.Dispose(); throw; }
    }
}
