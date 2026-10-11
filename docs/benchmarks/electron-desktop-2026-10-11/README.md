# Electron execution evidence

This folder records the Electron replacement on `feat/electron-desktop`.
Screenshots use seeded test transcripts. They contain no personal dictation or credentials.

## Successful runs

- Dispatch `77352eb9-c65e-42d7-be74-cdd7121d3477` packaged clean Windows and Linux outputs. Linux passed GTK, Electron controls, recording, indicator, and both restart checks.
- Dispatch `3cc281e6-2ff2-4849-9800-89360a2df9ad` passed 25 core tests and three Node transport tests.
- Windows interactive task `DictateElectron-20261011` returned zero. The task and the separate native-check task were removed.
- Mac `/tmp/dictate-electron-ui/test-results-complete` returned zero. Both `/tmp/dictate-electron-ui/restart-*` checks returned zero.

The Windows native checks also passed both selected CPU recognizers and two physical microphone cycles.
Linux used synthetic silence for the microphone adapter lifecycle.

`metrics.json` contains a process snapshot after the interface checks.
Values use Electron's native units of kilobytes. Shared resident pages can appear in several working sets.
These snapshots exclude native-service memory and are not steady-state resource benchmarks.

## Mac continuation

The signed Electron app is installed at `/Applications/Dictate.app` and runs with the default data directory.
The previous Avalonia bundle remains at `/tmp/dictate-electron-rollback.OQc3QT/Dictate.previous.app`.
The earlier Swift rollback remains at `/tmp/dictate-shared-rollback.EgK9HW/Dictate.app`.
Do not change the signing keychain or stored credentials.

The final AppKit field check stopped because the Mac locked.
`/tmp/dictate-electron-ui/native-results-fourth/fixture-command.focus.json` reported `com.apple.loginwindow`, an inactive app, and a non-key window.
A later diagnostic now refuses insertion unless the external fixture has focus.
The headless service, microphone adapter, and Electron UI use the existing stable signing identity.

After the user unlocks the Mac, stop only the installed task-owned Dictate process before native checks.
Leave unrelated apps and user data intact.
Launch the signed checks through LaunchServices:

```sh
cd /tmp/dictate-electron-ui
open -n -W build/Dictate.Checks.app --args \
  /tmp/dictate-electron-ui/native-results-final \
  /tmp/dictate-engines-20261011/portable/fixtures/speech \
  /tmp/dictate-shared-ui/check-models
```

The bundle has already been rebuilt with the separate `dev.local.dictate.fixture` AppKit target.
The checker verifies insertion, correction guards, Keychain, microphone restart, both CPU models, and the audio matrix.
Keep the Verified requirement. Do not weaken the delivery guard to accommodate a locked desktop.
Save successful results here and update the current verification record.
Reopen `/Applications/Dictate.app` after the checks.

## Corrected development failures

The Electron packager and signer require named exports in their pinned versions.
Electron 44 clipboard calls return promises. The app now awaits them.
The Mac service originally read Keychain credentials during startup and blocked on authorization.
Credentials now load when Cleanup opens, and Mac Keychain calls run away from the native event loop.
The service executable retains the stable `dev.local.dictate` signing identifier.
The Mac app originally remained alive after the asynchronous quit sequence.
The app now exits after the owned service stops. Final process checks left no verification processes running.

The Linux test container needs GTK, ALSA, accessibility services, Chromium libraries, and an isolated display.
The container uses the actual worker user ID to keep outputs writable.
Its Chromium sandbox requires namespace permission in this WSL environment.
The test container grants `SYS_ADMIN` and uses an unrestricted seccomp profile. The app renderer retains its sandbox.
These options are isolated test-container settings, not application installation requirements.

The restart diagnostic originally split a JavaScript string with an incorrectly escaped regular expression.
It now reads the engine label's text node. Saved selections were correct throughout that diagnostic failure.
The Windows runner now uses `System.Diagnostics.Process` to read reliable exit codes after redirected execution.

The T3 HTML preview tool failed twice because its headless Chrome process exited unexpectedly.
The collaborative browser preview and packaged Electron screenshots remained available.

Provider changes clear unsaved credentials from the previous provider's field.
Late replies cannot clear a draft for a different provider or a newer draft.
The final interface checks cover provider identity without saving a test key to the user's credential store.

The final packaging audit found files left from an earlier target in the shared backend output directory.
The builder now publishes into a clean staging directory before replacing the previous output.
The final package check refuses foreign platform executables and prepares both CPU profiles through the packaged service.

The dictionary check pauses the service output before saving a word.
It enters a new draft before releasing the save reply and checks that the newer draft survives.
Save callbacks also tolerate navigation away from the form.

Undo and raw restoration remain in the tray menu to preserve the destination's focus.
The main Settings page explains those actions without duplicating controls that would take focus from the destination.
A refused tray correction opens the app with a notice. The indicator receives no notice.
