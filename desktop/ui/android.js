if (window.AndroidBridge) {
  const pending = new Map(), listeners = new Set();
  let sequence = 0;
  window.dictateReceive = message => {
    if (message.id !== undefined) {
      const action = pending.get(message.id);
      if (!action) return;
      pending.delete(message.id); clearTimeout(action.timer);
      if (message.ok) action.resolve(message.result);
      else action.reject(new Error(message.error));
    } else for (const listener of listeners) listener(message);
  };
  function request(type, payload = {}) {
    if (pending.size >= 8) return Promise.reject(new Error('Wait for the current action to finish.'));
    const id = ++sequence;
    return new Promise((resolve, reject) => {
      const timer = setTimeout(() => { pending.delete(id); reject(new Error('The action did not finish in time.')); }, 360000);
      pending.set(id, { resolve, reject, timer });
      try { window.AndroidBridge.post(JSON.stringify({ id, type, payload })); }
      catch (error) { pending.delete(id); clearTimeout(timer); reject(error); }
    });
  }
  window.dictate = {
    state: () => request('state'),
    command: (command, args = {}) => request('command', { command, args }),
    window: action => request('window', action),
    subscribe: callback => { listeners.add(callback); return () => listeners.delete(callback); }
  };
}
