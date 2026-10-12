using System.Runtime.InteropServices;

namespace Dictate.Speech;

/// <summary>Controls the Core ML encoder independently of whisper.cpp's GPU flag.</summary>
internal static class CoreMlBridge
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void SetEnabled(int enabled);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int ReadFlag();
    private sealed record Bindings(SetEnabled Configure, ReadFlag Failed, ReadFlag Neural);
    private static readonly Lazy<Bindings?> bindings = new(Load);
    internal static bool Available => bindings.Value is not null;
    internal static bool PredictionFailed => bindings.Value?.Failed() == 1;
    internal static bool NeuralConfiguration => bindings.Value?.Neural() == 1;
    internal static void Configure(bool enabled) => bindings.Value?.Configure(enabled ? 1 : 0);

    private static Bindings? Load()
    {
        if (!OperatingSystem.IsMacOS() || RuntimeInformation.ProcessArchitecture != Architecture.Arm64) return null;
        var path = Path.Combine(AppContext.BaseDirectory, "runtimes", "coreml", "macos-arm64", "libwhisper.coreml.dylib");
        if (!NativeLibrary.TryLoad(path, out var handle)) return null;
        if (!NativeLibrary.TryGetExport(handle, "dictate_coreml_available", out var available)
            || !NativeLibrary.TryGetExport(handle, "dictate_coreml_set_enabled", out var configure)
            || !NativeLibrary.TryGetExport(handle, "dictate_coreml_prediction_failed", out var failed)
            || !NativeLibrary.TryGetExport(handle, "dictate_coreml_neural_configuration", out var neural))
        {
            NativeLibrary.Free(handle);
            return null;
        }
        if (Marshal.GetDelegateForFunctionPointer<ReadFlag>(available)() != 1)
        {
            NativeLibrary.Free(handle);
            return null;
        }
        // Whisper.net retains its native runtime for the process lifetime.
        return new(Marshal.GetDelegateForFunctionPointer<SetEnabled>(configure),
            Marshal.GetDelegateForFunctionPointer<ReadFlag>(failed), Marshal.GetDelegateForFunctionPointer<ReadFlag>(neural));
    }
}
