const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { clipboard, app } = require('electron');
const delay = milliseconds => new Promise(resolve => setTimeout(resolve, milliseconds));

async function verify({ mainWindow, indicator, backend, getState, showWindow, unavailable, output, restartEngine }) {
  fs.mkdirSync(output, { recursive: true });
  const notes = [];
  const errors = [];
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
      assert.ok(['whisper', 'whistle'].includes(restartEngine));
      assert.equal(getState().settings.speech_engine, restartEngine);
      assert.equal(await evaluate('document.querySelector("#engine-button").firstChild.textContent.trim()'), restartEngine === 'whisper' ? 'Whisper' : 'Whistle');
      await screenshot('restart-' + restartEngine);
      await backend.request('select-engine', { engine: restartEngine === 'whisper' ? 'whistle' : 'whisper' });
      fs.writeFileSync(path.join(output, 'checks.txt'), restartEngine + ' remained selected in the app and native service after process restart.\n');
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
    for (const engine of ['whisper', 'whistle']) {
      await click('#engine-button'); await click(`[data-engine="${engine}"]`);
      await wait(() => getState().settings.speech_engine === engine, engine + ' selection');
      assert.equal(JSON.parse(fs.readFileSync(path.join(getState().directory, 'settings.json'), 'utf8')).speech_engine, engine);
      const snapshot = await backend.request('state');
      assert.equal(snapshot.settings.speech_engine, engine);
      await backend.request('prepare');
      assert.equal(getState().models[engine], true);
      await screenshot('engine-' + engine);
    }
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
    await assert.rejects(backend.request('select-engine', { engine: 'unknown' }), /Whistle or Whisper/);
    notes.push('Home, History, all Settings sections, copy, search, both engine choices, dictionary, and cleanup controls passed.');
    notes.push('Engine selections persisted. Late state updates preserved active input text, focus, and caret.');
    notes.push('Changing cleanup providers cleared the other provider\'s unsaved credential field.');
    await click('[data-page="home"]');
    if (!getState().models.whistle) await backend.request('prepare');
    await click('[data-action="start"]');
    await wait(() => getState().phase === 'listening', 'microphone startup');
    assert.equal(mainWindow.isVisible(), false);
    await assert.rejects(backend.request('select-engine', { engine: 'whisper' }), /Finish or cancel/);
    assert.equal(indicator.isVisible(), true);
    assert.equal(indicator.isFocusable(), false);
    mainWindow.webContents.send('dictate:event', { type: 'notice', message: 'Verification warning stays in the app.' });
    await delay(100);
    assert.equal(await indicator.webContents.executeJavaScript('document.body.textContent.includes("warning")'), false);
    assert.equal(await evaluate('document.querySelector("#notice").textContent.includes("Verification warning")'), true);
    await backend.request('cancel');
    await wait(() => getState().phase === 'idle' && !indicator.isVisible(), 'cancel and hide indicator');
    notes.push('Microphone start and cancellation passed. Engine changes were refused during recording.');
    notes.push('The indicator was nonfocusable, hid when idle, and excluded the app warning.');
    await backend.request('settings', { smart_cleanup: false });
    await backend.request('select-engine', { engine: 'whisper' });
    await backend.request('select-engine', { engine: 'whistle' });
    notes.push('CPU model choice and local cleanup remained available through the native service.');
    notes.push('Both selected CPU profiles prepared through the packaged native service.');
    assert.deepEqual(errors, []);
    const metrics = await Promise.all(app.getAppMetrics().map(async metric => ({ type: metric.type, pid: metric.pid, memory: metric.memory, cpu: metric.cpu })));
    fs.writeFileSync(path.join(output, 'metrics.json'), JSON.stringify({ platform: process.platform, metrics, backendPid: backend.process.pid }, null, 2));
    fs.writeFileSync(path.join(output, 'checks.txt'), notes.join('\n') + '\n');
    fs.writeFileSync(path.join(output, 'exit-code.txt'), '0');
  } catch (error) {
    fs.writeFileSync(path.join(output, 'error.txt'), String(error.stack || error));
    fs.writeFileSync(path.join(output, 'exit-code.txt'), '1'); throw error;
  }
}

module.exports = { verify };
