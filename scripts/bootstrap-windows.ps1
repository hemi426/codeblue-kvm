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
      firmware  ESP-IDF toolchain (not yet available; the firmware is still being written)

.PARAMETER Component
    Which part to set up: all (default), viewer, or firmware.

.PARAMETER CheckOnly
    Report what is present and missing; change nothing. Exit code 1 if anything is missing.

.PARAMETER Yes
    Do not prompt; install whatever is missing and accept winget package agreements.
    Intended for unattended use such as CI.

.EXAMPLE
    .\scripts\bootstrap-windows.ps1 -CheckOnly

.EXAMPLE
    .\scripts\bootstrap-windows.ps1 -Component viewer
#>
[CmdletBinding()]
param(
    [ValidateSet('all', 'viewer', 'firmware')]
    [string]$Component = 'all',

    [switch]$CheckOnly,

    [switch]$Yes
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

function Invoke-FirmwareSetup {
    Write-Host ""
    Write-Host "Firmware (ESP-IDF)" -ForegroundColor White
    Write-Status 'ESP-IDF' 'INFO' 'not set up by this script yet; the ESP-IDF firmware is still being written.'
    return $true
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
