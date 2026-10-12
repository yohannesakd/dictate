# Waveform indicator verification

The desktop indicator displays nine bars without visible text, shortcut hints, or warnings.
Its transparent window measures 84 by 40 pixels. The visible pill measures 76 by 24 pixels.
Windows enforced a 39-pixel minimum height during the first check. The final dimensions passed on all desktop platforms.

Whisper is the default for new settings and settings without an engine value. Existing engine choices remain unchanged.
Whistle remains available in the engine menu. This change does not introduce a new recognition quality benchmark.

## Results

| Check | Result | Evidence |
| --- | --- | --- |
| Core contracts and settings | All 26 tests passed. | [core.txt](core.txt) |
| Desktop transport | All three tests passed. | [transport.txt](transport.txt) |
| Mac packaged interface | Microphone start, cancellation, engine preparation, and waveform checks passed. | [mac/checks.txt](mac/checks.txt) |
| Linux packaged interface | Interface and native checks passed. | [linux/checks.txt](linux/checks.txt) |
| Windows packaged interface | Interface, native delivery, microphone, speech, and engine restart checks passed. | [windows/checks.txt](windows/checks.txt), [windows/ui-checks.txt](windows/ui-checks.txt) |
| Android release | Installation, shared interface, and Whisper selection after restart passed on emulator-5556. | [android.png](android.png) |
| Groq and DeepSeek | Both saved keys passed live cleanup requests through production credential and cleanup code. | [providers.json](providers.json) |

Processing and delivery waveform checks used phase events. Recording and cancellation used the microphone.
Android used the owned Dictate_Compact_API_35 emulator. This task did not repeat the earlier speech corpus or test physical Android devices.

Dispatch ran the substantial Linux builds and checks. The final desktop job was `d885588e-9674-4130-9d8b-ec9336e70298`.
The Android release job was `ca6b5d23-957c-43df-907b-e844eaacd783`.
The core and transport job was `c7cce548-64b3-48af-8c17-5568e4742362`.
Mac builds and interface checks ran on the Mac. Windows checks ran in the interactive Windows session.

## Mac installation and credentials

The updated app is installed at `/Applications/Dictate.app`. The installer retained the previous bundle for rollback.
Settings and history hashes match before and after installation. See [mac/install.json](mac/install.json).

The app reuses the existing `dev.local.dictate` Keychain items for `groq-api-key` and `deepseek-api-key`.
The interface refreshes credential status when Cleanup opens through navigation or the native menu.
The renderer receives connection status and never receives the saved keys.

The signed service read both saved keys before installation. See [mac/credentials-before-install.json](mac/credentials-before-install.json).
The provider fixture used the production native main loop, credential store, and HTTP cleanup client.
Both providers returned the expected text for the public sample, `Hello world.` Only result flags were retained.
No key was copied to the server, another device, a screenshot, or a log.

After installation, the actual app displayed `Checking connection…` for Groq.
System Events identified this macOS prompt: `Dictate.Desktop wants to access key “Dictate” in your keychain.`
The prompt includes an authentication field and the `Always Allow`, `Deny`, and `Allow` buttons.
Actual app credential verification remains pending until the user completes that local prompt.
Unlocking Keychain alone did not complete this approval. The user has been asked to complete the identified prompt.

An SSH credential read returned `-25308`. That remote security session does not establish whether the GUI Keychain is locked.
A standalone GUI probe also timed out. The actual app prompt provides the direct evidence for the remaining check.
The old and new backend signing requirements match. This task did not modify Keychain entries or access rules.

## Limits

The desktop checks cover the tested Mac, Windows, and Linux sessions. They do not establish support for every device or Linux display server.
The Android check establishes emulator behavior. Physical Android resource usage remains outside this verification.
The T3 restart stopped the web preview process. Installed packages and saved verification results remain available.
