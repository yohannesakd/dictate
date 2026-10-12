# Verify the shared app

The [Whisper-only record](../../docs/whisper-only.md) lists the current change and verification limits.
The [October 10 archive](verification-2026-10-10.md) retains the original warning investigation and earlier platform results.

## Core and speech checks

```sh
dotnet test portable/Tests/Dictate.Tests.csproj
dotnet run --project portable/Checks -- --evaluate portable/fixtures/speech/samples.json portable/models portable/artifacts/audio.json
```

The speech check uses six checksum-verified English fixtures and five passes through Whisper.
It checks cancellation and recovery after the audio runs.
Output records model preparation and recognition separately.
Parent memory includes the managed app and its cached Whisper model. Earlier comparisons retain their original measurement limits.

## Linux checks

Build the Linux Electron package and `portable/NativeChecks` in Release mode.
Run `portable/scripts/check-native.sh` on a Linux worker with Docker.
The check opens GTK fields and the shared app in an isolated X11 display.
It verifies Unicode readback, undo, stale-focus refusal, edited-text refusal, all pages, copy, dictionary, cleanup, recording, and the Whisper profile after process restart.
The isolated display does not verify a physical microphone or Wayland shortcuts.

## Windows checks

Package Electron for `win32 x64` with the self-contained native service.
Publish NativeChecks separately for `win-x64`.
Put the pinned speech models in the fixture directory's `models` folder.
Use an interactive Windows session for native input checks.

```powershell
powershell -NoProfile -File portable/scripts/check-windows.ps1 `
  -PackageDirectory desktop/dist/Dictate-win32-x64 `
  -NativeChecksDirectory portable/artifacts/windows/native `
  -OutputDirectory portable/artifacts/windows/results `
  -FixtureDirectory portable/fixtures/speech
```

The runner checks real Edit controls, correction guards, microphone restart, and Whisper.
It checks Electron pages, controls, clipboard, recording, indicator isolation, and the Whisper profile after process restart.
Other applications must not take focus during native delivery checks.
An observed focus change must stop insertion.

## Mac checks

```sh
Tests/run-insertion.sh
Tests/run-keyboard.sh
./build.sh --checks
check_output=$(mktemp -d /tmp/dictate-checks.XXXXXX)
open -n -W build/Dictate.Checks.app --args "$check_output" "$PWD/portable/fixtures/speech" "$PWD/portable/models" &
check_launcher=$!
for attempt in {1..100}; do
  if test -f "$check_output/fixture-command.ready"; then
    osascript -e 'tell application "System Events" to tell first application process whose bundle identifier is "dev.local.dictate.fixture"' \
      -e 'set frontmost to true' -e 'perform action "AXRaise" of window "Dictate verification field"' -e 'end tell'
    break
  fi
  sleep 0.05
done
wait "$check_launcher"
cat "$check_output/exit-code.txt"
```

Use absolute argument paths when launching through `open`.
Normal LaunchServices launch is required for correct microphone permission attribution and application identity.
The signed check bundle uses the app’s stable identifier.
The launcher activates the separate verification field after its ready file appears.
Leave the keyboard and mouse idle during delivery checks.
It checks AppKit fields, correction guards, Keychain, and microphone restart.
It verifies recognition, field delivery, and the six-fixture audio matrix.
For hardware switching and CPU-only isolation, use the [hardware check](../../docs/benchmarks/acceleration-2026-10-12/README.md).

## Electron interface checks

Seed an isolated data directory with `node desktop/scripts/seed-checks.cjs /absolute/data`.
Run the packaged Electron executable with `--data-directory /absolute/data --verify-desktop /absolute/results`.
The check uses the real renderer, native service, clipboard, and microphone adapter.
It saves screenshots and exits with a result code.
The normal recording button must hide the main window before capture starts.
Warnings stay in the main window, and the indicator remains unfocused.
Run once more with `--verify-restart whisper` to check the profile after a complete process restart.
On macOS, launch the signed package through `open -n -W` with absolute argument paths.
Keep the Mac unlocked and the desktop active.
The Linux runner supplies synthetic silence to test capture lifecycle.

## Android checks

Use the Dispatch-managed Android emulator or a physical Android device with wireless debugging.
Follow the Dispatch skill's Android-on-Saibot workflow. Keep local Mac emulators stopped during routine checks.
Build a Debug APK for the device's architecture and install it on that device.
Open the device in the T3 Device panel before interaction.
Install the JFK fixture at the app’s internal `files/device-check.wav` path.
Copy the speech fixtures into `files/evaluation` to enable the audio matrix.
Grant microphone permission. Enable and select the Dictate keyboard before you run the instrumentation.

```sh
adb shell am instrument -w dev.local.dictate.portable/dev.local.dictate.portable.DeviceChecks
```

The instrumentation checks the shared WebView, settings, the Whisper profile, model preparation, clipboard, history search, and live input preservation.
It also checks a real EditText, binding changes, correction guards, Keystore, microphone capture, and CPU Whisper recognition.
Verify the English Whisper profile and persistence after restart through the visible Settings page.
Physical Android microphone, latency, and battery checks remain open.
