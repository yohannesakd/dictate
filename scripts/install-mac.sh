#!/bin/zsh
# Build Dictate on a Mac from this checkout and install it in /Applications. Run it on another machine, such as a server.
# The Mac keeps only the app and the build inputs that are not in Git, in "~/Library/Application Support/Dictate Build":
# models/Phonon-2-CoreML, vendor/phonon-coreml, and signing/ (see Build in the README).
# Usage: scripts/install-mac.sh [ssh-host]   (default: mac)
set -euo pipefail
cd "$(dirname "$0")/.."
HOST=${1:-mac}

ssh "$HOST" 'rm -rf /tmp/dictate-build && mkdir /tmp/dictate-build'
git ls-files -z | rsync -a --from0 --files-from=- ./ "$HOST:/tmp/dictate-build/"
ssh "$HOST" zsh -s <<'EOF'
set -euo pipefail
INPUTS="$HOME/Library/Application Support/Dictate Build"
cd /tmp/dictate-build
for input in models vendor signing; do
  [[ -d "$INPUTS/$input" ]] || { print -u2 "Missing $INPUTS/$input"; exit 1; }
  ln -s "$INPUTS/$input" "$input"
done
zsh scripts/setup-whistle.sh "$INPUTS"
zsh scripts/setup-whisper-mac.sh "$INPUTS"
./build.sh
pkill -x Dictate || true
while pgrep -x Dictate >/dev/null; do sleep 0.2; done
# Keep the previous app until the new one is in place.
rm -rf /Applications/Dictate.previous.app
if [[ -e /Applications/Dictate.app ]]; then mv /Applications/Dictate.app /Applications/Dictate.previous.app; fi
ditto build/Dictate.app /Applications/Dictate.app
rm -rf /Applications/Dictate.previous.app
open /Applications/Dictate.app
cd / && rm -rf /tmp/dictate-build
print 'Installed /Applications/Dictate.app'
EOF
