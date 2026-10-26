const { app, BrowserWindow, Menu, Tray, nativeImage, ipcMain, clipboard, screen, shell, systemPreferences } = require('electron');
const path = require('node:path');
const fs = require('node:fs');
const { Backend } = require('./backend.cjs');

app.disableHardwareAcceleration();
app.setName('Dictate');
const directoryFlag = process.argv.indexOf('--data-directory');
const dataDirectory = directoryFlag >= 0 ? process.argv[directoryFlag + 1] : null;
if (directoryFlag >= 0 && !dataDirectory) throw new Error('Supply a data directory.');
if (dataDirectory) app.setPath('userData', path.join(path.resolve(dataDirectory), 'electron'));
const ownsInstance = app.requestSingleInstanceLock();
if (!ownsInstance) app.quit();
let mainWindow, indicator, tray, backend, state, unavailable, quitting = false;
const verifyFlag = process.argv.indexOf('--verify-desktop');
const restartFlag = process.argv.indexOf('--verify-restart');
if (verifyFlag >= 0 && (!dataDirectory || !process.argv[verifyFlag + 1])) throw new Error('Verification requires isolated data and output directories.');
const commands = new Set(['prepare', 'settings', 'add-word', 'remove-word', 'permissions', 'enable-shortcuts',
  'start', 'finish', 'cancel', 'dismiss', 'credentials', 'save-key', 'remove-key', 'sign-in', 'sign-out']);
const rendererPath = path.join(__dirname, 'ui', 'index.html');
const indicatorPath = path.join(__dirname, 'ui', 'indicator.html');

function send(event, includeIndicator = false) {
  if (mainWindow && !mainWindow.isDestroyed()) mainWindow.webContents.send('dictate:event', event);
  if (includeIndicator && indicator && !indicator.isDestroyed()) indicator.webContents.send('dictate:event', event);
}
function showWindow(page) {
  mainWindow.show(); mainWindow.focus();
  if (page) send({ type: 'navigate', page });
}
function trayMenu() {
  if (!tray) return;
  tray.setContextMenu(Menu.buildFromTemplate([
    { label: 'Open Dictate', click: () => showWindow() },
    { type: 'separator' },
    { label: 'Undo last dictation', enabled: state?.phase === 'idle', click: () => run('undo') },
    { label: 'Restore original transcript', enabled: state?.phase === 'idle', click: () => run('restore') },
    { type: 'separator' }, { label: 'Quit Dictate', click: () => app.quit() }
  ]));
}
function updateIndicator() {
  if (!indicator) return;
  const phase = state?.phase || 'idle';
  indicator.webContents.send('dictate:event', { type: 'phase', phase });
  if (phase === 'idle' || unavailable) { indicator.hide(); return; }
  const { workArea } = screen.getDisplayNearestPoint(screen.getCursorScreenPoint());
  indicator.setPosition(Math.round(workArea.x + (workArea.width - 84) / 2), workArea.y + workArea.height - 64);
  indicator.showInactive();
}
async function run(command, args) {
  try {
    const result = await backend.request(command, args);
    if ((command === 'undo' || command === 'restore') && result === false) {
      showWindow();
      send({ type: 'notice', message: 'The original field cannot be corrected safely. Copy the transcript from History.' });
    }
    return result;
  }
  catch (error) { showWindow(); send({ type: 'notice', message: error.message }); return null; }
}
function trusted(event) {
  const window = BrowserWindow.fromWebContents(event.sender);
  if ((window !== mainWindow && window !== indicator) || event.senderFrame !== event.sender.mainFrame)
    throw new Error('The app request did not come from a trusted window.');
  return window;
}
function restrict(window) {
  window.webContents.setWindowOpenHandler(() => ({ action: 'deny' }));
  window.webContents.on('will-navigate', event => event.preventDefault());
  window.webContents.session.setPermissionRequestHandler((_, __, callback) => callback(false));
}
async function reply(action) {
  try { return { ok: true, result: await action() }; }
  catch (error) { return { ok: false, error: error.message }; }
}

