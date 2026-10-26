#!/bin/zsh
# Build and sign a separate bundle before replacing the previous valid app.
set -euo pipefail
cd "$(dirname "$0")"
export PATH=/opt/homebrew/bin:$PATH
export MACOSX_DEPLOYMENT_TARGET=15.0
TARGET="$(uname -m)-apple-macosx15.0"
APP_NAME=Dictate
PROJECT=portable/Desktop/Dictate.Desktop.csproj
EXECUTABLE=Dictate.Desktop
NATIVE_SOURCES=(Sources/Recorder.swift Sources/Insertion.swift Sources/Keyboard.swift Sources/Operation.swift portable/Desktop/Mac/NativeBridge.swift portable/Desktop/Mac/PhononSpeech.swift)
if [[ "${1:-}" == --checks ]]; then
  APP_NAME=Dictate.Checks
  PROJECT=portable/NativeChecks/Dictate.NativeChecks.csproj
  EXECUTABLE=Dictate.NativeChecks
  NATIVE_SOURCES+=(portable/NativeChecks/MacFixture.swift)
fi
mkdir -p build
STAGING=$(mktemp -d "$PWD/build/.dictate.XXXXXX")
APP=$STAGING/$APP_NAME.app
PREVIOUS=$STAGING/previous.app
KEYCHAINS_CHANGED=0
ORIGINAL_KEYCHAINS=()
cleanup() {
  if (( KEYCHAINS_CHANGED )); then
    security list-keychains -d user -s "${ORIGINAL_KEYCHAINS[@]}"
  fi
  if [[ -d "$PREVIOUS" && ! -e build/$APP_NAME.app ]]; then
    if ! mv "$PREVIOUS" build/$APP_NAME.app; then
      print -u2 "Previous app retained at $PREVIOUS"
      return 1
    fi
  fi
  rm -rf "$STAGING"
}
trap cleanup EXIT

PHONON=vendor/phonon-coreml
PHONON_TDT=$PHONON/Binaries/PhononTDT.xcframework/macos-arm64_x86_64
MODEL=models/Phonon-2-CoreML
for input in "$PHONON_TDT/libPhononTDT.a" "$MODEL/manifest.json" "$MODEL/decoder.bin" \
  "$MODEL/Phonon-2.mlpackage/Data/com.apple.CoreML/weights/weight.bin" "$MODEL/NOTICE" "$MODEL/LICENSE-WEIGHTS-CC-BY-4.0.txt"; do
  [[ -s "$input" ]] || { print -u2 "Missing Mac build input: $input"; exit 1; }
done
for weights in "$MODEL/decoder.bin" "$MODEL/Phonon-2.mlpackage/Data/com.apple.CoreML/weights/weight.bin"; do
  (( $(stat -f %z "$weights") > 1024 )) || { print -u2 "Download model weights instead of Git LFS pointers: $weights"; exit 1; }
