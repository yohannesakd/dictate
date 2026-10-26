#!/bin/zsh
# Build on the selected Mac, verify the new bundle, and replace only /Applications/Dictate.app.
set -euo pipefail
cd "$(dirname "$0")/.."
mac_host=${1:-mac}
workspace=$(ssh "$mac_host" 'mktemp -d /tmp/dictate-build.XXXXXX')
[[ "$workspace" =~ '^/tmp/dictate-build\.[A-Za-z0-9]+$' ]] || { print -u2 'The Mac returned an invalid build directory.'; exit 1; }
cleanup() { ssh "$mac_host" "rm -r -- '$workspace'" || print -u2 "Build files remain at $mac_host:$workspace"; }
trap cleanup EXIT
# Include local source changes, while excluding generated files and private build inputs.
git ls-files -co --exclude-standard -z | while IFS= read -r -d '' file; do
  [[ -f "$file" || -L "$file" ]] && printf '%s\0' "$file"
done | rsync -a --from0 --files-from=- ./ "$mac_host:$workspace/"
ssh "$mac_host" zsh -s -- "$workspace" <<'REMOTE'
set -euo pipefail
build_workspace=$1
inputs="$HOME/Library/Application Support/Dictate Build"
cd "$build_workspace"
for input in models vendor signing; do
  [[ -d "$inputs/$input" ]] || { print -u2 "Missing $inputs/$input"; exit 1; }
  ln -s "$inputs/$input" "$input"
done
./build.sh
codesign --verify --strict build/Dictate.app
install_stage=$(mktemp -d /Applications/.dictate-install.XXXXXX)
rollback=$(mktemp -d /tmp/dictate-install.XXXXXX)
installed=0
cleanup_install() {
  if (( ! installed )) && [[ -d "$rollback/Dictate.previous.app" ]]; then
    [[ ! -e /Applications/Dictate.app ]] || mv /Applications/Dictate.app "$install_stage/Dictate.failed.app"
    if ! mv "$rollback/Dictate.previous.app" /Applications/Dictate.app; then
      print -u2 "Restore the previous app from $rollback/Dictate.previous.app"
      return 1
    fi
  fi
  rm -r -- "$install_stage"
}
trap cleanup_install EXIT
# Copy and verify before stopping the current app. The final replacement uses one filesystem.
ditto build/Dictate.app "$install_stage/Dictate.app"
codesign --verify --strict "$install_stage/Dictate.app"
if pgrep -f '^/Applications/Dictate.app/Contents/MacOS/Dictate( |$)' >/dev/null; then
  osascript -e 'tell application "/Applications/Dictate.app" to quit'
  stop_deadline=$((SECONDS + 15))
  while pgrep -f '^/Applications/Dictate.app/Contents/MacOS/Dictate( |$)' >/dev/null; do
    (( SECONDS < stop_deadline )) || { print -u2 'Dictate did not exit. The installed app is unchanged.'; exit 1; }
    sleep 0.2
  done
fi
if [[ -e /Applications/Dictate.app ]]; then mv /Applications/Dictate.app "$rollback/Dictate.previous.app"; fi
mv "$install_stage/Dictate.app" /Applications/Dictate.app
codesign --verify --strict /Applications/Dictate.app
installed=1
print "Previous app retained at $rollback/Dictate.previous.app"
open /Applications/Dictate.app
print 'Installed /Applications/Dictate.app'
REMOTE
