# Platform speech verification

Mac restores Phonon-2 on the Neural Engine. Other platforms retain English Whisper with CPU fallback.

`mac-audio.json` records six checksum-verified fixtures with five passes each. All 30 runs passed.
`mac-hardware.json` records Neural Engine, CPU-only, and Neural Engine recognition in one process.
`mac-speech.txt` records cancellation recovery and hardware switching.
`mac-ui.txt` records signed Mac model preparation, hardware controls, microphone cancellation, and compact layout checks.
The corrected app is installed at `/Applications/Dictate.app`. Settings and history hashes remained unchanged.
The native AppKit delivery fixture could not obtain focus. Text delivery code is unchanged.

`core.txt` records 29 passing tests, including platform normalization and recorded engine identity.
`openvino-cpu.json` records real encoder inference, reference recognition, worker termination, and restart through OpenVINO on CPU.
The OpenVINO worker compiled for Windows and Linux x64.
Physical Intel NPU execution remains unverified because no suitable device is available.
`desktop-builds.txt` records Windows and Linux packages with their separate OpenVINO workers.
`linux-ui.txt` records packaged interface, model preparation, hardware controls, and restart checks.
The same Linux run passed native GTK insertion, Unicode readback, undo, focus changes, and refusal after editing.
`linux-hardware.json` records CPU-only and Automatic recognition, CPU fallback, and cancellation recovery.
That Linux environment had no usable GPU. It does not establish GPU execution.
`windows-hardware.json` records CPU-only, Vulkan GPU, CPU-only, and Vulkan GPU recognition in one Windows process.
Native logs confirm GPU use only in Automatic mode. Reference recognition and cancellation recovery passed.
`windows-environment.json` records the host's NVIDIA GeForce RTX 5070 Ti, AMD Radeon graphics, and driver versions.
The reported inference device is `Vulkan0`. The check does not identify which physical adapter that index represents.
`android-builds.txt` records arm64 Release and x64 Debug packages with Vulkan and CPU libraries.
`android-package.json` records the phone APK size and native libraries.
`android-device.txt` and `android-hardware.json` record Android 16 emulator checks.
The checks passed shared UI, hardware controls, model preparation, reference recognition, microphone capture, insertion, cancellation, and recovery.
Automatic used CPU because this emulator had no usable Vulkan accelerator.
Physical Android GPU, NPU, microphone quality, latency, and battery behavior remain unverified.
The downloaded phone APK is `portable/artifacts/android/Dictate-android-arm64.apk`. It uses a development signing key.
Its SHA-256 is `4b145b84a21890e907f701d6b4a90bbd771b295f1f2166586095b33fbc2c5c8f`.
These checks do not establish a universal performance winner.

The Mac uses the original Phonon runtime release `1.1.2` and the frame-capacity patch from commit `108f418`.
The portable decoder remains `base.en-q5_1`.
The OpenVINO encoder archive has SHA-256 `4f1728cc251f14736d85f751260d6249bc446bedd7a2d044bc3a1d777af7072f`.
Its pinned revision is `6c641e5ffec145595714a49d532297bb8e368c28`. Its size is 48,405,638 bytes.

Run the Mac audio check with the signed verification bundle:

```sh
./build.sh --checks
open -n build/Dictate.Checks.app --args --speech /path/to/fixtures /path/to/output
```

Run the portable encoder check against a published worker:

```sh
python3 portable/scripts/check-npu-worker.py /path/to/Dictate.NpuWorker /path/to/models /path/to/jfk.wav /path/to/report.json CPU
```

Use `NPU` in place of `CPU` on an Intel machine with a compatible NPU driver.
