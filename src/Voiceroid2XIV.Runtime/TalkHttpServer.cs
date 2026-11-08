using System.Net;
using System.Net.NetworkInformation;
using System.Text;
using System.Text.Json;
using System.Web;
using EmbedIO;
using EmbedIO.Actions;
using Voiceroid2XIV.Core;

namespace Voiceroid2XIV.Runtime;

public sealed class TalkHttpServer : IAsyncDisposable
{
    private static readonly object StartGate = new();
    private const int MaximumBodyBytes = 32768;
    private WebServer? server;
    private readonly Func<string, Task<BridgeResponse>> talk;
    private readonly CancellationTokenSource lifetime = new();
    private readonly TaskCompletionSource listening = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Task? run;
    private int disposed;

    public TalkHttpServer(int port, Func<string, Task<BridgeResponse>> talk)
    {
        if (port is < 1024 or > 65535) throw new ArgumentOutOfRangeException(nameof(port));
        Port = port;
        this.talk = talk;
    }

    private WebServer CreateServer()
    {
        // GETの本文がURLとしてライブラリの診断ログへ出ないようにする。
        Swan.Logging.Logger.NoLogging();
        // 数値アドレスで待ち受け、LANへ公開しない。localhostのIPv6接続も扱う。
        var result = new WebServer(options => options.WithMode(HttpListenerMode.EmbedIO)
                .WithUrlPrefix($"http://127.0.0.1:{Port}/")
                .WithUrlPrefix($"http://localhost:{Port}/"))
            .WithModule(new ActionModule("/", HttpVerbs.Any, HandleAsync));
        result.StateChanged += (_, args) =>
        {
            if (args.NewState == WebServerState.Listening) listening.TrySetResult();
        };
        return result;
    }

    public bool IsRunning => server?.State == WebServerState.Listening && run is { IsCompleted: false };
    public int Port { get; }

    public async Task StartAsync()
    {
        lock (StartGate)
        {
            ObjectDisposedException.ThrowIf(disposed != 0, this);
            if (run != null) throw new InvalidOperationException("HTTPサーバーは開始済みです。");
            // EmbedIOは同一プロセスのリスナーを共有するため、既存の待受を上書きする前に検出する。
            if (IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners().Any(endpoint => endpoint.Port == Port))
                throw new IOException($"ポート{Port}は使用中です。設定で別のポートを指定してください。");
            server = CreateServer();
            run = RunAsync();
        }
        await listening.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
    }

    private async Task RunAsync()
    {
        try
        {
            await server!.RunAsync(lifetime.Token).ConfigureAwait(false);
            listening.TrySetException(new IOException("HTTPサーバーが停止しました。ポートの使用状況を確認してください。"));
        }
        catch (Exception exception)
        {
            listening.TrySetException(exception);
            // 起動後の異常終了もIsRunningに反映する。タスクの例外はここで回収する。
        }
    }

    private async Task HandleAsync(IHttpContext context)
    {
        if (!IPAddress.IsLoopback(context.Request.RemoteEndPoint.Address))
        {
            await RespondAsync(context, 403, false, "ローカル接続のみ利用できます。");
            return;
        }
        // 任意のWebページからローカル読み上げを操作されないよう、ブラウザー起点の要求を拒否する。
        if (!string.IsNullOrEmpty(context.Request.Headers["Origin"]) ||
            context.Request.Headers["Sec-Fetch-Site"] == "cross-site")
        {
            await RespondAsync(context, 403, false, "ブラウザーからのクロスオリジン接続には対応していません。");
            return;
        }
        if (context.Request.Url.AbsolutePath != "/talk")
        {
            await RespondAsync(context, 404, false, "エンドポイントがありません。");
            return;
        }
        if (context.Request.HttpMethod == "OPTIONS")
        {
            context.Response.Headers["Allow"] = "GET, POST, OPTIONS";
            context.Response.StatusCode = 204;
            return;
        }

        try
        {
            string? text;
            if (context.Request.HttpMethod == "GET")
                text = context.Request.QueryString["text"];
            else if (context.Request.HttpMethod == "POST")
            {
                var contentType = context.Request.ContentType?.Split(';')[0].Trim();
                if (contentType != "application/json" && contentType != "application/x-www-form-urlencoded")
                {
                    await RespondAsync(context, 415, false, "JSONまたはフォーム形式で送信してください。");
                    return;
                }
                var body = await ReadBodyAsync(context).ConfigureAwait(false);
                if (contentType == "application/x-www-form-urlencoded")
                    text = HttpUtility.ParseQueryString(body)["text"];
                else
                {
                    using var json = JsonDocument.Parse(body);
                    text = json.RootElement.ValueKind == JsonValueKind.Object &&
                           json.RootElement.TryGetProperty("text", out var property) &&
                           property.ValueKind == JsonValueKind.String ? property.GetString() : null;
                }
            }
            else
            {
                context.Response.Headers["Allow"] = "GET, POST, OPTIONS";
                await RespondAsync(context, 405, false, "このHTTPメソッドには対応していません。");
                return;
            }

            if (string.IsNullOrWhiteSpace(text))
                await RespondAsync(context, 400, false, "textに文章を指定してください。");
            else if (text.Length > SpeechQueue.MaximumTextLength)
                await RespondAsync(context, 413, false, "文章が長すぎます。");
            else
            {
                var response = await talk(text).WaitAsync(TimeSpan.FromSeconds(10), lifetime.Token).ConfigureAwait(false);
                await RespondAsync(context, response.Success ? 200 : 503, response.Success, response.Message, text);
            }
        }
        catch (JsonException) { await RespondAsync(context, 400, false, "JSONの形式が正しくありません。"); }
        catch (InvalidDataException) { await RespondAsync(context, 413, false, "リクエストが大きすぎます。"); }
        catch (OperationCanceledException) { await RespondAsync(context, 503, false, "処理を中断しました。"); }
        catch (TimeoutException) { await RespondAsync(context, 503, false, "接続がタイムアウトしました。"); }
        catch (Exception) { await RespondAsync(context, 503, false, "読み上げに失敗しました。接続状態を確認してください。"); }
    }

    private async Task<string> ReadBodyAsync(IHttpContext context)
    {
        if (context.Request.ContentLength64 > MaximumBodyBytes) throw new InvalidDataException();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        using var body = new MemoryStream();
        var buffer = new byte[4096];
        int read;
        while ((read = await context.Request.InputStream.ReadAsync(buffer, timeout.Token).ConfigureAwait(false)) > 0)
        {
            if (body.Length + read > MaximumBodyBytes) throw new InvalidDataException();
            body.Write(buffer, 0, read);
        }
        return Encoding.UTF8.GetString(body.ToArray());
    }

    private static async Task RespondAsync(IHttpContext context, int code, bool success, string message, string? text = null)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new { success, message, text });
        context.Response.StatusCode = code;
        context.Response.ContentType = "application/json; charset=utf-8";
        context.Response.ContentLength64 = bytes.Length;
        await context.Response.OutputStream.WriteAsync(bytes).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        await lifetime.CancelAsync().ConfigureAwait(false);
        server?.Dispose();
        if (run != null) await run.ConfigureAwait(false);
        lifetime.Dispose();
    }
}
