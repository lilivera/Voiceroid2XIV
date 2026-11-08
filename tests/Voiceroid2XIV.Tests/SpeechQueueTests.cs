using Voiceroid2XIV.Core;

namespace Voiceroid2XIV.Tests;

public sealed class SpeechQueueTests
{
    [Fact]
    public void SequentialModeWaitsForPlaybackAndPreservesOrder()
    {
        var queue = new SpeechQueue(false);
        queue.Submit("一番目");
        queue.Submit("二番目");
        Assert.False(queue.TryTake(true, 0, out _));
        Assert.True(queue.TryTake(false, 0, out var first));
        Assert.Equal("一番目", first);
        Assert.False(queue.TryTake(false, 199, out _));
        Assert.False(queue.TryTake(true, 200, out _));
        Assert.True(queue.TryTake(false, 200, out var second));
        Assert.Equal("二番目", second);
    }

    [Fact]
    public void InterruptModeCanReplaceCurrentPlayback()
    {
        var queue = new SpeechQueue();
        queue.Submit("新しい文章");
        Assert.True(queue.TryTake(true, 0, out _));
    }

    [Fact]
    public void ClearRemovesEvenTheItemWaitingForCurrentPlayback()
    {
        var queue = new SpeechQueue(false);
        queue.Submit("消す文章");
        Assert.False(queue.TryTake(true, 0, out _));
        queue.Submit("<clear>");
        Assert.False(queue.TryTake(false, 500, out _));
        Assert.Equal(0, queue.Status().QueueCount);
    }

    [Fact]
    public void PauseAndResumeDoNotEnqueueEmptyUtterances()
    {
        var queue = new SpeechQueue();
        queue.Submit("<pause>待機する文章");
        Assert.False(queue.TryTake(false, 0, out _));
        queue.Submit("<resume>");
        Assert.True(queue.TryTake(false, 0, out var text));
        Assert.Equal("待機する文章", text);
        Assert.Equal(0, queue.Status().QueueCount);
    }

    [Fact]
    public void CommandsAreAppliedInAppearanceOrder()
    {
        var queue = new SpeechQueue();
        queue.Submit("<pause><resume><interrupt_disable><interrupt_enable>");
        Assert.False(queue.Status().Paused);
        Assert.True(queue.Status().Interrupt);
        Assert.Equal(0, queue.Status().QueueCount);
    }

    [Fact]
    public void UnknownTagsAndSpeakerPrefixArePreserved()
    {
        var queue = new SpeechQueue();
        queue.Submit("<resume>結月ゆかり＞<unknown>テスト");
        Assert.True(queue.TryTake(false, 0, out var text));
        Assert.Equal("結月ゆかり＞<unknown>テスト", text);
    }

    [Fact]
    public void ChatContentCannotChangeQueueSettings()
    {
        var queue = new SpeechQueue();
        queue.Submit("先の文章");
        queue.Submit("<clear><pause><interrupt_disable><stop>", false);
        Assert.Equal(2, queue.Status().QueueCount);
        Assert.False(queue.Status().Paused);
        Assert.True(queue.Status().Interrupt);
        Assert.False(queue.ConsumeStopRequest());
        queue.TryTake(false, 0, out _);
        Assert.True(queue.TryTake(false, 200, out var text));
        Assert.Equal("<clear><pause><interrupt_disable><stop>", text);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  \r\n")]
    public void EmptyInputIsRejected(string? text)
    {
        var queue = new SpeechQueue();
        Assert.False(queue.Submit(text).Success);
        Assert.Equal(0, queue.Status().QueueCount);
    }

    [Fact]
    public void OversizedInputIsRejectedBeforeCommandsRun()
    {
        var queue = new SpeechQueue();
        Assert.False(queue.Submit("<pause>" + new string('あ', SpeechQueue.MaximumTextLength)).Success);
        Assert.False(queue.Status().Paused);
    }

    [Fact]
    public void FullQueueRejectsAtomicallyButStillAcceptsControlCommands()
    {
        var queue = new SpeechQueue();
        for (var index = 0; index < SpeechQueue.MaximumQueueLength; index++)
            Assert.True(queue.Submit("文章").Success);
        Assert.False(queue.Submit("<pause>あふれる文章").Success);
        Assert.False(queue.Status().Paused);
        Assert.True(queue.Submit("<pause>").Success);
        Assert.True(queue.Submit("<clear>置き換えた文章").Success);
        Assert.Equal(1, queue.Status().QueueCount);
    }

    [Fact]
    public void StopClearsQueueAndStopsBeforeNextUtterance()
    {
        var queue = new SpeechQueue();
        queue.Submit("古い文章");
        queue.Submit("<stop>新しい文章");
        Assert.False(queue.TryTake(false, 0, out _));
        Assert.True(queue.ConsumeStopRequest());
        Assert.False(queue.ConsumeStopRequest());
        Assert.True(queue.TryTake(false, 0, out var text));
        Assert.Equal("新しい文章", text);
    }

    [Fact]
    public async Task ConcurrentProducersCannotExceedQueueLimit()
    {
        var queue = new SpeechQueue();
        var responses = await Task.WhenAll(Enumerable.Range(0, 512)
            .Select(index => Task.Run(() => queue.Submit(index.ToString()))));
        Assert.Equal(SpeechQueue.MaximumQueueLength, responses.Count(response => response.Success));
        Assert.Equal(SpeechQueue.MaximumQueueLength, queue.Status().QueueCount);
    }
}
