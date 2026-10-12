# Whisper-only verification evidence

These records verify the shared English CPU recognizer and the removal of active Whistle paths.
The current change starts after commit `e704bd6` on `feat/electron-desktop`.
The [decision record](../../whisper-only.md) distinguishes completed checks from unavailable devices.

## Completed checks

- `linux-core.txt` records 26 passing tests, including legacy settings and unchanged historical engine identities.
- `linux-audio.json` contains six checksum-verified English fixtures with five passes each. All 30 runs completed without errors.
- `mac-native-audio.json` contains the same 30 runs under the signed native Mac check bundle. All runs completed without errors.
- `linux-cpu-no-avx.txt` records recognition, cancellation, and recovery with `DOTNET_EnableHWIntrinsic=0`.
- `linux-checks.txt` records the packaged renderer, native service, compact layout, waveform isolation, and Whisper-only interface.
- `linux-transport.txt` records the three passing transport checks.
- `mac-native-checks.txt` records native Unicode delivery, correction guards, Keychain, microphone restart, recognition, and cancellation recovery.
- `mac-native-permissions.json` records granted permissions. `mac-native-fixture-command.focus.json` confirms the external AppKit field had focus.
- `mac-ui-checks.txt` records the packaged Electron checks after the final application rebuild.
- `mac-ui-restart-checks.txt` records the Whisper profile after a complete process restart.
- `mac-install.json` records signature verification and unchanged settings and history hashes during installation.
- `android-apk-contents.json` records the Debug and Release APK sizes, checksums, and Whisper native libraries.

The Mac update passed strict signature verification before and after installation.
Settings and history hashes matched before and after bundle replacement.
The installed app showed `Connected` for the existing Groq and DeepSeek credentials.
This confirms credential access. It does not measure cloud cleanup quality or provider availability.
The provider returned to Groq after verification. No credential was exported or replaced.

## Measurement limits

The audio matrix is a functional check of the current quantized model. It is not a new Whistle comparison.
The raw timing label mentions WAV and process work because these runs preceded a metadata-only correction.
Whisper processes PCM directly. Timing excludes fixture parsing, cloud cleanup, and field delivery.
Preparation includes verification and model loading. Parent RSS includes the cached model, but does not measure peak memory.
Most recordings are synthetic. The fixtures do not establish accuracy across speakers, accents, or noise conditions.
The VPS ran other work. These timings do not establish a performance winner or predict Saibot latency.
The no-AVX check selects the fallback runtime on a modern CPU. It does not reproduce an older physical CPU.

## Execution records

Linux functional checks ran through Dispatch job `e9530822-cbbc-4fb9-bd8c-6d026d9ee320`, with exit code 0.
Android ARM64 Debug and Release builds ran through job `3a8db797-a852-4ae9-a112-518205273c1d`, with exit code 0.
Both APK file lists exclude Whistle and Needle assets.
The final Linux, Windows, and Android package rebuild passed job `8eb5f901-2c26-470a-b314-6c7bbd95c16b`, with exit code 0.
`final-builds/` retains its build logs, repeated core tests, and final native Linux and interface checks.
`final-dispatch-receipt.json` records successful execution, complete evidence, and the source and output manifest hashes.
`android-release-final.json` records the final APK checksum, native libraries, and shared interface hashes.
`desktop-package-ui.json` and `mac-installed-ui.json` record the corresponding desktop interface hashes.
The installed Mac app and the Windows, Linux, and Android packages contain identical shared HTML, CSS, and JavaScript.
This confirms packaged assets. It does not replace Windows or Android runtime verification.
Mac builds and native checks ran through personal SSH with the stable local signing identity.
Mac native checks ran sequentially before Electron checks to preserve the native fixture's focus.

Saibot Windows and Linux are unavailable. The Dispatch Android service returned HTTP 503.
Local Mac emulators remain stopped under the user's routing instruction. Current Android runtime verification is unavailable.
No result here claims current Windows device verification or physical Android battery measurements.
