#!/bin/zsh
set -euo pipefail
cd "$(dirname "$0")/.."
xcrun swiftc -swift-version 5 -parse-as-library Sources/Operation.swift Sources/Insertion.swift Sources/Keyboard.swift \
  Tests/KeyboardChecks.swift -o /tmp/dictate-sol-fixes/keyboard-checks
/tmp/dictate-sol-fixes/keyboard-checks
