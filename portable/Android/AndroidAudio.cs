using Android.Media;
using Dictate.Core;

namespace Dictate.Android;

public sealed class AndroidAudio : IAudioCapture
{
    private readonly object sync = new();
    private readonly List<float> samples = [];
    private AudioRecord? record;
    private Task? reader;
    private bool recording;
    public event Action<float>? Level;
    public event Action<string>? Interrupted;

    public void Start()
    {
        var size = Math.Max(4096, AudioRecord.GetMinBufferSize(16000, ChannelIn.Mono, Encoding.Pcm16bit));
        // The Android binding uses ChannelOut for input masks too.
        var input = new AudioRecord.Builder()!.SetAudioSource(AudioSource.VoiceRecognition)!
            .SetAudioFormat(new AudioFormat.Builder()!.SetEncoding(Encoding.Pcm16bit)!.SetSampleRate(16000)!.SetChannelMask((ChannelOut)ChannelIn.Mono)!.Build()!)!
            .SetBufferSizeInBytes(size)!.Build()!;
        if (input.State != State.Initialized) { input.Dispose(); throw new InvalidOperationException("The microphone could not start."); }
        try { input.StartRecording(); }
        catch { input.Release(); input.Dispose(); throw; }
        lock (sync) { samples.Clear(); record = input; recording = true; }
        reader = Task.Run(() =>
        {
            var buffer = new short[1024];
            while (true)
            {
                lock (sync) { if (!recording || !ReferenceEquals(record, input)) return; }
                var count = input.Read(buffer, 0, buffer.Length);
                if (count <= 0)
                {
                    lock (sync) { if (!recording) return; }
                    Interrupted?.Invoke("The microphone stopped.");
                    return;
                }
                var block = buffer.Take(count).Select(sample => sample / 32768f).ToArray();
                lock (sync)
                {
                    if (!recording) return;
                    samples.AddRange(block.Take(Math.Max(0, 300 * 16000 - samples.Count)));
                }
                Level?.Invoke(MathF.Sqrt(block.Sum(sample => sample * sample) / block.Length));
            }
        });
    }

    public float[] Stop()
    {
        AudioRecord? input;
        lock (sync) { recording = false; input = record; record = null; }
        if (input is not null) { input.Stop(); reader?.GetAwaiter().GetResult(); input.Release(); input.Dispose(); }
        lock (sync) return samples.ToArray();
    }
    public void Dispose() => Stop();
}
