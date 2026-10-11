# Verify the shared app

The [current result record](../../docs/electron-desktop-verification.md) lists tested machines and remaining limits.
The [October 10 archive](verification-2026-10-10.md) retains the original warning investigation and earlier platform results.

## Core and CPU checks

```sh
dotnet test portable/Tests/Dictate.Tests.csproj
dotnet run --project portable/Checks -- --evaluate portable/fixtures/speech/samples.json portable/models portable/artifacts/audio.json
```

The speech check uses six checksum-verified English fixtures, five passes, and both selected profiles.
It checks cancellation and recovery after the audio runs.
Output records model preparation and recognition separately.
Parent memory after Whistle exits does not include the child’s peak memory.
Use the dedicated comparison harness for resource comparisons.

## Linux checks

Build the Linux Electron package and `portable/NativeChecks` in Release mode.
Run `portable/scripts/check-native.sh` on a Linux worker with Docker.
The check opens GTK fields and the shared app in an isolated X11 display.
It verifies Unicode readback, undo, stale-focus refusal, edited-text refusal, all pages, copy, dictionary, cleanup, recording, and both engine choices after process restart.
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

The runner checks real Edit controls, correction guards, microphone restart, and both CPU engines.
It checks Electron pages, controls, clipboard, recording, indicator isolation, and both engine choices after process restart.
Other applications must not take focus during native delivery checks.
An observed focus change must stop insertion.

## Mac checks

```sh
Tests/run-insertion.sh
Tests/run-keyboard.sh
./build.sh --checks
open -n -W build/Dictate.Checks.app --args /tmp/dictate-checks portable/fixtures/speech portable/models
```

Use absolute argument paths when launching through `open`.
Normal LaunchServices launch is required for correct microphone permission attribution and application identity.
The signed check bundle uses the app’s stable identifier.
It checks AppKit fields, correction guards, Keychain, and microphone restart.
It verifies CPU recognition, field delivery, and the six-fixture audio matrix.

## Electron interface checks

Seed an isolated data directory with `node desktop/scripts/seed-checks.cjs /absolute/data`.
Run the packaged Electron executable with `--data-directory /absolute/data --verify-desktop /absolute/results`.
The check uses the real renderer, native service, clipboard, and microphone adapter.
It saves screenshots and exits with a result code.
The normal recording button must hide the main window before capture starts.
Warnings stay in the main window, and the indicator remains unfocused.
Run twice more with `--verify-restart whistle` and `--verify-restart whisper` to check persistence across complete process restarts.
On macOS, launch the signed package through `open -n -W` with absolute argument paths.
Keep the Mac unlocked and the desktop active.
The Linux runner supplies synthetic silence to test capture lifecycle.

## Android checks

Build an ARM64 Debug APK and install it on the ARM64 emulator.
Open the device in the T3 Device panel before interaction.
Install the JFK fixture at the app’s internal `files/device-check.wav` path.
Copy the speech fixtures into `files/evaluation` to enable the audio matrix.
Grant microphone permission and run the instrumentation.

```sh
adb shell am instrument -w dev.local.dictate.portable/dev.local.dictate.portable.DeviceChecks
```

The instrumentation checks a real EditText, binding changes, correction guards, Keystore, microphone capture, and both selected speech profiles.
Verify both engine menu choices and persistence after restart through the visible Settings page.
Physical Android microphone, latency, and battery checks remain open.
