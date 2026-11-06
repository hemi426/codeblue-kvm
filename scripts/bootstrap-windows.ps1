<#
.SYNOPSIS
    Checks for (and optionally installs) the tools needed to build CodeBlue on Windows.

.DESCRIPTION
    The script is deliberately polite:
      * It checks first. Anything already installed at a usable version is left
        alone, no matter how it got there (winget, scoop, an installer, by hand).
      * It shows a report, then asks once before installing anything.
      * It never changes your global PATH or settings on its own; installers
        it runs (through winget) do their own normal setup.
      * It is safe to run again; on a working machine it just reports OK.

    Components:
      viewer    .NET SDK (version from global.json) and Git
      firmware  Python, Git, ESP-IDF (version from firmware/IDF_VERSION) and its esp32s3 toolchain

.PARAMETER Component
    Which part to set up: all (default), viewer, or firmware.

.PARAMETER CheckOnly
    Report what is present and missing; change nothing. Exit code 1 if anything is missing.

.PARAMETER Yes
    Do not prompt; install whatever is missing and accept winget package agreements.
    Intended for unattended use such as CI.

.PARAMETER IdfVersion
    ESP-IDF release tag to set up, such as v6.0.3. Defaults to the tested version in
    firmware/IDF_VERSION. 'latest' uses the newest ESP-IDF release instead; that is
    EXPERIMENTAL and the firmware may not build with it.

.PARAMETER IdfRoot
    Folder for ESP-IDF checkouts, one subfolder per version. Default: ~\esp

.EXAMPLE
    .\scripts\bootstrap-windows.ps1 -CheckOnly

.EXAMPLE
    .\scripts\bootstrap-windows.ps1 -Component firmware -IdfVersion latest

.EXAMPLE
    .\scripts\bootstrap-windows.ps1 -Component viewer
