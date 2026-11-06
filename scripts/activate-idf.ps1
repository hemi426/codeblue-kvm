<#
.SYNOPSIS
    Activates ESP-IDF for building the CodeBlue firmware, in the current terminal.

.DESCRIPTION
    Dot-source it from the repo root:   . .\scripts\activate-idf.ps1

    ESP-IDF's own export.ps1 picks its Python environment by asking whichever
    'python' comes first on PATH. On machines with several Pythons, that can be
    a different version from the one ESP-IDF was installed with, and activation
    fails with "Python virtual environment ... not found". This script finds the
    environment that was actually installed and points ESP-IDF at it directly
    (IDF_PYTHON_ENV_PATH), so PATH order doesn't matter.

.PARAMETER IdfVersion
    ESP-IDF release tag. Defaults to firmware/IDF_VERSION.

.PARAMETER IdfRoot
    Folder holding ESP-IDF checkouts. Default: ~\esp
#>
param(
    [string]$IdfVersion,
    [string]$IdfRoot = (Join-Path $HOME 'esp')
)

$repoRoot = Split-Path -Parent $PSScriptRoot
if (-not $IdfVersion) {
    $IdfVersion = (Get-Content -Path (Join-Path $repoRoot 'firmware\IDF_VERSION') -TotalCount 1).Trim()
}

$idfDir = Join-Path $IdfRoot "esp-idf-$IdfVersion"
if ($env:IDF_PATH -and (Test-Path (Join-Path $env:IDF_PATH 'tools\idf.py')) -and -not (Test-Path $idfDir)) {
    $idfDir = $env:IDF_PATH
}
if (-not (Test-Path (Join-Path $idfDir 'export.ps1'))) {
    Write-Host "ESP-IDF $IdfVersion not found at $idfDir. Run .\scripts\bootstrap-windows.ps1 -Component firmware first." -ForegroundColor Red
    return
}

# Environments are named idf<major.minor>_py<python major.minor>_env.
$mm = if ($IdfVersion -match '^v(\d+\.\d+)') { $Matches[1] } else { '*' }
$tools = if ($env:IDF_TOOLS_PATH) { $env:IDF_TOOLS_PATH } else { Join-Path $HOME '.espressif' }
$envDir = Get-ChildItem (Join-Path $tools 'python_env') -Directory -Filter "idf${mm}_py*_env" -ErrorAction SilentlyContinue |
    Where-Object { Test-Path (Join-Path $_.FullName 'Scripts\python.exe') } |
    Sort-Object { if ($_.Name -match '_py(\d+)\.(\d+)_') { [int]$Matches[1] * 1000 + [int]$Matches[2] } else { 0 } } -Descending |
    Select-Object -First 1
if (-not $envDir) {
    Write-Host "No installed ESP-IDF $mm Python environment under $tools. Run .\scripts\bootstrap-windows.ps1 -Component firmware first." -ForegroundColor Red
    return
}

$env:IDF_PYTHON_ENV_PATH = $envDir.FullName
. (Join-Path $idfDir 'export.ps1')
