# Command error and retry verification

The reported banner matches the earlier Keychain check timeout. It also contained Electron's internal remote-method prefix.
The Keychain approval resolved credential access. The app retained the earlier timeout notice after later credential checks succeeded.

Desktop IPC now returns explicit success or error replies. The preload returns the result or throws an error containing only the application message.
The renderer records which command produced its local notice. A successful retry clears that command's notice.
Success from another command does not clear the notice. Credential retries also preserve delivery warnings from app events.

## Verification

The packaged interface checks injected a credential timeout through the real Electron IPC path.
They checked the exact visible message, a successful retry, and preservation of unrelated warnings.
Credential replies use fixtures during interface verification. These checks do not read personal keys or require Keychain approval.
The first Mac run waited on a real credential read, so it could not reach the injected timeout check.
The final checks isolate that read and exercise the requested error behavior.

| Platform | Result | Evidence |
| --- | --- | --- |
| Mac | Packaged interface checks passed. The signed update is installed. | [mac/checks.txt](mac/checks.txt), [mac/install.json](mac/install.json) |
| Linux | Packaged interface and native checks passed through Dispatch. | [linux/checks.txt](linux/checks.txt) |
| Windows | Interactive packaged interface and native checks passed. | [windows/checks.txt](windows/checks.txt), [windows/exit-code.txt](windows/exit-code.txt) |
| Android | The release APK built with the shared renderer change. This task did not repeat emulator or physical-device checks. | Dispatch job `9c15366c-f7b6-40ba-b6ad-9913ac83529d`. |

All three existing transport tests passed in Dispatch job `e4bbedbe-2108-4330-898c-4168bac324c1`.
The final Linux checks and Windows package used Dispatch job `1136a62f-999e-4b4c-8934-8dcbf9b9b3fb`.
The first Dispatch command failed because the worker did not have RTK. The final worker commands ran directly.

Mac settings and history hashes match before and after installation. The installer retained the previous bundle for rollback.
After installation, System Events read `Connected` for Groq from the actual Cleanup interface.
The final settings check confirmed Groq and Whisper remained selected. See [mac/connection.json](mac/connection.json).
Both saved keys passed the earlier live provider checks. This task did not repeat provider API calls.
No recognition engine, audio processing, delivery logic, or provider API changed.
