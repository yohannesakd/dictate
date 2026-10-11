namespace Dictate.Core;

/// <summary>The two English CPU profiles offered by every app.</summary>
public static class SpeechProfiles
{
    public const string WhisperModel = "base.en-q5_1";
    public static readonly SpeechEngineKind[] Engines = [SpeechEngineKind.Whistle, SpeechEngineKind.Whisper];
    public static string Description(SpeechEngineKind engine) => engine == SpeechEngineKind.Whistle
        ? "Whistle · 17 MB. Lower memory use and fast short dictation."
        : "Whisper · 60 MB. Faster long dictation on Windows and Linux.";

    // Existing histories retain their model identities. Only future dictation settings change.
    public static DictationSettings Normalize(DictationSettings settings) => settings with
    {
        SpeechModel = WhisperModel,
        Language = "en"
    };
}
