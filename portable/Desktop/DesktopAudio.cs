using System.Diagnostics;
using System.Runtime.Versioning;
using Dictate.Core;
using NAudio.Wave;

namespace Dictate.Desktop;

public sealed class DesktopAudio : IAudioCapture
{
    private readonly object sync = new();
    private readonly List<float> samples = [];
    private WaveInEvent? windows;
    private Process? linux;
    private Task? reader;
    private bool recording;
    public event Action<float>? Level;
    public event Action<string>? Interrupted;

    public void Start()
    {
        lock (sync)
        {
            samples.Clear();
            recording = true;
            try
            {
                if (OperatingSystem.IsWindows()) StartWindows();
                else
                {
                    linux = ProcessCommand.Start("arecord", "-q", "-D", "default", "-t", "raw", "-f", "S16_LE", "-r", "16000", "-c", "1");
                    var process = linux;
                    reader = Task.Run(async () =>
                    {
                        var error = process.StandardError.ReadToEndAsync();
                        var buffer = new byte[2048];
                        var carry = -1;
                        while (await process.StandardOutput.BaseStream.ReadAsync(buffer).ConfigureAwait(false) is var count && count > 0)
                        {
                            var block = new List<float>();
                            var index = 0;
                            if (carry >= 0) { block.Add((short)(carry | (buffer[0] << 8)) / 32768f); index = 1; carry = -1; }
                            while (index + 1 < count) { block.Add((short)(buffer[index] | (buffer[index + 1] << 8)) / 32768f); index += 2; }
                            if (index < count) carry = buffer[index];
                            Append(block.ToArray());
                        }
                        lock (sync) { if (!ReferenceEquals(linux, process) || !recording) return; }
                        Interrupted?.Invoke("The microphone stopped. " + (await error.ConfigureAwait(false)).Trim());
                    });
                }
            }
            catch { recording = false; throw; }
        }
    }

    [SupportedOSPlatform("windows")]
    private void StartWindows()
    {
        var input = new WaveInEvent { WaveFormat = new WaveFormat(16000, 16, 1), BufferMilliseconds = 64 };
        input.DataAvailable += (_, args) =>
        {
            var block = new float[args.BytesRecorded / 2];
            for (var i = 0; i < block.Length; i++) block[i] = BitConverter.ToInt16(args.Buffer, i * 2) / 32768f;
            Append(block);
        };
        input.RecordingStopped += (_, args) =>
        {
            lock (sync) { if (!recording || !ReferenceEquals(windows, input)) return; }
            Interrupted?.Invoke(args.Exception?.Message ?? "The microphone stopped.");
        };
        windows = input;
        input.StartRecording();
    }

    private void Append(float[] block)
    {
        lock (sync)
        {
            if (!recording) return;
            samples.AddRange(block.Take(Math.Max(0, 300 * 16000 - samples.Count)));
        }
        Level?.Invoke(block.Length == 0 ? 0 : MathF.Sqrt(block.Sum(sample => sample * sample) / block.Length));
    }

    public float[] Stop()
    {
        WaveInEvent? input;
        Process? process;
        lock (sync)
        {
            recording = false;
            input = windows; windows = null;
            process = linux; linux = null;
        }
        if (OperatingSystem.IsWindows()) { input?.StopRecording(); input?.Dispose(); }
        if (process is not null) { if (!process.HasExited) process.Kill(); process.WaitForExit(); reader?.GetAwaiter().GetResult(); process.Dispose(); }
        lock (sync) return samples.ToArray();
    }

    public void Dispose() => Stop();
}
