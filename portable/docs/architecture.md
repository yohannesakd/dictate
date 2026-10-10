# Portable architecture

The existing Swift app remains independent.
The C# core ports its behavior for Windows, Linux, and Android.
Mac integration can follow after the portable contracts and platform behavior are stable.
The exported Mac fixtures preserve the current cleanup contract during that work.

## Ownership

`Core` owns cleanup rules, dictionary matching, the ordered-edit guard, provider requests, settings, history, shortcuts, and the dictation lifecycle.
`Speech` owns model downloads and CPU recognition through Whisper.net.
`Desktop` owns Windows and Linux microphones, field delivery, credentials, startup, shortcuts, and windows.
`Android` owns its microphone, input-method service, field connections, Keystore, and activities.
Platform code reports what delivery observed.
The core saves that receipt without converting uncertain delivery into verified text.

## Dictation lifecycle

Recording captures a settings snapshot.
Release captures the target field before recognition starts.
Cancellation retires the operation and stops capture.
A late recognition result cannot insert text or reset a newer operation.
Each completed delivery records its transcript, cleanup result, delivery outcome, and stage timings.
Delivery does not retry automatically.
A cancelled delivery can leave text already posted.

Undo and raw restoration require a verified insertion at a collapsed caret.
The field identity, complete text, and caret must still match the recorded receipt.
Correction expires after five minutes.
A correction owns the same delivery phase as replay.
Recording cannot overlap either operation.

## Platform boundaries

Windows snapshots the foreground window, focused child, and process.
Unicode input stops when that identity changes.
Standard Edit controls support readback and correction.
Other controls can receive text without verification.

Linux uses AT-SPI, the desktop accessibility interface, for editable fields.
Capture and delivery refresh focus state.
Insertion replaces the selected range, rather than the complete field.
Unreadable fields require manual copying.
X11 supplies global shortcuts and a non-activating indicator.
Wayland requires recording controls until a compositor shortcut integration exists.

Android snapshots an input connection and its binding generation.
Changing the input field retires that generation and cancels pending work.
Native input calls run on Android's main thread.
Closing the keyboard stops capture.
Password fields disable dictation.
The activity and keyboard apply system-bar insets to keep controls visible.
The inset policy follows [Android's layout guidance](https://developer.android.com/develop/ui/views/layout/edge-to-edge-manually).

## CPU behavior

The speech factory disables GPU use.
Desktop x86 devices select the no-AVX library when required CPU instructions are unavailable.
Android selects the platform CPU library.
The Android project references the runtime package directly so its native libraries enter the APK.
Android packages preserve untrimmed assemblies for settings serialization and library metadata.
They use the Mono runtime without ahead-of-time compilation.
The interface uses software rendering.
Tiny, base, and small models expose a memory and accuracy choice.
This implementation does not preserve the Mac's speech engine or its Neural Engine latency.

The CPU path omits repeated speculative recognition and Neural Engine warm-up.
Those operations can delay final recognition on a small CPU.
Add a CPU warm-up policy only after measured latency supports it.

## Verification tools

Core tests preserve the exported Mac cases and operation contracts.
The speech check uses the public JFK recording and an actual CPU model.
The Linux check creates a GTK field inside an isolated display container.
It checks Unicode insertion, undo, focus changes, text changes, and desktop navigation.
Android debug builds include `DeviceChecks` instrumentation.
Release builds exclude that instrumentation.
Device checks use an actual EditText, microphone, Keystore, and speech library.
Their fixed recording must be installed in the app's internal `files/device-check.wav` path.
