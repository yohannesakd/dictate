using Dictate.Core;
using Xunit;

namespace Dictate.Tests;

public class DeliveryChecks
{
    private sealed class Audio : IAudioCapture
    {
        public event Action<float>? Level { add { } remove { } }
        public event Action<string>? Interrupted { add { } remove { } }
        public void Start() { }
        public float[] Stop() => [];
        public void Dispose() { }
    }
    private sealed class Speech : ISpeechEngine
    {
        public Task<string> TranscribeAsync(float[] pcm, SpeechEngineKind engine, string model, string language, CancellationToken cancellation) => Task.FromResult("");
    }
    private sealed class Store : IDictationStore
    {
        public Task AppendAsync(TranscriptRecord record) => Task.CompletedTask;
    }
    private sealed class Client : ICleanupClient
    {
        public Task<string> CompleteAsync(CleanupProvider provider, string instructions, string message, CancellationToken cancellation) => Task.FromResult("");
    }
    private sealed class Delivery : ITextDelivery, ITextTarget, ITextCorrection
    {
        public string? Application => "field";
        public CancellationToken LastCancellation;
        public TaskCompletionSource<bool> Correction { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Insertions;
        public ITextTarget CaptureTarget() => this;
        public Task<DeliveryReceipt> InsertAsync(string text, string raw, CancellationToken cancellation)
        {
            Insertions++;
            return Task.FromResult(new DeliveryReceipt(DeliveryOutcome.Verified, text.Length, this));
        }
        public Task<bool> ReplaceAsync(string text, CancellationToken cancellation) { LastCancellation = cancellation; return Correction.Task; }
    }

    [Fact]
    public async Task CorrectionCannotOverlapRecordingAndLateCorrectionCannotResetNewRecording()
    {
        var delivery = new Delivery();
        using var controller = new DictationController(new Audio(), new Speech(), delivery, new(new Client()), new Store());
        await controller.RepeatAsync("Clean text", "raw text");
        var correction = controller.CorrectLastAsync(false);
        controller.Start(new());
        Assert.Equal(DictationPhase.Delivering, controller.Phase);
        controller.Cancel();
        Assert.True(delivery.LastCancellation.IsCancellationRequested);
        controller.Start(new());
        delivery.Correction.SetResult(false);
        Assert.False(await correction);
        Assert.Equal(DictationPhase.Listening, controller.Phase);
        var refused = await controller.RepeatAsync("again", "again");
        Assert.Equal(DeliveryOutcome.Failed, refused.Outcome);
        Assert.Equal(1, delivery.Insertions);
    }

    [Fact]
    public void UsageIncludesFullWeekAndOnlyVerifiedLatency()
    {
        var now = DateTimeOffset.Now;
        TranscriptRecord Record(DateTimeOffset date, DeliveryOutcome outcome, int latency) => new()
        {
            Date = date, Raw = "two words", Rules = "two words", Text = "two words", Smart = "off", AudioSeconds = 1,
            Delivery = outcome, Metrics = new() { ["release_to_text_ms"] = latency }
        };
        var records = Enumerable.Range(0, 100).Select(i => Record(now, i < 60 ? DeliveryOutcome.Verified : DeliveryOutcome.Sent, i)).ToArray();
        var usage = Usage.From(records.Append(Record(now.AddDays(-8), DeliveryOutcome.Sent, 1000)), now);
        Assert.Equal(200, usage.Week[^1].Words);
        Assert.Equal(100, usage.Week[^1].Dictations);
        Assert.Equal(35, usage.MedianLatency);
        Assert.Equal(120, usage.WordsPerMinute);
        Assert.Null(Usage.From(records.Take(1), now).WordsPerMinute);
    }
}
