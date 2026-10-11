using System.Text.Json;
using Dictate.Core;

namespace Dictate.Speech;

/// <summary>Routes the captured engine choice and owns cached models and native process lifetimes.</summary>
public sealed class SpeechRecognizer : ISpeechEngine, IDisposable
{
    private readonly WhisperEngine whisper;
    private readonly WhistleAssets whistle;
    private readonly IWhistleProcess process;
    private readonly string directory;
    private readonly SemaphoreSlim gate = new(1);
    private readonly CancellationTokenSource lifetime = new();
    private bool disposed;
    public event Action<string>? Status;
    public bool WhistleSupported => whistle.IsSupported;

    public SpeechRecognizer(string directory, IWhistleProcess? process = null, string? bundledWhistle = null)
    {
        this.directory = directory;
        whisper = new(directory);
        whistle = new(Path.Combine(directory, "Whistle"), bundledWhistle);
        this.process = process ?? new WhistleProcess();
        whisper.Status += message => Status?.Invoke(message);
    }

    /// <summary>Prepares the selected engine without changing an active operation's captured choice.</summary>
    public async Task PrepareAsync(SpeechEngineKind engine, string model, CancellationToken cancellation = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellation, lifetime.Token);
        await gate.WaitAsync(linked.Token).ConfigureAwait(false);
        try { await PrepareCore(engine, model, linked.Token).ConfigureAwait(false); }
        finally { gate.Release(); }
    }

    public async Task<string> TranscribeAsync(float[] pcm, SpeechEngineKind engine, string model, string language, CancellationToken cancellation)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (pcm.Length > 300 * 16000) throw new ArgumentException("Record at most five minutes of speech.", nameof(pcm));
        if (engine == SpeechEngineKind.Whistle && !WhistleAssets.Languages.Contains(language))
            throw new ArgumentException("Whistle supports auto, en, de, fr, es, it, nl, and pl.", nameof(language));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellation, lifetime.Token);
        await gate.WaitAsync(linked.Token).ConfigureAwait(false);
        try
        {
            await PrepareCore(engine, model, linked.Token).ConfigureAwait(false);
            if (engine == SpeechEngineKind.Whisper) return await whisper.TranscribeAsync(pcm, model, language, linked.Token).ConfigureAwait(false);
            var temporary = Path.Combine(directory, "dictate-audio-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(temporary);
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(temporary, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            try
            {
                var audio = Path.Combine(temporary, "audio.wav");
                await WaveAudio.WriteAsync(audio, pcm, linked.Token).ConfigureAwait(false);
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(linked.Token);
                deadline.CancelAfter(TimeSpan.FromSeconds(30 + pcm.Length / 16000d * 4));
                string output;
                try { output = await process.RunAsync(whistle.ExecutablePath, whistle.ModelPath, audio, language, deadline.Token).ConfigureAwait(false); }
                catch (OperationCanceledException) when (!linked.IsCancellationRequested) { throw new TimeoutException("Whistle did not finish within its time limit."); }
                linked.Token.ThrowIfCancellationRequested();
                using var document = JsonDocument.Parse(output);
                if (!document.RootElement.TryGetProperty("text", out var text) || text.ValueKind != JsonValueKind.String)
                    throw new InvalidDataException("Whistle returned no transcript field.");
                return text.GetString()!.Trim();
            }
            finally { Directory.Delete(temporary, true); }
        }
        finally { gate.Release(); }
    }

    private async Task PrepareCore(SpeechEngineKind engine, string model, CancellationToken cancellation)
    {
        if (engine == SpeechEngineKind.Whisper) await whisper.PrepareAsync(model, cancellation).ConfigureAwait(false);
        else if (engine == SpeechEngineKind.Whistle)
        {
            await whisper.UnloadAsync(cancellation).ConfigureAwait(false);
            Status?.Invoke("Preparing Whistle. Speech recognition stays on this device.");
            await whistle.PrepareAsync(cancellation).ConfigureAwait(false);
            Status?.Invoke("Whistle is ready.");
        }
        else throw new ArgumentOutOfRangeException(nameof(engine));
    }

    public void Dispose()
    {
        disposed = true;
        lifetime.Cancel();
        gate.Wait();
        try { whisper.Dispose(); }
        finally { gate.Release(); }
        lifetime.Dispose();
    }
}
