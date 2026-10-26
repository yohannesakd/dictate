# Third-party components

Dictate's source uses MIT. Dependencies and model weights retain their own licenses.
Keep the license files and notices when distributing a package.

| Component | License | Included notice |
|---|---|---|
| OpenAI Whisper weights | MIT | `portable/Speech/Licenses/Whisper-LICENSE.txt` |
| whisper.cpp | MIT | `portable/Speech/Licenses/whisper.cpp-LICENSE.txt` |
| Whisper.net 1.9.1 | MIT | `portable/Speech/Licenses/Whisper.net-LICENSE.txt` |
| NAudio 2.2.1 | MIT | `portable/Speech/Licenses/NAudio-LICENSE.txt` |
| Phonon Core ML runtime 1.1.2 | Apache-2.0 | `portable/Speech/Licenses/Apache-2.0.txt` and `Phonon-NOTICE.txt` |
| Phonon-2 Core ML weights | CC-BY-4.0 | Model `NOTICE` and `LICENSE-WEIGHTS-CC-BY-4.0.txt` in the Mac package |
| OpenVINO 2024.4 | Apache-2.0 | `Licenses/OpenVINO-LICENSE.txt` in the NPU worker |
| Vulkan and SPIR-V headers | Apache-2.0 and MIT | Header license files in Android assets |
| Android libc++ runtime | Apache-2.0 with LLVM exceptions and dependency notices | `Licenses/Android-libc++-NOTICE.txt` in Android assets |
| Electron | MIT and bundled dependency licenses | Electron distribution `LICENSE` and `LICENSES.chromium.html`; inside `Contents/Resources/Licenses` on Mac |
| .NET runtime | MIT and dependency notices | `portable/Speech/Licenses/DotNet-LICENSE.txt` and `DotNet-ThirdPartyNotices.txt` |
| Inter | SIL Open Font License 1.1 | `desktop/ui/fonts/LICENSE` |

Phonon-2 is by Fermion Research, based on NVIDIA Parakeet TDT 0.6B v3.
The Mac build uses runtime release `1.1.2` with the checked-in frame-capacity patch.
It bundles the original model attribution files.
The model license applies separately from Dictate's MIT license.

Sources: [Whisper](https://github.com/openai/whisper/blob/main/LICENSE),
[whisper.cpp](https://github.com/ggml-org/whisper.cpp/blob/f24588a272ae8e23280d9c220536437164e6ed28/LICENSE),
[Whisper.net](https://github.com/sandrohanea/whisper.net/blob/1.9.1/LICENSE),
[NAudio](https://github.com/naudio/NAudio/blob/v2.2.1/license.txt),
[Phonon runtime](https://github.com/fermionresearch/phonon-coreml/tree/1.1.2),
[Phonon weights](https://huggingface.co/FermionResearch/Phonon-2-CoreML),
[OpenVINO](https://github.com/openvinotoolkit/openvino),
[Electron](https://www.electronjs.org/docs/latest/tutorial/about),
[libc++](https://github.com/llvm/llvm-project/blob/llvmorg-18.1.8/libcxx/LICENSE.TXT), and
[.NET runtime](https://github.com/dotnet/runtime/blob/main/THIRD-PARTY-NOTICES.TXT).

NuGet lock files and `desktop/package-lock.json` record dependency versions.
Android native source revisions and archive checksums are pinned in `portable/scripts/build-android-speech.sh`.
This inventory is a release aid. Recheck notices when changing dependencies or distributing a new package.

## Speech fixtures

The [fixture manifests](portable/fixtures/speech/samples.json) identify the audio sources and hashes.
Four recordings use Apple's Samantha or Anna system voices. Redistribution permission has not been established.
These recordings are not covered by Dictate's MIT license.
See the [sharing review](docs/sharing-review.md) before public publication.
