using Dictate.Core;
using Dictate.Speech;

namespace Dictate.Desktop;

/// <summary>Runs the original Phonon-2 model through the Mac Core ML adapter.</summary>
public sealed class PhononEngine : ISpeechRecognizer
{
    private readonly SemaphoreSlim gate = new(1);
    private readonly CancellationTokenSource lifetime = new();
    private bool disposed;
    private readonly string modelDirectory = Path.Combine(AppContext.BaseDirectory,
        Path.GetFileName(Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory)) == "MacOS" ? "../Resources/Phonon-2-CoreML" : "Phonon-2-CoreML");
    public event Action<string>? Status;
    public SpeechEngineKind Kind => SpeechEngineKind.Phonon;
    public bool ModelReady => File.Exists(Path.Combine(modelDirectory, "manifest.json"));
    public string? ActiveHardware { get; private set; }
    public string? FallbackReason { get; private set; }
    public SpeechComputeMode? LoadedCompute { get; private set; }

    public Task PrepareAsync(string model, CancellationToken cancellation = default,
        SpeechComputeMode compute = SpeechComputeMode.Automatic) => RunAsync(null, model, compute, cancellation);

    public Task<string> TranscribeAsync(float[] pcm, string model, string language, CancellationToken cancellation,
        SpeechComputeMode compute = SpeechComputeMode.Automatic)
    {
        if (pcm.Length > 300 * 16000) throw new ArgumentException("Record at most five minutes of speech.", nameof(pcm));
        cancellation.ThrowIfCancellationRequested();
        if (pcm.All(sample => sample == 0)) return Task.FromResult("");
        return RunAsync(pcm, model, compute, cancellation);
    }

    private async Task<string> RunAsync(float[]? pcm, string model, SpeechComputeMode compute, CancellationToken cancellation)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (model != SpeechProfiles.PhononModel) throw new ArgumentException("Select the Phonon-2 speech model.", nameof(model));
        if (!Enum.IsDefined(compute)) throw new ArgumentException("Select a supported hardware mode.", nameof(compute));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellation, lifetime.Token);
        cancellation = linked.Token;
        await gate.WaitAsync(cancellation).ConfigureAwait(false);
        try
        {
            Status?.Invoke("Preparing Phonon-2. Speech recognition stays on this Mac.");
            var result = await MacNative.InvokeAsync((operation, callback, context) =>
                MacNative.Phonon(modelDirectory, pcm ?? [], pcm?.Length ?? 0, compute == SpeechComputeMode.CpuOnly ? 1 : 0,
                    operation, callback, context), cancellation).ConfigureAwait(false);
            cancellation.ThrowIfCancellationRequested();
            if (result.TryGetProperty("error", out var error)) throw new InvalidOperationException(error.GetString());
            ActiveHardware = result.GetProperty("hardware").GetString();
            FallbackReason = result.TryGetProperty("fallback", out var fallback) ? fallback.GetString() : null;
            LoadedCompute = compute;
            Status?.Invoke("Ready. Speech recognition stays on this Mac.");
            return result.TryGetProperty("text", out var text) ? text.GetString()! : "";
        }
        finally { gate.Release(); }
    }

    public void Dispose()
    {
        disposed = true;
        lifetime.Cancel();
        gate.Wait();
        try { MacNative.PhononUnload(); }
        finally { gate.Release(); }
        lifetime.Dispose();
    }
}
