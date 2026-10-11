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
NATIVE_SOURCES=(Sources/Recorder.swift Sources/Insertion.swift Sources/Keyboard.swift Sources/Operation.swift portable/Desktop/Mac/NativeBridge.swift)
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

# All desktops publish the same UI and core. Swift supplies only macOS adapters.
export PATH=/usr/local/share/dotnet:/opt/homebrew/bin:$PATH
ARCH=$(uname -m)
RID=osx-arm64
[[ "$ARCH" == x86_64 ]] && RID=osx-x64
mkdir -p "$APP/Contents/MacOS"
dotnet publish "$PROJECT" -c Release -r "$RID" --self-contained true \
  -p:UseAppHost=true -p:PublishSingleFile=true -p:DebugType=None -p:DebugSymbols=false -m:2 -o "$APP/Contents/MacOS"
mv "$APP/Contents/MacOS/$EXECUTABLE" "$APP/Contents/MacOS/Dictate"
xcrun swiftc -O -swift-version 5 -target "$TARGET" -emit-library \
  "${NATIVE_SOURCES[@]}" -o "$APP/Contents/MacOS/libdictate-macos.dylib"
cp Info.plist "$APP/Contents/Info.plist"

# This is the app's separate signing keychain. Restore the original search list after signing.
KEYCHAIN=$PWD/signing/dictate-signing.keychain-db
ORIGINAL_KEYCHAINS=("${(@f)$(security list-keychains -d user | sed -E 's/^ *"(.*)"$/\1/')}")
KEYCHAINS_CHANGED=1
security list-keychains -d user -s "${ORIGINAL_KEYCHAINS[@]}" "$KEYCHAIN"
security unlock-keychain -p "$(cat signing/keychain-password)" "$KEYCHAIN"
IDENTITY=$(security find-identity -p codesigning "$KEYCHAIN" | awk '/Dictate Local Signing/ { print $2; exit }')
[[ -n "$IDENTITY" ]]
# macOS requires signatures for every file in Contents/MacOS.
find "$APP/Contents/MacOS" -type f ! -name Dictate -print0 | while IFS= read -r -d '' library; do
  codesign --force --sign "$IDENTITY" "$library"
done
codesign --force --sign "$IDENTITY" --identifier dev.local.dictate "$APP"
codesign --verify --strict "$APP"

if [[ -e build/$APP_NAME.app ]]; then mv build/$APP_NAME.app "$PREVIOUS"; fi
mv "$APP" build/$APP_NAME.app
print "Built build/$APP_NAME.app"