#>
[CmdletBinding()]
param(
    [ValidateSet('all', 'viewer', 'firmware')]
    [string]$Component = 'all',

    [switch]$CheckOnly,

    [switch]$Yes,

    # ESP-IDF release tag. Defaults to the known-good version in firmware/IDF_VERSION.
    # 'latest' uses the newest release: EXPERIMENTAL, builds are not guaranteed.
    [string]$IdfVersion,

    # Where ESP-IDF checkouts live (one folder per version).
    [string]$IdfRoot = (Join-Path $HOME 'esp')
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$RepoRoot = Split-Path -Parent $PSScriptRoot
$ViewerDir = Join-Path $RepoRoot 'viewer'
$ViewerProject = Join-Path $ViewerDir 'CodeBlue.Viewer\CodeBlue.Viewer.csproj'

# ---------------------------------------------------------------- helpers

function Write-Status([string]$Name, [string]$State, [string]$Detail) {
    $color = switch ($State) {
        'OK'      { 'Green' }
        'MISSING' { 'Yellow' }
        'OLD'     { 'Yellow' }
        'INFO'    { 'Cyan' }
        default   { 'Gray' }
    }
    Write-Host ('  {0,-22}' -f $Name) -NoNewline
    Write-Host ('{0,-9}' -f $State) -ForegroundColor $color -NoNewline
    Write-Host $Detail
}

function Confirm-Step([string]$Question) {
    if ($Yes) { return $true }
    $answer = Read-Host "$Question [y/N]"
    return $answer -match '^(y|yes)$'
}

function Update-SessionPath {
    # Pick up PATH changes made by installers, for this session only.
    $machine = [Environment]::GetEnvironmentVariable('Path', 'Machine')
    $user = [Environment]::GetEnvironmentVariable('Path', 'User')
    $env:Path = (@($machine, $user) | Where-Object { $_ }) -join ';'
}

function Get-RequiredSdkVersion {
    $globalJson = Join-Path $RepoRoot 'global.json'
    $json = Get-Content -Raw -Path $globalJson | ConvertFrom-Json
    return [version]$json.sdk.version
}

# Each check returns an object: Name, Ok, State, Detail, WingetId
function Test-Git {
    $cmd = Get-Command git -ErrorAction SilentlyContinue
    if (-not $cmd) {
        return [pscustomobject]@{ Name = 'Git'; Ok = $false; State = 'MISSING'; Detail = ''; WingetId = 'Git.Git' }
    }
    $ver = (& git --version) -replace '^git version\s+', ''
    return [pscustomobject]@{ Name = 'Git'; Ok = $true; State = 'OK'; Detail = $ver; WingetId = 'Git.Git' }
}

function Test-DotNetSdk {
    $required = Get-RequiredSdkVersion
    $wingetId = "Microsoft.DotNet.SDK.$($required.Major)"
    $name = ".NET SDK $($required.Major).$($required.Minor)"

    $cmd = Get-Command dotnet -ErrorAction SilentlyContinue
    if (-not $cmd) {
        return [pscustomobject]@{ Name = $name; Ok = $false; State = 'MISSING'; Detail = "need $required or newer in $($required.Major).$($required.Minor).x"; WingetId = $wingetId }
    }

    # global.json uses rollForward=latestFeature: any SDK with the same
    # major.minor and a version at or above the pinned one will do.
    $sdks = & dotnet --list-sdks 2>$null | ForEach-Object {
        if ($_ -match '^(\d+\.\d+\.\d+)') { [version]$Matches[1] }
    }
    $usable = @($sdks | Where-Object {
        $_.Major -eq $required.Major -and $_.Minor -eq $required.Minor -and $_ -ge $required
    } | Sort-Object -Descending)

    if ($usable.Count -gt 0) {
        return [pscustomobject]@{ Name = $name; Ok = $true; State = 'OK'; Detail = "$($usable[0])"; WingetId = $wingetId }
    }
    $have = if ($sdks) { ($sdks | Sort-Object -Descending | Select-Object -First 3) -join ', ' } else { 'none' }
    return [pscustomobject]@{ Name = $name; Ok = $false; State = 'OLD'; Detail = "found $have; need $required+"; WingetId = $wingetId }
}

function Test-Winget {
    return [bool](Get-Command winget -ErrorAction SilentlyContinue)
}

function Install-WithWinget([string]$Id) {
    $wingetArgs = @('install', '--id', $Id, '--exact', '--source', 'winget')
    if ($Yes) { $wingetArgs += @('--accept-package-agreements', '--accept-source-agreements', '--disable-interactivity') }
    Write-Host ""
    Write-Host "  winget $($wingetArgs -join ' ')" -ForegroundColor DarkGray
    # Out-Host keeps installer output out of the function's return value.
    & winget @wingetArgs | Out-Host
    if ($LASTEXITCODE -ne 0) {
        throw "winget could not install $Id (exit code $LASTEXITCODE)."
    }
}

# ---------------------------------------------------------------- viewer

function Invoke-ViewerSetup {
    Write-Host ""
    Write-Host "Viewer (Windows app)" -ForegroundColor White

    $checks = @(Test-Git; Test-DotNetSdk)
    foreach ($c in $checks) { Write-Status $c.Name $c.State $c.Detail }

    $code = Get-Command code -ErrorAction SilentlyContinue
    if ($code) {
        Write-Status 'VS Code' 'OK' '(optional)'
    } else {
        Write-Status 'VS Code' 'INFO' 'optional; any editor works. With VS Code, add the C# Dev Kit extension.'
    }

    $missing = @($checks | Where-Object { -not $_.Ok })
    if ($missing.Count -eq 0) {
        Write-Host ""
        Write-Host "  Viewer toolchain is ready." -ForegroundColor Green
        Invoke-ViewerTestBuild
        return $true
    }

    if ($CheckOnly) { return $false }

    if (-not (Test-Winget)) {
        Write-Host ""
        Write-Host "  winget is not available, so nothing can be installed automatically." -ForegroundColor Yellow
        Write-Host "  Install these yourself (any method is fine), then run this script again:"
        foreach ($m in $missing) { Write-Host "    - $($m.Name)  (winget id: $($m.WingetId))" }
        return $false
    }

    Write-Host ""
    Write-Host "  To install with winget:"
    foreach ($m in $missing) { Write-Host "    - $($m.Name)  ($($m.WingetId))" }
    if (-not (Confirm-Step "  Install these now?")) {
        Write-Host "  Skipped. Nothing was changed."
        return $false
    }

    foreach ($m in $missing) { Install-WithWinget $m.WingetId }
    Update-SessionPath

    # Re-check so the report reflects reality, not intent.
    $recheck = @(Test-Git; Test-DotNetSdk)
    Write-Host ""
    foreach ($c in $recheck) { Write-Status $c.Name $c.State $c.Detail }
    $stillMissing = @($recheck | Where-Object { -not $_.Ok })
    if ($stillMissing.Count -gt 0) {
        Write-Host "  Some tools still aren't visible. Open a new terminal (to refresh PATH) and run this script again." -ForegroundColor Yellow
        return $false
    }

    Invoke-ViewerTestBuild
    return $true
}

function Invoke-ViewerTestBuild {
    if ($CheckOnly) { return }
    if (-not (Confirm-Step "  Run a test build of the viewer now?")) { return }
    Write-Host ""
    & dotnet build $ViewerProject -c Release | Out-Host
    if ($LASTEXITCODE -eq 0) {
        Write-Host ""
        Write-Host "  Build succeeded. Run it with:" -ForegroundColor Green
        Write-Host "    dotnet run --project `"$ViewerProject`""
    } else {
        Write-Host "  Build failed; see the output above." -ForegroundColor Red
    }
}

# ---------------------------------------------------------------- firmware

$IdfMinPython = [version]'3.10'

function Resolve-IdfVersion {
    if ($IdfVersion -eq 'latest') {
        $tag = (Invoke-RestMethod 'https://api.github.com/repos/espressif/esp-idf/releases/latest').tag_name
        Write-Host "  Using latest ESP-IDF release $tag (EXPERIMENTAL: not the tested version)." -ForegroundColor Yellow
        return $tag
    }
    if ($IdfVersion) { return $IdfVersion }
    return (Get-Content -Path (Join-Path $RepoRoot 'firmware\IDF_VERSION') -TotalCount 1).Trim()
}

function Test-Python {
    # Check plain 'python', because that is what ESP-IDF's install.ps1 runs.
    # (The 'py' launcher can report a different, older interpreter.)
    $cmd = Get-Command python -ErrorAction SilentlyContinue
    if ($cmd) {
        $out = & python --version 2>&1
        if ($out -match 'Python (\d+\.\d+)') {
            $ver = [version]$Matches[1]
            $where = $cmd.Source
            if ($ver -ge $IdfMinPython) {
                return [pscustomobject]@{ Name = 'Python'; Ok = $true; State = 'OK'; Detail = "$ver ($where)"; WingetId = 'Python.Python.3.12' }
            }
            return [pscustomobject]@{ Name = 'Python'; Ok = $false; State = 'OLD'; Detail = "found $ver at $where; need $IdfMinPython+"; WingetId = 'Python.Python.3.12' }
        }
    }
    return [pscustomobject]@{ Name = 'Python'; Ok = $false; State = 'MISSING'; Detail = "need $IdfMinPython+"; WingetId = 'Python.Python.3.12' }
}

# Returns the tag an ESP-IDF checkout is at, or $null.
function Get-IdfCheckoutTag([string]$Path) {
    if (-not (Test-Path (Join-Path $Path 'tools\idf.py'))) { return $null }
    $tag = & git -C $Path describe --tags --exact-match 2>$null
    if ($LASTEXITCODE -eq 0) { return $tag.Trim() }
    return 'unknown'
}

function Test-IdfToolchain([string]$Tag) {
    # install.ps1 puts the esp32s3 compiler and a version-specific Python env under ~\.espressif.
    $tools = if ($env:IDF_TOOLS_PATH) { $env:IDF_TOOLS_PATH } else { Join-Path $HOME '.espressif' }
    $mm = if ($Tag -match '^v(\d+\.\d+)') { $Matches[1] } else { '' }
    $compiler = Test-Path (Join-Path $tools 'tools\xtensa-esp-elf')
    $pyenv = @(Get-ChildItem (Join-Path $tools 'python_env') -Directory -Filter "idf${mm}_py*" -ErrorAction SilentlyContinue).Count -gt 0
    return ($compiler -and $pyenv)
}

function Invoke-FirmwareSetup {
    Write-Host ""
    Write-Host "Firmware (ESP-IDF)" -ForegroundColor White

    $tag = Resolve-IdfVersion
    $idfDir = Join-Path $IdfRoot "esp-idf-$tag"

    # Respect an existing ESP-IDF at the right version, wherever it lives.
    if ($env:IDF_PATH -and (Get-IdfCheckoutTag $env:IDF_PATH) -eq $tag) { $idfDir = $env:IDF_PATH }

    $python = Test-Python
    $git = Test-Git
    $haveTag = Get-IdfCheckoutTag $idfDir
    $sourceOk = $haveTag -eq $tag
    $toolsOk = $sourceOk -and (Test-IdfToolchain $tag)

    foreach ($c in @($git, $python)) { Write-Status $c.Name $c.State $c.Detail }
    Write-Status "ESP-IDF $tag" $(if ($sourceOk) { 'OK' } elseif ($haveTag) { 'OLD' } else { 'MISSING' }) `
        $(if ($haveTag -and -not $sourceOk) { "$idfDir is at $haveTag" } else { $idfDir })
    Write-Status 'esp32s3 toolchain' $(if ($toolsOk) { 'OK' } else { 'MISSING' }) ''

    if ($sourceOk -and $toolsOk) {
        Show-IdfNextSteps $idfDir
        return $true
    }
    if ($CheckOnly) { return $false }

    if ($haveTag -and -not $sourceOk) {
        Write-Host "  $idfDir exists but is at $haveTag. Move or delete it, then run this again." -ForegroundColor Yellow
        return $false
    }

    $missing = @(@($git, $python) | Where-Object { -not $_.Ok })
    Write-Host ""
    Write-Host "  To set up:"
    foreach ($m in $missing) { Write-Host "    - $($m.Name) with winget ($($m.WingetId))" }
    if (-not $sourceOk) { Write-Host "    - Clone ESP-IDF $tag into $idfDir (about 1 GB)" }
    Write-Host "    - Run ESP-IDF's install.ps1 for esp32s3 (downloads its toolchain into ~\.espressif)"
    if (-not (Confirm-Step "  Go ahead?")) {
        Write-Host "  Skipped. Nothing was changed."
        return $false
    }

    if ($missing.Count -gt 0) {
        if (-not (Test-Winget)) { throw "winget is not available; install $($missing.Name -join ', ') yourself, then run this again." }
        foreach ($m in $missing) { Install-WithWinget $m.WingetId }
        Update-SessionPath
    }

    if (-not $sourceOk) {
        New-Item -ItemType Directory -Force -Path $IdfRoot | Out-Null
        & git clone -b $tag --depth 1 --recursive --shallow-submodules `
            https://github.com/espressif/esp-idf.git $idfDir | Out-Host
        if ($LASTEXITCODE -ne 0) { throw "git clone of ESP-IDF $tag failed." }
    }

    & (Join-Path $idfDir 'install.ps1') esp32s3 | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "ESP-IDF install.ps1 failed." }

    Show-IdfNextSteps $idfDir
    return $true
}

function Show-IdfNextSteps([string]$IdfDir) {
    Write-Host ""
    Write-Host "  ESP-IDF is ready. In each new terminal, activate it, then build:" -ForegroundColor Green
    Write-Host "    . `"$IdfDir\export.ps1`""
    Write-Host "    cd firmware; idf.py build"
}

# ---------------------------------------------------------------- main

Write-Host "CodeBlue bootstrap" -ForegroundColor White
if ($CheckOnly) { Write-Host "(check only: nothing will be changed)" -ForegroundColor DarkGray }

$ok = $true
try {
    if ($Component -in 'all', 'viewer') { $ok = (Invoke-ViewerSetup) -and $ok }
    if ($Component -in 'all', 'firmware') { $ok = (Invoke-FirmwareSetup) -and $ok }
} catch {
    Write-Host ""
    Write-Host "Error: $($_.Exception.Message)" -ForegroundColor Red
    exit 2
}

Write-Host ""
exit ($(if ($ok) { 0 } else { 1 }))
