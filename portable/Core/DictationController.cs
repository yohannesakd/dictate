using System.Diagnostics;

namespace Dictate.Core;

public sealed class DictationController(IAudioCapture recorder, ISpeechEngine speech, ITextDelivery delivery,
    SmartCleanup cleanup, IDictationStore store) : IDisposable
{
    private sealed class Operation(DictationSettings settings)
    {
        public Guid Id { get; } = Guid.NewGuid();
        public DictationSettings Settings { get; } = settings with { Dictionary = settings.Dictionary.ToArray() };
        public CancellationTokenSource Cancellation { get; } = new();
        public long Started { get; } = Stopwatch.GetTimestamp();
        public Timer? AutoStop { get; set; }
    }

    private readonly object sync = new();
    private Operation? operation;
    private DictationPhase phase;
    private bool disposed;
    private (ITextCorrection Correction, string Raw, DateTimeOffset Created)? correction;
    public event Action<DictationPhase>? PhaseChanged;
    public event Action<string>? Notice;
    public event Action<TranscriptRecord>? Completed;
    public DictationPhase Phase { get { lock (sync) return phase; } }

    public void Start(DictationSettings settings)
    {
        lock (sync)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (phase != DictationPhase.Idle) return;
            correction = null;
            var next = new Operation(settings);
            try { recorder.Start(); }
            catch { next.Cancellation.Dispose(); throw; }
            operation = next;
            phase = DictationPhase.Listening;
            next.AutoStop = new Timer(_ => _ = FinishAsync(next), null, TimeSpan.FromMinutes(5), Timeout.InfiniteTimeSpan);
        }
        PhaseChanged?.Invoke(DictationPhase.Listening);
    }

    public Task FinishAsync() => FinishAsync(null);

    private async Task FinishAsync(Operation? expected)
    {
        Operation current;
        float[] pcm = [];
        ITextTarget? target = null;
        Exception? captureError = null;
        lock (sync)
        {
            if (phase != DictationPhase.Listening || operation is null) return;
            if (expected is not null && !ReferenceEquals(operation, expected)) return;
            current = operation;
            current.AutoStop?.Dispose();
            current.AutoStop = null;
            try { pcm = recorder.Stop(); target = delivery.CaptureTarget(); }
            catch (Exception error)
            {
                operation = null;
                phase = DictationPhase.Idle;
                current.Cancellation.Dispose();
                captureError = error;
            }
            if (captureError is null) phase = DictationPhase.Processing;
        }
        if (captureError is not null)
        {
            Notice?.Invoke(captureError.Message);
            PhaseChanged?.Invoke(DictationPhase.Idle);
            return;
        }
        PhaseChanged?.Invoke(DictationPhase.Processing);
        var release = Stopwatch.GetTimestamp();
        var cancellation = current.Cancellation.Token;
        try
        {
            if (pcm.Length < 8000) { Notice?.Invoke("No speech detected."); return; }
            var raw = (await speech.TranscribeAsync(pcm, current.Settings.SpeechModel, current.Settings.Language,
                cancellation, current.Settings.SpeechCompute).ConfigureAwait(false)).Trim();
            cancellation.ThrowIfCancellationRequested();
            var transcriptAt = Stopwatch.GetTimestamp();
            var settings = current.Settings;
            var ruled = new PersonalDictionary(settings.Dictionary).Apply(TextCleanup.Clean(raw, settings.Rules));
            var smart = settings.SmartCleanup && raw.Length > 0
                ? await cleanup.CleanAsync(raw, settings.Dictionary, settings.Provider, cancellation).ConfigureAwait(false)
                : new CleanupResult(null, null, "off");
            cancellation.ThrowIfCancellationRequested();
            var text = smart.Text ?? ruled;
            var ready = Stopwatch.GetTimestamp();
            lock (sync)
            {
                if (!ReferenceEquals(operation, current)) return;
                phase = DictationPhase.Delivering;
            }
            PhaseChanged?.Invoke(DictationPhase.Delivering);
            var receipt = text.Length == 0 ? new DeliveryReceipt(DeliveryOutcome.Failed, Error: "No speech detected.")
                : await target!.InsertAsync(text, raw, cancellation).ConfigureAwait(false);
            var delivered = Stopwatch.GetTimestamp();
            lock (sync)
            {
                if (ReferenceEquals(operation, current) && receipt.Outcome == DeliveryOutcome.Verified && receipt.Correction is not null)
                    correction = (receipt.Correction, raw, DateTimeOffset.UtcNow);
            }
            var record = new TranscriptRecord
            {
                Id = current.Id, Raw = raw, Rules = ruled, Text = text, Application = target!.Application,
                Smart = smart.Outcome, ModelReply = smart.Reply, Provider = settings.Provider.ToString(),
                Model = settings.SmartCleanup ? CleanupPrompt.Model(settings.Provider) : null,
                Prompt = settings.SmartCleanup ? CleanupPrompt.Version : null, Note = receipt.Error ?? smart.Error,
                SpeechEngine = SpeechEngineKind.Whisper, SpeechModel = settings.SpeechModel,
                AudioSeconds = pcm.Length / 16000d, Delivery = receipt.Outcome, PostedUtf16Units = receipt.PostedUtf16Units, DeliveryEvidence = receipt.Evidence,
                Metrics = new()
                {
                    ["recording_ms"] = Stopwatch.GetElapsedTime(current.Started, release).TotalMilliseconds,
                    ["inference_ms"] = Stopwatch.GetElapsedTime(release, transcriptAt).TotalMilliseconds,
                    ["cleanup_ms"] = Stopwatch.GetElapsedTime(transcriptAt, ready).TotalMilliseconds,
                    ["delivery_ms"] = Stopwatch.GetElapsedTime(ready, delivered).TotalMilliseconds,
                    ["release_to_text_ms"] = Stopwatch.GetElapsedTime(release, delivered).TotalMilliseconds
                }
            };
            // Save the observed receipt even when cancellation happens after some text was posted.
            await store.AppendAsync(record).ConfigureAwait(false);
            Completed?.Invoke(record);
            if (receipt.Error is not null) Notice?.Invoke(receipt.Error);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception error)
        {
            lock (sync) { if (!ReferenceEquals(operation, current)) return; }
            Notice?.Invoke(error.Message);
        }
        finally
        {
            var reset = false;
            lock (sync)
            {
                if (ReferenceEquals(operation, current)) { operation = null; phase = DictationPhase.Idle; reset = true; }
            }
            current.Cancellation.Dispose();
            if (reset) PhaseChanged?.Invoke(DictationPhase.Idle);
        }
    }

    public void Cancel()
    {
        Exception? stopError = null;
        lock (sync)
        {
            var previous = operation;
            if (previous is null) return;
            previous.Cancellation.Cancel();
            previous.AutoStop?.Dispose();
            if (phase == DictationPhase.Listening)
            {
                try { recorder.Stop(); }
                catch (Exception error) { stopError = error; }
                finally { previous.Cancellation.Dispose(); }
            }
            operation = null;
            phase = DictationPhase.Idle;
            correction = null;
        }
        PhaseChanged?.Invoke(DictationPhase.Idle);
        if (stopError is not null) Notice?.Invoke(stopError.Message);
    }

    public async Task<bool> CorrectLastAsync(bool restoreRaw)
    {
        (ITextCorrection Correction, string Raw, DateTimeOffset Created) saved;
        Operation current;
        lock (sync)
        {
            if (phase != DictationPhase.Idle || correction is not { } value || DateTimeOffset.UtcNow - value.Created > TimeSpan.FromMinutes(5)) return false;
            saved = value;
            correction = null;
            operation = current = new Operation(new());
            phase = DictationPhase.Delivering;
        }
        PhaseChanged?.Invoke(DictationPhase.Delivering);
        try { return await saved.Correction.ReplaceAsync(restoreRaw ? saved.Raw : "", current.Cancellation.Token).ConfigureAwait(false); }
        finally { EndDelivery(current); }
    }

    public async Task<DeliveryReceipt> RepeatAsync(string text, string raw)
    {
        Operation current;
        ITextTarget target;
        lock (sync)
        {
            if (phase != DictationPhase.Idle) return new(DeliveryOutcome.Failed, Error: "Finish or cancel the current dictation first.");
            target = delivery.CaptureTarget();
            operation = current = new Operation(new());
            correction = null;
            phase = DictationPhase.Delivering;
        }
        PhaseChanged?.Invoke(DictationPhase.Delivering);
        try
        {
            var receipt = await target.InsertAsync(text, raw, current.Cancellation.Token).ConfigureAwait(false);
            lock (sync)
            {
                if (ReferenceEquals(operation, current) && receipt.Outcome == DeliveryOutcome.Verified && receipt.Correction is not null)
                    correction = (receipt.Correction, raw, DateTimeOffset.UtcNow);
            }
            return receipt;
        }
        finally { EndDelivery(current); }
    }

    private void EndDelivery(Operation current)
    {
        var reset = false;
        lock (sync)
        {
            if (ReferenceEquals(operation, current)) { operation = null; phase = DictationPhase.Idle; reset = true; }
        }
        current.Cancellation.Dispose();
        if (reset) PhaseChanged?.Invoke(DictationPhase.Idle);
    }

    public void Dispose()
    {
        Cancel();
        disposed = true;
        recorder.Dispose();
        cleanup.Dispose();
    }
}
