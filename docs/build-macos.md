# Build Dictate on macOS

Use macOS 15 or later, Xcode command-line tools, Node.js 22.12.0 or later, and the .NET 10 SDK.
Apple silicon is the tested Mac architecture.
The build includes Phonon-2 and compiles its Core ML runtime.

## Download the runtime and model

From the repository root, clone the pinned runtime release:

```sh
git clone --branch 1.1.2 --depth 1 https://github.com/fermionresearch/phonon-coreml vendor/phonon-coreml
```

Runtime release `1.1.2` resolves to commit `bf59e574639b060e30ee1a3f9c0e003fed5744c8`.
The build applies the repository's frame-capacity patch to a temporary copy.

Download the complete [Phonon-2-CoreML model](https://huggingface.co/FermionResearch/Phonon-2-CoreML) into `models/Phonon-2-CoreML`.
The build requires these files:

- `manifest.json`
- `decoder.bin`
- `Phonon-2.mlpackage/`
- `NOTICE`
- `LICENSE-WEIGHTS-CC-BY-4.0.txt`
- `SHA256SUMS`

Use Git LFS or download the actual model files. Git LFS pointer files cannot run the model.
Keep the complete download until its checksum check passes.
From the downloaded model directory, run:

```sh
shasum -a 256 -c SHA256SUMS
```

The verified model uses manifest version `3.0` and encoder functions `5`, `10`, `15`, and `35`.

## Build and launch

From the repository root, run:

```sh
./make-signing-identity.sh
./build.sh
open build/Dictate.app
```

Run the signing script once.
Keep the signing identity to retain microphone and Accessibility permissions across builds.
Allow microphone and Accessibility access when requested.
Set the globe key action to **Do Nothing** in Keyboard settings.
The first model load prepares Phonon-2 for this Mac.

This identity supports local development. It does not provide Developer ID signing or notarization for public distribution.

## Install from a remote checkout

Retain `models/`, `vendor/`, and `signing/` in `~/Library/Application Support/Dictate Build` on the Mac.
From this checkout, run `scripts/install-mac.sh <ssh-host>` with your Mac's SSH host or alias.
The installer verifies the new bundle before replacement and retains the previous bundle for rollback.

## Existing Mac data

On first launch, Dictate imports existing settings, dictionary entries, and history into `~/Library/Application Support/Dictate/Desktop`.
The original files remain unchanged. Existing Keychain credentials remain in the same service.
New transcripts include delivery evidence and stage timings.
Use `scripts/latency-report.py` to inspect older detailed metrics.

See the [verification instructions](../portable/docs/verification.md#mac-checks) for native field, microphone, and recognition checks.
