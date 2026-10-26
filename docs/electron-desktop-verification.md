# Electron desktop verification

This archive describes an earlier release. See [hardware modes](acceleration.md) and [current verification](benchmarks/platform-speech-2026-10-13/README.md).

Branch: `feat/electron-desktop`. Verification date: October 11, 2026.

That release used one Electron interface on macOS, Windows, and Linux.
The default window is 480 by 600 pixels. The minimum window is 400 by 480 pixels.
The [design language](design-language.md) follows the selected quiet Mac style.
Neutral surfaces, compact controls, bundled Inter, and restrained blue accents replace the default Fluent controls.
Native window controls and shortcut labels follow the operating system.

The desktop packages retain the shared cleanup core, CPU speech engines, and native platform adapters.
Android packages the same interface assets in a local WebView.
Its native activity and voice keyboard retain permissions, input connections, microphone capture, and Keystore access.
Both hosts use the same core command validation.

## Results

Twenty-five core tests and three private transport tests passed through Dispatch on Saibot Linux.
The transport tests cover concurrent replies, fragmented responses, process failure, command limits, and refusal to replay work.

The packaged Electron app passed these checks on all three desktops:

- Home, History, and every Settings section opened.
- Transcript and original copy actions reached the system clipboard.
- History search, dictionary changes, cleanup rules, and provider selection worked.
- Whistle and Whisper selections persisted through complete process restarts.
- Both selected CPU profiles prepared through the packaged native service.
- Late state updates preserved input text, focus, and the caret.
- A delayed dictionary save reply preserved a newer draft.
- Changing cleanup providers cleared the other provider's unsaved credential field.
- The Record button hid the main window before capture started. Cancellation stopped capture and hid the indicator.
- The indicator remained nonfocusable and excluded warnings.
- The renderer used a sandbox, isolated context, and disabled hardware acceleration.
- The app and its owned service exited after each check.
- Both desktop window sizes fit without horizontal overflow.

### macOS

The signed Apple silicon package passed the interface checks and strict signature verification.
The app is installed at `/Applications/Dictate.app`.
Settings, the dictionary, and all 10 existing history entries retained their exact file hashes after installation.
The previous app remains at `/tmp/dictate-electron-rollback.OQc3QT/Dictate.previous.app` on the Mac.
The previous Electron package remains at `/tmp/dictate-compact-install.kS6wyV/Dictate.previous.app`.

The compact package passed full interface checks and both engine restart checks.
Late service events no longer reach closed windows during shutdown.

The final external AppKit field check passed after the Mac was unlocked and the verification field was activated.
Unicode delivery, raw restoration, undo, selection replacement, focus guards, edit guards, and cancellation passed.
Both CPU engines transcribed the JFK fixture and delivered the full transcript into the external field.
Keychain storage and deletion, two physical microphone cycles, and the native shortcut startup check passed.
Both engines completed six English audio fixtures over five passes, followed by cancellation and recovery checks.
Settings and history retained their exact file hashes after these checks.

### Windows

Saibot's interactive Windows 11 session passed Unicode insertion, selected-word capture, selection replacement, raw restoration, and undo.
Focus, edit, caret, and cancellation guards passed.
Both selected CPU models transcribed the JFK fixture and delivered text into real Edit controls.
Two physical microphone capture cycles returned audio.
The compact package is at `C:\Users\build-user\dictate-compact-final-20261011\package\Dictate.exe`.

### Linux

Saibot Linux passed real GTK insertion, Unicode readback, undo, stale-focus refusal, and refusal after edits.
The packaged app ran in an isolated X11 session with software rendering.
Synthetic silence supplied the capture lifecycle check.
Physical Linux microphone behavior and Wayland shortcuts remain unverified.

### Android

The Android 15 ARM64 emulator passed shared interface checks for navigation, settings, both engines, model preparation, clipboard, history search, and draft preservation.
State updates retained the active editor and the permanent dictation field.
A separate native field passed Unicode insertion, correction guards, stale binding refusal, Keystore, and microphone capture.
Both CPU engines transcribed the JFK fixture and delivered the transcript into that field.
Both engines completed six English audio fixtures over five passes. Cancellation and recovery also passed.

Android uses its system keyboard picker instead of desktop shortcuts.
The keyboard uses the shared styles, engine menu, recording control, and correction actions.
Warnings remain in the main app.
The final Release package passed system keyboard delivery, both engine restart selections, recording cancellation, and password-field refusal.
An external native search field passed Repeat, Undo, and Original restoration.
WebView insertion passed, but its incomplete readback prevented safe correction. The app displayed that refusal; the keyboard displayed no warning.
Physical Android microphone quality, latency, and battery behavior remain unverified.

## Resources and evidence

The interface uses plain HTML, CSS, and JavaScript, with no production UI framework dependency.
Electron still includes Chromium and adds memory and package overhead.
The signed Mac package occupies approximately 368 MB without downloaded speech models.
This implementation is not a measured reduction in total memory versus the previous app.

The evidence folder contains Electron process snapshots, not a controlled idle-memory benchmark.
Working-set values include shared pages. Their sum does not represent unique memory, and the native service is listed separately.
A software compositor can appear as a GPU process even when hardware acceleration is disabled.

The [English quality comparison](whistle-whisper-evaluation.md) still describes the unchanged recognizers and model choices.
Its total application memory figures do not describe this Electron interface.

See [saved screenshots](screenshots/electron) and [execution evidence](benchmarks/electron-desktop-2026-10-11).
See [compact screenshots](screenshots/compact) and [the compact parity record](benchmarks/compact-parity-2026-10-11).
The [verification instructions](../portable/docs/verification.md) explain how to repeat the checks.
