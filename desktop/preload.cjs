const { contextBridge, ipcRenderer } = require('electron');

contextBridge.exposeInMainWorld('dictate', {
  state: () => ipcRenderer.invoke('dictate:state'),
  command: (command, args = {}) => ipcRenderer.invoke('dictate:command', command, args),
  window: action => ipcRenderer.invoke('dictate:window', action),
  subscribe: callback => {
    const handler = (_, event) => callback(event);
    ipcRenderer.on('dictate:event', handler);
    return () => ipcRenderer.removeListener('dictate:event', handler);
  }
});
