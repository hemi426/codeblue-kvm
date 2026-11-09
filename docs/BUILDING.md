# Building CodeBlue

The bootstrap scripts automate everything here. This page is the manual
version, and the reference for what the scripts do.

## Viewer (Windows)

### Requirements

| Tool | Version | Notes |
|------|---------|-------|
| .NET SDK | 10.0.100 or newer 10.0.x | Pinned in `global.json` at the repo root |
| Git | any recent | |
| VS Code + C# Dev Kit | optional | Any editor works; `dotnet` does the building |

Visual Studio is **not** required.

### Automated

```powershell
.\scripts\bootstrap-windows.ps1 -CheckOnly       # report only, change nothing
.\scripts\bootstrap-windows.ps1 -Component viewer
.\scripts\bootstrap-windows.ps1 -Yes             # unattended (CI)
```

The script checks each tool's version before doing anything, leaves anything
already installed alone (however it was installed), and asks once before
installing what's missing with winget. Running it again on a working setup just
reports OK.

Exit codes: `0` ready, `1` something missing, `2` an install failed.

#### "Run only scripts that you trust" prompt

If you got the source from GitHub's **Download ZIP** button (or any other
browser download), Windows marks the extracted files as coming from the
internet, and PowerShell asks before running the script. Read the script first;
then, if you're happy with it, clear the mark for the whole checkout:

```powershell
Get-ChildItem -Recurse -File | Unblock-File
```

Run that from the repo root. It affects only these files; your execution
policy is unchanged. A checkout made with `git clone` never has the mark, so
it never shows the prompt.

### Manual

```powershell
winget install --id Microsoft.DotNet.SDK.10 --exact
winget install --id Git.Git --exact
dotnet build viewer\CodeBlue.Viewer -c Release
dotnet run --project viewer\CodeBlue.Viewer
```

### Dependency versions

NuGet versions live in one place, `viewer/Directory.Packages.props`
(Central Package Management). To update a dependency, change its line there.

libVLC comes in two packages that must stay at the same version:
`VideoLAN.LibVLC.Windows` (the LGPL core and plugins) and
`VideoLAN.LibVLC.Windows.GPL` (GPL-licensed plugins, including DirectShow
capture). Without the second one, video fails with "VLC is unable to open the
MRL 'dshow://'" in `%LOCALAPPDATA%\CodeBlue\libvlc.log`.

### Publishing a standalone build

```powershell
dotnet publish viewer\CodeBlue.Viewer -c Release -r win-x64 --self-contained
```

The output folder runs on machines without .NET installed. It includes the
.NET runtime and libVLC.

The viewer ships only the parts of libVLC it uses (x64 only, and about 20 of
its roughly 330 plugins), listed in `CodeBlue.Viewer.csproj` under
`VlcWindowsX64IncludeFiles`. If a change to the viewer needs another libVLC
feature, `libvlc.log` says "no ... modules matched", and the fix is adding
that plugin to the list.

## Firmware (ESP32-S3)

The firmware builds with ESP-IDF on Windows, Linux or macOS (the three hosts
ESP-IDF supports). Board presets, logging and testing are covered in
[../firmware/README.md](../firmware/README.md).

### ESP-IDF version

`firmware/IDF_VERSION` holds the tested ESP-IDF release (currently `v6.0.3`),
and the bootstrap scripts use it by default. To build against the newest
ESP-IDF release instead, pass `latest`. That is **experimental**: the firmware
may not build, and bug reports should say which version was used.

### Automated

```powershell
# Windows
.\scripts\bootstrap-windows.ps1 -Component firmware -CheckOnly
.\scripts\bootstrap-windows.ps1 -Component firmware
.\scripts\bootstrap-windows.ps1 -Component firmware -IdfVersion latest   # experimental
```

```sh
# Linux and macOS
scripts/bootstrap-unix.sh --check-only
scripts/bootstrap-unix.sh
scripts/bootstrap-unix.sh --idf-version latest                          # experimental
```

Both scripts check first and change nothing until you agree. They clone
ESP-IDF into `~/esp/esp-idf-<version>` (about 1 GB; change it with
`-IdfRoot` / `--idf-root`), then run ESP-IDF's own installer for the esp32s3
only, which puts its toolchain under `~/.espressif`. An ESP-IDF you already
have is used as-is when `IDF_PATH` points at the right version.

On Windows, missing Git or Python can be installed with winget. The Unix
script never installs system packages; it prints the command for your
package manager.

### Building

ESP-IDF has to be activated in each new terminal. From the repo root:

```powershell
. .\scripts\activate-idf.ps1               # Windows
```

```sh
. scripts/activate-idf.sh                   # Linux and macOS
```

Use these rather than ESP-IDF's own `export` scripts. Those pick ESP-IDF's
Python environment from whichever `python` is first on PATH, which on machines
with several Pythons can differ from the one ESP-IDF was installed with
("Python virtual environment ... not found"). The activate scripts find the
installed environment and point ESP-IDF at it directly.

Then:

```sh
cd firmware
idf.py build
idf.py -p COM10 flash                       # use your board's UART port
```

For another board revision, add `-DBOARD=devkitc-v1.1` (see
`firmware/boards/`). When switching boards, or after pulling changes to the
build defaults, delete `firmware/sdkconfig` first; the build stops with that
instruction if it notices a mismatch.

The UART port carries the viewer's protocol once the firmware is up, so
`idf.py monitor` shows only the start-up log. To check a freshly flashed
board, run `python tools/cb_test.py COM10 info` from the repo root; it
prints the firmware version and the target's status.
