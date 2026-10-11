# Electron desktop verification

Branch: `feat/electron-desktop`. Verification date: October 11, 2026.

Dictate now uses one Electron interface on macOS, Windows, and Linux.
The [design language](design-language.md) follows the selected quiet Mac style.
Neutral surfaces, compact controls, bundled Inter, and restrained blue accents replace the default Fluent controls.
Native window controls and shortcut labels follow the operating system.

The desktop packages retain the shared cleanup core, CPU speech engines, and native platform adapters.
Android retains its native activity and voice keyboard. Android, Core, and Speech source files did not change.

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

### macOS

The signed Apple silicon package passed the interface checks and strict signature verification.
The app is installed at `/Applications/Dictate.app`.
Settings, the dictionary, and all 10 existing history entries retained their exact file hashes after installation.
The previous app remains at `/tmp/dictate-electron-rollback.OQc3QT/Dictate.previous.app` on the Mac.

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
The packaged app is at `C:\Users\Yohannes\dictate-electron-20261011\package\Dictate.exe`.

### Linux

Saibot Linux passed real GTK insertion, Unicode readback, undo, stale-focus refusal, and refusal after edits.
The packaged app ran in an isolated X11 session with software rendering.
Synthetic silence supplied the capture lifecycle check.
Physical Linux microphone behavior and Wayland shortcuts remain unverified.

### Android

The unchanged Android app retains the earlier ARM64 emulator results for both engines, field delivery, microphone capture, and Keystore.
See [the previous platform record](shared-desktop-verification.md) for the audio matrix and emulator limits.
Electron does not run the Android keyboard.

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
The [verification instructions](../portable/docs/verification.md) explain how to repeat the checks.
