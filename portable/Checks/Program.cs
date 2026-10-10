using Dictate.Speech;

if (args.Length != 2) throw new ArgumentException("Supply a 16 kHz mono PCM16 WAV file and a model directory.");
using var file = File.OpenRead(args[0]);
using var reader = new BinaryReader(file);
if (new string(reader.ReadChars(4)) != "RIFF") throw new InvalidDataException("Expected RIFF audio.");
reader.ReadInt32();
if (new string(reader.ReadChars(4)) != "WAVE") throw new InvalidDataException("Expected WAVE audio.");
float[] pcm = [];
while (file.Position + 8 <= file.Length)
{
    var chunk = new string(reader.ReadChars(4));
    var length = reader.ReadInt32();
    var end = file.Position + length;
    if (chunk == "fmt ")
    {
        if (reader.ReadInt16() != 1 || reader.ReadInt16() != 1 || reader.ReadInt32() != 16000) throw new InvalidDataException("Expected mono PCM at 16 kHz.");
        reader.ReadInt32(); reader.ReadInt16();
        if (reader.ReadInt16() != 16) throw new InvalidDataException("Expected 16-bit audio.");
    }
    else if (chunk == "data")
    {
        pcm = new float[length / 2];
        for (var i = 0; i < pcm.Length; i++) pcm[i] = reader.ReadInt16() / 32768f;
    }
    file.Position = end + (length % 2);
}
using var engine = new WhisperEngine(args[1]);
engine.Status += Console.WriteLine;
var transcript = await engine.TranscribeAsync(pcm, "tiny.en", "en", CancellationToken.None);
Console.WriteLine(transcript);
if (!transcript.Contains("country", StringComparison.OrdinalIgnoreCase) || !transcript.Contains("fellow", StringComparison.OrdinalIgnoreCase))
    throw new InvalidOperationException("The CPU speech engine did not recognize the reference recording.");
if (await engine.TranscribeAsync(new float[32000], "tiny.en", "en", CancellationToken.None) != "")
    throw new InvalidOperationException("Silent audio produced dictation text.");
using var cancelled = new CancellationTokenSource();
cancelled.CancelAfter(TimeSpan.FromMilliseconds(100));
var longRecording = Enumerable.Range(0, 20).SelectMany(_ => pcm).ToArray();
try { await engine.TranscribeAsync(longRecording, "tiny.en", "en", cancelled.Token); throw new InvalidOperationException("Recognition ignored cancellation."); }
catch (OperationCanceledException) { }
if (await engine.TranscribeAsync(pcm, "tiny.en", "en", CancellationToken.None) != transcript)
    throw new InvalidOperationException("Recognition changed after cancellation.");
Console.WriteLine("CPU recognition and cancellation checks passed.");
