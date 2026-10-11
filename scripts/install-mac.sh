#!/bin/zsh
# Build Dictate on a Mac from this checkout and install it in /Applications. Run it on another machine, such as a server.
# The Mac keeps only the app and the build inputs that are not in Git, in "~/Library/Application Support/Dictate Build":
# signing/ (see Build in the README).
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
for input in signing; do
  [[ -d "$INPUTS/$input" ]] || { print -u2 "Missing $INPUTS/$input"; exit 1; }
  ln -s "$INPUTS/$input" "$input"
done
./build.sh
pkill -x Dictate || true
while pgrep -x Dictate >/dev/null; do sleep 0.2; done
# Keep the previous app until the new one is in place.
ROLLBACK=$(mktemp -d /tmp/dictate-install.XXXXXX)
if [[ -e /Applications/Dictate.app ]]; then mv /Applications/Dictate.app "$ROLLBACK/Dictate.previous.app"; fi
if ! ditto build/Dictate.app /Applications/Dictate.app; then
  rm -rf /Applications/Dictate.app
  [[ ! -e "$ROLLBACK/Dictate.previous.app" ]] || mv "$ROLLBACK/Dictate.previous.app" /Applications/Dictate.app
  exit 1
fi
codesign --verify --strict /Applications/Dictate.app
print "Previous app retained at $ROLLBACK/Dictate.previous.app"
open /Applications/Dictate.app
cd / && rm -rf /tmp/dictate-build
print 'Installed /Applications/Dictate.app'
EOF
