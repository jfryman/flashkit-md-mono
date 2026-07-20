#!/usr/bin/env bash
# Local CI: restore, build (warnings as errors), test.
# Must stay fast: no hardware, no sleeps, in-memory tests only.
set -euo pipefail
cd "$(dirname "$0")"

# shellcheck source=ensure-dotnet.sh
. ./ensure-dotnet.sh

dotnet restore flashkit-md.sln
dotnet build flashkit-md.sln --no-restore -c Release -warnaserror
dotnet test flashkit-md.sln --no-build -c Release
