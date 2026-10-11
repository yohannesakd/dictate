# Dictate

Dictate provides local English dictation on macOS, Windows, Linux, and Android.
The three desktop apps use one Electron interface, one core, and software rendering.
Speech recognition runs on the CPU without a GPU.

The **Speech engine** menu offers two choices:

- **Whistle** uses a 16.9 MB model. It uses less memory and handles short dictation well.
- **Whisper** uses the 59.7 MB quantized English `base.en-q5_1` model. It handles long dictation faster on the tested Windows and Linux machine.

The choice survives restart. Engine changes require idle dictation.
The [English comparison](docs/whistle-whisper-evaluation.md) explains quality and resource differences.
The [current verification record](docs/electron-desktop-verification.md) identifies tested devices and remaining limits.

## Use dictation

On macOS, hold **fn**, speak, and release.
On Windows and Linux, hold **F8**, speak, and release.
Press the dictation key twice for hands-free dictation.
Press **Esc** to cancel.
Set the Mac globe key action to **Do Nothing** in Keyboard settings.

The indicator shows recording or processing and disappears when dictation ends.
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
The pinned Whistle helper does not support Intel Macs.

```sh
./make-signing-identity.sh
./build.sh
open build/Dictate.app
```

Run the signing script once. Keep the signing identity to retain microphone and Accessibility permissions across builds.
Models download when selected and loaded in Settings.
The build no longer requires CoreML model folders or a Phonon runtime.

For a remote Mac, retain `signing/` in `~/Library/Application Support/Dictate Build`.
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

## Credits

Whistle and Needle are from Cactus Compute and use Apache-2.0 licences.
Whisper is from OpenAI. Whisper and whisper.cpp use MIT licences.
The desktop interface uses Electron and the Inter font.
The [design language](docs/design-language.md) defines its shared colors, spacing, controls, and indicator behavior.
