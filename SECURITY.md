# Security and privacy

The repository remains private. Report security defects privately to the repository owner through an existing private contact channel.
Include affected versions, reproduction steps, and the observed result.
Do not attach credentials or personal transcripts.

Speech recognition runs on the device. Optional smart cleanup sends transcripts to the selected provider.
Credentials use the operating system's credential store.
Transcript history is stored locally as plain text. Operating system access controls protect its directory.
The app does not encrypt transcript history separately.

Keep model downloads, signing keys, credential files, and personal recordings outside Git.
Before distributing a package, retain all dependency notices and use a release signing identity.
The Android build currently uses a development signing key.
