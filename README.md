# Dictate

Push-to-talk dictation for macOS, Windows, Linux, and Android.
Speak into a text field, then release the shortcut to insert the transcript.
Speech recognition runs on your device. Optional cloud cleanup can improve punctuation and phrasing.

Dictate includes a personal dictionary, local cleanup rules, transcript history, and guarded undo and original-text restoration.
The desktop apps share an Electron interface. Android uses the same interface with a native voice keyboard.

## Platforms

| Platform | Speech recognition | Hardware modes |
|---|---|---|
| macOS 15+ | Bundled Phonon-2, based on NVIDIA Parakeet TDT 0.6B v3 | Neural Engine preferred; CPU-only option |
| Windows x64 | English Whisper `base.en-q5_1`, 59.7 MB download | Supported Intel NPU, Vulkan GPU, or CPU |
| Linux x64 | English Whisper `base.en-q5_1`, 59.7 MB download | Supported Intel NPU, Vulkan GPU, or CPU |
| Android 8+ | English Whisper `base.en-q5_1`, 59.7 MB download | Vulkan GPU when supported; CPU fallback |

Apple silicon, Windows Vulkan, Linux CPU, and Android emulator checks have passed.
Physical Intel NPU and Android GPU execution remain unverified.
Linux global shortcuts require X11. On Wayland, use the app recording controls.
See [hardware modes](docs/acceleration.md) and [dated verification results](docs/benchmarks/platform-speech-2026-10-13/README.md) for tested configurations.

## Build and start

Dictate currently requires building from source.
Desktop builds require Node.js **22.12.0 or later** and the **.NET 10 SDK**.

- [Build on macOS](docs/build-macos.md). Includes model download, signing, permissions, and remote installation.
- [Build on Windows or Linux](portable/README.md#run-windows-or-linux). Includes required Linux packages and desktop packaging.
- [Build the Android keyboard](portable/README.md#build-the-android-keyboard). Includes the Android workload and native speech libraries.

Android packages currently use a development signing key.
The Mac build uses a local signing identity. Public package distribution requires separate release signing.

## Dictate and correct text

| Action | macOS | Windows and Linux |
|---|---|---|
| Hold to speak; release to insert | **fn** | **F8** |
| Start hands-free dictation | Press **fn** twice | Press **F8** twice |
| Cancel | **Esc** | **Esc** |
| Add the selected word to the dictionary | **fn+D** | **F8+D** |

On macOS, set the globe key action to **Do Nothing** in Keyboard settings.
On Android, select the Dictate keyboard, then tap **Record** and **Stop**.

The waveform disappears when dictation ends. Warnings stay inside the app.
If a field cannot verify delivery, select **Original** in History to copy the unmodified transcript.
Dictate does not repeat uncertain delivery automatically.

The desktop tray menu offers undo and original-text restoration after verified insertion.
The application, field, text, and caret must remain unchanged. Corrections expire after five minutes.

## Privacy

Audio is processed locally and is not saved in transcript history.
Rules and dictionary replacements work offline.
For fully local operation, turn off **Smart cleanup** in Settings.
When enabled and configured, cloud cleanup sends the transcript and dictionary hints to Groq, DeepSeek, or ChatGPT.
The cleanup checks can reject unsafe changes, but cannot prove that meaning is preserved.
ChatGPT cleanup uses an unofficial endpoint that can change.

Credentials use the operating system's credential store.
History stores original and final text, cleanup replies, and delivery details as local plain text.
History retains older entries on disk; the interface shows the latest 50.
See [security and privacy guidance](SECURITY.md) for storage locations and reporting.

## Development and license

See the [contribution guide](CONTRIBUTING.md), [architecture](portable/docs/architecture.md), and [verification instructions](portable/docs/verification.md).
The [documentation index](docs/README.md) links to build guides, hardware limits, and historical measurements.

Dictate's source uses the [MIT license](LICENSE).
Models and dependencies retain their own licenses. See [third-party notices](THIRD_PARTY.md).
Phonon-2 weights use CC-BY-4.0 and require attribution when distributed.
The [sharing review](docs/sharing-review.md) records removed test audio, historical copies, and public-release requirements.
