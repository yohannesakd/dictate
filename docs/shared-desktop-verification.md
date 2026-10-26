# Shared desktop verification archive

This archive describes an earlier release. See [hardware modes](acceleration.md) and [current verification](benchmarks/platform-speech-2026-10-13/README.md).

This record covers the replaced Avalonia interface. See [the later Electron record](electron-desktop-verification.md).

Branch: `feat/shared-desktop-ui`. Verification date: October 11, 2026.

That release compiled the same Avalonia window, indicator, and engine menu.
They share the Inter font, light theme, layout, cleanup core, and CPU speech profiles.
Native title bars, shortcut names, and permission setup follow the operating system.
The app uses software rendering on all three desktops.

Avalonia was already present in the portable app.
Its renderer supports a shared appearance across platforms. See [Avalonia’s rendering description](https://docs.avaloniaui.net/docs/welcome).
Reusing it avoids adding Electron’s Chromium process architecture. See [Electron’s process model](https://www.electronjs.org/docs/latest/tutorial/process-model).
This choice is an implementation decision, not a measured memory comparison against Electron.

## Selected profiles

The header and tray menus offer **Whistle** and **Whisper**.
The choice persists after restart and applies to future recordings.
An active recording retains its captured settings.

Whistle uses the pinned 16,919,407-byte model from revision `ca5287601bef25af26dcf2e1b2bdc0843a7c19e5`.
Whisper uses the pinned 59,721,011-byte English `base.en-q5_1` model from revision `5359861c739e955e79d9a303bcbc70fb988958b1`.
Downloads enforce size limits and SHA-256 verification.
The app fixes recognition to English and uses at most four threads.

These profiles retain Whistle’s lower resource use and Whisper’s faster long-recording performance on the tested x64 machine.
The [earlier quality and resource comparison](whistle-whisper-evaluation.md) explains that choice.
Older Whisper weights remain available only to reproduce diagnostic comparisons.
The app does not offer those weights in its menus.

## Results

Twenty-five core tests passed.
They include the frozen Mac cleanup cases, cancellation isolation, settings snapshots, digital silence, and delivery-evidence persistence.

**Mac:** The M4 Pro Mac passed the shared pages, both menu choices, and persisted settings.
Signed native checks passed Unicode delivery, selection replacement, raw restoration, undo, stale-focus refusal, edit guards, and cancellation.
The app’s own dictionary field received text.
The indicator preserved external focus, disappeared when idle, and excluded warnings.
Keychain and microphone restart passed.
Both selected models transcribed the JFK fixture and delivered text into real AppKit fields.

The new app is installed in `/Applications/Dictate.app` and passes strict signature verification.
The first-launch import preserved all 10 existing history entries and the dictionary.
Existing IDs remain unchanged. Older records without IDs receive new IDs.
Legacy files remain unchanged.
The previous app remains in `/tmp/dictate-shared-rollback.EgK9HW/Dictate.app` on the Mac.

**Windows:** Saibot’s Windows 11 desktop passed native field delivery, correction guards, two microphone cycles, and both CPU recognizers.
The Whisper no-AVX path passed with .NET hardware intrinsics disabled.
The shared pages rendered correctly.
Both engine choices saved the fixed English profile and survived restart.
F8 recording and Escape cancellation passed with an external field focused.

**Linux:** Saibot Linux passed real GTK insertion, Unicode readback, undo, stale-focus refusal, and edited-text refusal.
An isolated X11 session rendered the shared pages and preserved engine selection after restart.
This environment is WSL and does not establish physical Linux microphone behavior.

**Android:** The Android 15 ARM64 emulator passed real EditText delivery, stale-binding refusal, edit guards, Keystore, and microphone capture.
Both selected models passed CPU recognition, field delivery, cancellation, and recovery.
The visible Settings menu offered both choices and preserved selection after restart.
Android retains its native voice-keyboard interface.

The six English fixtures completed five passes per engine on all four environments, with no recognition-process errors.
Fixtures cover human speech, added noise, short dictation, technical words, long dictation, and digital silence.
Android also retained one older German diagnostic fixture, giving 70 total runs.
That extra fixture does not establish German support for the English Whisper profile.

Raw audio results and screenshots are in [the evidence folder](benchmarks/shared-desktop-2026-10-11).
Recognition-process success does not mean every transcript is correct.
These runs verify operation under load. They do not replace the dedicated resource measurements.
Parent memory after Whistle exits excludes its child’s peak memory.

## Delivery messages and limits

An exact digital-silence check prevents Whisper from inserting a word for zero-valued audio.
Other non-speech sounds can still produce text.
Whistle previously produced “Thank you.” for a hum fixture.

The original T3 Code warning cannot prove text loss because its log contains no observed field or caret evidence.
The [archived investigation](../portable/docs/verification-2026-10-10.md) retains that finding.
New receipts preserve mismatch details without storing the target field text.
A caret-only mismatch records `sent` and explains the uncertainty inside the app.
It does not enable correction or automatic replay.
A text mismatch remains failed delivery.

Whistle supports the tested Mac ARM64, Windows x64, Linux x64, and Android ARM64 packages.
Intel Mac Whistle and Android x64 Whistle remain unsupported.
Other desktop architectures require verified native binaries.
Physical Android microphone quality, battery use, and heat remain untested.
Wayland global shortcuts, unreadable Linux fields, and nonstandard Windows correction remain limited.
Live provider accounts and sign-in were not exercised.

## Build evidence

Dispatch performed Linux builds and tests on Saibot.
Job `c65bc7e9-f1b9-4509-a109-794e3f05980d` passed core tests and native Linux interface checks.
Job `671dedf0-04a2-433c-9c64-304a283e76e4` passed restart persistence for both Linux menu choices.
Job `9cb00f3f-8d57-4870-9070-873666350ddf` completed the selected Linux audio profiles.
Job `33b8571c-64ea-4dec-98e2-903040bfda8f` produced the installed ARM64 Android package.
Job `e3ffd52c-10c5-4f93-b77d-065bdfb821e6` rebuilt Android after removal of the obsolete idle-indicator setting.
Job `175d25b7-cff2-4c5b-9fc2-fb40a84cc736` verifies the final core and Windows package.
Native Mac builds used Xcode and .NET directly on the Mac.
Windows checks used the personal interactive desktop session.
