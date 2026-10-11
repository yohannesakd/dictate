#!/bin/zsh
# Build and sign a separate bundle before replacing the previous valid app.
set -euo pipefail
cd "$(dirname "$0")"
export PATH=/opt/homebrew/bin:$PATH
export MACOSX_DEPLOYMENT_TARGET=15.0
TARGET="$(uname -m)-apple-macosx15.0"
PHONON=vendor/phonon-coreml
PHONON_TDT=$PHONON/Binaries/PhononTDT.xcframework/macos-arm64_x86_64
mkdir -p build
STAGING=$(mktemp -d "$PWD/build/.dictate.XXXXXX")
APP=$STAGING/Dictate.app
PREVIOUS=$STAGING/previous.app
KEYCHAINS_CHANGED=0
ORIGINAL_KEYCHAINS=()
cleanup() {
  if (( KEYCHAINS_CHANGED )); then
    security list-keychains -d user -s "${ORIGINAL_KEYCHAINS[@]}"
  fi
  if [[ -d "$PREVIOUS" && ! -e build/Dictate.app ]]; then
    if ! mv "$PREVIOUS" build/Dictate.app; then
      print -u2 "Previous app retained at $PREVIOUS"
      return 1
    fi
  fi
  rm -rf "$STAGING"
}
trap cleanup EXIT

# Always rebuild this small target. Vendor, patch, headers, and compiler changes cannot reuse a stale archive.
# Apply the repository patch to a source copy. Keep the ignored vendor checkout unchanged.
cp -R "$PHONON/Sources/PhononCoreML" "$STAGING/PhononCoreML"
patch -s -d "$STAGING/PhononCoreML" -p1 < patches/phonon-frame-capacity.patch
mkdir -p "$STAGING/phonon"
xcrun swiftc -O -swift-version 6 -target "$TARGET" -parse-as-library -module-name PhononCoreML \
  -I "$PHONON_TDT/Headers" "$STAGING"/PhononCoreML/*.swift \
  -emit-module -emit-module-path "$STAGING/phonon/PhononCoreML.swiftmodule" \
  -emit-library -static -o "$STAGING/phonon/libPhononCoreML.a"

mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources/Phonon-2-CoreML"
mkdir -p "$APP/Contents/Helpers" "$APP/Contents/Resources/Whistle" "$APP/Contents/Resources/Whisper"
cp vendor/whistle/needle vendor/whisper/whisper-cli "$APP/Contents/Helpers/"
cp models/Whistle/whistle.cact vendor/whistle/LICENSE-needle "$APP/Contents/Resources/Whistle/"
cp models/Whisper/ggml-base.bin vendor/whisper/LICENSE-whisper "$APP/Contents/Resources/Whisper/"
cp Info.plist "$APP/Contents/Info.plist"
MODEL=models/Phonon-2-CoreML
cp -c -R "$MODEL/manifest.json" "$MODEL/decoder.bin" "$MODEL/Phonon-2.mlpackage" \
  "$MODEL/NOTICE" "$MODEL/LICENSE-WEIGHTS-CC-BY-4.0.txt" "$APP/Contents/Resources/Phonon-2-CoreML/"
xcrun swiftc -O -swift-version 5 -target "$TARGET" \
  -I "$STAGING/phonon" -I "$PHONON_TDT/Headers" \
  Sources/*.swift "$STAGING/phonon/libPhononCoreML.a" "$PHONON_TDT/libPhononTDT.a" \
  -framework Accelerate -framework CoreML -o "$APP/Contents/MacOS/Dictate"

# This is the app's separate signing keychain. Restore the original search list after signing.
KEYCHAIN=$PWD/signing/dictate-signing.keychain-db
ORIGINAL_KEYCHAINS=("${(@f)$(security list-keychains -d user | sed -E 's/^ *"(.*)"$/\1/')}")
KEYCHAINS_CHANGED=1
security list-keychains -d user -s "${ORIGINAL_KEYCHAINS[@]}" "$KEYCHAIN"
security unlock-keychain -p "$(cat signing/keychain-password)" "$KEYCHAIN"
IDENTITY=$(security find-identity -p codesigning "$KEYCHAIN" | awk '/Dictate Local Signing/ { print $2; exit }')
[[ -n "$IDENTITY" ]]
for helper in "$APP/Contents/Helpers/"*; do codesign --force --sign "$IDENTITY" "$helper"; done
codesign --force --sign "$IDENTITY" --identifier dev.local.dictate "$APP"
codesign --verify --strict "$APP"

rm -rf build/phonon
mv "$STAGING/phonon" build/phonon
if [[ -e build/Dictate.app ]]; then mv build/Dictate.app "$PREVIOUS"; fi
mv "$APP" build/Dictate.app
print 'Built build/Dictate.app'
