using System.Text;
using Dictate.Speech;

namespace Dictate.Android;

/// <summary>Executes the packaged PIE binary from Android's extracted native library directory.</summary>
public sealed class AndroidWhistleProcess : IWhistleProcess
{
    public async Task<string> RunAsync(string executable, string model, string audio, string language, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        var arguments = new List<string> { executable, "--model", model, "--audio", audio };
        if (language != "auto") arguments.AddRange(["--audio-language", language]);
        using var builder = new Java.Lang.ProcessBuilder(arguments);
        builder.Environment()!["NEEDLE_TELEMETRY"] = "0";
        builder.Environment()!["DO_NOT_TRACK"] = "1";
        using var process = builder.Start()!;
        using var registration = cancellation.Register(() => process.DestroyForcibly());
        try
        {
            var stdout = Task.Run(() => Read(process.InputStream!, process));
            var stderr = Task.Run(() => Read(process.ErrorStream!, process));
            var exit = Task.Run(() => process.WaitFor());
            await Task.WhenAll(stdout, stderr, exit).ConfigureAwait(false);
            cancellation.ThrowIfCancellationRequested();
            if (exit.Result != 0)
            {
                var detail = stderr.Result.Trim();
                throw new InvalidOperationException($"Whistle failed with exit code {exit.Result}: {detail[..Math.Min(detail.Length, 400)]}");
            }
            return stdout.Result;
        }
        finally { process.DestroyForcibly(); await Task.Run(() => process.WaitFor()).ConfigureAwait(false); }
    }

    private static string Read(Stream stream, Java.Lang.Process process)
    {
        using var reader = new StreamReader(stream);
        var text = new StringBuilder();
        var buffer = new char[4096];
        int read;
        while ((read = reader.Read(buffer, 0, buffer.Length)) != 0)
        {
            if (text.Length + read > 1024 * 1024) { process.DestroyForcibly(); throw new InvalidDataException("Whistle exceeded its output limit."); }
            text.Append(buffer, 0, read);
        }
        return text.ToString();
    }
}
