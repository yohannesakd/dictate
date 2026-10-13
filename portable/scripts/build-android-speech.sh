#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/../.."
tools="${DICTATE_ANDROID_TOOLS:-$PWD/portable/.tools}"
runtime="${1:-android-arm64}"
case "$runtime" in
  android-arm64) abi=arm64-v8a; triple=aarch64-linux-android ;;
  android-x64) abi=x86_64; triple=x86_64-linux-android ;;
  *) echo "Select android-arm64 or android-x64." >&2; exit 1 ;;
esac
ndk="$tools/android-sdk/ndk/27.2.12479018"
cmake="$tools/android-sdk/cmake/3.22.1/bin/cmake"
if [[ ! -x "$cmake" ]]; then
  JAVA_HOME="$tools/jdk" "$tools/android-sdk/cmdline-tools/latest/bin/sdkmanager" --sdk_root="$tools/android-sdk" "cmake;3.22.1"
fi
export PATH="$(dirname "$cmake"):$PATH"
if [[ ! -d "$ndk" ]]; then
  JAVA_HOME="$tools/jdk" "$tools/android-sdk/cmdline-tools/latest/bin/sdkmanager" --sdk_root="$tools/android-sdk" "ndk;27.2.12479018"
fi
root="$tools/android-speech"
mkdir -p "$root"
fetch() {
  local name=$1 url=$2 expected=$3
  if [[ ! -f "$root/$name.tar.gz" ]]; then
    curl --fail --location --silent --show-error --max-time 600 "$url" -o "$root/$name.tar.gz"
  fi
  echo "$expected  $root/$name.tar.gz" | sha256sum --check
  if [[ ! -d "$root/$name" ]]; then
    mkdir "$root/$name"
    tar -xzf "$root/$name.tar.gz" --strip-components=1 -C "$root/$name"
  fi
}
fetch whisper https://codeload.github.com/ggml-org/whisper.cpp/tar.gz/f24588a272ae8e23280d9c220536437164e6ed28 c3488eba0c6666a8b256f53267c129aa46292b35b0cb7391e4b5623cf9e23c8d
fetch vulkan https://codeload.github.com/KhronosGroup/Vulkan-Headers/tar.gz/refs/tags/v1.4.313 f3298b8dc620530493296759858a69b622f98ececa0e8c75488ad2000778148f
fetch spirv https://codeload.github.com/KhronosGroup/SPIRV-Headers/tar.gz/refs/tags/vulkan-sdk-1.4.313.0 f68be549d74afb61600a1e3a7d1da1e6b7437758c8e77d664909f88f302c5ac1
mkdir -p "$root/licenses"
cp "$root/whisper/LICENSE" "$root/licenses/whisper.cpp-LICENSE.txt"
cp "$root/vulkan/LICENSE.md" "$root/licenses/Vulkan-Headers-NOTICE.md"
cp "$root/vulkan/LICENSES/MIT.txt" "$root/licenses/Vulkan-Headers-MIT.txt"
cp "$root/spirv/LICENSE" "$root/licenses/SPIRV-Headers-LICENSE.txt"
prefix="$root/headers"
"$cmake" -S "$root/vulkan" -B "$root/vulkan-build" -DCMAKE_INSTALL_PREFIX="$prefix"
"$cmake" --install "$root/vulkan-build"
"$cmake" -S "$root/spirv" -B "$root/spirv-build" -DCMAKE_INSTALL_PREFIX="$prefix"
"$cmake" --install "$root/spirv-build"
export CMAKE_PREFIX_PATH="$prefix"
host=linux-x86_64
[[ "$(uname -s)" != Darwin ]] || host=darwin-x86_64
glslc="$ndk/shader-tools/$host/glslc"
build="$root/build-vulkan-$abi"
# Resolve Vulkan 1.1 functions through the existing dispatcher so Android 8 can still load the CPU path.
source="$root/whisper-patched"
mkdir -p "$source"
cp -p -R "$root/whisper/." "$source/"
patch -s -d "$source" -p1 < patches/android-vulkan-api26.patch
"$cmake" -S "$source" -B "$build" -DCMAKE_BUILD_TYPE=Release \
  -DCMAKE_TOOLCHAIN_FILE="$ndk/build/cmake/android.toolchain.cmake" -DANDROID_ABI="$abi" -DANDROID_PLATFORM=android-26 \
  -DANDROID_STL=c++_shared -DBUILD_SHARED_LIBS=ON -DGGML_NATIVE=OFF -DGGML_OPENMP=OFF -DGGML_VULKAN=ON \
  -DGGML_AVX=OFF -DGGML_AVX2=OFF -DGGML_FMA=OFF -DGGML_F16C=OFF -DGGML_SSE42=OFF -DGGML_BMI2=OFF \
  -DWHISPER_BUILD_TESTS=OFF -DWHISPER_BUILD_EXAMPLES=OFF -DWHISPER_BUILD_SERVER=OFF \
  -DVulkan_INCLUDE_DIR="$prefix/include" -DVulkan_LIBRARY="$ndk/toolchains/llvm/prebuilt/$host/sysroot/usr/lib/$triple/26/libvulkan.so" \
  -DVulkan_GLSLC_EXECUTABLE="$glslc" -DSPIRV-Headers_DIR="$prefix/share/cmake/SPIRV-Headers" \
  -DCMAKE_SHARED_LINKER_FLAGS="-Wl,-z,max-page-size=16384"
"$cmake" --build "$build" --parallel 2
destination="$root/lib/$abi"
mkdir -p "$destination"
find "$build" -name '*.so' -type f -exec cp '{}' "$destination/" \;
cp "$ndk/toolchains/llvm/prebuilt/$host/sysroot/usr/lib/$triple/libc++_shared.so" "$destination/"
