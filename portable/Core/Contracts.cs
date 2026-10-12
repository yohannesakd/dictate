namespace Dictate.Core;

public enum CleanupProvider { Groq, DeepSeek, ChatGPT }
public enum DictationPhase { Idle, Listening, Processing, Delivering }
public enum DeliveryOutcome { Verified, Sent, FocusChanged, Cancelled, Failed, Unsupported }
// Retain stored engine identities so old settings and transcript histories remain readable.
public enum SpeechEngineKind { Whisper, Whistle }

public sealed record DictationSettings
{
    public CleanupRules Rules { get; init; } = new();
    public DictionaryEntry[] Dictionary { get; init; } = [];
    public bool SmartCleanup { get; init; } = true;
    public CleanupProvider Provider { get; init; } = CleanupProvider.Groq;
    public string SpeechModel { get; init; } = SpeechProfiles.WhisperModel;
    public SpeechEngineKind SpeechEngine { get; init; } = SpeechEngineKind.Whisper;
    public string Language { get; init; } = "en";
    public bool OpenAtLogin { get; init; }
}

public interface IAudioCapture : IDisposable
{
    event Action<float>? Level;
    event Action<string>? Interrupted;
    void Start();
    float[] Stop();
}

public interface ISpeechEngine
{
    Task<string> TranscribeAsync(float[] pcm, string model, string language, CancellationToken cancellation);
}

public interface ITextTarget
{
    string? Application { get; }
    Task<DeliveryReceipt> InsertAsync(string text, string raw, CancellationToken cancellation);
}

public interface ITextDelivery
{
    ITextTarget CaptureTarget();
}

public interface ITextCorrection
{
    Task<bool> ReplaceAsync(string text, CancellationToken cancellation);
}

public sealed record DeliveryReceipt(DeliveryOutcome Outcome, int PostedUtf16Units = 0, ITextCorrection? Correction = null, string? Error = null)
{
    public Dictionary<string, string> Evidence { get; init; } = [];
}
public sealed record CleanupResult(string? Text, string? Reply, string Outcome, string? Error = null);

public interface ICleanupClient
{
    Task<string> CompleteAsync(CleanupProvider provider, string instructions, string message, CancellationToken cancellation);
}

public interface ISecretStore
{
    Task<string?> ReadAsync(string account, CancellationToken cancellation = default);
    Task WriteAsync(string account, string? value, CancellationToken cancellation = default);
}

public interface IDictationStore
{
    Task AppendAsync(TranscriptRecord record);
}

public sealed record TranscriptRecord
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public DateTimeOffset Date { get; init; } = DateTimeOffset.Now;
    public required string Raw { get; init; }
    public required string Rules { get; init; }
    public required string Text { get; init; }
    public string? Application { get; init; }
    public required string Smart { get; init; }
    public string? ModelReply { get; init; }
    public string? Note { get; init; }
    public string? Provider { get; init; }
    public string? Model { get; init; }
    public string? Prompt { get; init; }
    public SpeechEngineKind? SpeechEngine { get; init; }
    public string? LegacySpeechEngine { get; init; }
    public string? SpeechModel { get; init; }
    public double AudioSeconds { get; init; }
    public required DeliveryOutcome Delivery { get; init; }
    public Dictionary<string, string> DeliveryEvidence { get; init; } = [];
    public int PostedUtf16Units { get; init; }
    public Dictionary<string, double> Metrics { get; init; } = [];
}
