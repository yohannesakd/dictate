const { EventEmitter } = require('node:events');
const { spawn } = require('node:child_process');

class Backend extends EventEmitter {
  constructor(executable, args = []) {
    super();
    this.nextId = 0;
    this.pending = new Map();
    this.buffer = '';
    this.closed = false;
    this.process = spawn(executable, args, { stdio: ['pipe', 'pipe', 'pipe'], windowsHide: true });
    this.process.stdout.setEncoding('utf8');
    this.process.stdout.on('data', chunk => this.read(chunk));
    // Native diagnostics can contain transcripts. Keep them out of application logs.
    this.process.stderr.resume();
    this.process.on('error', error => this.fail(new Error(`The dictation service could not start: ${error.message}`)));
    this.process.on('exit', () => this.fail(new Error('The dictation service stopped. Reopen Dictate to continue.')));
    this.process.stdin.on('error', error => this.fail(error));
  }

  read(chunk) {
    this.buffer += chunk;
    while (this.buffer.includes('\n')) {
      const index = this.buffer.indexOf('\n');
      if (index > 8 * 1024 * 1024) return this.fail(new Error('The dictation service returned too much data.'));
      const line = this.buffer.slice(0, index);
      this.buffer = this.buffer.slice(index + 1);
      let message;
      try { message = JSON.parse(line); }
      catch { return this.fail(new Error('The dictation service returned an invalid response.')); }
      if (message.event) this.emit(message.event, message.data);
      else {
        const request = this.pending.get(message.id);
        if (!request) continue;
        clearTimeout(request.timer);
        this.pending.delete(message.id);
        if (message.ok === true) request.resolve(message.result);
        else request.reject(new Error(message.error || 'The action did not finish.'));
      }
    }
    if (this.buffer.length > 8 * 1024 * 1024) this.fail(new Error('The dictation service returned too much data.'));
  }

  request(command, args = {}) {
    if (this.closed) return Promise.reject(new Error('The dictation service is unavailable. Reopen Dictate to continue.'));
    if (this.pending.size >= 8) return Promise.reject(new Error('Wait for the current action to finish.'));
    const id = ++this.nextId;
    const line = JSON.stringify({ id, command, args }) + '\n';
    if (Buffer.byteLength(line) > 1024 * 1024) return Promise.reject(new Error('The app request is too large.'));
    return new Promise((resolve, reject) => {
      const timer = setTimeout(() => {
        this.pending.delete(id);
        reject(new Error('The action did not respond in time. Check its status before trying again.'));
      }, command === 'sign-in' ? 330000 : command === 'prepare' ? 300000 : 120000);
      this.pending.set(id, { resolve, reject, timer });
      this.process.stdin.write(line);
    });
  }

  fail(error) {
    if (this.closed) return;
    this.closed = true;
    for (const request of this.pending.values()) { clearTimeout(request.timer); request.reject(error); }
    this.pending.clear();
    this.emit('unavailable', error.message);
    this.process.stdin.end();
  }

  async stop() {
    if (this.process.exitCode !== null || this.process.signalCode !== null) return;
    this.process.stdin.end();
    await new Promise(resolve => {
      const deadline = setTimeout(() => { this.process.kill(); resolve(); }, 4000);
      this.process.once('exit', () => { clearTimeout(deadline); resolve(); });
    });
  }
}

module.exports = { Backend };
