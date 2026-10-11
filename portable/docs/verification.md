# Verify the shared app

The [current result record](../../docs/shared-desktop-verification.md) lists tested machines and remaining limits.
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

Build `portable/NativeChecks` in Release mode.
Run `portable/scripts/check-native.sh` on a Linux worker with Docker.
The check opens GTK fields and the shared app in an isolated X11 display.
It verifies Unicode readback, undo, stale-focus refusal, edited-text refusal, three pages, and engine persistence after restart.
The isolated display does not verify a physical microphone or Wayland shortcuts.

## Windows checks

Publish Desktop, NativeChecks, and Checks for `win-x64` into one package directory.
Use an interactive Windows session for native input checks.

```powershell
powershell -NoProfile -File portable/scripts/check-windows.ps1 `
  -PackageDirectory portable/artifacts/windows `
  -OutputDirectory portable/artifacts/windows/results `
  -FixtureAudio portable/fixtures/speech/jfk.wav `
  -ModelDirectory portable/models
```

The runner checks real Edit controls, microphone restart, both CPU engines, the no-AVX path, three pages, and engine selection.
It also checks F8 recording and Escape cancellation with an external field focused.
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
It checks the shared pages, both menu choices, AppKit fields, correction guards, Keychain, and microphone restart.
It verifies CPU recognition, field delivery, and the six-fixture audio matrix.

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
