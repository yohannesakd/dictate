using Dictate.Core;
using Xunit;

namespace Dictate.Tests;

public class ControllerChecks
{
    private sealed class Audio : IAudioCapture
    {
        public event Action<float>? Level { add { } remove { } }
        public event Action<string>? Interrupted { add { } remove { } }
        public void Start() { }
        public float[] Stop() => new float[16000];
        public void Dispose() { }
    }
    private sealed class Speech : ISpeechEngine
    {
        public readonly Queue<TaskCompletionSource<string>> Requests = new();
        public (string Model, string Language)? Selection;
        public Task<string> TranscribeAsync(float[] pcm, string model, string language, CancellationToken cancellation)
        {
            Selection = (model, language);
            var result = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            Requests.Enqueue(result);
            return result.Task;
        }
    }
    private sealed class Delivery : ITextDelivery, ITextTarget
    {
        public readonly List<string> Inserted = [];
        public string? Application => "test-field";
        public ITextTarget CaptureTarget() => this;
        public Task<DeliveryReceipt> InsertAsync(string text, string raw, CancellationToken cancellation)
        {
            cancellation.ThrowIfCancellationRequested();
            Inserted.Add(text);
            return Task.FromResult(new DeliveryReceipt(DeliveryOutcome.Sent, text.Length)
                { Evidence = new() { ["verification_mismatch"] = "selection" } });
        }
    }
    private sealed class Store : IDictationStore
    {
        public readonly List<TranscriptRecord> Records = [];
        public Task AppendAsync(TranscriptRecord record) { Records.Add(record); return Task.CompletedTask; }
    }
    private sealed class Client : ICleanupClient
    {
        public Task<string> CompleteAsync(CleanupProvider provider, string instructions, string message, CancellationToken cancellation) => Task.FromResult("unused");
    }

    [Fact]
    public async Task CancelledRecognitionCannotInsertOrResetNewRecording()
    {
        var speech = new Speech();
        var delivery = new Delivery();
        var store = new Store();
        using var controller = new DictationController(new Audio(), speech, delivery, new(new Client()), store);
        controller.Start(new() { SmartCleanup = false });
        var old = controller.FinishAsync();
        var oldReply = speech.Requests.Dequeue();
        controller.Cancel();
        controller.Start(new() { SmartCleanup = false });
        oldReply.SetResult("stale text");
        await old;
        Assert.Equal(DictationPhase.Listening, controller.Phase);
        Assert.Empty(delivery.Inserted);
        var current = controller.FinishAsync();
        speech.Requests.Dequeue().SetResult("Um, I I need help.");
        await current;
        Assert.Equal(["I need help."], delivery.Inserted);
        Assert.Equal(DeliveryOutcome.Sent, Assert.Single(store.Records).Delivery);
        Assert.Equal("selection", store.Records[0].DeliveryEvidence["verification_mismatch"]);
        Assert.Equal(DictationPhase.Idle, controller.Phase);
    }

    [Fact]
    public async Task SettingsAreCapturedBeforeRecognitionCompletes()
    {
        var speech = new Speech();
        var delivery = new Delivery();
        var store = new Store();
        using var controller = new DictationController(new Audio(), speech, delivery, new(new Client()), store);
        var entries = new[] { new DictionaryEntry("soul", "Sol") };
        controller.Start(new() { SmartCleanup = false, Dictionary = entries, SpeechEngine = SpeechEngineKind.Whisper, SpeechModel = "base" });
        var finished = controller.FinishAsync();
        entries[0] = new("soul", "Wrong");
        speech.Requests.Dequeue().SetResult("Ask soul.");
        await finished;
        Assert.Equal(["Ask Sol."], delivery.Inserted);
        Assert.Equal(("base", "en"), speech.Selection);
        Assert.Equal(SpeechEngineKind.Whisper, Assert.Single(store.Records).SpeechEngine);
        Assert.Equal("base", store.Records[0].SpeechModel);
    }
}
