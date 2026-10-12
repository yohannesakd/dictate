# Acceleration verification evidence

These records cover Automatic and CPU-only speech recognition after commit `a461213`.
The app uses the same English model and unchanged cleanup behavior.

`mac-hardware.json` records CPU, Automatic, CPU, and Automatic in one process.
Each mode transcribed the same JFK reference three times and returned the expected transcript.
Native context logs confirm `use gpu = 0` for CPU-only and Metal backend initialization for Automatic.
The recognizer's reported hardware matches those logs. Cancellation and recovery passed.

`mac-ui.txt` records the signed packaged app's controls, native mode changes, compact layout, and existing interface checks.
The CPU-only control displayed CPU. Automatic deferred its status until recognition could establish the active backend.
`mac-install.json` confirms strict signature verification, unchanged settings and history, and the installed interface hash.
The interface continues to use software rendering. Speech GPU acceleration is independent of interface rendering.

`core.txt` records 27 passing core tests, including mode persistence and recording-time settings capture.
`linux-hardware.json` records the same twelve recognition passes and four mode changes on the CPU-only VPS.
Automatic used CPU when Vulkan found no usable GPU. Native context flags remained disabled in CPU-only mode.
`linux-noavx.txt` records successful recognition, cancellation, and recovery with managed hardware intrinsics disabled.
`transport.txt` records the Electron transport tests.
`linux-ui.txt` records native GTK delivery, the packaged interface, and restart checks on the final UI revision.
Linux and Windows packages and the Android ARM64 Release APK built successfully in Dispatch job `38f4e2e3-1ba5-426f-90ee-e4c8450d40c2`.
`package-contents.json` confirms target-only desktop CPU, no-AVX, and Vulkan libraries.
The Android APK contains the exact shared `app.js` file and no Whistle or Needle libraries.
Windows GPU and Android runtime checks remain unavailable because Saibot is offline.
Android acceleration is unavailable in this runtime. The Android build check does not establish physical-device speed or battery use.
The Mac check establishes Metal execution. It does not establish Neural Engine execution or general recognition accuracy.

The hardware check can be repeated with a 16 kHz mono PCM16 reference:

```sh
dotnet run --project portable/Checks -c Release -- \
  --hardware /path/to/jfk.wav /path/to/models /path/to/report.json
```

The check switches modes within one recognizer, rejects GPU activity in CPU-only mode, and compares native logs with reported hardware.

The final check programs compiled successfully in Dispatch job `a7195adf-631c-4684-a82b-c8db152b7bc5`.
An earlier Android build selected an SDK without its workload. The final build used the workspace's isolated SDK.
The default build lockfiles passed locked restoration in job `10529a90-ee8b-4be2-8fd0-50e2160aab04`.
