using System.Text.RegularExpressions;

namespace Voiceroid2XIV.Core;

/// <summary>再生待ちと互換コマンドを管理する。VOICEROIDへの操作は専用スレッドで行う。</summary>
public sealed class SpeechQueue
{
    public const int MaximumTextLength = 4096;
    public const int MaximumQueueLength = 128;
    private static readonly Regex Commands = new(
        "<(clear|pause|resume|interrupt_enable|interrupt_disable|stop)>",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private readonly object gate = new();
    private readonly Queue<string> pending = new();
    private bool paused;
    private bool interrupt;
    private bool stopRequested;
    private long nextPlayMilliseconds;

    public SpeechQueue(bool interrupt = true) => this.interrupt = interrupt;

    public BridgeResponse Submit(string? text, bool interpretCommands = true)
    {
        if (string.IsNullOrWhiteSpace(text))
            return Status(false, "読み上げる文章を指定してください。");
        if (text!.Length > MaximumTextLength)
            return Status(false, $"文章は{MaximumTextLength}文字以内にしてください。");

        lock (gate)
        {
            var commands = new List<string>();
            var spoken = interpretCommands
                ? Commands.Replace(text, match => { commands.Add(match.Groups[1].Value); return string.Empty; })
                : text;
            // 拒否した要求では、同じ要求に含まれる設定変更も反映しない。
            if (!string.IsNullOrWhiteSpace(spoken) && pending.Count >= MaximumQueueLength &&
                !commands.Contains("clear") && !commands.Contains("stop"))
                return Status(false, "読み上げキューが満杯です。");

            foreach (var command in commands)
            {
                switch (command)
                {
                    case "clear": pending.Clear(); break;
                    case "pause": paused = true; break;
                    case "resume": paused = false; break;
                    case "interrupt_enable": interrupt = true; break;
                    case "interrupt_disable": interrupt = false; break;
                    case "stop": pending.Clear(); stopRequested = true; break;
                }
            }

            if (!string.IsNullOrWhiteSpace(spoken))
                pending.Enqueue(spoken);
            return Status(true, "受け付けました。");
        }
    }

    public bool TryTake(bool isSpeaking, long elapsedMilliseconds, out string? text)
    {
        lock (gate)
        {
            text = null;
            if (paused || stopRequested || pending.Count == 0 || (!interrupt && isSpeaking) ||
                elapsedMilliseconds < nextPlayMilliseconds)
                return false;
            text = pending.Dequeue();
            nextPlayMilliseconds = elapsedMilliseconds + 200;
            return true;
        }
    }

    public bool ConsumeStopRequest()
    {
        lock (gate)
        {
            var requested = stopRequested;
            stopRequested = false;
            return requested;
        }
    }

    public BridgeResponse Status(bool success = true, string message = "接続済み")
    {
        lock (gate)
            return new BridgeResponse
            {
                Success = success, Message = message, QueueCount = pending.Count,
                Paused = paused, Interrupt = interrupt,
            };
    }
}
