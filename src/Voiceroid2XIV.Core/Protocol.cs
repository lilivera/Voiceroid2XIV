namespace Voiceroid2XIV.Core;

public sealed class BridgeOptions
{
    public string EditorPath { get; set; } = string.Empty;
    public bool Interrupt { get; set; } = true;
}

public sealed class BridgeRequest
{
    public string Operation { get; set; } = "status";
    public string? Text { get; set; }
    public bool InterpretCommands { get; set; } = true;
    public BridgeOptions? Options { get; set; }
}

public sealed class BridgeResponse
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public int QueueCount { get; set; }
    public bool Paused { get; set; }
    public bool Interrupt { get; set; }
}
