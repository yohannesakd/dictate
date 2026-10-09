#!/bin/zsh
set -euo pipefail
cd "$(dirname "$0")/.."
mkdir -p /tmp/dictate-sol-fixes
xcrun swiftc -swift-version 5 -parse-as-library Sources/TextCleanup.swift Sources/PersonalDictionary.swift \
  Sources/SmartCleanup.swift Tests/CleanupStubs.swift Tests/CleanupChecks.swift -o /tmp/dictate-sol-fixes/cleanup-checks
/tmp/dictate-sol-fixes/cleanup-checks
