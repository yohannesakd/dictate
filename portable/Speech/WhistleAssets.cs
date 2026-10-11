using System.Runtime.InteropServices;

namespace Dictate.Speech;

/// <summary>Installs pinned Whistle assets. Android runs its packaged executable.</summary>
public sealed class WhistleAssets(string directory, string? bundledExecutable = null)
{
    public const string EngineRevision = "8354babbfb92d719ee559f2999d133f11fb9374c";
    public const string ModelRevision = "ca5287601bef25af26dcf2e1b2bdc0843a7c19e5";
    public const string ModelHash = "b6e02f048568ac5d01a2042556c658061e699acbc0aa2a1439f52f3d461dffeb";
    public const string AndroidHash = "aaf2e4f7f492c0bc67af354ac03b7246fcef890de16df0bebaf80b17b9aac90e";
    public static readonly string[] Languages = ["auto", "en", "de", "fr", "es", "it", "nl", "pl"];
    public string ModelPath => Path.Combine(directory, "whistle.cact");
    public string ExecutablePath => bundledExecutable ?? Path.Combine(directory, OperatingSystem.IsWindows() ? "needle.exe" : "needle");
    public bool IsSupported => bundledExecutable is not null ? File.Exists(bundledExecutable) : Platform() is not null;

    /// <summary>Verifies cached assets or downloads verified replacements without publishing partial files.</summary>
    public async Task PrepareAsync(CancellationToken cancellation)
    {
        Directory.CreateDirectory(directory);
        if (bundledExecutable is not null)
        {
            if (!await AssetDownload.HasHash(bundledExecutable, AndroidHash, cancellation).ConfigureAwait(false))
                throw new InvalidOperationException("The packaged Whistle runtime is missing or invalid. Install the ARM64 Android package.");
        }
        else
        {
            var platform = Platform() ?? throw new PlatformNotSupportedException("Whistle has no native runtime for this device. Select Whisper.");
            await AssetDownload.Download($"https://huggingface.co/Cactus-Compute/needle3/resolve/{EngineRevision}/{platform.Folder}/{(OperatingSystem.IsWindows() ? "needle.exe" : "needle")}",
                ExecutablePath, platform.Hash, 4 * 1024 * 1024, cancellation).ConfigureAwait(false);
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(ExecutablePath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
        await AssetDownload.Download($"https://huggingface.co/Cactus-Compute/whistle/resolve/{ModelRevision}/whistle.cact", ModelPath, ModelHash, 32 * 1024 * 1024, cancellation).ConfigureAwait(false);
    }

    private static (string Folder, string Hash)? Platform() => (OperatingSystem.IsWindows(), OperatingSystem.IsLinux(), OperatingSystem.IsMacOS(), RuntimeInformation.ProcessArchitecture) switch
    {
        (true, _, _, Architecture.X64) => ("windows-x86_64", "eaeef0a56977bc634167c2054231da2fd6a04eacb3b1c5fd51a32dcacaf4f46d"),
        (_, true, _, Architecture.X64) when !OperatingSystem.IsAndroid() => ("linux-x86_64", "960622906782526c8ff8fa3c5d6c67cc4684c2be1b6b6a34208b4efc78eef39e"),
        (_, _, true, Architecture.Arm64) => ("macos-arm64", "fe008fdcfdf9e3c25e511961c47c44c108e1b81302f8700bf5f797ac60888d0a"),
        _ => null
    };

}
