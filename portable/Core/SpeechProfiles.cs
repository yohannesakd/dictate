namespace Dictate.Core;

/// <summary>The English Whisper profile used by every app.</summary>
public static class SpeechProfiles
{
    public const string WhisperModel = "base.en-q5_1";

    // Existing histories retain their model identities. Only future dictation settings change.
    public static DictationSettings Normalize(DictationSettings settings) => settings with
    {
        SpeechEngine = SpeechEngineKind.Whisper,
        SpeechModel = WhisperModel,
        Language = "en"
    };
}
