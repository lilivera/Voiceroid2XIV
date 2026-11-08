using Voiceroid2XIV.Bridge;
using Newtonsoft.Json;
using Voiceroid2XIV.Core;

// VOICEROID2に接続せず、実プロセス間通信の切断・待機・直列化を検証する。
using var connection = BridgeConsole.Open(args);
var initial = JsonConvert.DeserializeObject<BridgeRequest>(Console.ReadLine()!);
if (initial?.Options?.EditorPath == "hang-initialize")
    await HangUntilDisconnected();
if (initial?.Options?.EditorPath == "fail-initialize")
{
    Console.WriteLine(JsonConvert.SerializeObject(new BridgeResponse { Message = "テスト用の接続エラー" }));
    return;
}
Console.WriteLine(JsonConvert.SerializeObject(new BridgeResponse { Success = true, Message = "接続済み" }));
string? line;
while ((line = Console.ReadLine()) != null)
{
    var request = JsonConvert.DeserializeObject<BridgeRequest>(line)!;
    if (request.Text == "exit") return;
    if (request.Text == "invalid-response") { Console.WriteLine("{"); continue; }
    if (request.Text == "hang-response") await HangUntilDisconnected();
    Console.WriteLine(JsonConvert.SerializeObject(new BridgeResponse
    {
        Success = true,
        Message = request.Text ?? "接続済み",
        Interrupt = request.InterpretCommands,
    }));
}

static async Task HangUntilDisconnected()
{
    // 操作スレッドが応答しない場合にも接続監視が動作する状況を再現する。
    await Task.Run(() => { while (Console.ReadLine() != null) { } });
    BridgeConsole.ExitAfterDisconnect();
    await Task.Delay(Timeout.Infinite);
}
