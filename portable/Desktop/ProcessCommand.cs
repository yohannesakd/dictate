using System.Diagnostics;

namespace Dictate.Desktop;

internal static class ProcessCommand
{
    public static Process Start(string command, params string[] arguments)
    {
        var info = new ProcessStartInfo(command) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true, CreateNoWindow = true };
        foreach (var argument in arguments) info.ArgumentList.Add(argument);
        return Process.Start(info) ?? throw new InvalidOperationException($"Could not start {command}.");
    }

    public static async Task<(int Exit, string Output)> RunAsync(string command, string[] arguments, string? input = null, CancellationToken cancellation = default)
    {
        using var process = Start(command, arguments);
        using var stop = cancellation.Register(() => { if (!process.HasExited) process.Kill(true); });
        var output = process.StandardOutput.ReadToEndAsync(cancellation);
        var error = process.StandardError.ReadToEndAsync(cancellation);
        if (input is not null) await process.StandardInput.WriteAsync(input.AsMemory(), cancellation).ConfigureAwait(false);
        process.StandardInput.Close();
        await process.WaitForExitAsync(cancellation).ConfigureAwait(false);
        var stdout = await output.ConfigureAwait(false);
        var stderr = await error.ConfigureAwait(false);
        return (process.ExitCode, process.ExitCode == 0 ? stdout : stderr);
    }
}