if (ownsInstance) app.whenReady().then(async () => {
  const webPreferences = { preload: path.join(__dirname, 'preload.cjs'), contextIsolation: true, sandbox: true, nodeIntegration: false, webgl: false };
  mainWindow = new BrowserWindow({ width: 480, height: 600, minWidth: 400, minHeight: 480, show: false,
    backgroundColor: '#F8F9FB', title: 'Dictate', titleBarStyle: 'hidden', icon: path.join(__dirname, 'assets', 'icon.png'),
    ...(process.platform === 'darwin' ? { trafficLightPosition: { x: 14, y: 10 } } : { titleBarOverlay: { color: '#F8F9FB', symbolColor: '#656B78', height: 32 } }),
    webPreferences });
  restrict(mainWindow);
  mainWindow.on('close', event => { if (!quitting) { event.preventDefault(); mainWindow.hide(); } });
  mainWindow.once('ready-to-show', () => mainWindow.show());
  indicator = new BrowserWindow({ width: 84, height: 40, frame: false, transparent: true, show: false,
    focusable: false, skipTaskbar: true, alwaysOnTop: true, resizable: false,
    ...(process.platform === 'darwin' ? { type: 'panel' } : {}), webPreferences });
  restrict(indicator);
  indicator.setIgnoreMouseEvents(true);
  if (process.platform === 'darwin') indicator.setVisibleOnAllWorkspaces(true, { visibleOnFullScreen: true });
  indicator.webContents.on('did-finish-load', updateIndicator);
  const binary = path.join(__dirname.replace(/app\.asar$/, 'app.asar.unpacked'), 'resources', 'backend', process.platform === 'win32' ? 'Dictate.Desktop.exe' : 'Dictate.Desktop');
  backend = new Backend(binary, dataDirectory ? ['--data-directory', path.resolve(dataDirectory)] : []);
  backend.on('state', value => {
    if (state && value.sequence <= state.sequence) return;
    state = value; send({ type: 'state', state }); trayMenu(); updateIndicator();
  });
  backend.on('level', level => send({ type: 'level', level }, true));
  backend.on('capture-word', word => { showWindow('settings'); send({ type: 'capture-word', word }); });
  backend.on('unavailable', message => { unavailable = message; send({ type: 'unavailable', message }); updateIndicator(); });
  const image = nativeImage.createFromPath(path.join(__dirname, 'assets', process.platform === 'darwin' ? 'trayTemplate.png' : 'tray.png'));
  image.setTemplateImage(process.platform === 'darwin');
  tray = new Tray(image); tray.setToolTip('Dictate'); tray.on('click', () => showWindow()); trayMenu();
  const menu = [
    ...(process.platform === 'darwin' ? [{ label: 'Dictate', submenu: [{ role: 'about' }, { label: 'Settings…', accelerator: 'Cmd+,', click: () => showWindow('settings') }, { type: 'separator' }, { role: 'hide' }, { role: 'hideOthers' }, { type: 'separator' }, { role: 'quit' }] }] : []),
    { role: 'editMenu' }, { role: 'windowMenu' }
  ];
  Menu.setApplicationMenu(Menu.buildFromTemplate(menu));
  ipcMain.handle('dictate:state', event => reply(() => {
    const window = trusted(event);
    if (window === indicator) return { phase: state?.phase || 'idle' };
    return { state, unavailable };
  }));
  ipcMain.handle('dictate:command', (event, command, args = {}) => reply(async () => {
    const window = trusted(event);
    if (window !== mainWindow || typeof command !== 'string' || !commands.has(command)) throw new Error('The app command is not supported.');
    if (command === 'settings' && Object.hasOwn(args, 'open_at_login')) {
      if (typeof args.open_at_login !== 'boolean') throw new Error('Choose whether to open at login.');
      const old = state?.settings.open_at_login || false;
      await setLogin(args.open_at_login);
      try { return await backend.request(command, args); }
      catch (error) {
        try { await setLogin(old); }
        catch (rollback) { throw new Error(`${error.message} The login setting could not be restored: ${rollback.message}`); }
        throw error;
      }
    }
    if (command === 'permissions' && process.platform === 'darwin') await systemPreferences.askForMediaAccess('microphone');
    if (command === 'start') {
      // Return focus to the destination app before audio starts. The core captures the field at release.
      mainWindow.hide();
      if (process.platform === 'darwin') app.hide();
      await new Promise(resolve => setTimeout(resolve, 150));
    }
    try { return await backend.request(command, args); }
    catch (error) { if (command === 'start' && !quitting) showWindow(); throw error; }
  }));
  ipcMain.handle('dictate:window', (event, action) => reply(async () => {
    if (trusted(event) !== mainWindow) throw new Error('The window action is not supported.');
    if (action === 'quit') app.quit();
    else if (action === 'open-log' && state) { const error = await shell.openPath(state.directory); if (error) throw new Error(error); }
    else if (action === 'keyboard-settings' && process.platform === 'darwin') await shell.openExternal('x-apple.systempreferences:com.apple.Keyboard-Settings.extension');
    else if (action?.type === 'copy' && typeof action.id === 'string' && typeof action.original === 'boolean') {
      const record = state?.history.find(item => item.id === action.id);
      if (!record) throw new Error('This transcript is no longer available.');
      await clipboard.writeText(action.original ? record.raw : record.text);
    } else throw new Error('The window action is not supported.');
  }));
  await Promise.all([mainWindow.loadFile(rendererPath), indicator.loadFile(indicatorPath)]);
  if (verifyFlag >= 0) {
    require('./verification.cjs').verify({ mainWindow, indicator, backend, getState: () => state,
      showWindow, unavailable: () => unavailable, output: path.resolve(process.argv[verifyFlag + 1]),
      restartEngine: restartFlag >= 0 ? process.argv[restartFlag + 1] : null })
      .then(() => app.quit()).catch(async error => { console.error(error); await backend.stop(); app.exit(1); });
  }
}).catch(error => { console.error(error.message); app.quit(); });

async function setLogin(enabled) {
  if (process.platform === 'linux') {
    const config = process.env.XDG_CONFIG_HOME || path.join(app.getPath('home'), '.config');
    const destination = path.join(config, 'autostart', 'dictate.desktop');
    if (!enabled) { fs.rmSync(destination, { force: true }); return; }
    fs.mkdirSync(path.dirname(destination), { recursive: true });
    const executable = process.execPath.replace(/\\/g, '\\\\').replace(/"/g, '\\"').replace(/`/g, '\\`').replace(/\$/g, '\\$');
    fs.writeFileSync(destination, `[Desktop Entry]\nType=Application\nName=Dictate\nExec="${executable}"\nTerminal=false\n`);
  } else await app.setLoginItemSettings({ openAtLogin: enabled });
}
app.on('second-instance', () => { if (mainWindow) showWindow(); });
app.on('activate', () => { if (mainWindow) showWindow(); });
app.on('before-quit', event => {
  if (quitting) return;
  quitting = true; event.preventDefault(); indicator?.hide();
  backend?.removeAllListeners();
  Promise.resolve(backend?.stop()).finally(() => { tray?.destroy(); app.exit(0); });
});
