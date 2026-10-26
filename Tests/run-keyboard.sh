#!/bin/zsh
set -euo pipefail
cd "$(dirname "$0")/.."
check_directory=$(mktemp -d "${TMPDIR:-/tmp}/dictate-keyboard.XXXXXX")
trap 'rm -rf "$check_directory"' EXIT
xcrun swiftc -swift-version 5 -parse-as-library Sources/Operation.swift Sources/Insertion.swift Sources/Keyboard.swift \
  Tests/KeyboardChecks.swift -o "$check_directory/keyboard-checks"
"$check_directory/keyboard-checks"
