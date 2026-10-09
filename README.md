# Dictate

System-wide dictation for macOS in the style of Wispr Flow. Hold fn, speak, and release: the text is typed into the focused field. Speech recognition runs on this Mac with Phonon-2 on the Neural Engine. Optional smart cleanup uses GPT-6-Luna through your Codex login.

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

Smart cleanup sends each transcript to GPT-6-Luna through the Codex CLI and your ChatGPT login. It removes fillers by context, applies spoken corrections ("at two, actually three"), and uses the dictionary as hints. Turn it off in the Cleanup section.

- Requirements: the Codex CLI in `~/.local/bin`, `/opt/homebrew/bin`, or `/usr/local/bin`, and `codex login` with ChatGPT.
- Dictate runs one `codex app-server` process (about 30 MB) with its own Codex home in `~/Library/Application Support/Dictate/codex`. Your skills, AGENTS.md, hooks, and MCP servers do not load there. Only `~/.codex/auth.json` is shared, through a symlink.
- A dictation takes about 1.5 s more. Codex needs about 6 s to start when Dictate opens.
- When Codex does not reply within 3 s, or the reply is not a cleanup of the transcript, Dictate types the rule-based result instead.
- Usage counts against your Codex plan limits.

Each dictation appends resource metrics to `~/Library/Logs/Dictate/metrics.jsonl`.

## Credits

Phonon-2 by Fermion Research, based on NVIDIA Parakeet TDT 0.6B v3. Model weights: CC-BY-4.0. Runtime: Apache-2.0.
