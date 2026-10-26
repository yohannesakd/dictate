# Security and privacy

The repository remains private. Report security defects privately to the repository owner through an existing private contact channel.
Include affected versions, reproduction steps, and the observed result.
Do not attach credentials or personal transcripts.

Speech recognition runs on the device.
Optional smart cleanup sends transcripts and dictionary hints to the selected provider.
Turn off Smart cleanup for fully local operation.
Credentials use the operating system's credential store.
History stores original and final transcripts, cleanup replies, and delivery details as local plain text.
Older entries remain on disk. The interface shows the latest 50.
Audio is not saved in transcript history.
Operating system access controls protect the history directory.
The app does not encrypt transcript history separately.

Desktop data locations:

- macOS: `~/Library/Application Support/Dictate/Desktop`
- Windows: `%LOCALAPPDATA%\Dictate`
- Linux: `$XDG_DATA_HOME/dictate`, or `~/.local/share/dictate` when unset.

Android stores data in the app's private files directory.

Keep model downloads, signing keys, credential files, and personal recordings outside Git.
Before distributing a package, retain all dependency notices and use a release signing identity.
The Android build currently uses a development signing key.
