# English Whisper on every platform

Dictate uses the same 59.7 MB English Whisper model, `base.en-q5_1`, on Mac, Windows, Linux, and Android.
Recognition runs on the CPU. A GPU or Apple Neural Engine is not required.
The [Whisper.net CPU runtime](https://github.com/sandrohanea/whisper.net#whispernetruntime) supports these platforms and supplies a no-AVX alternative for older supported CPUs.
The app explicitly selects CPU libraries and sets `UseGpu = false` in [WhisperEngine](../portable/Speech/WhisperEngine.cs).

Whistle is removed from recognition, downloading, menus, settings controls, and Android native packaging.
The core calls Whisper directly. One gate owns model loading and recognition instead of a routing wrapper plus a recognizer gate.
Digital-silence rejection, cancellation, recovery, and the five-minute recording limit remain part of recognition.

## Why remove Whistle

The [archived comparison](whistle-whisper-evaluation.md) contains 280 runs across four tested platforms.
Both engines received identical recordings, with five passes per recording.
Whistle used less memory and returned short recordings quickly. Whisper finished the tested long recordings faster on Windows and Linux.
These measurements used older configurations. They do not establish an optimized performance winner for the current app.

Most recordings were synthetic. The small set does not establish general accuracy for daily speech, accents, or background noise.
The user's dictation experience with Whistle was poor. The smaller model and shorter startup delay did not justify keeping it for this use.

No tested device needs Whistle because it lacks a Neural Engine. The Windows, Linux, Mac, and Android recognition paths already support CPU Whisper.
Devices with very little memory may require separate measurements. Physical Android battery use and older physical CPU performance remain unverified.

## Existing data

Old Whistle settings load as English Whisper. Cleanup, provider, dictionary, and login settings retain their values.
Later settings writes save the normalized profile. Loading settings does not rewrite transcript history.
The stored `Whistle` engine identity remains readable only for old settings and historical records.
The history interface can display the engine that originally produced a transcript. New recognition always records Whisper.
Old downloaded model caches remain available for rollback. The current app does not load or execute them.
Keychain, Credential Manager, Secret Service, and Keystore credentials are unchanged.

## Verification

The [October 12 evidence](benchmarks/whisper-only-2026-10-12/) records completed checks and their limits.

| Boundary | Result | Limit |
| --- | --- | --- |
| Shared core | All 26 tests passed. Legacy settings, history identity, digital silence, cancellation, and delivery guards passed. | These tests do not replace device checks. |
| Mac | Signed app and native checks passed. Thirty English audio runs, native field delivery, cancellation, Keychain, and microphone restart passed. | Other target applications remain unverified. |
| Mac interface | All pages, Whisper preparation, restart behavior, clipboard, input preservation, and waveform isolation passed. | The automated checks use isolated app data. |
| Linux | Thirty CPU audio runs, cancellation recovery, native GTK delivery, software-rendered Electron pages, and restart checks passed. | The VPS check uses isolated X11 and synthetic microphone capture. Saibot Linux is unavailable. |
| Older CPU path | Recognition and cancellation passed with .NET hardware intrinsics disabled, which selects the no-AVX runtime. | No older physical CPU was tested. |
| Windows | The Windows x64 package and native check bundle built successfully through Dispatch. | Saibot is unavailable. Current native delivery and interface checks cannot run. |
| Android | ARM64 Debug and Release packages built. Neither APK contains Whistle or Needle files. | Runtime verification is unavailable. The Dispatch Android service is offline, and local Mac emulators remain stopped. |

The Mac update preserved settings and transcript history hashes exactly.
The installed app still reads the saved Groq and DeepSeek credentials. Credentials were not exported or rewritten.
The final Windows, Linux, and Android packages passed Dispatch job `8eb5f901-2c26-470a-b314-6c7bbd95c16b`, with exit code 0.
The final Linux package repeated the native GTK, interface, and restart checks successfully.
Shared HTML, CSS, and JavaScript hashes match across the installed Mac app and all three other platform packages.
Physical Android microphone, latency, and battery checks remain open.
