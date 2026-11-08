using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.Text;
using Dalamud.Interface.Windowing;

namespace Voiceroid2XIV;

internal sealed class SettingsWindow : Window
{
    private readonly Plugin plugin;
    private string testText = "こんにちは。読み上げのテストです。";

    private static readonly (XivChatType Type, string Label)[] Channels =
    [
        (XivChatType.Say, "Say"), (XivChatType.Party, "パーティ"),
        (XivChatType.CrossParty, "クロスワールドパーティ"), (XivChatType.Alliance, "アライアンス"),
        (XivChatType.FreeCompany, "フリーカンパニー"), (XivChatType.TellIncoming, "Tell受信"),
        (XivChatType.TellOutgoing, "Tell送信"), (XivChatType.Shout, "Shout"), (XivChatType.Yell, "Yell"),
        (XivChatType.Ls1, "LS1"), (XivChatType.Ls2, "LS2"), (XivChatType.Ls3, "LS3"), (XivChatType.Ls4, "LS4"),
        (XivChatType.Ls5, "LS5"), (XivChatType.Ls6, "LS6"), (XivChatType.Ls7, "LS7"), (XivChatType.Ls8, "LS8"),
        (XivChatType.CrossLinkShell1, "CWLS1"), (XivChatType.CrossLinkShell2, "CWLS2"),
        (XivChatType.CrossLinkShell3, "CWLS3"), (XivChatType.CrossLinkShell4, "CWLS4"),
        (XivChatType.CrossLinkShell5, "CWLS5"), (XivChatType.CrossLinkShell6, "CWLS6"),
        (XivChatType.CrossLinkShell7, "CWLS7"), (XivChatType.CrossLinkShell8, "CWLS8"),
        (XivChatType.Echo, "Echo"), (XivChatType.NPCDialogue, "NPCの発言"),
    ];

    public SettingsWindow(Plugin plugin) : base("Voiceroid2XIV 設定###Voiceroid2XIVSettings")
    {
        this.plugin = plugin;
        Size = new Vector2(660, 660);
        SizeCondition = ImGuiCond.FirstUseEver;
        SizeConstraints = new WindowSizeConstraints { MinimumSize = new Vector2(460, 420), MaximumSize = new Vector2(float.MaxValue) };
    }

    public override void Draw()
    {
        var config = plugin.Configuration;
        var status = plugin.Bridge.Status;
        ImGui.TextUnformatted(plugin.Bridge.IsConnected ? "VOICEROID2 接続済み" : "VOICEROID2 未接続");
        ImGui.TextWrapped(status.Message);
        ImGui.TextUnformatted($"待機: {status.QueueCount}件 / キュー: {(status.Paused ? "一時停止" : "実行中")} / 割り込み: {(status.Interrupt ? "有効" : "無効")}");
        if (!string.IsNullOrEmpty(plugin.Notice)) ImGui.TextWrapped(plugin.Notice);
        ImGui.Separator();

        var changed = false;
        ImGui.BeginDisabled(plugin.Busy);
        var path = config.EditorPath;
        if (ImGui.InputText("VOICEROID2の実行ファイル", ref path, 1024)) { config.EditorPath = path; changed = true; }
        ImGui.TextWrapped("空欄で自動検出します。起動済みのVOICEROID2があれば接続します。");
        var auto = config.AutoConnect;
        if (ImGui.Checkbox("プラグイン読込時に接続", ref auto)) { config.AutoConnect = auto; changed = true; }
        var administrator = config.RunBridgeAsAdministrator;
        if (ImGui.Checkbox("ヘルパーを管理者権限で起動", ref administrator)) { config.RunBridgeAsAdministrator = administrator; changed = true; }
        if (administrator) ImGui.TextWrapped("接続時にWindowsの確認画面が表示されます。FFXIVの起動権限は変更しません。");
        var interrupt = config.Interrupt;
        if (ImGui.Checkbox("新しい文章の割り込みを許可", ref interrupt))
        {
            config.Interrupt = interrupt;
            changed = true;
            if (plugin.Bridge.IsConnected) plugin.Speak(interrupt ? "<interrupt_enable>" : "<interrupt_disable>");
        }
        if (ImGui.Button(plugin.Bridge.IsConnected ? "再接続・設定を適用" : "接続")) plugin.Connect();
        ImGui.SameLine();
        if (ImGui.Button("切断")) plugin.Disconnect();
        ImGui.EndDisabled();
        if (plugin.Busy) ImGui.TextUnformatted("接続設定を処理しています…");

        ImGui.Separator();
        ImGui.TextUnformatted("読み上げ操作");
        ImGui.InputTextMultiline("##TestText", ref testText, 4096, new Vector2(-1, 80));
        ImGui.BeginDisabled(!plugin.Bridge.IsConnected);
        if (ImGui.Button("読み上げ")) plugin.Speak(testText);
        ImGui.SameLine();
        if (ImGui.Button(status.Paused ? "キューを再開" : "キューを一時停止")) plugin.Speak(status.Paused ? "<resume>" : "<pause>");
        ImGui.SameLine();
        if (ImGui.Button("キューを消去")) plugin.Speak("<clear>");
        ImGui.SameLine();
        if (ImGui.Button("停止")) plugin.Speak("<stop>");
        ImGui.EndDisabled();
        ImGui.TextWrapped("話者は「結月ゆかり＞文章」のように指定できます。一時停止は次の文章から適用します。");

        if (ImGui.CollapsingHeader("チャット読み上げ", ImGuiTreeNodeFlags.DefaultOpen))
        {
            var read = config.ReadChat;
            if (ImGui.Checkbox("選択したチャットを読み上げる", ref read)) { config.ReadChat = read; changed = true; }
            var sender = config.ReadSender;
            if (ImGui.Checkbox("発言者名も読み上げる", ref sender)) { config.ReadSender = sender; changed = true; }
            var twoColumns = ImGui.GetContentRegionAvail().X >= 600;
            for (var index = 0; index < Channels.Length; index++)
            {
                var (type, label) = Channels[index];
                var enabled = config.Channels.Contains(type);
                if (ImGui.Checkbox(label, ref enabled))
                {
                    if (enabled) config.Channels.Add(type); else config.Channels.Remove(type);
                    changed = true;
                }
                if (twoColumns && index % 2 == 0 && index < Channels.Length - 1) ImGui.SameLine(300);
            }
        }

        if (ImGui.CollapsingHeader("HTTP互換API"))
        {
            ImGui.BeginDisabled(plugin.Busy);
            var enabled = config.EnableHttp;
            if (ImGui.Checkbox("HTTP APIを有効にする（再接続後に反映）", ref enabled)) { config.EnableHttp = enabled; changed = true; }
            var port = config.HttpPort;
            if (ImGui.InputInt("ポート", ref port)) { config.HttpPort = Math.Clamp(port, 1024, 65535); changed = true; }
            ImGui.EndDisabled();
            ImGui.TextUnformatted(plugin.HttpRunning ? "HTTP待受中" : "HTTP停止中");
            ImGui.TextWrapped($"接続先: http://127.0.0.1:{plugin.HttpPort}/talk （このPCのみ）");
            ImGui.TextWrapped("GET・JSON POST・フォームPOSTに対応します。既存のProxyは終了してから接続してください。");
        }
        if (changed) plugin.Save();
    }
}
