using Dalamud.Configuration;
using Dalamud.Game.Text;

namespace Voiceroid2XIV;

[Serializable]
public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;
    public string EditorPath { get; set; } = string.Empty;
    public bool AutoConnect { get; set; }
    public bool RunBridgeAsAdministrator { get; set; }
    public bool Interrupt { get; set; } = true;
    public bool EnableHttp { get; set; }
    public int HttpPort { get; set; } = 4532;
    public bool ReadChat { get; set; }
    public bool ReadSender { get; set; }
    public HashSet<XivChatType> Channels { get; set; } = [];
}
