#!/usr/bin/env bash
# CodeBlue bootstrap for Linux and macOS: checks for (and optionally installs)
# ESP-IDF and its esp32s3 toolchain, for building the firmware.
# (The viewer is Windows-only; see bootstrap-windows.ps1.)
#
# Polite by design: checks first, leaves anything already installed alone,
# asks once before changing anything, never installs system packages itself
# (it prints the command for your package manager instead), and is safe to rerun.
#
# Usage: scripts/bootstrap-unix.sh [--check-only] [--yes]
#                                  [--idf-version vX.Y.Z|latest] [--idf-root DIR]
#   --idf-version  default: firmware/IDF_VERSION (tested). 'latest' is EXPERIMENTAL.
#   --idf-root     where ESP-IDF checkouts live, one per version. Default: ~/esp
# Exit codes: 0 ready, 1 something missing or skipped, 2 an install step failed.

set -euo pipefail

repo_root="$(cd "$(dirname "$0")/.." && pwd)"
check_only=0; yes=0; idf_version=""; idf_root="$HOME/esp"
min_python="3.10"

while [ $# -gt 0 ]; do
    case "$1" in
        --check-only) check_only=1 ;;
        --yes) yes=1 ;;
        --idf-version) idf_version="${2:?--idf-version needs a value}"; shift ;;
        --idf-root) idf_root="${2:?--idf-root needs a value}"; shift ;;
        -h|--help) sed -n '2,15p' "$0"; exit 0 ;;
        *) echo "Unknown option: $1" >&2; exit 2 ;;
    esac
    shift
done

if [ -t 1 ]; then green=$'\e[32m'; yellow=$'\e[33m'; red=$'\e[31m'; off=$'\e[0m'; else green=; yellow=; red=; off=; fi
status() { # name state detail
    local c=$yellow; [ "$2" = OK ] && c=$green
    printf '  %-22s%s%-9s%s%s\n' "$1" "$c" "$2" "$off" "${3:-}"
}
confirm() {
    [ "$yes" = 1 ] && return 0
    local a; read -r -p "$1 [y/N]: " a || return 1
    [[ "$a" =~ ^([yY]|[yY][eE][sS])$ ]]
}
fail() { echo "${red}Error: $*${off}" >&2; exit 2; }

echo "CodeBlue bootstrap (firmware)"
[ "$check_only" = 1 ] && echo "(check only: nothing will be changed)"
echo

