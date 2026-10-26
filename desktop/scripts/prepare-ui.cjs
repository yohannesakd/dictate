const fs = require('node:fs');
const path = require('node:path');
const root = path.join(__dirname, '..');
fs.mkdirSync(path.join(root, 'ui', 'fonts'), { recursive: true });
for (const weight of [400, 500, 600]) fs.copyFileSync(path.join(root, 'node_modules', '@fontsource', 'inter', 'files', `inter-latin-${weight}-normal.woff2`), path.join(root, 'ui', 'fonts', `inter-${weight}.woff2`));
fs.copyFileSync(path.join(root, 'node_modules', '@fontsource', 'inter', 'LICENSE'), path.join(root, 'ui', 'fonts', 'LICENSE'));
