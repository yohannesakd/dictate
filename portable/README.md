# Build Dictate for other platforms

The desktop apps use the same Electron window, indicator, menus, and shared core.
Android uses a native activity and voice keyboard with the same English speech choices.

## Run Windows or Linux

Install the .NET 10 SDK and Node.js 22 or later.
On Linux, install `alsa-utils`, `libsecret-tools`, `at-spi2-core`, and `libgomp1`.
Use X11 for global shortcuts.
On Wayland, use the app recording controls.
Linux insertion requires an accessible, editable field. Other fields require copying from History.

```sh
desktop/scripts/build-backend.sh linux-x64
cd desktop
npm ci
npm run prepare-ui
npm start
```

Choose **Whistle** or **Whisper** in the header menu or tray menu.
In Settings, select **Download and load model**.
Both engines recognize English on the CPU.
The app fixes Whisper to `base.en-q5_1` and does not expose other model sizes.

Hold **F8** to speak. Release **F8** to insert text.
Press **F8** twice or press **F8+Space** for hands-free dictation.
Press **Esc** to cancel.
Press **Ctrl+Shift+V** to repeat the last transcript.
Select a word and press **F8+D** to add a dictionary entry.
Close the window to keep Dictate in the tray.
Use the tray menu to quit.

## Publish a desktop package

These commands create independent distributions that include the .NET runtime.

```sh
desktop/scripts/build-backend.sh linux-x64
cd desktop
npm ci
npm run prepare-ui
npm run package -- linux x64
```

The package is in `desktop/dist/Dictate-linux-x64`.
For Windows, build the backend with `win-x64` and package with `win32 x64`.
From PowerShell, publish into `desktop/resources/backend` before packaging:

```powershell
dotnet publish portable/Desktop -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -p:DebugType=None -p:DebugSymbols=false `
  -o desktop/resources/backend
cd desktop
npm ci
npm run prepare-ui
npm run package -- win32 x64
```

The package includes Electron and the native service.
If Linux restricts user namespaces, install the bundled Chromium sandbox with root ownership and mode `4755`.
The packaged app does not require Node.js or the .NET SDK.
The publisher retains only the target speech libraries and removes release debug symbols.
The x64 Whisper runtime includes a no-AVX fallback.
The selected Whistle binaries support Windows x64, Linux x64, and macOS ARM64.
Intel Mac Whistle and other desktop architectures need additional verified native binaries.

## Build the Android keyboard

Install the .NET 10 Android workload, Android SDK, and required Java SDK.
The setup script installs isolated build tools and accepts Android SDK licences.

```sh
portable/scripts/setup-android.sh
portable/scripts/build-android.sh Release android-arm64
```

For an x64 emulator, use `portable/scripts/build-android.sh Debug android-x64`.
Whistle requires ARM64. Android x64 supports Whisper only.
Install the APK from `portable/artifacts/android`.
Open Dictate and allow microphone access.
Enable the voice keyboard and download the selected speech model.
Choose Dictate from Android’s keyboard picker while a text field has focus.
Tap **Start dictation**, speak, then tap **Stop and insert**.
Tap **Keyboard** to return to another keyboard.

The keyboard stops capture when it closes.
Changing fields cancels pending work. Password fields disable dictation.
Android targets version 8 or later.
Generated packages use a debug signing key until release signing is configured.
Physical Android microphone, latency, and battery checks remain open.

## Verify the app

```sh
dotnet test portable/Tests/Dictate.Tests.csproj
```

The frozen cleanup fixtures preserve the original Mac guard cases.
See [verification instructions](docs/verification.md) and the [current results](../docs/electron-desktop-verification.md).
