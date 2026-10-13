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
Platform shortcuts, model names, and setup controls follow the native platform.
The native window frame follows the operating system.

The Mac bridge retains microphone capture, fn shortcuts, text delivery, and Keychain. Electron owns login-item integration.
The old Swift window is removed. The Phonon runtime remains behind the native Mac adapter.
The Mac build keeps the bundle identifier and signing identity.
A first-launch import copies legacy preferences and transcripts into the shared data directory.
The import leaves legacy sources unchanged and writes settings last to mark completion.

## Recognition and operations

Mac uses Phonon-2. Other platforms use quantized English Whisper `base.en-q5_1`. There is no engine selector.
Asset downloads enforce a size limit and check a pinned SHA-256 hash before atomic replacement.
Digital silence returns no transcript before model loading.
This check does not detect all non-speech noise.

Recording captures settings before recognition starts.
Release captures the target field.
Cancellation stops capture and retires the operation.
A late result cannot insert text or reset a newer operation.
Whisper owns one cached native model and serializes preparation and recognition through one gate.
Its lifetime and operation cancellation tokens stop pending recognition. It uses at most four CPU threads alongside any supported GPU.
Each recording captures Automatic or CPU-only mode with its settings.
A changed mode reloads the native context before recognition. CPU-only sets `UseGpu = false`.
Whisper.net loads one native library per process. The runtime order stays GPU-capable even if CPU-only runs first.
Mac uses `PhononEngine` through the existing Swift adapter.
It compiles the pinned Phonon runtime with the original frame-capacity patch and keeps the 34.9-second single-pass boundary.
On a Mac with a Neural Engine, Automatic first loads Core ML with `cpuAndNeuralEngine`.
GPU and CPU remain load fallbacks. CPU-only loads the same model with `cpuOnly`.
The native queue completes before managed cancellation returns. A cancelled result cannot reach text delivery.
A hardware change waits for previous model preparation before loading the next configuration.
The bundled model remains separate from editable settings and history.

Windows and Linux x64 keep Vulkan and CPU in the main service.
An isolated OpenVINO worker owns NPU inference because Whisper.net loads one native library per process.
The worker probes available devices before downloading the checksum-verified encoder archive.
Only an actual OpenVINO NPU device enables this path. The decoder remains on CPU.
The worker keeps its factory and processor across recordings. Its input accepts at most five minutes of PCM audio.
While the NPU worker is ready, the main service releases its fallback model.
Cancellation terminates the worker and retires its response before another recording can use the recognizer.
An observed worker failure discards its result and uses the main service's GPU or CPU path.
CPU-only skips and stops the NPU worker and sets `UseGpu = false` in the main service.
The worker packages the OpenVINO 2024.4 native libraries expected by the pinned Whisper.net runtime.

The desktop x64 runtime falls back to the bundled no-AVX library when required CPU instructions are unavailable.
Android builds the matching whisper.cpp revision with Vulkan and CPU backends in one native runtime.
The Android build resolves Vulkan 1.1 entry points dynamically to preserve CPU loading on Android 8.
Whistle executables, model downloads, process runners, and Android native libraries are removed.
Stored engine names remain readable for old settings and history.
Settings normalize to the platform profile without rewriting historical records.

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
