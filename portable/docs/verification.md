# Portable verification

## Required behavior

The existing Mac app remains the behavior reference.
The requested Mac warning presentation change is included in this branch.
Keep the Mac speech runtime, model, build, and installation scripts unchanged.

The portable core preserves filler removal, stutters, scratch-that cleanup, literal dictionary matching, and the ordered-edit guard.
Each platform supplies microphone capture, a local speech runtime, text delivery, credential storage, and its interface.
The portable apps do not change the Mac's runtime or model.

## Mac warning checks

The indicator appears only during recording and processing.
Warnings appear on Home, History, and Settings until dismissed.
Warnings do not open the app or take focus from the target field.

On October 10, Mac delivery and controller checks passed through SSH on `saiMBP`.
Run `Tests/run-insertion.sh` and `python3 Tests/ControllerChecks.py` on a Mac.
The controller checks include a real indicator panel, idle visibility, and keyboard focus behavior.
They also check that a retained warning cannot reset a later recording.
Delivery checks cover partial text, changed text, and a different caret with complete text.
Those checks retain strict verification and refuse correction snapshots after a mismatch.
The full signed Mac build passed.
The verified build was installed in `/Applications/Dictate.app` and started without opening the app window.
The installed executable matches the checked build and passes strict signature verification.
The previous app remains at `/tmp/dictate-notices.RPe2Cy/Installed.previous.app` on the Mac.

A scratch interface check rendered the warning on all three app pages.
Its accessibility traversal did not reach the SwiftUI dismissal button.
Window capture through SSH failed with `could not create image from window`.
Visual dismissal verification remains incomplete.
The screenshots from that scratch check do not establish complete interface rendering.

The reported warning matches dictation `082229E2-537B-4DA3-A784-DE94B7DBBC90` at `2026-10-10T10:29:28Z`.
The target was `site.saibot.t3code.stable`.
Dictate posted 263 UTF-16 units and recorded `unexpected` after 160 milliseconds of verification.
The existing logs contain no observed field text or caret evidence for that event.
The user could not confirm whether text was missing.
The incident therefore cannot establish actual text loss or identify the precise mismatch.
New mismatch diagnostics record lengths, selection ranges, and whether text, the caret, or both differed.
The diagnostics exclude the target field text.
Dictate does not retry uncertain delivery.

## Core checks

Run the existing cleanup cases against the portable guard.
Include signed numbers, percentages, decimal notation, reordered names, unmatched dictionary terms, and repeated phrases.
Check that dictionary matches prefer the longest phrase and do not chain replacements.
Cancel recognition, start another recording, and resolve the old recognition request.
The old request must not insert text or reset the new recording.
Change settings while recognition runs.
The operation must retain the settings captured when recording started.
Check cleanup timeouts, request limits, duplicate requests, rejected replies, and incomplete provider replies.

## Speech checks

Load a real model with GPU use disabled.
Transcribe a fixed speech recording on a Linux worker.
Verify meaningful words from that recording.
Cancel transcription and verify that the next request completes.
Record the model, CPU runtime, and transcript with the check result.
Do not infer interactive latency from a build or unit test.

## Desktop checks

Build Linux and Windows distributions.
Render Home, History, and Settings with software rendering.
On X11, hold and release F8 while an external editable field has focus.
Verify the field receives the transcript.
Switch the focused field during recognition and verify that insertion stops.
Cancel during insertion and inspect remaining text.
Verify that uncertain delivery is recorded as `sent`, rather than `verified`.
On Windows, verify undo and raw restoration in a readable Edit control.
Change the text, caret, or field before correction.
The correction must refuse the changed field.
Test microphone interruption and app shutdown during recording.

The Windows native checks use a separate process with two standard Edit controls.
Run the checks in an interactive Windows desktop session.
The check process opens and closes its own windows and records two short microphone samples.
The PowerShell runner also checks CPU recognition and the Home, History, and Settings pages.
It checks F8 recording and Escape cancellation with an external field focused.
Keep other applications from taking focus during that check.

Publish the three check packages before running the Windows runner.

```sh
dotnet publish portable/NativeChecks -c Release -r win-x64 --self-contained false -o portable/artifacts/windows/native
dotnet publish portable/Checks -c Release -r win-x64 --self-contained false -o portable/artifacts/windows/speech
dotnet publish portable/Desktop -c Release -r win-x64 --self-contained false -o portable/artifacts/windows/app
```

Supply the public JFK WAV file and a model directory to the runner.
The speech check downloads `tiny.en` if that model is absent.

```powershell
powershell -NoProfile -File portable/scripts/check-windows.ps1 `
  -PackageDirectory portable/artifacts/windows `
  -OutputDirectory portable/artifacts/windows/results `
  -FixtureAudio portable/artifacts/jfk.wav `
  -ModelDirectory portable/models
```

## Android checks

Build an x64 APK and install it on the shared Android emulator.
Open the device in the T3 Device panel before interaction.
Verify Home, History, Settings, microphone permission, and keyboard selection.
Record through the voice keyboard and verify text insertion into a real EditText.
Change fields while recognition runs.
The old transcript must not enter the new field.
Close the keyboard while recording and verify microphone cleanup.
Verify undo, raw restoration, and refusal after a caret or text change.
Verify that password fields disable recording.
Save, read, and remove a test credential through Android Keystore.
Do not use real provider credentials for these checks.

## Remaining platform work

