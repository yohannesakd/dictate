using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using Dictate.Core;
using Dictate.Speech;

namespace Dictate.Checks;

/// <summary>Runs identical, checksum verified recordings through the app's recognizer.</summary>
public static class SpeechEvaluation
{
    public sealed record Sample(string Name, string Kind, string Sha256, string Reference, string Language = "en");
    public sealed record Measurement(string Sample, SpeechEngineKind Engine, int Pass, double AudioSeconds,
        double PreparationMs, double RecognitionMs, string? Text, string? Error, long ParentRssBytes);

    public static async Task RunAsync(SpeechRecognizer speech, string manifest, string output, string whisperModel = SpeechProfiles.WhisperModel)
    {
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true, WriteIndented = true };
        var samples = JsonSerializer.Deserialize<Sample[]>(File.ReadAllText(manifest), options)!;
        var rows = new List<Measurement>();
        var directory = Path.GetDirectoryName(Path.GetFullPath(manifest))!;
        foreach (var sample in samples)
        {
            var path = Path.Combine(directory, sample.Name + ".wav");
            using var file = File.OpenRead(path);
            if (!Convert.ToHexString(SHA256.HashData(file)).Equals(sample.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Fixture checksum differs: " + sample.Name);
        }
        void Save() => File.WriteAllText(output, JsonSerializer.Serialize(new
        {
            host = Environment.MachineName, platform = RuntimeInformation.OSDescription,
            architecture = RuntimeInformation.ProcessArchitecture.ToString(), logicalCores = Environment.ProcessorCount,
            whisperModel, passes = 5, samples, measurements = rows,
            timing = "Preparation includes model verification/loading. Recognition includes app WAV/process work; cleanup and field delivery are excluded.",
            memory = "Parent RSS after completion excludes Whistle's exited child. Do not use this value to compare total engine memory."
        }, options));
        for (var pass = 0; pass < 5; pass++)
        foreach (var sample in samples)
        {
            var pcm = WaveAudio.Read(Path.Combine(directory, sample.Name + ".wav"));
            var order = pass % 2 == 0 ? new[] { SpeechEngineKind.Whistle, SpeechEngineKind.Whisper } : [SpeechEngineKind.Whisper, SpeechEngineKind.Whistle];
            foreach (var engine in order)
            {
                var preparation = Stopwatch.StartNew();
                double preparationMs = 0, recognitionMs = 0;
                string? text = null, error = null;
                using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(6));
                try
                {
                    await speech.PrepareAsync(engine, whisperModel, deadline.Token);
                    preparationMs = preparation.Elapsed.TotalMilliseconds;
                    var recognition = Stopwatch.StartNew();
                    try { text = await speech.TranscribeAsync(pcm, engine, whisperModel, sample.Language, deadline.Token); }
                    finally { recognitionMs = recognition.Elapsed.TotalMilliseconds; }
                }
                catch (Exception failure) { error = failure.ToString(); }
                rows.Add(new(sample.Name, engine, pass + 1, pcm.Length / 16000d, preparationMs, recognitionMs,
                    text, error, Process.GetCurrentProcess().WorkingSet64));
                Save();
                Console.WriteLine($"{sample.Name} {engine} pass {pass + 1}: {recognitionMs:F1} ms; {error ?? text}");
            }
        }
        if (rows.Any(row => row.Error is not null)) throw new InvalidOperationException("Speech evaluation contains failed runs. Inspect " + output);
    }

    public static async Task CheckCancellationAsync(SpeechRecognizer speech, float[] reference, string whisperModel = SpeechProfiles.WhisperModel)
    {
        foreach (var engine in Enum.GetValues<SpeechEngineKind>())
        {
            await speech.PrepareAsync(engine, whisperModel);
            using var cancelled = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
            var longRecording = Enumerable.Range(0, Math.Min(20, 300 * 16000 / reference.Length)).SelectMany(_ => reference).ToArray();
            try
            {
                await speech.TranscribeAsync(longRecording, engine, whisperModel, "en", cancelled.Token);
                throw new InvalidOperationException(engine + " ignored cancellation.");
            }
            catch (OperationCanceledException) { }
            var recovered = await speech.TranscribeAsync(reference, engine, whisperModel, "en", CancellationToken.None);
            if (!recovered.Contains("country", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(engine + " did not recover after cancellation: " + recovered);
        }
    }
}
