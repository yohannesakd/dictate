#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/../.."
RID=${1:-linux-x64}
mkdir -p desktop/resources
STAGING=$(mktemp -d desktop/resources/.backend.XXXXXX)
trap 'rm -rf "$STAGING"' EXIT
dotnet publish portable/Desktop/Dictate.Desktop.csproj -c Release -r "$RID" --self-contained true \
  -p:PublishSingleFile=true -p:DebugType=None -p:DebugSymbols=false -m:2 -o "$STAGING"
rm -rf desktop/resources/backend
mv "$STAGING" desktop/resources/backend
