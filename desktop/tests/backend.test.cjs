const test = require('node:test');
const assert = require('node:assert/strict');
const path = require('node:path');
const { Backend } = require('../backend.cjs');
const fixture = path.join(__dirname, 'fixtures', 'backend.cjs');

test('The transport matches concurrent replies by identity and accepts fragmented state events.', async () => {
  const backend = new Backend(process.execPath, [fixture]);
  const state = new Promise(resolve => backend.once('state', resolve));
  try {
    const first = backend.request('slow', { value: 'first' });
    const second = backend.request('fast', { value: 'second' });
    assert.equal(await second, 'second'); assert.equal(await first, 'first');
    assert.equal((await state).sequence, 1);
    await assert.rejects(backend.request('failed'), /refused/);
  } finally { await backend.stop(); }
});

test('Service exit rejects pending work and does not replay it.', async () => {
  const backend = new Backend(process.execPath, [fixture]);
  backend.on('unavailable', () => {});
  try {
    await assert.rejects(backend.request('exit'), /stopped/);
    await assert.rejects(backend.request('fast'), /unavailable/);
    assert.equal(backend.pending.size, 0);
  } finally { await backend.stop(); }
});

test('The transport bounds outstanding commands and payload size.', async () => {
  const backend = new Backend(process.execPath, [fixture]);
  try {
    await assert.rejects(backend.request('fast', { text: 'x'.repeat(1024 * 1024) }), /too large/);
    const pending = Array.from({ length: 8 }, (_, index) => backend.request('slow', { value: index }));
    await assert.rejects(backend.request('fast'), /Wait/);
    await Promise.all(pending);
  } finally { await backend.stop(); }
});
