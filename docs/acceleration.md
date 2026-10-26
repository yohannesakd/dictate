# Speech models and hardware modes

The Mac app uses Phonon-2, based on NVIDIA Parakeet TDT 0.6B v3.
Automatic prefers the Neural Engine. Its decoder runs on CPU.
If Neural Engine loading fails, Core ML can use GPU or CPU.
CPU only runs the same Phonon-2 model with Core ML CPU execution.
The Mac build includes the complete original model. It does not download a Whisper Neural Engine encoder.

Windows, Linux, and Android use quantized English Whisper `base.en-q5_1`.
Windows and Linux x64 include Vulkan GPU support and an isolated OpenVINO worker for supported Intel NPUs.
The worker checks actual available devices before downloading a checksum-verified 48 MB encoder archive.
An unavailable or failed NPU path falls back to Vulkan GPU, then CPU.
An NPU requires a compatible driver and model. Other NPU vendors use the available GPU or CPU path.
CPU only stops the NPU worker and disables GPU inference.
Older supported x64 CPUs use the no-AVX runtime.

Android packages include a Vulkan GPU backend and CPU execution in the same native runtime.
Automatic uses Vulkan when the device driver supports the required operations. CPU only disables GPU inference.
Android NPU runtimes are not included. An unsupported phone uses CPU.
GPU support depends on driver capabilities, rather than a fixed phone list.

Automatic and CPU only persist after restart. Each recording retains the mode selected when recording starts.
Settings shows the active processor after recognition or model preparation establishes it.
Speech recognition and transcript history stay on the device. Cleanup retains its existing local and cloud choices.

The [current verification record](benchmarks/platform-speech-2026-10-13/README.md) records completed checks and device limits.
The [earlier Whisper Neural Engine record](benchmarks/neural-engine-2026-10-12/README.md) describes the replaced Mac configuration.

[Phonon runtime documentation](https://github.com/fermionresearch/phonon-coreml) describes the original Core ML model and runtime.
[Whisper.net runtime loading](https://github.com/sandrohanea/whisper.net/blob/1.9.1/Whisper.net/LibraryLoader/NativeLibraryLoader.cs) explains the separate native runtimes.
[Whisper Vulkan documentation](https://github.com/ggml-org/whisper.cpp#vulkan-gpu-support) describes its cross-vendor GPU backend.
