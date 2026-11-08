using Voiceroid2XIV.Core;
using Voiceroid2XIV.Runtime;
using System.ComponentModel;
using System.Diagnostics;

namespace Voiceroid2XIV.Tests;

public sealed class BridgeClientTests
{
    private static BridgeClient CreateClient() => new(Path.Combine(AppContext.BaseDirectory,
        "test-bridge", "Voiceroid2XIV.TestBridge.exe"));

    [Fact]
    public async Task CancelledElevationIsReportedAndConnectionCanBeRetried()
    {
        var launchCount = 0;
        await using var client = new BridgeClient(Path.Combine(AppContext.BaseDirectory, "test-bridge", "Voiceroid2XIV.TestBridge.exe"), info =>
        {
            Assert.True(info.UseShellExecute);
            Assert.Equal("runas", info.Verb);
            Assert.False(info.RedirectStandardInput);
            Assert.Equal("--pipe", info.ArgumentList[0]);
            if (++launchCount == 1) throw new Win32Exception(1223);
            // 自動テストではUACを開かず、同じ通信経路で子プロセスを起動する。
            info.UseShellExecute = false;
            info.Verb = string.Empty;
            return Process.Start(info);
        });
        var exception = await Assert.ThrowsAsync<IOException>(() => client.StartAsync(new BridgeOptions(), true));
        Assert.Contains("管理者権限での起動がキャンセル", exception.Message);
        Assert.False(client.IsConnected);
        await client.StartAsync(new BridgeOptions(), true);
        Assert.True(client.IsConnected);
    }

    [Fact]
    public async Task HelperExitsOnDisconnectWithoutParentTerminatingIt()
    {
        Process? child = null;
        var client = new BridgeClient(Path.Combine(AppContext.BaseDirectory, "test-bridge", "Voiceroid2XIV.TestBridge.exe"), info =>
        {
            info.UseShellExecute = false;
            info.Verb = string.Empty;
            var started = Process.Start(info)!;
            child = Process.GetProcessById(started.Id);
            return started;
        });
        try
        {
            // 昇格モードの終了処理では親からKillしないため、子の切断監視が必要になる。
            await client.StartAsync(new BridgeOptions(), true);
            var request = client.TalkAsync("hang-response");
            await client.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10));
            Assert.False((await request).Success);
            Assert.True(child!.HasExited);
        }
        finally
        {
            await client.DisposeAsync();
            if (child != null)
            {
                if (!child.HasExited) child.Kill();
                child.Dispose();
            }
        }
    }

    [Fact]
    public async Task RoundTripPreservesJapaneseNewlinesAndLiteralMode()
    {
        await using var client = CreateClient();
        await client.StartAsync(new BridgeOptions());
        Assert.True(client.IsConnected);
        var response = await client.TalkAsync("結月ゆかり＞日本語\r\n<clear>二行目", false);
        Assert.True(response.Success);
        Assert.False(response.Interrupt);
        Assert.Equal("結月ゆかり＞日本語\r\n<clear>二行目", response.Message);
        await client.StopAsync();
        Assert.False(client.IsConnected);
        await client.StartAsync(new BridgeOptions());
        Assert.True(client.IsConnected);
    }

    [Fact]
    public async Task ConcurrentRequestsKeepTheirOwnResponses()
    {
        await using var client = CreateClient();
        await client.StartAsync(new BridgeOptions());
        await Task.WhenAll(Enumerable.Range(0, 32).Select(async index =>
        {
            var response = await client.TalkAsync($"要求{index}");
            Assert.True(response.Success);
            Assert.Equal($"要求{index}", response.Message);
        }));
    }

    [Fact]
    public async Task StartupFailureReportsDiagnosticAndAllowsRetry()
    {
        await using var client = CreateClient();
        var exception = await Assert.ThrowsAsync<IOException>(() => client.StartAsync(new BridgeOptions { EditorPath = "fail-initialize" }));
        Assert.Contains("テスト用の接続エラー", exception.Message);
        Assert.False(client.IsConnected);
        await client.StartAsync(new BridgeOptions());
        Assert.True(client.IsConnected);
    }

    [Theory]
    [InlineData("exit")]
    [InlineData("invalid-response")]
    public async Task LostOrMalformedResponseDisconnectsCleanly(string text)
    {
        await using var client = CreateClient();
        await client.StartAsync(new BridgeOptions());
        Assert.False((await client.TalkAsync(text)).Success);
        Assert.False(client.IsConnected);
        await client.StartAsync(new BridgeOptions());
        Assert.True(client.IsConnected);
    }

    [Fact]
    public async Task DisposalCancelsInitializationWithoutWaitingForStartupTimeout()
    {
        var client = CreateClient();
        var startup = client.StartAsync(new BridgeOptions { EditorPath = "hang-initialize" });
        await client.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => startup);
        Assert.False(client.IsConnected);
    }

    [Fact]
    public async Task DisposalCancelsPendingRequestAndStopsUnresponsiveChild()
    {
        var client = CreateClient();
        await client.StartAsync(new BridgeOptions());
        var request = client.TalkAsync("hang-response");
        await client.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10));
        Assert.False((await request).Success);
        Assert.False(client.IsConnected);
    }

    [Fact]
    public async Task MissingExecutableCanBeDisposed()
    {
        await using var client = new BridgeClient(Path.Combine(AppContext.BaseDirectory, "missing.exe"));
        await Assert.ThrowsAsync<FileNotFoundException>(() => client.StartAsync(new BridgeOptions()));
        Assert.False(client.IsConnected);
    }

    [Fact]
    public async Task InvalidTextIsRejectedWithoutSendingToHelper()
    {
        await using var client = CreateClient();
        await client.StartAsync(new BridgeOptions());
        Assert.False((await client.TalkAsync(new string('あ', 4097))).Success);
        Assert.True(client.IsConnected);
        Assert.False((await client.TalkAsync("  ")).Success);
    }
}
