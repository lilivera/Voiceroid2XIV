using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Voiceroid2XIV.Core;

/// <summary>パイプの両端が、この接続のために起動したプロセスであることを確認する。</summary>
public static class PipePeerIdentity
{
    public static void ValidateClient(SafeHandle handle, int expectedProcessId)
    {
        if (!GetNamedPipeClientProcessId(handle, out var processId)) throw new Win32Exception();
        if (processId != expectedProcessId) throw new IOException("接続相手が起動したヘルパーと一致しません。");
    }

    public static void ValidateServer(SafeHandle handle, int expectedProcessId)
    {
        if (!GetNamedPipeServerProcessId(handle, out var processId)) throw new Win32Exception();
        if (processId != expectedProcessId) throw new IOException("接続元が起動したプラグインと一致しません。");
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetNamedPipeClientProcessId(SafeHandle handle, out uint processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetNamedPipeServerProcessId(SafeHandle handle, out uint processId);
}
