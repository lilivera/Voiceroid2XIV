using System.Diagnostics;
using System.Text;
using System.IO.Pipes;
using System.Text.Json;
using Voiceroid2XIV.Core;

namespace Voiceroid2XIV.Tests;

public sealed class ActualBridgeStartupTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task FrameworkPipeValidatesParentAndReturnsInitializationErrors(bool correctParent)
    {
        var name = $"Voiceroid2XIV-{Guid.NewGuid():N}";
        await using var pipe = new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        var info = new ProcessStartInfo
        {
            FileName = Path.Combine(AppContext.BaseDirectory, "actual-bridge", "Voiceroid2XIV.Bridge.exe"),
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
        };
        info.ArgumentList.Add("--pipe");
        info.ArgumentList.Add(name);
        info.ArgumentList.Add((correctParent ? Environment.ProcessId : int.MaxValue).ToString());
        using var child = Process.Start(info)!;
        try
        {
            await pipe.WaitForConnectionAsync().WaitAsync(TimeSpan.FromSeconds(10));
            PipePeerIdentity.ValidateClient(pipe.SafePipeHandle, child.Id);
            Assert.Throws<IOException>(() => PipePeerIdentity.ValidateClient(pipe.SafePipeHandle, int.MaxValue));
            if (correctParent)
            {
                using var writer = new StreamWriter(pipe, new UTF8Encoding(false), 4096, true) { AutoFlush = true };
                using var reader = new StreamReader(pipe, new UTF8Encoding(false), false, 4096, true);
                // 実際のVOICEROID2へ接続する前に拒否される要求を使用する。
                await writer.WriteLineAsync("{\"Operation\":\"invalid\"}");
                var response = await reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(10));
                using var json = JsonDocument.Parse(response!);
                Assert.False(json.RootElement.GetProperty("Success").GetBoolean());
                Assert.Contains("初期化要求がありません。", json.RootElement.GetProperty("Message").GetString());
            }
            await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(correctParent ? 1 : 2, child.ExitCode);
        }
        finally
        {
            if (!child.HasExited) { child.Kill(); await child.WaitForExitAsync(); }
        }
    }

    [Fact]
    public async Task FrameworkWinExeCanReadPipesWithoutConsoleWindow()
    {
        using var child = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = Path.Combine(AppContext.BaseDirectory, "actual-bridge", "Voiceroid2XIV.Bridge.exe"),
                Arguments = "--stdio",
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardErrorEncoding = new UTF8Encoding(false),
            },
        };
        child.Start();
        try
        {
            // 初期化を意図的に拒否させるので、VOICEROID2への接続や起動は発生しない。
            await child.StandardInput.WriteLineAsync("{\"Operation\":\"invalid\"}");
            child.StandardInput.Close();
            var error = child.StandardError.ReadToEndAsync();
            await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(1, child.ExitCode);
            Assert.Contains("初期化要求がありません。", await error);
        }
        finally
        {
            if (!child.HasExited)
            {
                child.Kill();
                await child.WaitForExitAsync();
            }
        }
    }
}
