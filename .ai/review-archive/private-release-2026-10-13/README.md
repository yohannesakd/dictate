# Private release audit: October 13, 2026

The repository remains private. This audit prepares the code and documentation for possible later publication.
The owner selected MIT for Dictate's source. Model weights and dependencies retain separate licenses.

## Scope

The audit started at `71f3eb7` on `feat/electron-desktop`.
The publication compares this topic branch with `origin/main` and includes the earlier cross-platform migration.
The review examined shared dictation state, persistence, downloads, recognition, process transport, credentials, native recording and delivery, packaging, and documentation.
It checked reachable failures and existing callers before implementing fixes.
This is a source review with targeted runtime verification. It is not an independent review or a complete security certification.

## Fixed defects

1. Response headers ended the HTTP timeout before model downloads finished.
   A linked cancellation deadline now covers the response body, checksum verification, and replacement.
   Tests confirm that stalled, oversized, and invalid downloads preserve the previous model and remove temporary files.
2. History reads could parse an incomplete concurrent append.
   Readers now hold the existing writer gate during enumeration.
   History retains only its last 50 records in memory. Usage aggregates the streamed records.
3. Unix transcript directories could inherit permissions that allowed other users to read transcripts.
   FileStore now sets the directory to owner-only access, including existing directories.
4. NPU service replies could wait indefinitely.
   Device discovery and encoder compilation now have two-minute reply deadlines.
   Recognition has a six-minute deadline across input writes and the reply.
   A worker timeout follows the existing GPU or CPU fallback. Caller cancellation remains cancellation.
5. The remote Mac installer deleted a predictable directory and waited indefinitely for app exit.
   It now uses unique directories, verifies before replacement, bounds the exit wait, and restores the previous bundle after replacement failure.
   The real installer completed successfully. Installed signatures passed. Settings and history hashes remained unchanged.
6. Distribution notices were incomplete.
   Packages now include Dictate's MIT license, dependency licenses, .NET notices, and Phonon runtime attribution.
   The Mac bundle retains Electron and Chromium licenses inside the app.
   Android header notices moved from a late target into evaluated project assets.
   The final Release APK contains the expected Vulkan and SPIR-V notices.

## Documentation and repository checks

Current guides describe Phonon-2 on Mac and English Whisper on other platforms.
Archived evaluations retain their dates, measurements, and engine identities.
Historical personal paths and account names were replaced with build placeholders in the current checkout.
Git history was not rewritten. Before any future public release, review historical metadata separately.
The contribution guide, security guidance, dependency inventory, and documentation index describe the private project.
Local links resolve. Documentation JSON files parse. No GitHub Actions workflow was added.
Gitleaks detected no credentials in 121 commits or the scanned worktree.
The scan does not prove that every possible secret is absent.

## Verification

- 34 core tests and three transport tests passed.
- Linux packaged UI, restart, GTK insertion, Unicode readback, correction guards, recording cancellation, and hardware controls passed.
- Windows Vulkan GPU recognition, CPU isolation, switching, and cancellation passed on the physical Windows host.
- Mac signed UI, Phonon model preparation, hardware controls, microphone cancellation, and restart passed.
- The updated Mac installer installed a signature-verified bundle with all required notices.
- Android arm64 Release and x64 Debug builds passed. Android 16 emulator instrumentation passed.
- OpenVINO CPU encoding, recognition, worker termination, and restart passed.
- Final shared history streaming passed the core suite and Linux packaged checks.

The adjacent evidence files record the runtime results.
The previous [platform speech record](../../../../docs/benchmarks/platform-speech-2026-10-13/README.md) contains the Mac audio matrix and earlier native delivery results.

## Limits and release follow-up

Physical Intel NPU and Android GPU execution remain unverified.
Other NPU vendors and Android NPU runtimes are unsupported; available GPU or CPU execution remains the fallback.
The Android package uses a development signing key.
The latest Mac AppKit delivery fixture could not obtain focus in the earlier speech run.
This audit verified the signed UI and installer; it does not claim a new successful external-field Mac delivery run.
The upstream Phonon runtime emits Swift concurrency and deprecated Accelerate warnings.
No change was made to that upstream concurrency implementation.
Real phone microphone quality, battery use, older CPU performance, and Wayland global shortcuts remain outside these checks.
Transcript history is plain text under operating system access controls.
There is no automatic repair for a history file damaged by an interrupted disk write.

## Execution records

- Shared tests: Dispatch `88f5828c-39df-4837-95c1-ecf36d485610`.
- Desktop packages and Linux runtime: `79f50474-8eb5-4fcc-9ddd-cdf2848f1305`.
- Android builds and emulator: `0ee6aa7f-9b09-4d94-acc0-84c2314e8293`.
- OpenVINO and Windows check build: `ee583955-d647-4767-bd96-5fad645fac5a`.
- Android license package check: `338eb39a-c6b7-474d-94d3-55bf81552c42`.
- Final history tests and Linux package: `4b95bad0-27a7-4b59-aeb6-7529e0abb8d5`.
