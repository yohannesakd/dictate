using Dictate.Speech;
using Dictate.Checks;

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
Console.WriteLine("CPU recognition and cancellation checks passed for Whisper.");
