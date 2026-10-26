#!/bin/zsh
set -euo pipefail
cd "$(dirname "$0")/.."
check_directory=$(mktemp -d "${TMPDIR:-/tmp}/dictate-insertion.XXXXXX")
trap 'rm -rf "$check_directory"' EXIT
xcrun swiftc -swift-version 5 -parse-as-library Sources/Operation.swift Sources/Insertion.swift \
  Tests/InsertionChecks.swift -o "$check_directory/insertion-checks"
"$check_directory/insertion-checks"
