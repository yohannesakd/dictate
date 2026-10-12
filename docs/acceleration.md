# Speech hardware modes

Dictate defaults to Automatic speech recognition. It uses supported acceleration and falls back to CPU when acceleration is unavailable.
CPU only disables Neural Engine and GPU execution. The choice persists after restart and applies to the next recording or model preparation.
Each recording retains the mode selected when recording starts.

Apple silicon Macs prefer the Neural Engine for the encoder and use Metal GPU acceleration for decoding.
Automatic uses Metal when the Neural Engine is unavailable, then CPU if no accelerator can run.
The encoder requires an additional 38 MB download.
Windows and Linux x64 packages include Vulkan and CPU libraries. Vulkan requires a compatible graphics driver.
Older supported x64 CPUs use the no-AVX runtime when the accelerator library's CPU requirements are unavailable.
Intel Macs and other desktop architectures use CPU when their packaged runtime cannot initialize a GPU.
Android currently uses CPU. Whisper.net 1.9.1 does not package an Android accelerator backend.
Neural processors outside Apple silicon are not enabled by these packages.

Settings shows the active backend after recognition. Model preparation loads weights, but the runtime initializes the GPU when recognition starts.
Before detection, Settings gives an explicit instruction to dictate. An unavailable accelerator produces a CPU status and an explanation.
Changing modes reloads the model before recognition. CPU only remains CPU-only after an accelerated recording.
Missing accelerator libraries fall back through Whisper.net's loader. GPU model-loading failures retry with GPU use disabled.
A missing or unusable Neural Engine encoder falls back to Metal.
If a Neural Engine prediction fails, Dictate discards that result and retries once with Core ML disabled.
Speech recognition and transcript history stay on this device. The English model does not change with hardware mode.
Cleanup continues to use local rules or the selected cloud provider.

The [Neural Engine evidence](benchmarks/neural-engine-2026-10-12/README.md) records preference, CPU-only isolation, and Metal fallback.
The [earlier GPU evidence](benchmarks/acceleration-2026-10-12/README.md) records other hardware modes and unavailable devices.
The [Whisper-only record](whisper-only.md) describes the previous CPU-only release and Whistle removal.

Runtime behavior is based on the pinned [Whisper.net runtime loader](https://github.com/sandrohanea/whisper.net/blob/1.9.1/Whisper.net/LibraryLoader/NativeLibraryLoader.cs).
The [factory options](https://github.com/sandrohanea/whisper.net/blob/1.9.1/Whisper.net/WhisperFactoryOptions.cs) control GPU use for each model context.
