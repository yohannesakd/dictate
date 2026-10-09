# Dictate

System-wide dictation for macOS in the style of Wispr Flow. Hold fn, speak, and release: the text is typed into the focused field. Speech recognition runs on this Mac with Phonon-2 on the Neural Engine. Optional smart cleanup uses DeepSeek or GPT-6-Luna.

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
- Esc cancels. Control + Command + V inserts the last dictation again.

Set "Press 🌐 key to" to "Do Nothing" in Keyboard settings, so fn does not also open the emoji picker or Apple Dictation.

## Smart cleanup

Smart cleanup sends a transcript to a language model when it contains words that need context: ambiguous fillers ("like", "you know"), self-corrections ("actually", "no wait"), or dictionary words. The model removes the fillers, keeps only the corrected version, and uses the dictionary as hints. Other dictations use the rules only.

Choose the model in Settings (the gear button, or ⌘,), under Cleanup:

- DeepSeek (`deepseek-chat`): about 1 s. Save a DeepSeek API key in Dictate. Dictate keeps it in the Keychain.
- ChatGPT (`gpt-6-luna`): about 1.5 s. Click "Sign In with ChatGPT". Dictate uses the same sign-in flow as the Codex CLI and Pi, and keeps its own tokens in the Keychain. The endpoint (`chatgpt.com/backend-api/codex/responses`) is not a public API, so OpenAI can change it. Usage counts against your plan limits.

Dictate types the rule result when the model does not reply within 3 s, or when the reply contains a word that is not in the transcript or the dictionary. That check stops answers to dictated questions.

Every dictation is saved in `~/Library/Application Support/Dictate/transcripts.jsonl`: the raw transcript, the rule result, the model reply, the inserted text, and the app.

Each dictation appends resource metrics to `~/Library/Logs/Dictate/metrics.jsonl`.

## Credits

Phonon-2 by Fermion Research, based on NVIDIA Parakeet TDT 0.6B v3. Model weights: CC-BY-4.0. Runtime: Apache-2.0.
