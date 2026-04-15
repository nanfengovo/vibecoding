#!/bin/zsh
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "$0")/.." && pwd)"
VENV_DIR="$ROOT_DIR/.desktop-build-venv"

if [[ ! -d "$VENV_DIR" ]]; then
  python3 -m venv "$VENV_DIR"
fi

source "$VENV_DIR/bin/activate"

python -m pip install --upgrade pip
python -m pip install -r "$ROOT_DIR/desktop-requirements.txt" pyinstaller

pyinstaller \
  --noconfirm \
  --clean \
  --windowed \
  --name "Copper Pulse" \
  --distpath "$ROOT_DIR" \
  --workpath "$ROOT_DIR/build" \
  --specpath "$ROOT_DIR/build-spec" \
  --exclude-module tkinter \
  --collect-all yfinance \
  --collect-all curl_cffi \
  "$ROOT_DIR/desktop_app.py"

echo
echo "Build complete:"
echo "$ROOT_DIR/Copper Pulse.app"
