using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json;
using Dictate.Core;

namespace Dictate.Speech;

// OpenVINO and Vulkan require different whisper.cpp libraries. Isolate the NPU library in its own process.
internal sealed class NpuWorker(string models) : IDisposable
{
    private Process? process;
    private readonly object sync = new();
    private string? diagnostic;
    private const string Prefix = "DICTATE_NPU ";
    internal bool Ready { get; private set; }

    internal async Task<bool> PrepareAsync(string modelPath, CancellationToken cancellation)
    {
        if (Ready) return true;
        var folder = Path.Combine(AppContext.BaseDirectory, "npu-worker");
        var executable = Path.Combine(folder, OperatingSystem.IsWindows() ? "Dictate.NpuWorker.exe" : "Dictate.NpuWorker");
        if (!(OperatingSystem.IsWindows() || OperatingSystem.IsLinux()) || !File.Exists(executable)) return false;
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = folder };
        start.ArgumentList.Add(models);
        start.ArgumentList.Add(modelPath);
        start.ArgumentList.Add("NPU");
        var search = OperatingSystem.IsWindows() ? "PATH" : "LD_LIBRARY_PATH";
        start.Environment[search] = folder + Path.PathSeparator + Environment.GetEnvironmentVariable(search);
        var current = Process.Start(start) ?? throw new IOException("The NPU service did not start.");
        diagnostic = null;
        process = current;
        current.ErrorDataReceived += (_, args) => { if (args.Data is { } text) diagnostic = text.Length > 512 ? text[..512] : text; };
        current.BeginErrorReadLine();
        using var registration = cancellation.Register(Stop);
        var greeting = await ReplyAsync(current, cancellation, TimeSpan.FromMinutes(2)).ConfigureAwait(false);
        if (!greeting.GetProperty("available").GetBoolean()) { Stop(); return false; }
        await PrepareEncoderAsync(cancellation).ConfigureAwait(false);
        await current.StandardInput.BaseStream.WriteAsync(new byte[] { 1 }, cancellation).ConfigureAwait(false);
        await current.StandardInput.BaseStream.FlushAsync(cancellation).ConfigureAwait(false);
        _ = await ReplyAsync(current, cancellation, TimeSpan.FromMinutes(2)).ConfigureAwait(false);
        lock (sync) { cancellation.ThrowIfCancellationRequested(); Ready = true; }
        return true;
    }

    private async Task PrepareEncoderAsync(CancellationToken cancellation)
    {
        var destination = Path.Combine(models, "openvino-base.en");
        if (Directory.Exists(destination)) return;
        var archive = Path.Combine(models, "ggml-base.en-encoder-openvino.zip");
        await AssetDownload.Download("https://huggingface.co/sandrohanea/whisper.net/resolve/6c641e5ffec145595714a49d532297bb8e368c28/openvino/ggml-base.en-encoder.zip",
            archive, "4f1728cc251f14736d85f751260d6249bc446bedd7a2d044bc3a1d777af7072f", 48405638, cancellation).ConfigureAwait(false);
        var staging = Path.Combine(models, ".openvino-" + Guid.NewGuid().ToString("N"));
        try
        {
            await Task.Run(() => ZipFile.ExtractToDirectory(archive, staging), cancellation).ConfigureAwait(false);
            cancellation.ThrowIfCancellationRequested();
            Directory.Move(staging, destination);
        }
        finally { if (Directory.Exists(staging)) Directory.Delete(staging, true); }
    }

    internal async Task<string> TranscribeAsync(float[] pcm, CancellationToken cancellation)
    {
        using var registration = cancellation.Register(Stop);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        deadline.CancelAfter(TimeSpan.FromMinutes(6));
        try
        {
            var current = process ?? throw new IOException("The NPU service is unavailable.");
            var input = current.StandardInput.BaseStream;
            await input.WriteAsync(new byte[] { 2 }, deadline.Token).ConfigureAwait(false);
            await input.WriteAsync(BitConverter.GetBytes(pcm.Length), deadline.Token).ConfigureAwait(false);
            var bytes = new byte[pcm.Length * sizeof(float)];
            Buffer.BlockCopy(pcm, 0, bytes, 0, bytes.Length);
            await input.WriteAsync(bytes, deadline.Token).ConfigureAwait(false);
            await input.FlushAsync(deadline.Token).ConfigureAwait(false);
            var result = await ReplyAsync(current, deadline.Token, TimeSpan.FromMinutes(6)).ConfigureAwait(false);
            return result.GetProperty("text").GetString()!;
        }
        catch (OperationCanceledException) when (!cancellation.IsCancellationRequested)
        { throw new IOException("The NPU service did not respond in time."); }
    }

    private async Task<JsonElement> ReplyAsync(Process current, CancellationToken cancellation, TimeSpan timeout)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        deadline.CancelAfter(timeout);
        var output = current.StandardOutput;
        try
        {
            while (await output.ReadLineAsync(deadline.Token).ConfigureAwait(false) is { } line)
            {
                if (!line.StartsWith(Prefix, StringComparison.Ordinal)) continue;
                using var document = JsonDocument.Parse(line[Prefix.Length..]);
                var result = document.RootElement.Clone();
                if (result.TryGetProperty("error", out var error)) throw new IOException(error.GetString());
                return result;
            }
        }
        catch (OperationCanceledException) when (!cancellation.IsCancellationRequested)
        { throw new IOException("The NPU service did not respond in time."); }
        cancellation.ThrowIfCancellationRequested();
        throw new IOException("The NPU service stopped. " + diagnostic);
    }

    internal void Stop()
    {
        Process? previous;
        lock (sync) { Ready = false; previous = process; process = null; }
        if (previous is null) return;
        try
        {
            try { if (!previous.HasExited) previous.Kill(true); }
            catch (InvalidOperationException) when (previous.HasExited) { }
            previous.WaitForExit();
        }
        finally { previous.Dispose(); }
    }
    public void Dispose() => Stop();
}
