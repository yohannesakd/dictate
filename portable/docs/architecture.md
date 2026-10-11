# Shared Dictate architecture

`Core` owns cleanup, dictionary matching, provider requests, settings, history, shortcuts, and the dictation lifecycle.
`Speech` owns the two English CPU profiles, asset checks, and native recognition lifetimes.
`Desktop` owns the shared Avalonia interface and platform adapters.
`Android` owns the native activity, microphone, input-method service, input connections, and Keystore.

## Desktop interface

macOS, Windows, and Linux compile the same `MainWindow`, `FlowBar`, and tray menu.
The apps use the Inter font, a light theme, and software rendering.
Only platform shortcut names and setup controls differ.
The native window frame follows the operating system.

The Mac bridge retains microphone capture, fn shortcuts, text delivery, Keychain, and login-item integration.
The old Swift app and Phonon paths are removed.
The Mac build keeps the bundle identifier and signing identity.
A first-launch import copies legacy preferences and transcripts into the shared data directory.
The import leaves legacy sources unchanged and writes settings last to mark completion.

## Recognition and operations

The menu offers Whistle and quantized English Whisper `base.en-q5_1`.
Asset downloads enforce a size limit and check a pinned SHA-256 hash before atomic replacement.
Digital silence returns no transcript before model loading.
This check does not detect all non-speech noise.

Recording captures settings before recognition starts.
Release captures the target field.
Cancellation stops capture and retires the operation.
A late result cannot insert text or reset a newer operation.
Whistle runs in an owned process with bounded output and a deadline.
Cancellation waits for process cleanup.
Selecting Whistle unloads the cached Whisper model before inference.
Both recognizers use the CPU and at most four threads.

The core saves the raw transcript, cleanup result, delivery receipt, engine, model, and stage timings.
Delivery does not retry automatically.
Cancellation can leave characters already posted.
Correction requires verified insertion at a collapsed caret.
The process, field, complete text, and caret must still match.
Correction expires after five minutes.

## Native boundaries

macOS snapshots the application identity and focused Accessibility element.
Unicode events stop when the target changes.
Readable fields verify text and caret before correction becomes available.
A caret-only mismatch records `sent` with an app message and retains mismatch evidence.
A text mismatch remains a failed delivery.
Unreadable fields can receive events without readback and record `sent`.

Windows snapshots the foreground window, focused child, and process.
Standard Edit controls support readback and correction.
Other controls can receive text without verification.

Linux uses AT-SPI to replace the selected range in an editable field.
X11 supplies global shortcuts and the indicator.
Wayland shortcut integration remains open.
Unreadable fields require copying from History.

Android snapshots the input connection and binding generation.
A binding change cancels pending work.
Native input calls run on Android’s main thread.
Closing the keyboard stops capture. Password fields disable dictation.

Credentials use each operating system’s protected credential service.
The core does not save credentials as plain text.
