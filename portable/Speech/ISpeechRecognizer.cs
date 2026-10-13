using Dictate.Core;

namespace Dictate.Speech;

/// <summary>A platform recognizer owns preparation, hardware selection, and its native lifetime.</summary>
public interface ISpeechRecognizer : ISpeechEngine, IDisposable
{
    event Action<string>? Status;
    SpeechEngineKind Kind { get; }
    bool ModelReady { get; }
    string? ActiveHardware { get; }
    string? FallbackReason { get; }
    SpeechComputeMode? LoadedCompute { get; }
    Task PrepareAsync(string model, CancellationToken cancellation = default,
        SpeechComputeMode compute = SpeechComputeMode.Automatic);
}
