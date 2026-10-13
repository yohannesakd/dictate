namespace Dictate.Core;

/// <summary>Recognition profiles selected by the platform.</summary>
public static class SpeechProfiles
{
    public const string WhisperModel = "base.en-q5_1";
    public const string PhononModel = "phonon-2";

    // Existing histories retain their model identities. Only future dictation settings change.
    public static DictationSettings Normalize(DictationSettings settings, SpeechEngineKind engine = SpeechEngineKind.Whisper) => settings with
    {
        SpeechEngine = engine,
        SpeechModel = engine == SpeechEngineKind.Phonon ? PhononModel : WhisperModel,
        Language = engine == SpeechEngineKind.Phonon ? "auto" : "en"
    };
}
