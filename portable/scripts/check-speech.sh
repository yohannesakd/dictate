#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
mkdir -p artifacts models
if [[ ! -f artifacts/jfk.wav ]]; then
  curl --fail --location --silent --show-error https://raw.githubusercontent.com/ggml-org/whisper.cpp/master/samples/jfk.wav -o artifacts/jfk.wav
fi
dotnet run --project Checks/Dictate.Checks.csproj -- artifacts/jfk.wav models
