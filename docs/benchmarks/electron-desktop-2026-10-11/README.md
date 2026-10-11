# Electron execution evidence

This folder records the Electron replacement on `feat/electron-desktop`.
Screenshots use seeded test transcripts. They contain no personal dictation or credentials.

## Successful runs

- Dispatch `77352eb9-c65e-42d7-be74-cdd7121d3477` packaged clean Windows and Linux outputs. Linux passed GTK, Electron controls, recording, indicator, and both restart checks.
- Dispatch `3cc281e6-2ff2-4849-9800-89360a2df9ad` passed 25 core tests and three Node transport tests.
- Windows interactive task `DictateElectron-20261011` returned zero. The task and the separate native-check task were removed.
- Mac `/tmp/dictate-electron-ui/test-results-complete` returned zero. Both `/tmp/dictate-electron-ui/restart-*` checks returned zero.
- Mac `/tmp/dictate-electron-ui/native-results-unlocked-idle` returned zero for native delivery, microphone, Keychain, and the CPU audio matrix.

The Windows native checks also passed both selected CPU recognizers and two physical microphone cycles.
Linux used synthetic silence for the microphone adapter lifecycle.

`metrics.json` contains a process snapshot after the interface checks.
Values use Electron's native units of kilobytes. Shared resident pages can appear in several working sets.
These snapshots exclude native-service memory and are not steady-state resource benchmarks.

## Mac native check

The signed Electron app is installed at `/Applications/Dictate.app` and runs with the default data directory.
The previous Avalonia bundle remains at `/tmp/dictate-electron-rollback.OQc3QT/Dictate.previous.app`.
The earlier Swift rollback remains at `/tmp/dictate-shared-rollback.EgK9HW/Dictate.app`.
Do not change the signing keychain or stored credentials.

The first AppKit field check stopped because the Mac locked.
`/tmp/dictate-electron-ui/native-results-fourth/fixture-command.focus.json` reported `com.apple.loginwindow`, an inactive app, and a non-key window.
A later check refused insertion while T3 Code retained focus on the unlocked desktop.
The checker now waits for the external fixture's activation before capturing its target.
The launcher activates the existing fixture through LaunchServices after its ready file appears.
One attempt received extra keystrokes. The final run passed while the keyboard and mouse remained idle.
The headless service, microphone adapter, and Electron UI use the existing stable signing identity.

Before native checks, stop only the installed task-owned Dictate process.
Leave unrelated apps and user data intact.
Keep the Mac unlocked and leave the keyboard and mouse idle during delivery checks.
Launch the signed checks and activate the external fixture:

```sh
cd /tmp/dictate-electron-ui
check_output=$(mktemp -d /tmp/dictate-electron-ui/native-results.XXXXXX)
open -n -W build/Dictate.Checks.app --args \
  "$check_output" \
  /tmp/dictate-engines-20261011/portable/fixtures/speech \
  /tmp/dictate-shared-ui/check-models &
check_launcher=$!
for attempt in {1..100}; do
  if test -f "$check_output/fixture-command.ready"; then
    open build/Dictate.Fixture.app
    break
  fi
  sleep 0.05
done
wait "$check_launcher"
cat "$check_output/exit-code.txt"
```

The bundle has already been rebuilt with the separate `dev.local.dictate.fixture` AppKit target.
The checker verifies insertion, correction guards, Keychain, microphone restart, both CPU models, and the audio matrix.
Keep the Verified requirement. Do not weaken the delivery guard to accommodate an inactive desktop.
The saved `mac/native-checks.txt`, `mac/audio.json`, and `mac/fixture-focus.json` record the successful run.
The audio record contains 60 completed measurements and no recognition errors.
The installed app was reopened after the checks. Settings and all 10 history entries retained their exact file hashes.

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
