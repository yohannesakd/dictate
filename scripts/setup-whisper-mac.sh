#!/bin/zsh
# Build the pinned CPU-only helper in ignored vendor storage. Keep downloads checksum verified.
set -euo pipefail
REPO=${0:A:h:h}
ROOT=${1:-$REPO}
TOOLS=$ROOT/vendor/whisper
mkdir -p "$TOOLS" "$ROOT/models/Whisper"
fetch() {
  local url=$1 destination=$2 expected=$3 partial
  if [[ -f "$destination" && "$(shasum -a 256 "$destination" | awk '{print $1}')" == "$expected" ]]; then return; fi
  partial=$(mktemp "$destination.XXXXXX")
  if ! curl -fsSL --connect-timeout 15 --max-time 600 "$url" -o "$partial" || \
     [[ "$(shasum -a 256 "$partial" | awk '{print $1}')" != "$expected" ]]; then
    rm -f "$partial"; print -u2 "Download verification failed: $destination"; return 1
  fi
  mv -f "$partial" "$destination"
}
fetch https://codeload.github.com/ggml-org/whisper.cpp/tar.gz/refs/tags/v1.9.5 "$TOOLS/source.tar.gz" \
  ff1a9053feb509ff9d7729703355541ae9690073a6b1c40eb692c962e0dc1720
fetch https://files.pythonhosted.org/packages/7f/09/516b0d709672bc430eb13278f0316acd34869269447744f5d136daeef689/cmake-3.31.6-py3-none-macosx_10_10_universal2.whl "$TOOLS/cmake.whl" \
  da9d4fd9abd571fd016ddb27da0428b10277010b23bb21e3678f8b9e96e1686e
if [[ ! -x "$TOOLS/cmake/cmake/data/bin/cmake" ]]; then
  /usr/bin/python3 -m zipfile -e "$TOOLS/cmake.whl" "$TOOLS/cmake"
  chmod +x "$TOOLS/cmake/cmake/data/bin/"*
fi
if [[ ! -d "$TOOLS/whisper.cpp-1.9.5" ]]; then tar -xzf "$TOOLS/source.tar.gz" -C "$TOOLS"; fi
export MACOSX_DEPLOYMENT_TARGET=15.0
"$TOOLS/cmake/cmake/data/bin/cmake" -S "$TOOLS/whisper.cpp-1.9.5" -B "$TOOLS/build" \
  -DCMAKE_BUILD_TYPE=Release -DBUILD_SHARED_LIBS=OFF -DWHISPER_BUILD_TESTS=OFF \
  -DWHISPER_BUILD_EXAMPLES=ON -DWHISPER_CURL=OFF -DGGML_METAL=OFF -DGGML_CUDA=OFF \
  -DGGML_NATIVE=OFF -DGGML_VULKAN=OFF -DGGML_BLAS=OFF -DGGML_OPENMP=OFF -DGGML_BACKEND_DL=OFF
"$TOOLS/cmake/cmake/data/bin/cmake" --build "$TOOLS/build" --target whisper-cli -j 4
cp "$TOOLS/build/bin/whisper-cli" "$TOOLS/whisper-cli"
cp "$TOOLS/whisper.cpp-1.9.5/LICENSE" "$TOOLS/LICENSE-whisper"
fetch https://huggingface.co/ggerganov/whisper.cpp/resolve/5359861c739e955e79d9a303bcbc70fb988958b1/ggml-base.bin \
  "$ROOT/models/Whisper/ggml-base.bin" 60ed5bc3dd14eea856493d334349b405782ddcaf0028d4b5df4088345fba2efe
