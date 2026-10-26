# Neural Engine preference verification

This change follows commit `cbe3922` and adds Neural Engine preference on Apple silicon Macs.
The English Whisper decoder model remains `base.en-q5_1`.
The additional encoder archive uses revision `5359861c739e955e79d9a303bcbc70fb988958b1` and SHA-256 `8cf860309e2449e2bdc8be834cf838ab2565747ecc8c0ef914ef5975115e192b`.
Its verified download size is 37,950,917 bytes.

## Completed checks

The Mac recognizer passed CPU, Automatic, CPU, and Automatic in one process, with three JFK reference passes per mode.
CPU-only native context flags disabled GPU use. No Core ML model loaded during those CPU-only passes, despite the installed encoder.
Automatic reported Neural Engine encoding with Metal decoding.
The bridge verified the loaded model's `CPUAndNeuralEngine` configuration.

Apple's compute plan assigned all 153 encoder layers to `MLNeuralEngineComputeDevice`.
This confirms model placement preference. It does not measure hardware power consumption.

A missing encoder produced the expected transcript through Metal.
After the encoder was restored, the same recognizer recovered Neural Engine preference.
The six checksum-verified audio fixtures completed five passes each without errors. Cancellation and recovery passed.

The signed packaged Mac app passed its settings, hardware controls, compact layout, microphone start, and cancellation checks.
The final signed Mac build passed its packaged interface check with exit code zero.
The update is installed at `/Applications/Dictate.app` and opened successfully.
Strict signature verification passed. Settings and transcript history remained unchanged.
The installed interface matches the source. The installed Core ML bridge matches the signed build.
All five encoder files match the checksum-verified archive.

`core.txt` records 27 passing core tests.
The first test-host connection timed out. The retry passed with `VSTEST_CONNECTION_TIMEOUT=180`.
Windows, Linux, and Android packages passed Dispatch job `03f7df31-f6a5-4507-a1ab-11efc2f177de`.
These package checks preceded the final optional-encoder HTTP timeout handling change.
Native Windows and Android checks remain unavailable while Saibot is offline.

## Evidence and reproduction

`mac-hardware.json` records hardware switching, native logs, recognition timings, and cancellation.
`mac-fallback.json` records missing-encoder fallback and restoration.
`mac-audio.json` records 30 audio runs. `mac-compute-plan.json` records the preferred device for all encoder layers.
`mac-ui.txt` records the final packaged interface check. `mac-install.json` records installation checks.
`checks-build.txt` records successful compilation after the optional-encoder timeout handling change.

Run the hardware checks on an Apple silicon Mac with a 16 kHz mono PCM16 reference:

```sh
dotnet run --project portable/Checks -c Release -- \
  --hardware /path/to/jfk.wav /path/to/models /path/to/hardware.json
dotnet run --project portable/Checks -c Release -- \
  --neural-fallback /path/to/jfk.wav /path/to/models /path/to/fallback.json
```

Use a separate model directory for the fallback check. The check temporarily moves the encoder folder and restores it.
The checks establish model preference, CPU-only isolation, and recovery. They do not establish general accuracy or physical-device battery use.
