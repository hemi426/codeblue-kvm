# Activates ESP-IDF for building the CodeBlue firmware, in the current shell.
# Source it from the repo root:   . scripts/activate-idf.sh [--idf-version vX.Y.Z] [--idf-root DIR]
#
# ESP-IDF's export.sh picks its Python environment from whichever python3 is
# first on PATH, which can differ from the one ESP-IDF was installed with.
# This finds the installed environment and points ESP-IDF at it directly
# (IDF_PYTHON_ENV_PATH), so PATH order doesn't matter.
# (No 'set -e' here: this file is sourced into your shell.)

_cb_root="$(cd "$(dirname "${BASH_SOURCE[0]:-$0}")/.." && pwd)"
_cb_ver=""; _cb_idf_root="$HOME/esp"
while [ $# -gt 0 ]; do
    case "$1" in
        --idf-version) _cb_ver="$2"; shift ;;
        --idf-root) _cb_idf_root="$2"; shift ;;
    esac
    shift
done
[ -n "$_cb_ver" ] || _cb_ver="$(head -n1 "$_cb_root/firmware/IDF_VERSION" | tr -d '[:space:]')"

_cb_idf="$_cb_idf_root/esp-idf-$_cb_ver"
if [ ! -f "$_cb_idf/export.sh" ] && [ -n "${IDF_PATH:-}" ] && [ -f "$IDF_PATH/export.sh" ]; then _cb_idf="$IDF_PATH"; fi

if [ ! -f "$_cb_idf/export.sh" ]; then
    echo "ESP-IDF $_cb_ver not found at $_cb_idf. Run scripts/bootstrap-unix.sh first." >&2
else
    _cb_mm="$(echo "$_cb_ver" | sed -n 's/^v\([0-9]*\.[0-9]*\).*/\1/p')"
    _cb_tools="${IDF_TOOLS_PATH:-$HOME/.espressif}"
    # Newest installed environment for this ESP-IDF version (names: idf<X.Y>_py<A.B>_env).
    _cb_env="$(ls -d "$_cb_tools/python_env/idf${_cb_mm}_py"*_env 2>/dev/null \
        | while read -r d; do [ -x "$d/bin/python" ] && echo "$d"; done | sort -V | tail -n1)"
    if [ -z "$_cb_env" ]; then
        echo "No installed ESP-IDF $_cb_mm Python environment under $_cb_tools. Run scripts/bootstrap-unix.sh first." >&2
    else
        export IDF_PYTHON_ENV_PATH="$_cb_env"
        . "$_cb_idf/export.sh"
    fi
fi
unset _cb_root _cb_ver _cb_idf_root _cb_idf _cb_mm _cb_tools _cb_env