Wayland global shortcuts need a compositor-supported integration.
Unreadable Linux fields require copying from History.
Windows correction currently supports standard Edit controls.
Other Windows controls need an accessibility adapter for verified correction and selected-word capture.
Linux microphone hardware still requires a physical-device check.
Microphone capture remains untested in Saibot Linux's WSL environment.
The Windows F8 check remains incomplete after Saibot lost its connection during the final check run.
Android ARM64 packages compile, but physical ARM64 devices still need microphone and latency checks.
Provider cleanup has protocol tests but no live account checks.
Provider sign-in still needs a live account check.

The CPU path omits repeated speculative recognition and Neural Engine warm-up.
The Mac uses those operations to reduce release latency.
CPU devices need separate latency evidence before the same policy is added.
The portable model differs from Phonon-2.
Accuracy and interactive latency require recordings from each target device.

## Evidence

On October 10, 2026, Dispatch job `d5b5071a-fe3d-49f2-bb6e-f329a8a6fe81` passed the Linux checks on Saibot Linux.
It passed 22 core tests, CPU recognition, silence, active cancellation, and recovery.
It also passed the native GTK field checks and captured Home, History, and Settings with software rendering.
The local screenshots are under `portable/artifacts/saibot-linux`.

Dispatch job `95893162-e52c-4d62-808a-42f2fc12345d` published the Windows app and check packages on Saibot Linux.
Dispatch job `e8731468-fd01-4128-a176-33772f265111` published the final native check fixture.
On Saibot's Windows 11 desktop, native checks passed Unicode insertion, selection replacement, selected-word capture, raw restoration, and undo.
They refused stale fields, edited text, and changed carets.
Cancelled insertion left the field unchanged.
The microphone produced samples at 16 kHz in two recording cycles.
CPU recognition, silence, active cancellation, and recovery passed with normal hardware intrinsics and with hardware intrinsics disabled.
The Windows app opened Home, History, and Settings with software rendering.
The local screenshots are under `portable/artifacts/saibot-windows`.

The separate F8 check could not establish focus while Task Manager held the foreground.
The final retry did not return a result before Saibot became unreachable through SSH and Dispatch.
The task-owned Windows task is `DictateVerification-20261010`, with a ten-minute execution limit.
After Saibot returns, stop that task.
Close only its check processes before another run.
The check files are under `C:\Users\yohannes\AppData\Local\DictateVerification`.
Restore Task Manager if the temporary check left its window minimized.

The Android 15 emulator on Saibot repeated `DeviceChecks` and passed all native checks.
The voice keyboard also completed a silent recording cycle through Start dictation and Stop and insert.
It reported `No speech detected.` and left the input field unchanged.
Android reported a completed microphone operation, with no recording still running.
Home, History, and Settings remained usable after recording.

Dispatch job `1f3b591b-1ca0-49ae-b6e9-ae66cd8cbe25` passed CPU recognition and cancellation with .NET hardware intrinsics disabled.
That setting selected the no-AVX speech library on x64.

Dispatch job `97a8ec89-baa6-4a38-9b64-8eee85e4eba3` passed 22 core tests after the recording deadline fix.
An expired recording deadline cannot stop a newer recording.

Dispatch job `99537668-2dec-4cd3-842a-c67f7c9feebc` passed 22 core tests.
Those tests included all 57 exported Mac guard cases.
The job also transcribed the public JFK recording with `tiny.en` and GPU use disabled.
It cancelled an active recognition and completed the next recognition.

Dispatch job `deb2224b-b62c-4bdf-85e1-8e7e67364bde` published a Windows x64 distribution.
It repeated the CPU speech and cancellation check.
This result proves compilation, rather than Windows input or microphone behavior.

Dispatch job `afa45f05-1084-4c18-bc59-919119d24cbd` passed the Linux native field checks.
It checked Unicode readback, undo, a focus change within one window, and refusal after editing.
Home, History, and Settings produced distinct screenshots with software rendering.
Screenshots are local artifacts under `portable/artifacts/native`.

Dispatch job `b65fb227-5916-4ac8-82f2-fb7269444a55` built the final Android x64 debug APK with native CPU speech libraries.
Dispatch job `5a2f5c86-31b6-4ca2-bdd8-f9bbc704e144` built the final Android ARM64 release APK.
Both APKs are local artifacts under `portable/artifacts/android`.
The release package uses the standard debug signing key until a release signing key is configured.

On Android 16, `DeviceChecks` passed native Unicode insertion, undo, stale-binding refusal, and refusal after text changes.
It also passed encrypted credential storage, credential removal, microphone sampling, and CPU recognition of the public JFK recording.
The final debug package repeated those checks on Android 15 and passed the silence check.
These checks used the `tiny.en` model with GPU use disabled.
They do not establish physical-device latency or microphone accuracy.

On Android 15, interface checks verified Home, History, Settings, microphone permission, keyboard setup, recording, and native undo.
Closing the keyboard ended the microphone operation.
A password field disabled the recording button.
The activity and keyboard layouts account for the system bars.

Dispatch job `6954d95d-1b6e-4a4b-b9ee-34fef309abe5` passed CPU speech, silence, and active cancellation checks after the blank-audio fix.
The engine removes Whisper's `[BLANK_AUDIO]` annotation before delivery.
The silence check produced an empty transcript while the JFK recording retained its expected words.

The Mac build and installation scripts have no changes against `origin/main`.
Mac source and tests include the requested indicator and warning changes, with delivery mismatch diagnostics.
The portable core is a behavior port.
The Mac app does not yet consume that core.
