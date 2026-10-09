#!/bin/zsh
set -euo pipefail
cd "$(dirname "$0")/.."
mkdir -p /tmp/dictate-sol-fixes
xcrun swiftc -swift-version 5 -parse-as-library Sources/Operation.swift Sources/Insertion.swift \
  Tests/InsertionChecks.swift -o /tmp/dictate-sol-fixes/insertion-checks
/tmp/dictate-sol-fixes/insertion-checks
