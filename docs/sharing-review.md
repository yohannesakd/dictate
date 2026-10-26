# Sharing review: October 26, 2026

Public publication still requires resolving the speech fixtures' redistribution terms.
The repository remains private.

## Public-sharing issue

The fixture manifests identify four recordings made with Apple's Samantha or Anna system voices:

- `portable/fixtures/speech/dictate-short.wav`
- `portable/fixtures/speech/dictate-technical.wav`
- `portable/fixtures/speech/dictate-long.wav`
- `portable/fixtures/speech/german.wav`

[Apple's macOS license, section 2F](https://www.apple.com/legal/sla/docs/macOSTahoe.pdf), restricts system-voice use and public redistribution.
This review did not establish separate permission to distribute these recordings.
Replace them with recordings that permit redistribution, or establish the required permission before publication.
Their existing Git history also requires a publication decision. Deleting current files alone does not remove historical copies.

The [English manifest](../portable/fixtures/speech/samples.json) and [German manifest](../portable/fixtures/speech/german-samples.json) retain provenance and hashes.
Historical recognition results apply to those exact recordings.
New recordings require new hashes and recognition checks; they do not inherit the historical results.

## Completed source changes

The README now separates supported platforms, build instructions, shortcuts, privacy, and measured limits.
The Mac build details have a dedicated guide. Desktop prerequisites specify Node.js 22.12.0 or later.
Privacy guidance now includes dictionary hints sent during cloud cleanup and older transcripts retained on disk.

Authentication mutations now share one gate.
Sign-out removes credentials after pending refresh completes and invalidates earlier browser sign-in attempts.
This removes local credentials; it does not revoke an issued token or cancel an active cleanup request.

Electron is pinned to 44.9.3. The lockfile uses Undici 7.30.1.
These versions address the reported [Electron](https://github.com/electron/electron/security/advisories/GHSA-jr5w-w6x2-84p9)
and [Undici](https://github.com/nodejs/undici/security/advisories/GHSA-xjj6-f33q-929c) dependency advisories.
The lockfile update reported no remaining npm audit findings.

Builds use the tracked Inter font files directly. The redundant font-copy script and dependency have been removed.
Font files and their license remain unchanged.
Mac regression-check runners now use separate temporary directories and remove them on exit.

## Review and verification limits

Independent source reviews covered authentication, cleanup providers, Electron boundaries, downloads, storage, native process calls, and Android WebView isolation.
The authentication fix received a second independent source review with no actionable findings.
The reviews were focused checks, not a full security certification or dependency-license audit.

All 38 core tests and three transport tests passed in an isolated environment without network access.
Three authentication regression cases failed against the original implementation, then passed with the fix.
The main-branch history scan reported no detected secrets in 52 commits.
The remote-branch history scan reported no detected secrets in 74 commits.
Changed documentation links resolve. Font files and their license match the previous revision exactly.
Secret scanning cannot prove that every sensitive value is absent. Historical screenshots require a separate privacy review before public publication.

Public binary distribution also requires release signing and platform smoke checks.
This review did not rebuild Android or execute the new Electron runtime on each platform.
The [hardware verification record](benchmarks/platform-speech-2026-10-13/README.md) retains physical-device and driver limits.
