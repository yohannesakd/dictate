using Dictate.Speech;
using Dictate.Checks;
using Dictate.Core;
using System.Diagnostics;
using System.Text.Json;
using Whisper.net.LibraryLoader;
using Whisper.net.Logger;

if (args is ["--hardware", var audio, var directory, var report])
{
    var hardwarePcm = WaveAudio.Read(audio);
    using var speech = new WhisperEngine(directory);
    var results = new List<object>();
    foreach (var mode in new[] { SpeechComputeMode.CpuOnly, SpeechComputeMode.Automatic, SpeechComputeMode.CpuOnly, SpeechComputeMode.Automatic })
    {
        var logs = new List<string>();
        using var logger = LogProvider.AddLogger((_, message) => { if (message is not null) lock (logs) logs.Add(message); });
        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        var timer = Stopwatch.StartNew();
        await speech.PrepareAsync(SpeechProfiles.WhisperModel, deadline.Token, mode);
        var preparationMs = timer.Elapsed.TotalMilliseconds;
        var timings = new List<double>();
        string? text = null;
        for (var pass = 0; pass < 3; pass++)
        {
            timer.Restart();
            text = await speech.TranscribeAsync(hardwarePcm, SpeechProfiles.WhisperModel, "en", deadline.Token, mode);
            timings.Add(timer.Elapsed.TotalMilliseconds);
            if (!text.Contains("country", StringComparison.OrdinalIgnoreCase) || !text.Contains("fellow", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Unexpected transcript: " + text);
        }
        if (mode == SpeechComputeMode.CpuOnly && (speech.ActiveHardware != "CPU" || logs.Any(line => line.Contains("whisper_backend_init_gpu: using ", StringComparison.Ordinal)
            || line.Contains("Core ML model loaded", StringComparison.Ordinal))))
            throw new InvalidOperationException("CPU-only mode used an accelerator.");
        if (logs.Any(line => line.Contains("whisper_backend_init_gpu: using ", StringComparison.Ordinal)) && speech.ActiveHardware == "CPU")
            throw new InvalidOperationException("An active GPU was reported as CPU.");
        if (!logs.Any(line => line.Contains("use gpu    = " + (mode == SpeechComputeMode.CpuOnly ? "0" : "1"), StringComparison.Ordinal)))
            throw new InvalidOperationException("Missing native context evidence for " + mode);
        results.Add(new { mode, speech.ActiveHardware, speech.FallbackReason, runtime = RuntimeOptions.LoadedLibrary,
            preparationMs, recognitionMs = timings, text, nativeContext = logs.Where(line => line.Contains("use gpu", StringComparison.Ordinal)
                || line.Contains("whisper_backend_init_gpu:", StringComparison.Ordinal) || line.Contains("Core ML", StringComparison.Ordinal)).ToArray() });
    }
    await SpeechEvaluation.CheckCancellationAsync(speech, hardwarePcm);
    File.WriteAllText(report, JsonSerializer.Serialize(new { results, cancellation = "passed" }, new JsonSerializerOptions { WriteIndented = true }));
    Console.WriteLine("Hardware switching, CPU isolation, recognition, and cancellation passed.");
    return;
}

if (args is ["--neural-fallback", var reference, var modelDirectory, var evidence])
{
    using var speech = new WhisperEngine(modelDirectory);
    var samples = WaveAudio.Read(reference);
    await speech.PrepareAsync(SpeechProfiles.WhisperModel);
    var encoder = Path.Combine(modelDirectory, "ggml-base.en-encoder.mlmodelc");
    var hidden = encoder + ".check";
    var rows = new List<object>();
    Directory.Move(encoder, hidden);
    try
    {
        var text = await speech.TranscribeAsync(samples, SpeechProfiles.WhisperModel, "en", CancellationToken.None);
        if (speech.ActiveHardware != "Metal GPU" || !text.Contains("country", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Missing encoder did not fall back to Metal.");
        rows.Add(new { stage = "missing_encoder", speech.ActiveHardware, speech.FallbackReason, text });
    }
    finally { Directory.Move(hidden, encoder); }
    var recovered = await speech.TranscribeAsync(samples, SpeechProfiles.WhisperModel, "en", CancellationToken.None);
    if (speech.ActiveHardware?.StartsWith("Neural Engine", StringComparison.Ordinal) != true || !recovered.Contains("country", StringComparison.OrdinalIgnoreCase))
        throw new InvalidOperationException("Restored encoder did not recover Neural Engine preference.");
    rows.Add(new { stage = "restored_encoder", speech.ActiveHardware, speech.FallbackReason, text = recovered });
    File.WriteAllText(evidence, JsonSerializer.Serialize(rows, new JsonSerializerOptions { WriteIndented = true }));
    Console.WriteLine("Missing encoder fallback and Neural Engine recovery passed.");
    return;
}

if (args is ["--evaluate", var manifest, var models, var output])
{
    using var speech = new WhisperEngine(models);
    await SpeechEvaluation.RunAsync(speech, manifest, output);
    await SpeechEvaluation.CheckCancellationAsync(speech, WaveAudio.Read(Path.Combine(Path.GetDirectoryName(manifest)!, "jfk.wav")));
    Console.WriteLine("Whisper recovered after cancellation.");
    return;
}
if (args.Length != 2) throw new ArgumentException("Supply a 16 kHz mono PCM16 WAV file and a model directory.");
var pcm = WaveAudio.Read(args[0]);
using var engine = new WhisperEngine(args[1]);
await SpeechEvaluation.CheckCancellationAsync(engine, pcm);
Console.WriteLine($"Whisper recognition and cancellation checks passed. Using {engine.ActiveHardware}.");
