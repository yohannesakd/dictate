#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
mkdir -p .tools
tools="${DICTATE_ANDROID_TOOLS:-$PWD/.tools}"
if [[ ! -x "$tools/dotnet/dotnet" ]]; then
  curl --fail --location --silent --show-error https://dot.net/v1/dotnet-install.sh -o .tools/dotnet-install.sh
  bash .tools/dotnet-install.sh --version 10.0.401 --install-dir "$tools/dotnet"
fi
export DOTNET_ROOT="$tools/dotnet"
export PATH="$DOTNET_ROOT:$PATH"
dotnet workload install android --skip-manifest-update
bash scripts/setup-sdk.sh
