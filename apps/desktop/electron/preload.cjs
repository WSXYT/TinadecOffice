const { contextBridge, ipcRenderer } = require('electron');

contextBridge.exposeInMainWorld('tinadec', {
  gatewayUrl: () => process.env.TINADEC_GATEWAY_URL ?? 'http://127.0.0.1:48730',
  openProjectDialog: () => ipcRenderer.invoke('tinadec:open-project'),
  minimizeWindow: () => ipcRenderer.send('tinadec:minimize'),
  maximizeWindow: () => ipcRenderer.send('tinadec:maximize'),
  closeWindow: () => ipcRenderer.send('tinadec:close')
});
