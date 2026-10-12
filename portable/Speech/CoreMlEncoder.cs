using System.IO.Compression;

namespace Dictate.Speech;

internal static class CoreMlEncoder
{
    private const string Name = "ggml-base.en-encoder.mlmodelc";
    private const string Hash = "8cf860309e2449e2bdc8be834cf838ab2565747ecc8c0ef914ef5975115e192b";

    internal static async Task PrepareAsync(string directory, CancellationToken cancellation)
    {
        var destination = Path.Combine(directory, Name);
        if (Directory.Exists(destination)) return;
        var archive = Path.Combine(directory, Name + ".zip");
        await AssetDownload.Download("https://huggingface.co/ggerganov/whisper.cpp/resolve/5359861c739e955e79d9a303bcbc70fb988958b1/" + Name + ".zip",
            archive, Hash, 37950917, cancellation).ConfigureAwait(false);
        var staging = Path.Combine(directory, ".coreml-" + Guid.NewGuid().ToString("N"));
        try
        {
            await Task.Run(() => ZipFile.ExtractToDirectory(archive, staging), cancellation).ConfigureAwait(false);
            cancellation.ThrowIfCancellationRequested();
            Directory.Move(Path.Combine(staging, Name), destination);
        }
        finally { if (Directory.Exists(staging)) Directory.Delete(staging, true); }
    }
}
