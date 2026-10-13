const path = require('node:path');
const fs = require('node:fs/promises');
const { packager } = require('@electron/packager');
const root = path.join(__dirname, '..');
const platform = process.argv[2] || process.platform;
const arch = process.argv[3] || process.arch;
packager({ dir: root, out: path.join(root, 'dist'), name: 'Dictate', executableName: 'Dictate',
  platform, arch, overwrite: true, appBundleId: 'dev.local.dictate', appCategoryType: 'public.app-category.productivity',
  icon: path.join(root, 'assets', platform === 'darwin' ? 'icon.icns' : platform === 'win32' ? 'icon.ico' : 'icon.png'),
  appCopyright: 'Yohannes', prune: true, asar: { unpackDir: 'resources/backend' },
  afterExtract: [async ({ buildPath, platform: target }) => {
    if (target !== 'darwin') return;
    // The installer copies only the app bundle. Retain the licenses inside it.
    const licenses = path.join(buildPath, 'Electron.app', 'Contents', 'Resources', 'Licenses');
    await fs.mkdir(licenses, { recursive: true });
    for (const name of ['LICENSE', 'LICENSES.chromium.html'])
      await fs.copyFile(path.join(buildPath, name), path.join(licenses, name));
  }],
  ignore: [/^\/dist($|\/)/, /^\/node_modules($|\/)/, /^\/test-results($|\/)/, /^\/tests($|\/)/, /^\/scripts($|\/)/],
  extendInfo: { NSMicrophoneUsageDescription: 'Dictate records your voice while dictation is active and transcribes it on this device.', LSMinimumSystemVersion: '15.0' }
}).then(paths => paths.forEach(value => console.log(value))).catch(error => { console.error(error); process.exitCode = 1; });
