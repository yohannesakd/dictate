#!/bin/zsh
# Download the pinned Whistle inputs into vendor/whistle and models/Whistle, and check each against
# whistle-inputs.sha256 before it replaces the destination. Valid files already in place are kept.
#   needle: the upstream macos-arm64 tool, Cactus-Compute/needle3 revision 8354babbfb92d719ee559f2999d133f11fb9374c
#   whistle.cact: Cactus-Compute/whistle revision ca5287601bef25af26dcf2e1b2bdc0843a7c19e5
# Usage: scripts/setup-whistle.sh [destination-root]   (default: the checkout)
# For scripts/install-mac.sh, the root is "~/Library/Application Support/Dictate Build".
set -euo pipefail
REPO=${0:A:h:h}
ROOT=${1:-$REPO}
MANIFEST=$REPO/whistle-inputs.sha256
NEEDLE=https://huggingface.co/Cactus-Compute/needle3/resolve/8354babbfb92d719ee559f2999d133f11fb9374c
WHISTLE=https://huggingface.co/Cactus-Compute/whistle/resolve/ca5287601bef25af26dcf2e1b2bdc0843a7c19e5
typeset -A SOURCES=(
  vendor/whistle/needle "$NEEDLE/macos-arm64/needle"
  vendor/whistle/LICENSE-needle "$NEEDLE/LICENSE"
  models/Whistle/whistle.cact "$WHISTLE/whistle.cact"
)

sha256() { shasum -a 256 "$1" | awk '{ print $1 }'; }

for relative in vendor/whistle/needle vendor/whistle/LICENSE-needle models/Whistle/whistle.cact; do
  expected=$(awk -v file="$relative" '$2 == file { print $1 }' "$MANIFEST")
  [[ -n "$expected" ]] || { print -u2 "$relative has no hash in $MANIFEST"; exit 1; }
  destination=$ROOT/$relative
  if [[ -f "$destination" && "$(sha256 "$destination")" == "$expected" ]]; then
    # The hash does not cover the file mode.
    [[ "$relative" != */needle ]] || chmod 755 "$destination"
    print "Kept $relative"
  else
    mkdir -p "${destination:h}"
    partial=$(mktemp "$destination.XXXXXX")
    if ! curl -fsSL --retry 3 --retry-delay 2 --connect-timeout 15 --max-time 300 "$SOURCES[$relative]" -o "$partial"; then
      rm -f "$partial"
      print -u2 "Download failed: $SOURCES[$relative]"
      exit 1
    fi
    if [[ "$(sha256 "$partial")" != "$expected" ]]; then
      rm -f "$partial"
      print -u2 "$relative does not match the pinned hash. This file was not installed."
      exit 1
    fi
    if [[ "$relative" == */needle ]]; then chmod 755 "$partial"; else chmod 644 "$partial"; fi
    mv -f "$partial" "$destination"
    print "Installed $relative"
  fi
done
