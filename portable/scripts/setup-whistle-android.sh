#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/../.."
destination=portable/.tools/whistle/arm64-v8a
mkdir -p "$destination"
runtime="$destination/libdictate_needle.so"
expected=aaf2e4f7f492c0bc67af354ac03b7246fcef890de16df0bebaf80b17b9aac90e
if [[ ! -f "$runtime" ]] || [[ "$(sha256sum "$runtime" | cut -d ' ' -f 1)" != "$expected" ]]; then
  staging=$(mktemp "$destination/.needle.XXXXXX")
  trap 'rm -f "$staging"' EXIT
  curl --fail --location --silent --show-error --max-time 300 \
    https://huggingface.co/Cactus-Compute/needle3/resolve/8354babbfb92d719ee559f2999d133f11fb9374c/android-arm64/needle -o "$staging"
  [[ "$(sha256sum "$staging" | cut -d ' ' -f 1)" == "$expected" ]]
  chmod 755 "$staging"
  mv "$staging" "$runtime"
fi
