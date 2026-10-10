# Portable verification

## Required behavior

The existing Mac app remains the behavior reference.
Changes under `Sources`, `Tests`, `build.sh`, `Info.plist`, and the Mac installation scripts are outside this change.
Check those paths against the branch base before each commit.

The portable core preserves filler removal, stutters, scratch-that cleanup, literal dictionary matching, and the ordered-edit guard.
Each platform supplies microphone capture, a local speech runtime, text delivery, credential storage, and its interface.
The portable apps do not change the Mac's runtime or model.

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
Desktop microphone hardware and Windows input delivery still require physical-device checks.
Android ARM64 packages compile, but physical ARM64 devices still need microphone and latency checks.
Provider cleanup has protocol tests but no live account checks.
Provider sign-in still needs a live account check.

The CPU path omits repeated speculative recognition and Neural Engine warm-up.
The Mac uses those operations to reduce release latency.
CPU devices need separate latency evidence before the same policy is added.
The portable model differs from Phonon-2.
Accuracy and interactive latency require recordings from each target device.

## Evidence

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

The Mac source, tests, build, and installation scripts have no changes against `origin/main`.
The portable core is a behavior port.
The Mac app does not yet consume that core.
