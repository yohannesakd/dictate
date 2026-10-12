export function createPreview() {
  const subscribers = new Set();
  const now = new Date();
  const week = [138, 246, 189, 352, 208, 94, 177].map((words, index) => {
    const date = new Date(now); date.setDate(date.getDate() - 6 + index);
    return { date: `${date.getFullYear()}-${String(date.getMonth() + 1).padStart(2, '0')}-${String(date.getDate()).padStart(2, '0')}`, words, dictations: 3 + index, seconds: 120 };
  });
  const state = {
    sequence: 1, platform: 'mac', phase: 'idle', notice: null, preparing: false, signing_in: false, wayland: false,
    permissions: { microphone: true, accessibility: true, keyboard: true, fn_free: true },
    settings: { speech_engine: 'whisper', smart_cleanup: false, provider: 'groq', rules: { fillers: true, repeats: true, scratch_that: true }, dictionary: [{ heard: 'yohannes', written: 'Yohannes' }], open_at_login: false },
    credentials: { groq: false, deep_seek: false, chat_gpt: false }, credentials_error: null,
    models: { whisper: true },
    usage: { week, median_latency: 780, words_per_minute: 132 },
    history: [
      { id: 'one', text: 'Let’s keep the interface simple. The important controls should be easy to find, and everything else can stay out of the way.', raw: 'lets keep the interface simple', date: new Date(now.getTime() - 900000).toISOString(), speech_engine: 'whisper', delivery: 'verified' },
      { id: 'two', text: 'I will send the updated notes this afternoon.', raw: 'i will send the updated notes this afternoon', date: new Date(now.getTime() - 3900000).toISOString(), speech_engine: 'whisper', delivery: 'verified' }
    ]
  };
  function publish() { state.sequence++; subscribers.forEach(callback => callback({ type: 'state', state: structuredClone(state) })); }
  return {
    state: async () => ({ state: structuredClone(state) }),
    subscribe: callback => { subscribers.add(callback); return () => subscribers.delete(callback); },
    window: async () => {},
    command: async (command, args) => {
      if (command === 'settings') {
        for (const [key, value] of Object.entries(args)) { if (key in state.settings.rules) state.settings.rules[key] = value; else state.settings[key] = value; }
      }
      if (command === 'add-word') state.settings.dictionary.push(args);
      if (command === 'remove-word') state.settings.dictionary = state.settings.dictionary.filter(entry => entry.heard !== args.heard || entry.written !== args.written);
      if (command === 'dismiss') state.notice = null;
      if (command === 'start') state.phase = 'listening';
      if (command === 'finish' || command === 'cancel') state.phase = 'idle';
      if (command === 'save-key') state.credentials[args.provider] = true;
      if (command === 'remove-key') state.credentials[args.provider] = false;
      if (command === 'prepare') state.models.whisper = true;
      publish(); return command === 'undo' || command === 'restore' ? false : null;
    }
  };
}
