#!/bin/zsh
# Builds Dictate.app: the Phonon-2 Core ML runtime, the app, and the model inside the bundle.
set -euo pipefail
cd "$(dirname "$0")"
export PATH=/opt/homebrew/bin:$PATH

# Phonon-2 runtime (Core ML), built as its own module.
PHONON=vendor/phonon-coreml
PHONON_TDT=$PHONON/Binaries/PhononTDT.xcframework/macos-arm64_x86_64
mkdir -p build/phonon
if [[ ! -f build/phonon/libPhononCoreML.a ]]; then
  xcrun swiftc -O -swift-version 6 -parse-as-library -module-name PhononCoreML \
    -I $PHONON_TDT/Headers $PHONON/Sources/PhononCoreML/*.swift \
    -emit-module -emit-module-path build/phonon/PhononCoreML.swiftmodule \
    -emit-library -static -o build/phonon/libPhononCoreML.a
fi

APP=build/Dictate.app
rm -rf $APP
mkdir -p $APP/Contents/MacOS $APP/Contents/Resources/Phonon-2-CoreML
cp Info.plist $APP/Contents/Info.plist
# The model ships inside the app. cp -c makes an APFS clone, so it takes no extra disk space.
MODEL=models/Phonon-2-CoreML
cp -c -R $MODEL/manifest.json $MODEL/decoder.bin $MODEL/Phonon-2.mlpackage $MODEL/NOTICE $MODEL/LICENSE-WEIGHTS-CC-BY-4.0.txt \
  $APP/Contents/Resources/Phonon-2-CoreML/

xcrun swiftc -O -swift-version 5 \
  -I build/phonon -I $PHONON_TDT/Headers \
  Sources/*.swift build/phonon/libPhononCoreML.a $PHONON_TDT/libPhononTDT.a \
  -framework Accelerate -framework CoreML \
  -o $APP/Contents/MacOS/Dictate

# Sign with a stable local certificate so macOS keeps permission grants across rebuilds.
# signing/ holds a separate keychain made for this app (not the login keychain).
# codesign finds the identity only through the user keychain search list, so add the
# keychain for the signing step and restore the original list afterwards.
KEYCHAIN=$PWD/signing/dictate-signing.keychain-db
ORIGINAL_KEYCHAINS=("${(@f)$(security list-keychains -d user | sed -E 's/^ *"(.*)"$/\1/')}")
trap 'security list-keychains -d user -s "${ORIGINAL_KEYCHAINS[@]}"' EXIT
security list-keychains -d user -s "${ORIGINAL_KEYCHAINS[@]}" "$KEYCHAIN"
security unlock-keychain -p "$(cat signing/keychain-password)" "$KEYCHAIN"
IDENTITY=$(security find-identity -p codesigning "$KEYCHAIN" | awk '/Dictate Local Signing/ { print $2; exit }')
codesign --force --sign "$IDENTITY" --identifier dev.local.dictate $APP

echo "Built $APP"
