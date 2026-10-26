# Dictate

Dictate provides local English dictation on macOS, Windows, Linux, and Android.
The three desktop apps use one Electron interface, one core, and software rendering.
Speech recognition uses available acceleration, with a CPU fallback.

The Mac app uses the original Phonon-2 model, based on NVIDIA Parakeet TDT 0.6B v3.
Automatic prefers the Neural Engine. CPU only disables acceleration for the same model.
Windows, Linux, and Android use quantized English Whisper `base.en-q5_1`, with a 59.7 MB download.
Windows and Linux x64 can use Intel NPUs through OpenVINO, Vulkan GPUs, or CPU.
A supported NPU requires compatible drivers. Unsupported hardware uses GPU or CPU.
In Settings, choose Automatic or CPU only. See [hardware modes](docs/acceleration.md) for the platform limits.

## Use dictation

On macOS, hold **fn**, speak, and release.
On Windows and Linux, hold **F8**, speak, and release.
Press the dictation key twice for hands-free dictation.
Press **Esc** to cancel.
Set the Mac globe key action to **Do Nothing** in Keyboard settings.

The indicator shows only a small waveform and disappears when dictation ends.
Warnings appear inside the app.
Dictate saves transcripts and delivery results in History.
Use **Copy original** when a field cannot verify delivery.
Dictate never repeats uncertain delivery automatically.

The tray menu offers undo and raw restoration after verified insertion.
The process, field, text, and caret must remain unchanged.
Corrections expire after five minutes.
Select a word and press **fn+D** or **F8+D** to add a dictionary entry.

## Build the Mac app

The Mac build requires macOS 15 or later, Xcode tools, Node.js 22 or later, and the .NET 10 SDK.
The tested Mac uses Apple silicon.

The Mac build bundles Phonon-2 and compiles its original Core ML runtime.
Clone the runtime release into `vendor/phonon-coreml`:

```sh
git clone --branch 1.1.2 --depth 1 https://github.com/fermionresearch/phonon-coreml vendor/phonon-coreml
```

Download the complete [Phonon-2-CoreML](https://huggingface.co/FermionResearch/Phonon-2-CoreML) into `models/Phonon-2-CoreML`.
The build requires `manifest.json`, `decoder.bin`, `Phonon-2.mlpackage`, `NOTICE`, `LICENSE-WEIGHTS-CC-BY-4.0.txt`, and `SHA256SUMS`.
Keep the complete download until its upstream checksum check passes.
Use Git LFS or download the actual files. Git LFS pointer files cannot run the model.
The verified model uses manifest version `3.0` and encoder functions `5`, `10`, `15`, and `35`.
Run `shasum -a 256 -c SHA256SUMS` from the downloaded model directory before building.
Runtime release `1.1.2` resolves to commit `bf59e574639b060e30ee1a3f9c0e003fed5744c8`.
The first load prepares the model for this Mac.

```sh
./make-signing-identity.sh
./build.sh
open build/Dictate.app
```

Run the signing script once. Keep the signing identity to retain microphone and Accessibility permissions across builds.

For a remote Mac, retain `models/`, `vendor/`, and `signing/` in `~/Library/Application Support/Dictate Build`.
Run `scripts/install-mac.sh mac` from this checkout.
See [portable build instructions](portable/README.md) for Windows, Linux, and Android.

Existing Mac settings, dictionary entries, and transcript history import into `~/Library/Application Support/Dictate/Desktop` on first launch.
The import leaves the original files unchanged.
Existing Keychain credentials remain in the same service.
New transcripts contain delivery evidence and stage timings in the shared history file.
Older detailed metrics remain available for audit with `scripts/latency-report.py`.

## Cleanup and privacy

Rules and dictionary replacement work offline.
Optional smart cleanup sends transcripts to Groq, DeepSeek, or ChatGPT.
Speech recognition stays on the device.
The app checks cleanup proposals and uses the rule result after timeout or rejection.
These checks reduce mistakes but do not prove meaning preservation.

Credentials use macOS Keychain, Windows Credential Manager, Linux Secret Service, or Android Keystore.
ChatGPT cleanup uses an unofficial endpoint that can change.

## Project status and license

This repository remains private. The source uses the [MIT license](LICENSE).
Model weights and dependencies retain their own licenses. See [third-party notices](THIRD_PARTY.md).
The Mac model uses CC-BY-4.0 and requires attribution.
Android packages currently use a development signing key.
Physical Intel NPU and Android GPU execution remain unverified.
See [verification results](docs/benchmarks/platform-speech-2026-10-13/README.md) for tested hardware and limits.

See the [documentation index](docs/README.md), [contribution guide](CONTRIBUTING.md), and [security guidance](SECURITY.md).
