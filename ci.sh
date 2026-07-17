#!/usr/bin/env bash
# Local CI: restore, build (warnings as errors), test.
# Must stay fast: no hardware, no sleeps, in-memory tests only.
set -euo pipefail
cd "$(dirname "$0")"

# Prefer a user-local SDK install if dotnet is not already on PATH.
if ! command -v dotnet >/dev/null 2>&1 && [ -x "$HOME/.dotnet/dotnet" ]; then
  export PATH="$HOME/.dotnet:$PATH"
fi
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1

dotnet restore flashkit-md.sln
dotnet build flashkit-md.sln --no-restore -c Release -warnaserror
dotnet test flashkit-md.sln --no-build -c Release
