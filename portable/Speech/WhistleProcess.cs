using System.Diagnostics;
using System.Text;

namespace Dictate.Speech;

/// <summary>Runs one native speech process. Implementations wait for cancellation to stop the process.</summary>
public interface IWhistleProcess
{
    Task<string> RunAsync(string executable, string model, string audio, string language, CancellationToken cancellation);
}

/// <summary>Uses an owned desktop process with bounded output and no shell.</summary>
public sealed class WhistleProcess : IWhistleProcess
{
    public async Task<string> RunAsync(string executable, string model, string audio, string language, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        using var process = new Process { StartInfo = new(executable) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true } };
        foreach (var argument in new[] { "--model", model, "--audio", audio, "--threads", Math.Clamp(Environment.ProcessorCount, 1, 4).ToString(System.Globalization.CultureInfo.InvariantCulture) }) process.StartInfo.ArgumentList.Add(argument);
        if (language != "auto") { process.StartInfo.ArgumentList.Add("--audio-language"); process.StartInfo.ArgumentList.Add(language); }
        process.StartInfo.Environment["NEEDLE_TELEMETRY"] = "0";
        process.StartInfo.Environment["DO_NOT_TRACK"] = "1";
        if (!process.Start()) throw new InvalidOperationException("Whistle did not start.");
        using var registration = cancellation.Register(() => Stop(process));
        try
        {
            var stdout = ReadAsync(process.StandardOutput, process, cancellation);
            var stderr = ReadAsync(process.StandardError, process, cancellation);
            await Task.WhenAll(stdout, stderr, process.WaitForExitAsync(cancellation)).ConfigureAwait(false);
            cancellation.ThrowIfCancellationRequested();
            if (process.ExitCode != 0)
            {
                var detail = stderr.Result.Trim();
                throw new InvalidOperationException($"Whistle failed with exit code {process.ExitCode}: {detail[..Math.Min(detail.Length, 400)]}");
            }
            return stdout.Result;
        }
        finally { Stop(process); await process.WaitForExitAsync().ConfigureAwait(false); }
    }

    private static async Task<string> ReadAsync(StreamReader reader, Process process, CancellationToken cancellation)
    {
        var text = new StringBuilder();
        var buffer = new char[4096];
        int read;
        while ((read = await reader.ReadAsync(buffer, cancellation).ConfigureAwait(false)) != 0)
        {
            if (text.Length + read > 1024 * 1024) { Stop(process); throw new InvalidDataException("Whistle exceeded its output limit."); }
            text.Append(buffer, 0, read);
        }
        return text.ToString();
    }

    private static void Stop(Process process)
    {
        try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
        catch (InvalidOperationException) { }
        catch (System.ComponentModel.Win32Exception) when (process.HasExited) { }
    }
}
