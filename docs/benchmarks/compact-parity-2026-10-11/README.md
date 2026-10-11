# Compact interface verification

Branch: `feat/electron-desktop`. Date: October 11, 2026.

## Change

The desktop window changed from 580 by 720 pixels to 480 by 600 pixels.
The minimum window is 400 by 480 pixels. Text sizes remain unchanged.
Section gaps, recording padding, list spacing, and the usage chart are smaller.
Android now packages the same HTML, CSS, JavaScript, and fonts through a local WebView.
Native platform adapters still own text delivery, credentials, microphone access, and operating system integration.

Android retains focused input elements during state updates. Its permanent dictation field remains outside the replaced content area.
This preserves the native input connection while recording state changes.
The keyboard displays no warnings. The app displays warnings.
The desktop host removes service event listeners before closing its windows.
This prevents a late process exit from raising an error against a destroyed window.

## Completed checks

The packaged app passed full interface checks and both engine restart checks on Mac, Windows, and Linux.
Both desktop sizes passed horizontal overflow checks.
The checks cover clipboard access, history search, dictionary changes, cleanup controls, provider identity, draft preservation, recording, cancellation, and indicator behavior.

The Mac package passed strict signature verification and is installed at `/Applications/Dictate.app`.
Settings and history retained their exact hashes. See `mac/install.json`.
The prior Electron package is retained at `/tmp/dictate-compact-install.kS6wyV/Dictate.previous.app`.

Windows checks ran in Saibot's interactive Windows 11 session.
Linux checks used real GTK fields in an isolated X11 session on Saibot Linux.
Linux capture used synthetic silence. Physical Linux microphone behavior and Wayland shortcuts remain unverified.

The Android Debug package passed shared interface checks and the existing native delivery checks.
Both models prepared through the shared interface. Engine selections reached persistent settings.
The checks cover clipboard access, history search, cleanup settings, provider identity, and preservation of active editors.
The native field checks cover Unicode, correction guards, stale bindings, Keystore, microphone capture, and both CPU models.
All 60 English audio runs completed. Cancellation and recovery passed for both engines.
Physical Android microphone quality, latency, and battery behavior remain unverified.

The final Android Release package started after installation and two complete process restarts.
Both engine menu selections persisted. Both engines entered listening and returned to idle after cancellation.
Repeat delivered `Sample transcript.` through the system keyboard into the app's WebView and Android Settings' native search field.
The WebView retained its text and input connection after recording and cancellation.
The native search field passed Undo and Original restoration. Original changed the text to `sample raw`.
The password field disabled Record, Repeat, and Add word.
Current-process Android runtime logs contained no errors.

The WebView does not provide the complete extracted text required by the existing correction guard.
Undo safely refused that field. Its warning appeared only in the main app, while the keyboard remained clear.
This platform limitation remains. Native Android fields support correction when they provide complete readback.

The Android emulator was created for this task. It used Android 15, ARM64, two CPU cores, and software graphics.
The existing emulator belonged to another session and remained unchanged.
Android's default image required more disk space than the Mac had available.
A separate sparse data image allowed this task's emulator to start without changing other device data.

## Execution evidence

| Check | Dispatch job | Result |
| --- | --- | --- |
| Core, transport, initial desktop packages | `7d1fc447-e4d1-4784-b98f-d32acea987ab` | Exit 0. |
| Final desktop packages and Linux checks | `76d5bde4-8624-4159-b5fb-9df741b83155` | Exit 0. |
| Android Debug package | `37196255-f1a8-41c5-9c78-94ff41975a7a` | Exit 0. |
| Initial Android Release package | `d1d246cd-fa69-4854-8523-62be43c5bdde` | Build passed. Runtime startup failed. |
| Final Android Release package | `9ebff050-df14-4ac4-8442-8135f89596ca` | Exit 0. |

Mac checks exited through `open -n -W`. Windows checks used the repository's scheduled interactive runner.
The `mac`, `windows`, and `linux` folders contain their final check results and process snapshots.
The `android` folder contains the instrumentation result and complete audio evaluation.
The screenshots are in [`docs/screenshots/compact`](../../screenshots/compact).
These process snapshots are not a controlled memory benchmark.

## Issues resolved during verification

Reading `enabled_input_methods` caused an Android startup failure.
The bridge now uses `InputMethodManager.EnabledInputMethodList`.
The [Android API reference](https://developer.android.com/reference/android/provider/Settings.Secure.html#ENABLED_INPUT_METHODS) specifies this API for apps targeting Android 14 or later.

The initial Release package failed to register `MainActivity.n_onCreate` and could not start.
Debug registration worked. Release now sets `AndroidEnableMarshalMethods` to `false` and uses runtime registration.
The [.NET issue record](https://github.com/dotnet/maui/issues/35209) describes the same startup failure and workaround.
A clean Release build removed stale generated Java classes after this setting changed.

Instrumentation cannot faithfully provide the keyboard's system connection inside the instrumented keyboard app.
Direct WebView connections also require Chromium's input thread.
The lasting instrumentation checks the shared interface and a real native field.
System keyboard behavior requires a separate device check outside instrumentation.

The device driver can select its test keyboard during text entry.
Native keyboard checks used snapshot references without driver text entry. Platform commands selected Dictate when necessary.
The T3 Device panel remains open during these checks.

The preview helper binds to loopback so the existing private Tailscale Serve route can own its external port.
No Serve routes or Funnel settings were changed.
