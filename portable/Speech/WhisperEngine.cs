using System.Runtime.InteropServices;
using System.Runtime.Intrinsics.X86;
using Dictate.Core;
using Whisper.net;
using Whisper.net.Ggml;
using Whisper.net.LibraryLoader;

namespace Dictate.Speech;

public sealed class WhisperEngine(string directory) : ISpeechEngine, IDisposable
{
    // Older weights remain available to reproduce the recorded quality comparison. Apps select only SpeechProfiles.WhisperModel.
    public static readonly string[] Models = ["tiny", "base", "small", "tiny.en", "base.en", "small.en", SpeechProfiles.WhisperModel];
    private readonly SemaphoreSlim gate = new(1);
    private WhisperFactory? factory;
    private string? loadedModel;
    private readonly CancellationTokenSource lifetime = new();
    private bool disposed;
    public event Action<string>? Status;

    public async Task PrepareAsync(string model, CancellationToken cancellation = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellation, lifetime.Token);
        cancellation = linked.Token;
        await gate.WaitAsync(cancellation).ConfigureAwait(false);
        try { await LoadAsync(model, cancellation).ConfigureAwait(false); }
        finally { gate.Release(); }
    }

    public async Task<string> TranscribeAsync(float[] pcm, string model, string language, CancellationToken cancellation)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (pcm.Length > 300 * 16000) throw new ArgumentException("Record at most five minutes of speech.", nameof(pcm));
        cancellation.ThrowIfCancellationRequested();
        // Exact digital silence must not load a model or produce a hallucinated transcript.
        if (pcm.All(sample => sample == 0)) return "";
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellation, lifetime.Token);
        cancellation = linked.Token;
        await gate.WaitAsync(cancellation).ConfigureAwait(false);
        try
        {
            await LoadAsync(model, cancellation).ConfigureAwait(false);
            var builder = factory!.CreateBuilder().WithThreads(Math.Max(1, Math.Min(4, Environment.ProcessorCount))).WithNoContext();
            if (language == "auto") builder.WithLanguageDetection();
            else builder.WithLanguage(language);
            await using var processor = builder.Build();
            var parts = new List<string>();
            await foreach (var segment in processor.ProcessAsync(pcm, cancellation).ConfigureAwait(false))
            {
                var text = segment.Text.Replace("[BLANK_AUDIO]", "", StringComparison.Ordinal).Trim();
                if (text.Length > 0) parts.Add(text);
            }
            return string.Join(' ', parts);
        }
        finally { gate.Release(); }
    }

    private async Task LoadAsync(string model, CancellationToken cancellation)
    {
        if (!Models.Contains(model)) throw new ArgumentException("Select a supported speech model.", nameof(model));
        if (loadedModel == model) return;
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "ggml-" + model + ".bin");
        if (model == SpeechProfiles.WhisperModel)
        {
            Status?.Invoke("Preparing English Whisper. Speech recognition stays on this device.");
            await AssetDownload.Download($"https://huggingface.co/ggerganov/whisper.cpp/resolve/5359861c739e955e79d9a303bcbc70fb988958b1/ggml-{model}.bin",
                path, "4baf70dd0d7c4247ba2b81fafd9c01005ac77c2f9ef064e00dcf195d0e2fdd2f", 59721011, cancellation).ConfigureAwait(false);
        }
        else if (!File.Exists(path))
        {
            Status?.Invoke("Downloading the speech model. Keep Dictate open.");
            var type = model switch
            {
                "tiny" => GgmlType.Tiny, "base" => GgmlType.Base, "small" => GgmlType.Small,
                "tiny.en" => GgmlType.TinyEn, "base.en" => GgmlType.BaseEn, "small.en" => GgmlType.SmallEn,
                _ => throw new ArgumentOutOfRangeException(nameof(model))
            };
            var staging = path + ".download";
            try
            {
                await using (var source = await WhisperGgmlDownloader.Default.GetGgmlModelAsync(type, cancellationToken: cancellation).ConfigureAwait(false))
                await using (var output = File.Create(staging)) await source.CopyToAsync(output, cancellation).ConfigureAwait(false);
                cancellation.ThrowIfCancellationRequested();
                File.Move(staging, path, true);
            }
            finally { if (File.Exists(staging)) File.Delete(staging); }
        }
        Status?.Invoke("Loading the speech model.");
        var x86 = RuntimeInformation.ProcessArchitecture is Architecture.X64 or Architecture.X86;
        var vectorCpu = Avx.IsSupported && Avx2.IsSupported && Fma.IsSupported && (X86Base.CpuId(1, 0).Ecx & (1 << 29)) != 0;
        RuntimeOptions.RuntimeLibraryOrder = x86 && !OperatingSystem.IsAndroid() && !vectorCpu ? [RuntimeLibrary.CpuNoAvx] : [RuntimeLibrary.Cpu];
        var next = await Task.Run(() => WhisperFactory.FromPath(path, new WhisperFactoryOptions { UseGpu = false }), cancellation).ConfigureAwait(false);
        if (cancellation.IsCancellationRequested) { next.Dispose(); cancellation.ThrowIfCancellationRequested(); }
        factory?.Dispose();
        factory = next;
        loadedModel = model;
        Status?.Invoke("Ready. Speech recognition stays on this device.");
    }

    public void Dispose()
    {
        disposed = true;
        lifetime.Cancel();
        gate.Wait();
        try { factory?.Dispose(); }
        finally { gate.Release(); }
        lifetime.Dispose();
    }
}
