# Build Dictate for other platforms

The desktop apps use the same Electron window, indicator, menus, and shared core.
Android uses the same interface through a local WebView, with a native activity and voice keyboard.

## Run Windows or Linux

Install the .NET 10 SDK and Node.js 22.12.0 or later.
On Linux, install `alsa-utils`, `libsecret-tools`, `at-spi2-core`, and `libgomp1`.
Use X11 for global shortcuts.
On Wayland, use the app recording controls.
Linux insertion requires an accessible, editable field. Other fields require copying from History.

```sh
desktop/scripts/build-backend.sh linux-x64
cd desktop
npm ci
npm start
```

In Settings, select **Download and load model**.
Whisper recognizes English on this device. Choose Automatic or CPU only in Settings.
Mac uses the original Phonon-2 model on the Neural Engine. CPU-only disables its acceleration.
Windows and Linux x64 can use supported Intel NPUs through OpenVINO, with a separate encoder download.
Windows and Linux x64 use Vulkan when supported.
Android uses Vulkan when its GPU and driver support the required operations.
If no supported accelerator is available, Whisper uses CPU.
See [hardware modes](../docs/acceleration.md) for limits and verification.
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
npm run package -- linux x64
```

The package is in `desktop/dist/Dictate-linux-x64`.
For Windows, build the backend with `win-x64` and package with `win32 x64`.
From PowerShell, publish into `desktop/resources/backend` before packaging:

```powershell
dotnet publish portable/Desktop -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -p:DebugType=None -p:DebugSymbols=false `
  -o desktop/resources/backend
dotnet publish portable/NpuWorker -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -p:DebugType=None -p:DebugSymbols=false `
  -o desktop/resources/backend/npu-worker
cd desktop
npm ci
npm run package -- win32 x64
```

The package includes Electron and the native service.
If Linux restricts user namespaces, install the bundled Chromium sandbox with root ownership and mode `4755`.
The packaged app does not require Node.js or the .NET SDK.
The publisher retains only the target speech libraries and removes release debug symbols.
The x64 Whisper runtime includes a no-AVX fallback.

## Build the Android keyboard

Install the .NET 10 Android workload, Android SDK, and required Java SDK.
Run the setup script on Linux. It installs isolated build tools and accepts Android SDK licences.
On other hosts, install matching tools manually and set `DICTATE_ANDROID_TOOLS` to their parent directory.
That directory must contain `dotnet/`, `jdk/`, and `android-sdk/`.
The build script compiles the pinned whisper.cpp runtime with Vulkan and CPU support.
It installs Android NDK 27.2 and CMake 3.22.1 when required.

```sh
portable/scripts/setup-android.sh
portable/scripts/build-android.sh Release android-arm64
```

For an x64 emulator, use `portable/scripts/build-android.sh Debug android-x64`.
Install the APK from `portable/artifacts/android`.
Open Dictate and allow microphone access.
Enable the voice keyboard and download the English speech model.
Choose Dictate from Android’s keyboard picker while a text field has focus.
Tap **Record**, speak, then tap **Stop**.
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
See [verification instructions](docs/verification.md) and [current hardware modes](../docs/acceleration.md).
