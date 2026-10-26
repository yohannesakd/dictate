const fs = require('node:fs');
const path = require('node:path');
const directory = path.resolve(process.argv[2]);
fs.mkdirSync(directory, { recursive: true });
fs.writeFileSync(path.join(directory, 'settings.json'), JSON.stringify({ speech_engine: 'whistle', speech_model: 'base.en-q5_1', language: 'en', smart_cleanup: false, provider: 'groq', rules: { fillers: true, repeats: true, scratch_that: true }, dictionary: [], open_at_login: false }));
const now = new Date();
const records = [
  ['Let us keep the interface simple. The main controls should be easy to find.', 'let us keep the interface simple the main controls should be easy to find'],
  ['I will send the updated notes this afternoon.', 'i will send the updated notes this afternoon']
].map(([text, raw], index) => ({ id: require('node:crypto').randomUUID(), date: new Date(now.getTime() - (index + 1) * 60000).toISOString(), text, raw, rules: text, smart: 'off', speech_engine: index ? 'whisper' : 'whistle', delivery: 'verified', audio_seconds: 8, metrics: { release_to_text_ms: 800 } }));
fs.writeFileSync(path.join(directory, 'transcripts.jsonl'), records.map(record => JSON.stringify(record)).join('\n') + '\n');
