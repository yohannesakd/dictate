#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/../.."
tools="${DICTATE_ANDROID_TOOLS:-$PWD/portable/.tools}"
mkdir -p "$tools"
if [[ ! -x "$tools/jdk/bin/java" ]]; then
  curl --fail --location --retry 2 --connect-timeout 15 --max-time 600 \
    https://api.adoptium.net/v3/binary/latest/21/ga/linux/x64/jdk/hotspot/normal/eclipse -o "$tools/jdk.tar.gz"
  mkdir -p "$tools/jdk"
  tar -xzf "$tools/jdk.tar.gz" --strip-components=1 -C "$tools/jdk"
  rm "$tools/jdk.tar.gz"
fi
if [[ ! -x "$tools/android-sdk/cmdline-tools/latest/bin/sdkmanager" ]]; then
  sdk_url=https://dl.google.com/android/repository/commandlinetools-linux-15859902_latest.zip
  download_bytes=$(curl --fail --silent --show-error --head --location --connect-timeout 15 --max-time 30 "$sdk_url" | tr -d '\r' | awk 'tolower($1) == "content-length:" { size=$2 } END { print size }')
  if [[ ! "$download_bytes" =~ ^[0-9]+$ ]]; then echo "The SDK download size is unavailable." >&2; exit 1; fi
  chunk_bytes=$(( (download_bytes + 7) / 8 ))
  mkdir -p "$tools/sdk-parts"
  echo "Downloading the Android SDK tools in eight checked ranges."
  jobs=()
  for index in {0..7}; do
    start=$((index * chunk_bytes))
    end=$((start + chunk_bytes - 1))
    if ((end >= download_bytes)); then end=$((download_bytes - 1)); fi
    curl --fail --silent --show-error --location --retry 2 --connect-timeout 15 --max-time 600 \
      --range "$start-$end" --max-filesize "$chunk_bytes" "$sdk_url" -o "$tools/sdk-parts/$index" &
    jobs+=("$!")
  done
  for pid in "${jobs[@]}"; do wait "$pid"; done
  cat "$tools/sdk-parts/"{0..7} > "$tools/command-tools.zip"
  rm -r "$tools/sdk-parts"
  echo "4e4c464f145a7512b57d088ac6c278c03c9eea610886b35a5e0804e74eedf583  $tools/command-tools.zip" | sha256sum --check
  python3 - "$tools" <<'PY'
import os, pathlib, shutil, sys, zipfile
root=pathlib.Path(sys.argv[1]); staging=root/'sdk-download'
with zipfile.ZipFile(root/'command-tools.zip') as source:
    source.extractall(staging)
    for item in source.infolist():
        mode=item.external_attr >> 16
        if mode and not item.is_dir(): os.chmod(staging/item.filename, mode)
target=root/'android-sdk/cmdline-tools/latest'
target.parent.mkdir(parents=True,exist_ok=True)
shutil.move(str(staging/'cmdline-tools'),target)
shutil.rmtree(staging)
(root/'command-tools.zip').unlink()
PY
fi
export JAVA_HOME="$tools/jdk"
export PATH="$JAVA_HOME/bin:$PATH"
(set +o pipefail; yes 2>/dev/null | "$tools/android-sdk/cmdline-tools/latest/bin/sdkmanager" --sdk_root="$tools/android-sdk" --licenses)
(set +o pipefail; yes 2>/dev/null | "$tools/android-sdk/cmdline-tools/latest/bin/sdkmanager" --sdk_root="$tools/android-sdk" "platform-tools" "platforms;android-36" "build-tools;36.0.0")
