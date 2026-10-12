# Whistle and Whisper evaluation

This archived comparison describes revision `d515489`. The [current app](whisper-only.md) uses English Whisper on the CPU.

Both CPU engines completed 280 recognition runs on Mac, Windows, Linux, and an ARM64 Android emulator.
Each engine received seven recordings, with five alternating passes per recording.
Phonon-2 remains the Mac default. Whisper base remains the portable default.

The recordings include human speech, short dictation, technical terms, a 36.9-second message, silence, added noise, and German speech.
The synthetic recordings use the Mac Samantha and Anna voices.
No private microphone recordings are retained in the evaluation fixtures.

## Observed recognition times

The table shows median milliseconds, followed by the minimum and maximum in parentheses.
The short recording lasts 4.7 seconds. The long recording lasts 36.9 seconds.
These are production-default functional measurements. They do not establish an optimized speed winner.

| Platform | Engine | Short recording, ms | Long recording, ms | Word error rate |
|---|---|---:|---:|---:|
| Mac | Whistle | 39 (35–40) | 619 (573–622) | 2.15% |
| Mac | Whisper | 825 (816–837) | 1771 (1753–1812) | 2.58% |
| Linux | Whistle | 243 (138–3039) | 8739 (5954–10832) | 2.58% |
| Linux | Whisper | 664 (621–712) | 1501 (1435–1575) | 1.72% |
| Windows | Whistle | 126 (121–355) | 6034 (5232–6553) | 3.00% |
| Windows | Whisper | 780 (763–806) | 1817 (1815–1862) | 1.72% |
| Android emulator | Whistle | 191 (120–240) | 1103 (909–1453) | 3.00% |
| Android emulator | Whisper | 890 (876–1147) | 2218 (2170–2576) | 1.72% |

Word error rate counts substitutions, deletions, and insertions against the reference words.
Case and punctuation are ignored. Numeric spelling and proper names still count as differences.
These recordings do not establish general accuracy across speakers, accents, or environments.
Both engines returned empty text for all digital-silence runs.

Mac timings include the Engine callback, WAV preparation, process startup, model loading, and inference.
Portable timings exclude the separately recorded preparation stage but include WAV preparation and process work.
Cleanup providers and field delivery are excluded from these timing columns.
Linux runs shared Saibot with builds and other work. Its wide timing ranges limit performance conclusions.
Mac uses whisper.cpp v1.9.5. Portable apps use Whisper.net 1.9.1.
Mac detects language automatically. Portable evaluation forces the reference language.
Do not compare platforms as equivalent hardware or identical Whisper configurations.

For the Mac human reference, Whistle child peak memory was 56 MiB median; Whisper was 289 MiB.
For the long recording, the corresponding values were 93 MiB and 339 MiB.
Those values exclude the parent app. Portable parent RSS excludes the exited Whistle child and cannot compare complete engine memory.

## Verification boundaries

| Platform | Verified | Remaining limit |
|---|---|---|
| Mac M4 Pro, macOS 26.5.2 | Signed native build; 70 CPU runs; cancellation recovery; six Phonon regression recordings; controller, indicator, and build checks; selector persistence after restart. | These results do not verify every target application. |
| Saibot Windows 11 x64 | 70 CPU runs; both transcripts reached a real Edit field; microphone capture; cancellation recovery; selector persistence after restart. | These results do not verify every target application or the pre-existing F8 shortcut gap. |
| Saibot Linux x64 in WSL | 70 CPU runs; cancellation recovery; native GTK insertion and correction; software-rendered app pages; selector persistence after restart. | Live Linux microphone capture and a physical Linux desktop remain unverified. |
| Android 15 ARM64 emulator on Mac | 70 runs under the app UID; both transcripts reached an InputConnection; microphone capture; cancellation recovery; selector persistence. | Physical Android hardware remains unverified. |

The ARM64 Android Debug and Release packages built successfully.
The Release package contains the exact pinned Whistle executable hash.
The Debug package ran the full audio evaluation.
The Release package also ran Whistle through the voice keyboard and inserted a transcript into the app's test field.
Release Whisper completed microphone recognition and returned `No speech detected.`
Android x64 supports Whisper; this branch does not supply an x64 Whistle runtime.

The Windows fixture initially lacked horizontal scrolling and truncated the reference text.
The corrected Edit fixture accepts the complete transcript. The production delivery implementation did not change.
A cancelled Mac Phonon measurement finished before cancellation; it does not prove interrupted Phonon inference.
Mac Phonon regression recordings completed successfully.
The Mac temporary-folder counter reads the shared user directory.
Six measurements saw one folder, but later measurements and the final directory check saw none.
This counter cannot distinguish this benchmark's files from concurrent app files.
A final check used a separate temporary directory for both CPU engines.
Both produced the reference transcript and left that directory empty.

## Evidence and reproduction

The [raw results](benchmarks/2026-10-11/) retain transcripts, individual times, preparation times, and errors.
The [fixture manifest](../portable/fixtures/speech/samples.json) records sources, references, and SHA-256 hashes.
The [German manifest](../portable/fixtures/speech/german-samples.json) records the additional language sample.

Use the built Mac app with this command.

These archived commands require revision `d515489`.
The current branch uses [shared verification commands](../portable/docs/verification.md).

```sh
python3 scripts/evaluate-mac-speech.py "$PWD/build/Dictate.app" "$PWD/portable/fixtures/speech" "$PWD/mac-results.json"
```

Run portable audio evaluation from the repository root.

```sh
dotnet run --project portable/Checks/Dictate.Checks.csproj -c Release -- --evaluate portable/fixtures/speech/samples.json portable/models results.json
dotnet run --project portable/Checks/Dictate.Checks.csproj -c Release -- --evaluate portable/fixtures/speech/german-samples.json portable/models german-results.json
```

Android Debug instrumentation is `dev.local.dictate.portable.DeviceChecks`.
Its fixtures and models belong in the app FilesDir. Its JSON output is `speech-evaluation.json`.
The Windows helper at that revision is `portable/scripts/check-windows-engines.ps1`.

Dispatch evidence: Linux audio `d1a1a56d`, German audio `ef6df4e6`, native Linux `eb2e6a17`, Linux selector `b4dc9453`.
Android Debug evidence is `0eef9372`. Android Release evidence is `20a00818`.
Final portable tests and cancellation checks: `fa303177`, exit code 0.
Mac build and checks ran natively over personal SSH. Windows checks ran in the personal interactive desktop session.

Whistle assets use pinned revisions and SHA-256 verification.
The [Whistle release](https://cactuscompute.com/blog/whistle) describes its CPU model and seven supported languages.
Mac Whisper uses the [v1.9.5 source release](https://github.com/ggml-org/whisper.cpp/releases/tag/v1.9.5).
The setup scripts pin its source archive, build tool, and model hash.
