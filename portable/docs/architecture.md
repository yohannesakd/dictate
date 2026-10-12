# Shared Dictate architecture

`Core` owns cleanup, dictionary matching, provider requests, settings, history, shortcuts, and the dictation lifecycle.
`Speech` owns English Whisper, asset checks, and the native recognition lifetime.
`desktop/` owns the Electron windows, tray, clipboard, login settings, and shared HTML interface.
`portable/Desktop` owns the headless native service and platform adapters.
`Android` owns the local WebView bridge, native activity, microphone, input-method service, input connections, and Keystore.

## Desktop interface

macOS, Windows, and Linux package the same HTML, CSS, JavaScript, and bundled font.
Electron disables hardware acceleration and WebGL.
The renderer has no Node.js access and uses a sandboxed preload with narrow commands.
The main process exchanges bounded JSON lines with one owned native service through private pipes.
There is no local HTTP API. Service failure rejects pending commands without replay.
The service retains the native main loop and owns microphone capture, delivery, settings, and recognition.
Closing the window hides it. Quitting stops the service before Electron exits.
The floating indicator receives only dictation phase and audio level.
It preserves destination focus and disappears when idle. Warnings appear in the app.
All four apps use the same HTML interface, Inter font, light theme, and software rendering.
Only platform shortcut names and setup controls differ.
The native window frame follows the operating system.

The Mac bridge retains microphone capture, fn shortcuts, text delivery, and Keychain. Electron owns login-item integration.
The old Swift app and Phonon paths are removed.
The Mac build keeps the bundle identifier and signing identity.
A first-launch import copies legacy preferences and transcripts into the shared data directory.
The import leaves legacy sources unchanged and writes settings last to mark completion.

## Recognition and operations

Every app uses quantized English Whisper `base.en-q5_1`. There is no engine selector.
Asset downloads enforce a size limit and check a pinned SHA-256 hash before atomic replacement.
Digital silence returns no transcript before model loading.
This check does not detect all non-speech noise.

Recording captures settings before recognition starts.
Release captures the target field.
Cancellation stops capture and retires the operation.
A late result cannot insert text or reset a newer operation.
Whisper owns one cached native model and serializes preparation and recognition through one gate.
Its lifetime and operation cancellation tokens stop pending recognition. It uses the CPU and at most four threads.
The x64 runtime falls back to the bundled no-AVX library when required CPU instructions are unavailable.
Whistle executables, model downloads, process runners, and Android native libraries are removed.
Stored engine names remain readable for old settings and history. Settings normalize to Whisper without rewriting historical records.

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
X11 supplies global shortcuts. Electron owns the indicator.
Wayland shortcut integration remains open.
Unreadable fields require copying from History.

Android snapshots the input connection and binding generation.
A binding change cancels pending work.
Native input calls run on Android’s main thread.
Closing the keyboard stops capture. Password fields disable dictation.
The activity and voice keyboard embed the shared interface assets in a WebView.
Only bundled assets from `https://dictate.local` are served. Navigation, file access, and external resources are blocked.
The JavaScript bridge accepts bounded requests and explicit commands. It never returns credential values.
`Core/AppCommands.cs` validates settings, provider names, and dictionary entries for both hosts.
Both Android views share one session and serialized settings writes.
Focused Android editors and the permanent dictation test field retain their native input connection during state updates.

Credentials use each operating system’s protected credential service.
The core does not save credentials as plain text.
