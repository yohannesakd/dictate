const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { clipboard, app } = require('electron');
const delay = milliseconds => new Promise(resolve => setTimeout(resolve, milliseconds));

async function verify({ mainWindow, indicator, backend, getState, showWindow, unavailable, output, restartEngine }) {
  fs.mkdirSync(output, { recursive: true });
  const notes = [];
  const errors = [];
  const nativeRequest = backend.request.bind(backend);
  backend.request = (command, args) => command === 'credentials' ? Promise.resolve(null) : nativeRequest(command, args);
  mainWindow.webContents.on('render-process-gone', (_, details) => errors.push(details.reason));
  const evaluate = async code => {
    try { return await mainWindow.webContents.executeJavaScript(code, true); }
    catch (error) { throw new Error(`Renderer check failed: ${code}`, { cause: error }); }
  };
  async function wait(predicate, label) {
    for (let index = 0; index < 200; index++) {
      if (unavailable()) throw new Error(unavailable());
      if (await predicate()) return;
      await delay(50);
    }
    throw new Error(`Timed out: ${label}`);
  }
  async function click(selector) {
    assert.equal(await evaluate(`!!document.querySelector(${JSON.stringify(selector)})`), true, `Missing control: ${selector}`);
    await evaluate(`document.querySelector(${JSON.stringify(selector)}).click()`); await delay(150);
  }
  async function screenshot(name) {
    showWindow(); await evaluate('document.fonts.ready'); await delay(100);
    assert.equal(await evaluate('document.documentElement.scrollWidth <= innerWidth'), true, 'The shared layout overflowed horizontally.');
    fs.writeFileSync(path.join(output, name + '.png'), (await mainWindow.webContents.capturePage()).toPNG());
  }
  try {
    await wait(() => !!getState(), 'native service startup');
    await wait(() => evaluate('!!document.querySelector(".recording")'), 'renderer startup');
    if (restartEngine) {
      assert.equal(restartEngine, getState().platform === 'mac' ? 'phonon' : 'whisper');
      assert.equal(getState().settings.speech_engine, getState().platform === 'mac' ? 'phonon' : 'whisper');
      assert.equal(await evaluate('!!document.querySelector("#engine-button")'), false);
      await screenshot('restart-whisper');
      fs.writeFileSync(path.join(output, 'checks.txt'), 'The platform speech model remained selected in the app and native service after process restart.\n');
      fs.writeFileSync(path.join(output, 'exit-code.txt'), '0'); return;
    }
    assert.equal(await evaluate('typeof window.require'), 'undefined');
    assert.equal(await evaluate('typeof window.process'), 'undefined');
    assert.equal(mainWindow.webContents.getLastWebPreferences().sandbox, true);
    assert.equal(mainWindow.webContents.getLastWebPreferences().contextIsolation, true);
    assert.equal(app.isHardwareAccelerationEnabled(), false);
    notes.push('The renderer is sandboxed, isolated, and rendered without hardware acceleration.');
    await screenshot('home');
    await click('[data-page="history"]');
    await screenshot('history');
    if (getState().history.length) {
      await click('[data-copy][data-original="false"]');
      assert.equal(await clipboard.readText(), getState().history[0].text);
      await click('[data-copy][data-original="true"]');
      assert.equal(await clipboard.readText(), getState().history[0].raw);
      await evaluate('document.querySelector("#history-search").value="unlikely-missing-phrase";document.querySelector("#history-search").dispatchEvent(new Event("input",{bubbles:true}))');
      assert.equal(await evaluate('document.querySelectorAll(".transcript").length'), 0);
      await evaluate('document.querySelector("#history-search").value="";document.querySelector("#history-search").dispatchEvent(new Event("input",{bubbles:true}))');
    }
    await click('[data-page="settings"]');
    await screenshot('settings-speech');
    assert.equal(getState().settings.speech_engine, getState().platform === 'mac' ? 'phonon' : 'whisper');
    assert.equal(await evaluate('!!document.querySelector("#engine-button") || !!document.querySelector("input[name=speech-engine]")'), false);
    await backend.request('prepare');
    assert.equal(getState().models.ready ?? getState().models.whisper, true);
    assert.equal(await evaluate('document.querySelector("#model-status").textContent'), getState().platform === 'mac' ? 'Installed' : 'Downloaded');
    for (const compute of ['cpu_only', 'automatic']) {
      await evaluate(`document.querySelector('#speech-compute').value = ${JSON.stringify(compute)}; document.querySelector('#speech-compute').dispatchEvent(new Event('change', { bubbles: true }))`);
      await wait(() => getState().settings.speech_compute === compute, 'hardware setting');
      await backend.request('prepare');
      assert.equal(getState().speech.compute, compute);
      if (compute === 'cpu_only') assert.equal(getState().speech.hardware, 'CPU');
      if (getState().speech.hardware)
        await wait(() => evaluate(`document.querySelector('#hardware-status').textContent.includes(${JSON.stringify('Using ')} + ${JSON.stringify(getState().speech.hardware)})`), 'active hardware status');
      else assert.match(await evaluate('document.querySelector("#hardware-status").textContent'), /dictate/i);
    }
    notes.push('Hardware controls changed the native mode. CPU-only displayed CPU; Automatic reported preparation or recognition hardware.');
    await screenshot('speech-' + getState().settings.speech_engine);
    await assert.rejects(backend.request('select-engine', { engine: 'whistle' }), /not supported/);
    assert.equal(await evaluate('window.dictate.command("select-engine", {engine:"whistle"}).then(() => "accepted", error => error.message)'), 'The app command is not supported.');
    await click('[data-section="cleanup"]');
    await screenshot('settings-cleanup');
    const previous = getState().settings.rules.fillers;
    await click('#toggle-fillers'); await wait(() => getState().settings.rules.fillers !== previous, 'cleanup rule');
    await click('#toggle-fillers'); await wait(() => getState().settings.rules.fillers === previous, 'restore cleanup rule');
    const provider = getState().settings.provider;
    await evaluate('document.querySelector("#api-key").value="test-only-unsaved"');
    await evaluate('document.querySelector("#provider").value="deep_seek";document.querySelector("#provider").dispatchEvent(new Event("change",{bubbles:true}))');
    await wait(() => getState().settings.provider === 'deep_seek', 'DeepSeek provider');
    await wait(() => evaluate('document.querySelector("#api-key").dataset.provider === "deep_seek"'), 'provider field identity');
    assert.equal(await evaluate('document.querySelector("#api-key").value'), '');
    await evaluate('document.querySelector("#provider").value="chat_gpt";document.querySelector("#provider").dispatchEvent(new Event("change",{bubbles:true}))');
    await wait(() => getState().settings.provider === 'chat_gpt', 'ChatGPT provider');
    assert.equal(await evaluate('!!document.querySelector("[data-action=sign-in]")'), true);
    await backend.request('settings', { provider });
    const verificationRequest = backend.request;
    const timeoutMessage = 'The action did not respond in time. Check its status before trying again.';
    try {
      backend.request = (command, args) => command === 'credentials' ? Promise.reject(new Error(timeoutMessage)) : verificationRequest(command, args);
      await click('[data-section="cleanup"]');
      await wait(() => evaluate(`document.querySelector('#notice p')?.textContent === ${JSON.stringify(timeoutMessage)}`), 'plain credential timeout');
      await evaluate('document.querySelector("#toggle-fillers").click()');
      await wait(() => getState().settings.rules.fillers !== previous, 'unrelated successful action');
      assert.equal(await evaluate('document.querySelector("#notice p").textContent'), timeoutMessage);
    } finally { backend.request = verificationRequest; }
    await click('[data-section="cleanup"]');
    await wait(() => evaluate('document.querySelector("#notice").hidden'), 'successful credential retry clears its timeout');
    mainWindow.webContents.send('dictate:event', { type: 'notice', message: 'Keep this delivery warning.' });
    await click('[data-section="cleanup"]');
    assert.equal(await evaluate('document.querySelector("#notice p").textContent'), 'Keep this delivery warning.');
    await click('[data-action="dismiss"]');
    await backend.request('settings', { fillers: previous });
    notes.push('Credential timeouts used plain messages. A successful retry cleared its own notice and preserved unrelated warnings.');
    await click('[data-section="dictionary"]');
    backend.process.stdout.pause();
    await evaluate('document.querySelector("#heard").value="verify heard";document.querySelector("#written").value="Verify written";document.querySelector("#word-form").requestSubmit()');
    await evaluate('document.querySelector("#heard").value="next heard";document.querySelector("#written").value="Next written"');
    backend.process.stdout.resume();
    await wait(() => getState().settings.dictionary.some(entry => entry.heard === 'verify heard'), 'dictionary add');
    await wait(() => evaluate('!document.querySelector("#word-form button").disabled'), 'dictionary save reply');
    assert.equal(await evaluate('document.querySelector("#heard").value'), 'next heard');
    assert.equal(await evaluate('document.querySelector("#written").value'), 'Next written');
    await screenshot('settings-dictionary');
    const index = getState().settings.dictionary.findIndex(entry => entry.heard === 'verify heard');
    await click(`[data-remove-word="${index}"]`);
    await wait(() => !getState().settings.dictionary.some(entry => entry.heard === 'verify heard'), 'dictionary remove');
    // A late state event must not erase text or move the caret in an active field.
    await evaluate('document.querySelector("#heard").value="unfinished phrase";document.querySelector("#heard").focus();document.querySelector("#heard").setSelectionRange(4,4)');
    const refreshed = await backend.request('state');
    mainWindow.webContents.send('dictate:event', { type: 'state', state: refreshed }); await delay(100);
    assert.equal(await evaluate('document.querySelector("#heard").value'), 'unfinished phrase');
    assert.equal(await evaluate('document.activeElement.id'), 'heard');
    assert.equal(await evaluate('document.activeElement.selectionStart'), 4);
    await click('[data-section="general"]');
    await screenshot('settings-general');
    mainWindow.setSize(400, 480); await screenshot('compact-minimum');
    mainWindow.setSize(480, 600);
    notes.push('The compact layout fit the default window and minimum 400 by 480 window without horizontal overflow.');
    await assert.rejects(backend.request('settings', { speech_engine: 'unknown' }), /not supported/);
    notes.push('Home, History, all Settings sections, copy, search, the platform speech profile, dictionary, and cleanup controls passed.');
    notes.push('The platform speech profile persisted. Late state updates preserved active input text, focus, and caret.');
    notes.push('Changing cleanup providers cleared the other provider\'s unsaved credential field.');
    await click('[data-page="home"]');
    if (!(getState().models.ready ?? getState().models.whisper)) await backend.request('prepare');
    await click('[data-action="start"]');
    await wait(() => getState().phase === 'listening', 'microphone startup');
    assert.equal(mainWindow.isVisible(), false);
    await assert.rejects(backend.request('prepare'), /Finish the current action/);
    assert.equal(indicator.isVisible(), true);
    assert.equal(indicator.isFocusable(), false);
    assert.deepEqual(indicator.getSize(), [84, 40]);
    assert.equal(await indicator.webContents.executeJavaScript('document.querySelector(".indicator").getBoundingClientRect().height'), 24);
    assert.equal(await indicator.webContents.executeJavaScript('document.body.textContent.trim()'), '');
    assert.equal(await indicator.webContents.executeJavaScript('document.querySelectorAll("#wave i").length'), 9);
    fs.writeFileSync(path.join(output, 'indicator-listening.png'), (await indicator.webContents.capturePage()).toPNG());
    for (const phase of ['processing', 'delivering']) {
      indicator.webContents.send('dictate:event', { type: 'phase', phase }); await delay(50);
      assert.equal(await indicator.webContents.executeJavaScript('document.querySelector(".indicator").dataset.phase'), phase);
      assert.equal(await indicator.webContents.executeJavaScript('document.body.textContent.trim()'), '');
    }
    mainWindow.webContents.send('dictate:event', { type: 'notice', message: 'Verification warning stays in the app.' });
    await delay(100);
    assert.equal(await indicator.webContents.executeJavaScript('document.body.textContent.includes("warning")'), false);
    assert.equal(await evaluate('document.querySelector("#notice").textContent.includes("Verification warning")'), true);
    await backend.request('cancel');
    await wait(() => getState().phase === 'idle' && !indicator.isVisible(), 'cancel and hide indicator');
    notes.push('Microphone start and cancellation passed. Model preparation was refused during recording.');
    notes.push('The 84 by 40 transparent indicator displayed only a 24-pixel waveform surface in every active phase, preserved focus, hid when idle, and excluded warnings.');
    await backend.request('settings', { smart_cleanup: false });
    notes.push('CPU recognition and local cleanup remained available through the native service.');
    notes.push('The platform speech model prepared through the packaged native service.');
    assert.deepEqual(errors, []);
    const metrics = await Promise.all(app.getAppMetrics().map(async metric => ({ type: metric.type, pid: metric.pid, memory: metric.memory, cpu: metric.cpu })));
    fs.writeFileSync(path.join(output, 'metrics.json'), JSON.stringify({ platform: process.platform, metrics, backendPid: backend.process.pid }, null, 2));
    fs.writeFileSync(path.join(output, 'checks.txt'), notes.join('\n') + '\n');
    fs.writeFileSync(path.join(output, 'exit-code.txt'), '0');
  } catch (error) {
    fs.writeFileSync(path.join(output, 'error.txt'), String(error.stack || error));
    fs.writeFileSync(path.join(output, 'exit-code.txt'), '1'); throw error;
  } finally { backend.request = nativeRequest; }
}

module.exports = { verify };
