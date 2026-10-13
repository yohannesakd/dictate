#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/../.."
tools="${DICTATE_ANDROID_TOOLS:-$PWD/portable/.tools}"
export DOTNET_ROOT="$tools/dotnet"
export PATH="$DOTNET_ROOT:$PATH"
configuration="${1:-Release}"
runtime="${2:-android-arm64}"
./portable/scripts/build-android-speech.sh "$runtime"
dotnet publish portable/Android/Dictate.Android.csproj -c "$configuration" -r "$runtime" -p:RuntimeIdentifiers="$runtime" \
  -p:AndroidSpeechDirectory="$tools/android-speech/lib" \
  -p:AndroidSdkDirectory="$tools/android-sdk" \
  -p:JavaSdkDirectory="$tools/jdk" -p:AndroidPackageFormats=apk -p:EmbedAssembliesIntoApk=true -m:1 --nologo
mkdir -p portable/artifacts/android
cp "portable/Android/bin/$configuration/net10.0-android/$runtime/publish/dev.local.dictate.portable-Signed.apk" \
  "portable/artifacts/android/$runtime-$configuration.apk"
