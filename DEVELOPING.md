# Developing flashkit-md-dotnet

This is the contributor guide: building, testing, architecture, and how
releases are cut. User-facing docs live in [README.md](README.md);
[CLAUDE.md](CLAUDE.md) carries the condensed rules AI coding agents work
from — keep the three in sync when things change.

## Building and testing

Requires the .NET 8 SDK.

```
./ci.sh        # restore + build (warnings as errors) + all tests, a few seconds
./publish.sh   # self-contained single-file binaries into artifacts/<rid>/
```

`publish.sh` cross-publishes every supported target
(`linux-x64 linux-arm64 osx-x64 osx-arm64 win-x64`) from any host; set
`RIDS` to build a subset and `VERSION` to override the git-derived version
stamp. The macOS targets also get a `FlashKit MD.app` bundle assembled by
`packaging/macos/make-app.sh`.

If you publish by hand instead, keep
`-p:IncludeNativeLibrariesForSelfExtract=true`: without it the serial-port
native library lands next to the binary instead of inside it, and the
binary alone cannot open any port (this bug has shipped twice).

## Architecture

The project is library-first: all device workflows live in
`FlashKit.Core` and the front-ends only render them — the CLI and the
Avalonia GUI build on the same tested code.

- `src/FlashKit.Core/` — the library.
  - `Device`/`Cart`: serial protocol and cart logic, **ported verbatim**
    from the original client (lowercase method names and all) behind an
    `ISerialPort` seam. They are kept diffable against the original
    source in `flashkit-md-src.zip`; behavior changes belong in
    `FlashKitSession` or in separate commits with tests.
  - `DeviceConnector`/`PortDiscovery`: cross-platform port discovery with
    surfaced per-port errors.
  - `FlashKitSession`: the front-end API — `GetInfo`, `ReadRom`,
    `WriteRom`, `ReadRam`, `WriteRam`, `BakeSave`. Operations are
    synchronous, report progress via an `Action<OperationProgress>`
    callback, throw `VerifyException` on read-back mismatches, and do no
    console or file I/O.
- `src/flashkit-md/` — the CLI: argument parsing, file I/O, rendering.
- `src/FlashKit.Gui/` — the Avalonia GUI over `FlashKitSession`:
  device/cart status polling, transaction log, auto-dump/auto-write.
  Operations run on a worker thread; one serial session is held while the
  programmer is reachable (see the FTDI note below).
- `tests/FlashKit.Core.Tests/` — wire-format tests locked to the original
  protocol, plus behavior/e2e tests against `FakeFlashKitDevice`, an
  in-memory emulation of the programmer firmware and a synthetic cart.
- `tests/FlashKit.Gui.Tests/` — headless Avalonia tests driving the real
  window against the fake device.

### Testing rules

- CI never touches hardware, sleeps, or the clock; the whole suite runs
  in memory in a few seconds.
- `DeviceWireFormatTests` hardcode the original client's exact byte
  sequences. Do not "fix" them to match changed code — they lock the wire
  format.
- Real-hardware validation is a manual checklist; record results in
  [docs/hardware-validation.md](docs/hardware-validation.md). Order
  operations least- to most-destructive (info → read-rom → read-ram →
  write-ram → write-rom).

### macOS FTDI close gotcha

`SerialPort.Close()` drains via `tcdrain`, which can wedge forever after
multi-megabyte flash writes on the macOS FTDI driver. `SystemSerialPort`
discards the output queue and abandons a stuck close; an abandoned close
only frees the descriptor at process exit, so long-lived front-ends must
not close per operation — the GUI holds one session while the programmer
is reachable. Keep both behaviors if you touch the serial layer.

## Changelog and releases

`CHANGELOG.md` follows the mitchellh/HashiCorp style: one
`## X.Y.Z (Month D, YYYY)` section per release with FEATURES /
IMPROVEMENTS / BUG FIXES headings and `component:`-prefixed entries
(cli, gui, core, serial, release). Every user-visible change adds an
entry under `## Unreleased` in the same commit as the change.

To cut a release: rename `## Unreleased` to the version + date, commit,
and push a `vX.Y.Z` tag. The release workflow builds and signs all
targets, packages the Flatpak, extracts that changelog section for the
release notes, and fails if the section is missing. Code-signing and
notarization credentials are repository secrets — see
[docs/RELEASING.md](docs/RELEASING.md) for what to configure and what
happens when they are absent.

## Packaging

- `packaging/macos/` — `FlashKit MD.app` assembly (`make-app.sh`,
  `Info.plist`, icon) and the hardened-runtime entitlements used when a
  signing identity is available.
- `packaging/flatpak/` — Flatpak manifest, desktop entry, AppStream
  metainfo, and icon. CI builds the bundle on every push to main; the
  release workflow attaches `flashkit-md-vX.Y.Z-x86_64.flatpak` to the
  release.
- `packaging/99-flashkit-md.rules` — optional udev rule for serial
  access on Linux.
