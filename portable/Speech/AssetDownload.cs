using System.Security.Cryptography;

namespace Dictate.Speech;

internal static class AssetDownload
{
    private static readonly HttpClient http = new() { Timeout = TimeSpan.FromMinutes(5) };
    internal static async Task<bool> HasHash(string path, string hash, CancellationToken cancellation)
    {
        if (!File.Exists(path)) return false;
        await using var file = File.OpenRead(path);
        return Convert.ToHexString(await SHA256.HashDataAsync(file, cancellation).ConfigureAwait(false)).Equals(hash, StringComparison.OrdinalIgnoreCase);
    }

    internal static async Task Download(string url, string path, string hash, long limit, CancellationToken cancellation, HttpClient? client = null)
    {
        if (await HasHash(path, hash, cancellation).ConfigureAwait(false)) return;
        client ??= http;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        deadline.CancelAfter(client.Timeout);
        cancellation = deadline.Token;
        var staging = path + "." + Guid.NewGuid().ToString("N") + ".download";
        try
        {
            using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellation).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            await using (var source = await response.Content.ReadAsStreamAsync(cancellation).ConfigureAwait(false))
            await using (var target = File.Create(staging))
            {
                var buffer = new byte[65536];
                long bytes = 0;
                int read;
                while ((read = await source.ReadAsync(buffer, cancellation).ConfigureAwait(false)) != 0)
                {
                    bytes += read;
                    if (bytes > limit) throw new InvalidDataException("The speech asset download exceeds its expected size.");
                    await target.WriteAsync(buffer.AsMemory(0, read), cancellation).ConfigureAwait(false);
                }
            }
            if (!await HasHash(staging, hash, cancellation).ConfigureAwait(false)) throw new InvalidDataException("The speech asset download failed checksum verification.");
            cancellation.ThrowIfCancellationRequested();
            File.Move(staging, path, true);
        }
        finally { if (File.Exists(staging)) File.Delete(staging); }
    }
}