done
cp -R "$PHONON/Sources/PhononCoreML" "$STAGING/PhononCoreML"
patch -s -d "$STAGING/PhononCoreML" -p1 < patches/phonon-frame-capacity.patch
mkdir -p "$STAGING/phonon"
xcrun swiftc -O -swift-version 6 -target "$TARGET" -parse-as-library -module-name PhononCoreML \
  -I "$PHONON_TDT/Headers" "$STAGING"/PhononCoreML/*.swift \
  -emit-module -emit-module-path "$STAGING/phonon/PhononCoreML.swiftmodule" \
  -emit-library -static -o "$STAGING/phonon/libPhononCoreML.a"
PHONON_FLAGS=(-I "$STAGING/phonon" -I "$PHONON_TDT/Headers" "$STAGING/phonon/libPhononCoreML.a" "$PHONON_TDT/libPhononTDT.a" -framework Accelerate -framework CoreML)

# Electron owns the window. Swift supplies only macOS adapters.
export PATH=/usr/local/share/dotnet:/opt/homebrew/bin:$PATH
ARCH=$(uname -m)
RID=osx-arm64
[[ "$ARCH" == x86_64 ]] && RID=osx-x64
if [[ "${1:-}" == --checks ]]; then
  mkdir -p "$APP/Contents/MacOS"
  dotnet publish "$PROJECT" -c Release -r "$RID" --self-contained true \
    -p:UseAppHost=true -p:PublishSingleFile=true -p:DebugType=None -p:DebugSymbols=false -m:2 -o "$APP/Contents/MacOS"
  mv "$APP/Contents/MacOS/$EXECUTABLE" "$APP/Contents/MacOS/Dictate"
  xcrun swiftc -O -warnings-as-errors -swift-version 5 -target "$TARGET" -emit-library \
    "${NATIVE_SOURCES[@]}" "${PHONON_FLAGS[@]}" -o "$APP/Contents/MacOS/libdictate-macos.dylib"
  MODEL_OUTPUT="$APP/Contents/Resources/Phonon-2-CoreML"
  cp Info.plist "$APP/Contents/Info.plist"
else
  ./desktop/scripts/build-backend.sh "$RID"
  xcrun swiftc -O -warnings-as-errors -swift-version 5 -target "$TARGET" -emit-library \
    "${NATIVE_SOURCES[@]}" "${PHONON_FLAGS[@]}" -o desktop/resources/backend/libdictate-macos.dylib
  MODEL_OUTPUT=desktop/resources/backend/Phonon-2-CoreML
  mkdir -p "$MODEL_OUTPUT"
  cp -c -R "$MODEL/manifest.json" "$MODEL/decoder.bin" "$MODEL/Phonon-2.mlpackage" \
    "$MODEL/NOTICE" "$MODEL/LICENSE-WEIGHTS-CC-BY-4.0.txt" "$MODEL_OUTPUT/"
  (cd desktop && npm ci && npm run package -- darwin "$([[ "$ARCH" == arm64 ]] && print arm64 || print x64)")
  ELECTRON_ARCH=$ARCH
  [[ "$ARCH" == arm64 ]] || ELECTRON_ARCH=x64
  ditto "desktop/dist/Dictate-darwin-$ELECTRON_ARCH/Dictate.app" "$APP"
fi
if [[ "${1:-}" == --checks ]]; then
  mkdir -p "$MODEL_OUTPUT"
  cp -c -R "$MODEL/manifest.json" "$MODEL/decoder.bin" "$MODEL/Phonon-2.mlpackage" \
    "$MODEL/NOTICE" "$MODEL/LICENSE-WEIGHTS-CC-BY-4.0.txt" "$MODEL_OUTPUT/"
fi

# This is the app's separate signing keychain. Restore the original search list after signing.
KEYCHAIN=$PWD/signing/dictate-signing.keychain-db
ORIGINAL_KEYCHAINS=("${(@f)$(security list-keychains -d user | sed -E 's/^ *"(.*)"$/\1/')}")
KEYCHAINS_CHANGED=1
security list-keychains -d user -s "${ORIGINAL_KEYCHAINS[@]}" "$KEYCHAIN"
security unlock-keychain -p "$(cat signing/keychain-password)" "$KEYCHAIN"
IDENTITY=$(security find-identity -p codesigning "$KEYCHAIN" | awk '/Dictate Local Signing/ { print $2; exit }')
[[ -n "$IDENTITY" ]]
if [[ "${1:-}" == --checks ]]; then
  find "$APP/Contents/MacOS" -type f ! -name Dictate -print0 | while IFS= read -r -d '' library; do
    codesign --force --sign "$IDENTITY" "$library"
  done
  codesign --force --sign "$IDENTITY" --identifier dev.local.dictate "$APP"
else
  # Sign the service before signing Electron's nested frameworks and outer bundle.
  find "$APP/Contents/Resources/app.asar.unpacked/resources/backend" -type f -print0 | while IFS= read -r -d '' library; do
    codesign --force --sign "$IDENTITY" "$library"
  done
  codesign --force --sign "$IDENTITY" --identifier dev.local.dictate \
    "$APP/Contents/Resources/app.asar.unpacked/resources/backend/Dictate.Desktop"
  DICTATE_SIGN_APP="$APP" DICTATE_SIGN_IDENTITY="$IDENTITY" DICTATE_SIGN_KEYCHAIN="$KEYCHAIN" \
    node desktop/scripts/sign-mac.cjs
fi
codesign --verify --strict "$APP"

if [[ -e build/$APP_NAME.app ]]; then mv build/$APP_NAME.app "$PREVIOUS"; fi
mv "$APP" build/$APP_NAME.app
if [[ "${1:-}" == --checks ]]; then
  FIXTURE=$STAGING/Dictate.Fixture.app
  ditto build/Dictate.Checks.app "$FIXTURE"
  /usr/libexec/PlistBuddy -c 'Set :CFBundleIdentifier dev.local.dictate.fixture' "$FIXTURE/Contents/Info.plist"
  /usr/libexec/PlistBuddy -c 'Set :CFBundleName Dictate Fixture' "$FIXTURE/Contents/Info.plist"
  codesign --force --sign "$IDENTITY" --identifier dev.local.dictate.fixture "$FIXTURE"
  codesign --verify --strict "$FIXTURE"
  if [[ -e build/Dictate.Fixture.app ]]; then mv build/Dictate.Fixture.app "$STAGING/previous-fixture.app"; fi
  mv "$FIXTURE" build/Dictate.Fixture.app
fi
print "Built build/$APP_NAME.app"
