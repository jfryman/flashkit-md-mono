# Sourced by ci.sh and publish.sh (after cd to the repo root): put a
# usable dotnet on PATH, installing the SDK pinned in global.json into
# ~/.dotnet if no install satisfies it.

# Prefer a user-local SDK install if dotnet is not already on PATH.
if ! command -v dotnet >/dev/null 2>&1 && [ -x "$HOME/.dotnet/dotnet" ]; then
  export PATH="$HOME/.dotnet:$PATH"
fi
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1

# `dotnet --version` honors global.json and exits nonzero when the pinned
# SDK is missing, so this covers both "no dotnet at all" and "only an
# older SDK installed".
if ! dotnet --version >/dev/null 2>&1; then
  channel="$(sed -n 's/.*"version": *"\([0-9][0-9]*\.[0-9][0-9]*\)\..*/\1/p' global.json)"
  echo "== .NET SDK ${channel} not found; installing into $HOME/.dotnet =="
  curl -fsSL https://dot.net/v1/dotnet-install.sh \
    | bash -s -- --channel "$channel" --install-dir "$HOME/.dotnet"
  # Prepend even if another dotnet is on PATH: the fresh install must win
  # over an older system-wide SDK that failed the version check above.
  export DOTNET_ROOT="$HOME/.dotnet"
  export PATH="$HOME/.dotnet:$PATH"
  dotnet --version >/dev/null
fi
