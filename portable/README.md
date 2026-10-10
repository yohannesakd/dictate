# Portable Dictate

This branch adds a portable implementation beside the existing Mac app.
The Mac source, speech model, build, and installation path stay unchanged.
The portable apps share cleanup, dictionary matching, request limits, cancellation, history, and delivery contracts.

## Run the desktop app

Install the .NET 10 SDK.
On Linux, install `alsa-utils`, `libsecret-tools`, `at-spi2-core`, and `libgomp1`.
Use an X11 session for global shortcuts.
Automatic insertion requires a readable, editable accessibility field.
Other fields require copying from History.
On Wayland, use the recording controls.

Run this command from the repository root.

```sh
dotnet run --project portable/Desktop/Dictate.Desktop.csproj
```

In Settings, download a speech model.
Choose `tiny` for lower memory use, `base` for general use, or `small` for higher accuracy.
The `.en` models recognize English only.
Other models support language detection.

Hold F8 to speak, then release F8 to insert text.
Press F8 twice for hands-free recording.
Press F8+Space for hands-free recording.
Press Esc to cancel.
Press Ctrl+Shift+V to repeat the last dictation.
Select a word and press F8+D to add a dictionary entry.
The app saves each completed transcript and its delivery outcome in History.
Close the window to keep Dictate in the system tray.
Use the tray menu or Settings to quit.

The interface uses software rendering.
Speech recognition explicitly uses the CPU.
The app includes a runtime for x86 processors without AVX instructions.
CPU speed, memory, and model size determine transcription delay.
Performance has not been established on all supported devices.

## Build the Android keyboard

Install the .NET 10 Android workload, Android SDK, and required Java SDK.
The setup script installs isolated build tools under `portable/.tools`.
Run the setup script on Linux.
It accepts the Android SDK licenses during installation.

```sh
portable/scripts/setup-android.sh
portable/scripts/build-android.sh Release android-arm64
```

For an x64 emulator, run `portable/scripts/build-android.sh Debug android-x64`.
Install the APK from `portable/artifacts/android`.
Open Dictate, allow microphone access, enable the voice keyboard, and download a speech model.
Choose Dictate from Android's keyboard picker while a text field has focus.
Tap **Start dictation**, speak, then tap **Stop and insert**.
Use **Keyboard** to return to another keyboard.
The keyboard also supports replay, selected-word capture, undo, and raw transcript restoration.

The keyboard stops recording when it closes.
Changing the input field cancels pending work.
Dictation is disabled in password fields.
The project targets Android 8 or later on arm64 devices.
See the verification plan for tested devices.
Android x64 builds support emulator verification.
The generated APK uses a debug signing key until release signing is configured.

## Cleanup and credentials

Rule cleanup and dictionary replacement work offline.
Smart cleanup sends a transcript to Groq, DeepSeek, or ChatGPT when a repeat or dictionary term needs review.
The app accepts only ordered edits allowed by the Mac cleanup guard.
It uses the rule result after a three-second timeout or a rejected reply.
The shared request manager limits requests to 12 per minute, two active requests, and 32 accepted cached results.

Windows stores credentials in Credential Manager.
Linux uses the desktop Secret Service through `secret-tool`.
Android encrypts credentials with an Android Keystore key.
Missing credential services produce setup errors.
The app does not save credentials as plain text.
ChatGPT uses the existing app's sign-in flow and non-public cleanup endpoint.
Changes to that endpoint can break cleanup.

## Verify the core

```sh
python3 portable/scripts/export-mac-fixtures.py
dotnet test portable/Tests/Dictate.Tests.csproj
```

The fixtures preserve the existing Mac cleanup-guard cases.
The tests also check dictionary matching and cancelled recognition.
The Mac implementation remains the reference until platform parity is verified.
See [the verification plan](docs/verification.md) for platform checks and current gaps.
