const { contextBridge, ipcRenderer } = require('electron');

async function invoke(channel, ...args) {
  const reply = await ipcRenderer.invoke(channel, ...args);
  if (!reply.ok) throw new Error(reply.error);
  return reply.result;
}

contextBridge.exposeInMainWorld('dictate', {
  state: () => invoke('dictate:state'),
  command: (command, args = {}) => invoke('dictate:command', command, args),
  window: action => invoke('dictate:window', action),
  subscribe: callback => {
    const handler = (_, event) => callback(event);
    ipcRenderer.on('dictate:event', handler);
    return () => ipcRenderer.removeListener('dictate:event', handler);
  }
});