# ---- ESP-IDF version
if [ "$idf_version" = latest ]; then
    command -v curl >/dev/null || fail "curl is needed to look up the latest ESP-IDF release."
    idf_version="$(curl -fsSL https://api.github.com/repos/espressif/esp-idf/releases/latest \
        | sed -n 's/.*"tag_name": *"\([^"]*\)".*/\1/p' | head -n1)"
    [ -n "$idf_version" ] || fail "could not determine the latest ESP-IDF release."
    echo "${yellow}Using latest ESP-IDF release $idf_version (EXPERIMENTAL: not the tested version).${off}"
elif [ -z "$idf_version" ]; then
    idf_version="$(head -n1 "$repo_root/firmware/IDF_VERSION" | tr -d '[:space:]')"
fi
idf_dir="$idf_root/esp-idf-$idf_version"

checkout_tag() { # dir -> tag, 'unknown', or nothing
    [ -f "$1/tools/idf.py" ] || return 0
    git -C "$1" describe --tags --exact-match 2>/dev/null || echo unknown
}
# Respect an existing ESP-IDF at the right version, wherever it lives.
if [ -n "${IDF_PATH:-}" ] && [ "$(checkout_tag "$IDF_PATH")" = "$idf_version" ]; then idf_dir="$IDF_PATH"; fi

# ---- prerequisites (reported, never installed by this script)
missing_pkgs=0
if command -v git >/dev/null; then status Git OK "$(git --version | awk '{print $3}')"
else status Git MISSING; missing_pkgs=1; fi

py_ok=0
if command -v python3 >/dev/null; then
    pv="$(python3 -c 'import sys; print("%d.%d" % sys.version_info[:2])')"
    if python3 -c "import sys; sys.exit(0 if sys.version_info[:2] >= tuple(map(int, '$min_python'.split('.'))) else 1)"; then
        if python3 -c 'import venv, ensurepip' 2>/dev/null; then status Python OK "$pv"; py_ok=1
        else status Python MISSING "$pv found, but the venv module is missing"; missing_pkgs=1; fi
    else status Python OLD "found $pv; need $min_python+"; missing_pkgs=1; fi
else status Python MISSING "need $min_python+"; missing_pkgs=1; fi

have_tag="$(checkout_tag "$idf_dir")"
source_ok=0; [ "$have_tag" = "$idf_version" ] && source_ok=1
tools="${IDF_TOOLS_PATH:-$HOME/.espressif}"
mm="$(echo "$idf_version" | sed -n 's/^v\([0-9]*\.[0-9]*\).*/\1/p')"
tools_ok=0
if [ "$source_ok" = 1 ] && [ -d "$tools/tools/xtensa-esp-elf" ] && compgen -G "$tools/python_env/idf${mm}_py*" >/dev/null; then tools_ok=1; fi

if [ "$source_ok" = 1 ]; then status "ESP-IDF $idf_version" OK "$idf_dir"
elif [ -n "$have_tag" ]; then status "ESP-IDF $idf_version" OLD "$idf_dir is at $have_tag"
else status "ESP-IDF $idf_version" MISSING "$idf_dir"; fi
if [ "$tools_ok" = 1 ]; then status "esp32s3 toolchain" OK; else status "esp32s3 toolchain" MISSING; fi

next_steps() {
    echo
    echo "${green}ESP-IDF is ready. In each new shell, activate it, then build:${off}"
    echo "    . \"$idf_dir/export.sh\""
    echo "    cd firmware && idf.py build"
}

if [ "$source_ok" = 1 ] && [ "$tools_ok" = 1 ] && [ "$missing_pkgs" = 0 ]; then next_steps; exit 0; fi

if [ "$missing_pkgs" = 1 ]; then
    echo
    echo "Install the missing prerequisites with your package manager, then run this again. For example:"
    case "$(uname -s)" in
        Darwin) echo "    brew install git python cmake ninja dfu-util" ;;
        *) echo "    Debian/Ubuntu: sudo apt install git wget flex bison gperf python3 python3-venv cmake ninja-build ccache libffi-dev libssl-dev dfu-util libusb-1.0-0"
           echo "    Fedora:        sudo dnf install git wget flex bison gperf python3 cmake ninja-build ccache dfu-util libusbx"
           echo "    Arch:          sudo pacman -S --needed git wget flex bison gperf python cmake ninja ccache dfu-util libusb" ;;
    esac
    exit 1
fi

[ "$check_only" = 1 ] && exit 1

if [ -n "$have_tag" ] && [ "$source_ok" = 0 ]; then
    echo "${yellow}$idf_dir exists but is at $have_tag. Move or delete it, then run this again.${off}"
    exit 1
fi

echo
echo "  To set up:"
[ "$source_ok" = 0 ] && echo "    - Clone ESP-IDF $idf_version into $idf_dir (about 1 GB)"
echo "    - Run ESP-IDF's install.sh for esp32s3 (downloads its toolchain into $tools)"
confirm "  Go ahead?" || { echo "  Skipped. Nothing was changed."; exit 1; }

if [ "$source_ok" = 0 ]; then
    mkdir -p "$idf_root"
    git clone -b "$idf_version" --depth 1 --recursive --shallow-submodules \
        https://github.com/espressif/esp-idf.git "$idf_dir" || fail "git clone of ESP-IDF $idf_version failed."
fi
"$idf_dir/install.sh" esp32s3 || fail "ESP-IDF install.sh failed."

next_steps
