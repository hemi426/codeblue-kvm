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

### Build options

| Property | Default | Effect |
|----------|---------|--------|
| `CodeBlueLegacyProtocol` | `true` | Includes the **deprecated** legacy text protocol. Build with `-p:CodeBlueLegacyProtocol=false` to leave it out. This option will be removed once the ESP-IDF firmware ships. |

### Publishing a standalone build

```powershell
dotnet publish viewer\CodeBlue.Viewer -c Release -r win-x64 --self-contained
```

The output folder runs on machines without .NET installed. It includes the
libVLC runtime, so expect it to be fairly large.

## Firmware (ESP32-S3)

The ESP-IDF firmware is in progress. Planned toolchain handling:

- A known-good ESP-IDF release recorded in `firmware/IDF_VERSION`, used by
  default.
- An experimental option to use the newest ESP-IDF release instead, for people
  who prefer to stay current. Builds on it are not guaranteed.
- Bootstrap support on all three hosts ESP-IDF supports: Windows
  (`bootstrap-windows.ps1`) and Linux/macOS (`bootstrap-unix.sh`).
