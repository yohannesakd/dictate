const { sign } = require('@electron/osx-sign');
sign({ app: process.env.DICTATE_SIGN_APP, identity: process.env.DICTATE_SIGN_IDENTITY,
  keychain: process.env.DICTATE_SIGN_KEYCHAIN, type: 'development', identityValidation: false,
  optionsForFile: () => ({ hardenedRuntime: false, timestamp: 'none' }),
  ignore: file => file.includes('/app.asar.unpacked/resources/backend/'),
  preAutoEntitlements: false, preEmbedProvisioningProfile: false
}).catch(error => { console.error(error); process.exitCode = 1; });
