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

    public static async Task RunAsync(ISpeechRecognizer speech, string manifest, string output, string speechModel = SpeechProfiles.WhisperModel)
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
            speechModel, passes = 5, samples, measurements = rows,
            timing = "Preparation includes model verification and loading. Recognition includes decoding and transcript assembly. Cleanup and field delivery are excluded.",
            memory = "Parent RSS after recognition includes the managed process and cached speech model."
        }, options));
        for (var pass = 0; pass < 5; pass++)
        foreach (var sample in samples)
        {
            var pcm = WaveAudio.Read(Path.Combine(directory, sample.Name + ".wav"));
            var engine = speech.Kind;
            var preparation = Stopwatch.StartNew();
            double preparationMs = 0, recognitionMs = 0;
            string? text = null, error = null;
            using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(6));
            try
            {
                await speech.PrepareAsync(speechModel, deadline.Token);
                preparationMs = preparation.Elapsed.TotalMilliseconds;
                var recognition = Stopwatch.StartNew();
                try { text = await speech.TranscribeAsync(pcm, speechModel, sample.Language, deadline.Token); }
                finally { recognitionMs = recognition.Elapsed.TotalMilliseconds; }
            }
            catch (Exception failure) { error = failure.ToString(); }
            rows.Add(new(sample.Name, engine, pass + 1, pcm.Length / 16000d, preparationMs, recognitionMs,
                text, error, Process.GetCurrentProcess().WorkingSet64));
            Save();
            Console.WriteLine($"{sample.Name} {engine} pass {pass + 1}: {recognitionMs:F1} ms; {error ?? text}");
        }
        if (rows.Any(row => row.Error is not null)) throw new InvalidOperationException("Speech evaluation contains failed runs. Inspect " + output);
    }

    public static async Task CheckCancellationAsync(ISpeechRecognizer speech, float[] reference, string speechModel = SpeechProfiles.WhisperModel)
    {
        var engine = speech.Kind;
        await speech.PrepareAsync(speechModel);
        using var cancelled = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        var longRecording = Enumerable.Range(0, Math.Min(20, 300 * 16000 / reference.Length)).SelectMany(_ => reference).ToArray();
        try
        {
            await speech.TranscribeAsync(longRecording, speechModel, "en", cancelled.Token);
            throw new InvalidOperationException(engine + " ignored cancellation.");
        }
        catch (OperationCanceledException) { }
        var recovered = await speech.TranscribeAsync(reference, speechModel, "en", CancellationToken.None);
        if (!recovered.Contains("country", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(engine + " did not recover after cancellation: " + recovered);
    }
}
