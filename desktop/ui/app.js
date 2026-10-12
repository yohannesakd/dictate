const icons = {
  mic: '<rect x="9" y="2" width="6" height="12" rx="3"/><path d="M5 10v2a7 7 0 0 0 14 0v-2M12 19v3M8 22h8"/>',
  copy: '<rect x="8" y="8" width="13" height="13" rx="2"/><path d="M16 8V5a2 2 0 0 0-2-2H5a2 2 0 0 0-2 2v9a2 2 0 0 0 2 2h3"/>',
  check: '<path d="m5 12 4 4L19 6"/>',
  close: '<path d="m6 6 12 12M6 18 18 6"/>',
  stop: '<rect x="6" y="6" width="12" height="12" rx="2"/>',
  clock: '<circle cx="12" cy="12" r="9"/><path d="M12 7v5l3 2"/>',
  trash: '<path d="M3 6h18M9 6V3h6v3M5 6l1 15h12l1-15M10 10v7M14 10v7"/>',
  alert: '<path d="m12 3 10 18H2L12 3Z M12 9v4M12 17h.01"/>',
  external: '<path d="M15 3h6v6M21 3 10 14M11 3H5a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h14a2 2 0 0 0 2-2v-6"/>'
};
const icon = name => `<svg aria-hidden="true" viewBox="0 0 24 24">${icons[name] || ''}</svg>`;
const escape = value => String(value ?? '').replace(/[&<>"']/g, character => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' })[character]);
const engineName = engine => engine === 'whisper' ? 'Whisper' : 'Whistle';
const $ = selector => document.querySelector(selector);
let api = window.dictate, state, page = 'home', settingPage = 'speech', localNotice, unavailable, search = '', busy = new Set();
let toastTimer;
const android = () => state?.platform === 'android';
const keyboard = () => !!state?.keyboard;
if (!api && new URLSearchParams(location.search).has('preview')) {
  api = (await import('./preview.js')).createPreview();
  $('#footer').textContent = 'Design preview with sample dictations.';
}

function apply(next) {
  if (!next || (state && next.sequence <= state.sequence)) return;
  state = next;
  document.body.dataset.platform = state.platform; document.body.dataset.surface = keyboard() ? 'keyboard' : 'app';
  render();
}
function notify(message, command) { localNotice = { message, command }; renderNotice(); }
function toast(message) {
  document.querySelector('.toast')?.remove(); clearTimeout(toastTimer);
  const element = document.createElement('div'); element.className = 'toast'; element.textContent = message; element.role = 'status';
  document.body.append(element); toastTimer = setTimeout(() => element.remove(), 2000);
}
async function command(name, args = {}, success) {
  if (busy.has(name)) return;
  busy.add(name); render();
  try {
    const result = await api.command(name, args);
    if (localNotice?.command === name) localNotice = null;
    if (success) success(result);
  }
  catch (error) { notify(error.message, name); }
  finally { busy.delete(name); render(); }
}
async function windowAction(action, success) {
  try { await api.window(action); if (success) success(); }
  catch (error) { notify(error.message); }
}
function button(label, action, style = 'secondary', disabled = false, symbol = '') {
  return `<button class="${style}" data-action="${action}" ${disabled ? 'disabled' : ''}>${symbol ? icon(symbol) : ''}${label}</button>`;
}
function renderNotice() {
  if (keyboard()) { $('#notice').hidden = true; return; }
  const message = unavailable || localNotice?.message || state?.notice;
  $('#notice').hidden = !message;
  $('#notice').innerHTML = message ? `${icon('alert')}<p>${escape(message)}</p><button data-action="dismiss" aria-label="Dismiss message">${icon('close')}</button>` : '';
}
function preservedRender(html) {
  const content = $('#content');
  const fields = new Map([...content.querySelectorAll('input[type=text],input[type=password],input[type=search]')].map(field => [field.id, { value: field.value, provider: field.dataset.provider }]));
  const active = document.activeElement;
  const focus = content.contains(active) ? { id: active.id, selection: active.selectionStart, end: active.selectionEnd } : null;
  const scroll = $('#content').scrollTop;
  content.innerHTML = html;
  for (const [id, saved] of fields) {
    const field = document.getElementById(id);
    if (field && field.dataset.provider === saved.provider) field.value = saved.value;
  }
  if (focus?.id) {
    const field = document.getElementById(focus.id);
    if (field) { field.focus({ preventScroll: true }); if (typeof focus.selection === 'number') field.setSelectionRange(focus.selection, focus.end); }
  }
  $('#content').scrollTop = scroll;
}
function render() {
  renderNotice();
  document.querySelectorAll('[data-page]').forEach(button => button.setAttribute('aria-current', button.dataset.page === page ? 'page' : 'false'));
  if (!state) { if (unavailable) $('#connection').textContent = 'Service unavailable'; return; }
  const idle = state.phase === 'idle';
  $('#connection').innerHTML = `<span class="status-dot"></span>${idle ? 'On-device dictation' : state.phase === 'listening' ? 'Listening' : state.phase === 'processing' ? 'Processing' : 'Inserting text'}`;
  if (unavailable) $('#connection').textContent = 'Service unavailable';
  $('#dictation-test-container').hidden = !android() || keyboard() || page !== 'home';
  if (android() && !keyboard() && $('#content').contains(document.activeElement) && document.activeElement.matches('input[type=text],input[type=password],input[type=search]')) {
    if (document.activeElement.id === 'history-search') $('#transcripts').innerHTML = historyItems();
    return;
  }
  preservedRender(keyboard() ? keyboardHome() : page === 'home' ? home() : page === 'history' ? history() : settings());
}
function setup() {
  if (android()) return !state.permissions.microphone || !state.permissions.keyboard ? `<div class="setup">Enable the microphone and Dictate keyboard to speak into text fields.<div class="inline-actions">${button('Allow microphone', 'permissions', 'quiet')}${button('Enable keyboard', 'keyboard-settings', 'quiet')}${button('Choose keyboard', 'choose-keyboard', 'quiet')}</div></div>` : '';
  const permissionMissing = state.permissions && (!state.permissions.microphone || !state.permissions.accessibility);
  if (permissionMissing) return `<div class="setup">Allow microphone and Accessibility access to dictate in other apps.<br>${button('Allow access', 'permissions', 'quiet')}</div>`;
  if (state.wayland) return '<div class="setup">On Wayland, use Record. If the destination field is unavailable, copy the transcript from History.</div>';
  return '';
}
function home() {
  const phase = state.phase, idle = phase === 'idle', shortcut = state.platform === 'mac' ? 'fn' : 'F8';
  const ready = state.models.whisper;
  const week = state.usage.week, words = week.reduce((total, day) => total + day.words, 0);
  const max = Math.max(1, ...week.map(day => day.words));
  return `${setup()}<section class="recording" data-phase="${phase}"><div class="recording-top"><span class="wave" aria-hidden="true">${'<i></i>'.repeat(7)}</span><div><h2>${idle ? 'Ready to dictate' : phase === 'listening' ? 'Listening' : phase === 'processing' ? 'Processing your words' : 'Inserting text'}</h2><p>${idle ? (android() ? 'Use the Dictate keyboard in any text field.' : 'Put the cursor where your words belong.') : phase === 'listening' ? (android() ? 'Speak naturally. Tap Cancel to stop.' : 'Speak naturally. Press Esc to cancel.') : 'Your transcript will appear in the selected field.'}</p></div>${idle && !ready ? button('Get model', 'go-speech', 'primary', false) : button(idle ? 'Record' : phase === 'listening' ? 'Stop' : 'Cancel', idle ? (android() ? 'dictate-field' : 'start') : phase === 'listening' ? 'finish' : 'cancel', 'primary', state.preparing || !!unavailable || busy.has('start'), idle ? 'mic' : phase === 'listening' ? 'stop' : 'close')}</div>${android() ? '<div class="keyboard-hint">Choose Dictate, tap Record, then tap Stop to insert.</div>' : `<div class="keyboard-hint"><kbd>${shortcut}</kbd><span>Hold to speak. Release to insert. Press twice for hands-free.</span></div>`}</section><section class="weekly"><div class="section-title"><h3>This week</h3><span><strong>${words.toLocaleString()}</strong> words</span></div><div class="chart" role="img" aria-label="Words per day: ${week.map(day => `${day.date}: ${day.words}`).join(', ')}">${week.map(day => `<div class="day" title="${day.words} words"><div class="bar-track"><div class="bar" style="height:${Math.round(day.words / max * 52)}px"></div></div><small>${new Date(day.date + 'T12:00:00').toLocaleDateString('en', { weekday: 'short' })}</small></div>`).join('')}</div><div class="metrics"><span><strong>${week.reduce((sum, day) => sum + day.dictations, 0)}</strong>dictations</span>${state.usage.words_per_minute ? `<span><strong>${state.usage.words_per_minute}</strong>words / min</span>` : ''}${state.usage.median_latency !== null ? `<span><strong>${(state.usage.median_latency / 1000).toFixed(1)}s</strong>to text</span>` : ''}</div></section><section><div class="section-title"><h3>Recent dictations</h3>${button('View all', 'go-history', 'quiet')}</div>${state.history.length ? state.history.slice(0, 3).map(transcript).join('') : empty('Your first dictation starts here.', android() ? 'Choose Dictate in a text field, tap Record, and speak.' : `Hold ${shortcut}, speak, and release. Your transcript will be saved here.`)}</section>`;
}
function transcript(item) {
  const date = new Date(item.date).toLocaleString('en', { month: 'short', day: 'numeric', hour: 'numeric', minute: '2-digit' });
  return `<article class="transcript"><p class="transcript-text">${escape(item.text)}</p>${item.note ? `<p class="delivery-note">${escape(item.note)}</p>` : ''}<div class="transcript-meta"><span><time datetime="${escape(item.date)}">${escape(date)}</time>${item.speech_engine ? `<span class="engine-tag">${engineName(item.speech_engine)}</span>` : ''}${!item.note && !['verified', 'sent'].includes(item.delivery) ? `<span>${escape(item.delivery.replaceAll('_', ' '))}</span>` : ''}</span><div class="copy-actions"><button class="quiet" data-copy="${escape(item.id)}" data-original="false" aria-label="Copy transcript">${icon('copy')}Copy</button><button class="quiet" data-copy="${escape(item.id)}" data-original="true">Original</button></div></div></article>`;
}
function empty(title, detail) { return `<div class="empty">${icon('clock')}<h3>${escape(title)}</h3><p>${escape(detail)}</p></div>`; }
function history() {
  return `<div class="page-title"><div><h2>History</h2><p>Your last 50 dictations, saved on this device.</p></div>${button(android() ? 'Export log' : 'Open log', 'open-log', 'quiet', false, 'external')}</div><div class="toolbar"><input class="search" type="search" id="history-search" aria-label="Search dictations" placeholder="Search dictations" value="${escape(search)}"></div><div id="transcripts">${historyItems()}</div>`;
}
function historyItems() {
  const matching = state.history.filter(item => item.text.toLowerCase().includes(search.toLowerCase()) || item.raw.toLowerCase().includes(search.toLowerCase()));
  return matching.length ? matching.map(transcript).join('') : empty(search ? 'No matching dictations.' : 'No dictations yet.', search ? 'Try another word or phrase.' : 'Your transcripts will appear here after you dictate.');
}
function toggle(label, detail, key, checked) {
  return `<div class="setting-row"><div><label for="toggle-${key}">${label}</label>${detail ? `<p>${detail}</p>` : ''}</div><input id="toggle-${key}" class="switch" type="checkbox" role="switch" data-setting="${key}" ${checked ? 'checked' : ''} ${busy.has('settings') || !!unavailable ? 'disabled' : ''}></div>`;
}
function settings() {
  return `<div class="page-title"><div><h2>Settings</h2><p>Make Dictate work the way you speak.</p></div></div><nav class="subnav" aria-label="Settings sections">${[['speech', 'Speech'], ['cleanup', 'Cleanup'], ['dictionary', 'Dictionary'], ['general', 'General']].map(([key, label]) => `<button data-section="${key}" aria-current="${settingPage === key ? 'page' : 'false'}">${label}</button>`).join('')}</nav>${settingPage === 'speech' ? speechSettings() : settingPage === 'cleanup' ? cleanupSettings() : settingPage === 'dictionary' ? dictionarySettings() : generalSettings()}`;
}
function speechSettings() {
  return `<h3>Speech recognition</h3><p class="section-description">Whisper recognizes English on your CPU. No GPU or Neural Engine is required.</p><div class="setting-row"><span>English model · 60 MB</span><span id="model-status" class="model-status">${state.models.whisper ? 'Downloaded' : 'Not downloaded'}</span></div><div class="inline-actions">${button(state.preparing ? 'Preparing model…' : 'Download and load model', 'prepare', 'primary', state.phase !== 'idle' || state.preparing || !!unavailable || busy.has('prepare'))}</div>${shortcutsSettings()}`;
}
function shortcutsSettings() {
  if (android()) return `<div class="group">${setup()}<h3>Voice keyboard</h3><p class="section-description">Choose Dictate in a text field. Tap Record to speak, then Stop to insert. The keyboard also offers undo, original text, repeat, and selected-word capture.</p><div class="inline-actions">${button('Enable keyboard', 'keyboard-settings')}${button('Choose keyboard', 'choose-keyboard')}</div></div>`;
  return `<div class="group">${setup()}<h3>Shortcuts</h3><div class="shortcuts"><div><span>Hold to dictate</span><kbd>${state.platform === 'mac' ? 'fn' : 'F8'}</kbd></div><div><span>Hands-free dictation</span><span><kbd>${state.platform === 'mac' ? 'fn' : 'F8'}</kbd> twice</span></div><div><span>Cancel dictation</span><kbd>Esc</kbd></div><div><span>Repeat last dictation</span><kbd>${state.platform === 'mac' ? '⌃ ⌘ V' : 'Ctrl Shift V'}</kbd></div><div><span>Add selected word</span><kbd>${state.platform === 'mac' ? 'fn D' : 'F8 D'}</kbd></div></div>${state.permissions && !state.permissions.fn_free ? '<p class="section-description">Set “Press fn key to” to “Do Nothing” in macOS Keyboard Settings.</p>' + button('Open Keyboard Settings', 'keyboard-settings', 'quiet', false, 'external') : ''}</div>`;
}
function keyboardHome() {
  const idle = state.phase === 'idle', listening = state.phase === 'listening';
  const ready = state.models.whisper;
  return `<section class="recording" data-phase="${state.phase}"><div class="recording-top"><span class="wave" aria-hidden="true">${'<i></i>'.repeat(7)}</span><div><h2>${idle ? (state.can_record ? 'Ready to dictate' : 'Dictation unavailable here') : listening ? 'Listening' : 'Processing'}</h2></div>${ready ? button(idle ? 'Record' : listening ? 'Stop' : 'Cancel', idle ? 'start' : listening ? 'finish' : 'cancel', 'primary', state.preparing || (idle && !state.can_record), idle ? 'mic' : listening ? 'stop' : 'close') : button('Get model', 'open-app', 'primary')}${listening ? button('Cancel', 'cancel', 'quiet') : ''}</div></section><div class="keyboard-actions">${button('Undo', 'undo', 'secondary', !idle)}${button('Original', 'restore', 'secondary', !idle)}${button('Repeat', 'repeat', 'secondary', !idle || !state.can_record || !state.history.length)}${button('Add word', 'capture-word', 'quiet', !idle || !state.can_record)}${button('Settings', 'open-app', 'quiet')}${button('Keyboard', 'choose-keyboard', 'quiet')}</div>`;
}
function cleanupSettings() {
  const settings = state.settings, provider = settings.provider;
  const connected = state.credentials[provider];
  return `<h3>Local cleanup</h3><p class="section-description">These rules and your dictionary run on this device.</p>${toggle('Remove filler sounds', 'Remove “um” and “uh”.', 'fillers', settings.rules.fillers)}${toggle('Remove stutters', 'Remove repeated words.', 'repeats', settings.rules.repeats)}${toggle('Apply “scratch that”', 'Remove the phrase you corrected.', 'scratch_that', settings.rules.scratch_that)}<div class="group"><h3>Smart cleanup</h3>${toggle('Smart cleanup', 'Improve punctuation and phrasing.', 'smart_cleanup', settings.smart_cleanup)}<p class="section-description">When enabled, your transcript is sent to the selected provider. Audio stays on this device.</p><div class="setting-row provider-row"><label for="provider">Provider</label><select id="provider" class="select" ${busy.has('settings') ? 'disabled' : ''}>${[['groq', 'Groq'], ['deep_seek', 'DeepSeek'], ['chat_gpt', 'ChatGPT']].map(([key, label]) => `<option value="${key}" ${provider === key ? 'selected' : ''}>${label}</option>`).join('')}</select></div><p class="connection-state">${connected ? icon('check') : ''}${state.credentials_error ? 'Credential store unavailable.' : connected === undefined ? (busy.has('credentials') ? 'Checking connection…' : 'Connection status not checked.') : connected ? 'Connected' : 'Not connected'}</p>${state.credentials_error ? `<p class="delivery-note">${escape(state.credentials_error)}</p>` : ''}${provider === 'chat_gpt' ? `<div class="inline-actions">${button(state.signing_in ? 'Signing in…' : 'Sign in with ChatGPT', 'sign-in', 'primary', state.signing_in || busy.has('sign-in'))}${button('Sign out', 'sign-out', 'quiet', !connected || busy.has('sign-out'))}</div>` : `<div class="field"><label for="api-key">API key</label><input id="api-key" data-provider="${provider}" type="password" placeholder="Enter API key" autocomplete="off" spellcheck="false"></div><div class="inline-actions">${button('Save key', 'save-key', 'primary', busy.has('save-key'))}${button('Remove key', 'remove-key', 'quiet', !connected || busy.has('remove-key'))}</div><p class="section-description">Keys are stored in your system credential store.</p>`}</div>`;
}
function dictionarySettings() {
  return `<h3>Personal dictionary</h3><p class="section-description">Teach Dictate how to write names and phrases. Add what it hears and what you want written.</p>${state.settings.dictionary.length ? state.settings.dictionary.map((entry, index) => `<div class="dictionary-row"><p>${escape(entry.heard)}<span aria-hidden="true">→</span>${escape(entry.written)}</p><button data-remove-word="${index}" aria-label="Remove ${escape(entry.written)}">${icon('trash')}</button></div>`).join('') : empty('Add your first word.', 'Names, abbreviations, and uncommon words belong here.')}<form id="word-form"><div class="field-pair"><div class="field"><label for="heard">Heard as</label><input id="heard" type="text" placeholder="Example: yohannes" maxlength="1024" required></div><div class="field"><label for="written">Write as</label><input id="written" type="text" placeholder="Example: Yohannes" maxlength="1024" required></div></div><div class="inline-actions"><button type="submit" class="primary" ${busy.has('add-word') ? 'disabled' : ''}>Add word</button></div></form>`;
}
function generalSettings() {
  const mobile = android();
  return `<h3>General</h3>${mobile ? '' : toggle('Open at login', 'Keep Dictate ready when you sign in.', 'open_at_login', state.settings.open_at_login)}<div class="group"><h3>Last dictation</h3><p class="section-description">${mobile ? 'Use Undo or Original in the Dictate keyboard.' : 'Focus the original field. Use the Dictate menu icon to undo or restore its text.'} Corrections require unchanged text and caret within five minutes.</p></div>${state.platform === 'mac' ? `<div class="group"><h3>Permissions</h3><p class="section-description">Microphone: ${state.permissions?.microphone ? 'allowed' : 'not allowed'}. Accessibility: ${state.permissions?.accessibility ? 'allowed' : 'not allowed'}.</p><div class="inline-actions">${button('Allow access', 'permissions')}${button('Enable shortcuts', 'enable-shortcuts')}</div></div>` : mobile ? `<div class="group"><h3>Permissions</h3><p class="section-description">Microphone: ${state.permissions.microphone ? 'allowed' : 'not allowed'}. Voice keyboard: ${state.permissions.keyboard ? 'enabled' : 'not enabled'}.</p><div class="inline-actions">${button('Allow microphone', 'permissions')}${button('Enable keyboard', 'keyboard-settings')}${button('Choose keyboard', 'choose-keyboard')}</div></div>` : ''}<div class="group"><h3>App</h3><p class="section-description">${mobile ? 'The Dictate keyboard stays available after you close the app.' : 'Closing this window keeps dictation and the menu icon available.'}</p><div class="inline-actions">${button(mobile ? 'Export transcript log' : 'Open transcript log', 'open-log', 'quiet', false, 'external')}${button(mobile ? 'Close Dictate' : 'Quit Dictate', 'quit', 'quiet')}</div></div>`;
}

document.addEventListener('click', event => {
  const target = event.target.closest('button');
  if (!target || target.disabled) return;
  if (target.dataset.page) { if (android()) document.activeElement.blur(); page = target.dataset.page; render(); $('#content').scrollTop = 0; if (page === 'settings' && settingPage === 'cleanup') command('credentials'); return; }
  if (target.dataset.section) { if (android()) document.activeElement.blur(); settingPage = target.dataset.section; render(); $('#content').scrollTop = 0; if (settingPage === 'cleanup') command('credentials'); return; }
  if (target.dataset.copy) { windowAction({ type: 'copy', id: target.dataset.copy, original: target.dataset.original === 'true' }, () => toast(target.dataset.original === 'true' ? 'Original copied' : 'Transcript copied')); return; }
  if (target.dataset.removeWord !== undefined) { const entry = state.settings.dictionary[Number(target.dataset.removeWord)]; if (entry) command('remove-word', entry); return; }
  const action = target.dataset.action;
  if (action === 'dictate-field') { $('#dictation-test').focus(); windowAction(action); }
  else if (['choose-keyboard', 'open-app', 'capture-word'].includes(action)) windowAction(action);
  else if (android() && ['permissions', 'keyboard-settings'].includes(action)) windowAction(action);
  else if (action === 'go-history') { if (android()) document.activeElement.blur(); page = 'history'; render(); }
  else if (action === 'go-speech') { if (android()) document.activeElement.blur(); page = 'settings'; settingPage = 'speech'; render(); }
  else if (action === 'dismiss') { localNotice = null; if (!unavailable) command('dismiss'); else renderNotice(); }
  else if (['quit', 'open-log', 'keyboard-settings'].includes(action)) windowAction(action);
  else if (action === 'save-key') {
    const provider = state.settings.provider;
    const key = $('#api-key').value;
    command(action, { provider, key }, () => {
      if (state.settings.provider === provider && $('#api-key')?.value === key) $('#api-key').value = '';
      toast('Key saved');
    });
  }
  else if (action === 'remove-key') command(action, { provider: state.settings.provider }, () => toast('Key removed'));
  else if (action) command(action);
});
document.addEventListener('change', event => {
  const target = event.target;
  if (target.dataset.setting) command('settings', { [target.dataset.setting]: target.checked });
  if (target.id === 'provider') { if (android()) document.activeElement.blur(); const key = $('#api-key'); if (key) key.value = ''; command('settings', { provider: target.value }, () => command('credentials')); }
});
document.addEventListener('input', event => {
  if (event.target.id === 'history-search') { search = event.target.value; render(); }
});
document.addEventListener('submit', event => {
  if (event.target.id !== 'word-form') return;
  event.preventDefault();
  const entry = { heard: $('#heard').value, written: $('#written').value };
  if (android()) document.activeElement.blur();
  command('add-word', entry, () => {
    const heard = $('#heard'), written = $('#written');
    if (heard?.value === entry.heard && written?.value === entry.written) { heard.value = ''; written.value = ''; }
    toast('Word added');
  });
});
document.addEventListener('keydown', event => {
  if (event.key === 'Escape' && state?.phase !== 'idle') command('cancel');
});

document.addEventListener('focusout', () => { if (android() && !keyboard()) setTimeout(render, 0); });

if (api) {
  api.subscribe(event => {
    if (event.type === 'state') apply(event.state);
    else if (event.type === 'notice') notify(event.message);
    else if (event.type === 'unavailable') { unavailable = event.message; render(); }
    else if (event.type === 'navigate') { page = event.page; if (event.section) settingPage = event.section; if (android()) document.activeElement.blur(); render(); if (page === 'settings' && settingPage === 'cleanup') command('credentials'); }
    else if (event.type === 'capture-word') { page = 'settings'; settingPage = 'dictionary'; render(); $('#written').value = event.word; $('#heard').focus(); }
  });
  try { const initial = await api.state(); unavailable = initial.unavailable; if (initial.section) { page = 'settings'; settingPage = ['speech', 'cleanup', 'dictionary', 'general'].includes(initial.section) ? initial.section : 'speech'; }
    if (initial.word) { page = 'settings'; settingPage = 'dictionary'; }
    apply(initial.state); if (initial.word) $('#written').value = initial.word; renderNotice(); }
  catch (error) { unavailable = error.message; renderNotice(); }
} else { unavailable = 'Open Dictate from the installed app to start the dictation service.'; renderNotice(); }
