# Dictate

System-wide dictation for macOS in the style of Wispr Flow. Hold fn, speak, and release: the text is typed into the focused field. Speech recognition runs on this Mac with Phonon-2 on the Neural Engine. Optional smart cleanup uses Groq, DeepSeek, or GPT-6-Luna.

## Build

Requirements: Apple silicon, macOS 15 or later, Xcode.

1. Get the Phonon-2 runtime (version 1.1.2):
   `git clone --branch 1.1.2 --depth 1 https://github.com/fermionresearch/phonon-coreml vendor/phonon-coreml`
2. Download the model folder from https://huggingface.co/FermionResearch/Phonon-2-CoreML into `models/Phonon-2-CoreML`.
   The build needs `manifest.json`, `decoder.bin`, `Phonon-2.mlpackage`, `NOTICE`, and `LICENSE-WEIGHTS-CC-BY-4.0.txt`.
3. Run `./make-signing-identity.sh` once. It creates a separate keychain in `signing/` with a local signing certificate. A stable signature keeps the Microphone and Accessibility grants across rebuilds.
4. Run `./build.sh`, then open `build/Dictate.app`.

The first launch prepares the model for the Neural Engine. This takes about one minute, once per Mac.

## Use

- Hold fn to talk; release to insert the text.
- Double-tap fn, or press fn + Space, for hands-free mode. Press fn again to finish.
- Esc cancels pending work and stops further typing. Characters already sent can remain in the field.
- Control + Command + V inserts the last dictation again. Check uncertain delivery before using this shortcut.
- The status menu has **Undo Last Dictation** and **Restore Raw Transcript**. These actions require a verified insertion at a collapsed caret.
  The same process, field, caret, and complete field text must remain unchanged. Corrections expire after five minutes.
  Fields must support Accessibility range replacement. Unreadable fields and fields larger than one million UTF-16 units have no correction snapshot.
  **Copy Original** in History remains available for manual recovery.
- To fix a misheard word, select it in any app and press fn + D. Type what you meant and press Return. Dictate adds it to the dictionary.

Set "Press 🌐 key to" to "Do Nothing" in Keyboard settings, so fn does not also open the emoji picker or Apple Dictation.

## Smart cleanup

Cleanup is minimal. The rules remove filler sounds (um, uh, er, hmm), stutters ("I I"), and the clause before "scratch that". Dictate keeps words such as "like", "you know", and "actually", and does not apply spoken corrections.

Smart cleanup sends a transcript to a language model when it contains a repeat or a restart ("I want to, I want to go") or a dictionary word. The model proposes removed repeats and dictionary replacements. Dictate checks each proposal before insertion. Other dictations use the rules only.

Choose the model in Settings (the gear button, or ⌘,), under Cleanup:

- Groq (`qwen/qwen3.8-27b`, thinking off): about 0.2 s. This is the default. Save a Groq API key in Dictate. Groq lists this model as a preview, so it can remove it at short notice.
- DeepSeek (`deepseek-chat`): about 1 s. Save a DeepSeek API key in Dictate. Dictate keeps each API key in the Keychain.
- ChatGPT (`gpt-6-luna`): about 1.5 s. Click "Sign In with ChatGPT". Dictate uses the same sign-in flow as the Codex CLI and Pi, and keeps its own tokens in the Keychain. The endpoint (`chatgpt.com/backend-api/codex/responses`) is not a public API, so OpenAI can change it. Usage counts against your plan limits.

Dictate uses the rule result when the model exceeds 3 seconds or fails the ordered-edit guard. The guard keeps the rule result's words in order. It permits dictionary replacements, exact repeats ("the new, the new design"), and restarts that begin with the same two or more words after punctuation ("is it like, is it possible"). A repeat that ends at punctuation, such as "no, no, no," or "I know, I know.", stays. So does one word twice without a comma, such as "that that". The guard rejects other omissions, reordered words, and changed numeric notation.

Smart cleanup sends at most 12 requests a minute, below the Groq free tier's 8000 tokens a minute. Requests during recording stop at 8 a minute, so the request at fn release still runs. Above the limit, Dictate uses the rule result. These checks reduce mistakes; they do not prove meaning preservation.

Every dictation is saved in `~/Library/Application Support/Dictate/transcripts.jsonl`: the raw transcript, the rule result, the model reply, the final text, delivery outcome, and app.

Each dictation appends one JSON line of timing and resource metrics to `~/Library/Logs/Dictate/metrics.jsonl`.
The `at_*_ms` fields are a timeline in milliseconds since the fn press: microphone ready, first audio, first and last speech, fn release, transcript, rules, model request start and end, text ready, events posted, and text verified.
The `llm_*` fields split each model request into connection, network round trip, time to the first byte, download, and tokens.

To graph them, run `scripts/latency-report.py`. It writes `latency.html` (charts by stage) and `latency.csv` next to the log.
Durations use a monotonic clock. Metrics separate event posting, verified text, request cache hits, and delivery outcomes.
Dictate checks readable fields against the expected text and caret. Verification has a 150-millisecond polling budget, plus Accessibility call time.
Many apps, such as Electron apps, do not expose the field text. Dictate then logs `sent`: every event was posted, but the text was not read back. Dictate never repeats uncertain delivery automatically.

## Credits

Phonon-2 by Fermion Research, based on NVIDIA Parakeet TDT 0.6B v3. Model weights: CC-BY-4.0. Runtime: Apache-2.0.
