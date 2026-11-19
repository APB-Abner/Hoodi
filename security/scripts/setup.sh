#!/usr/bin/env bash
# Cria o ambiente Python isolado (security/.venv) com Semgrep e o SDK da Anthropic.
# Funciona mesmo sem o pacote python3-venv/ensurepip: reaproveita o pip do sistema.
set -euo pipefail
SECURITY_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
VENV="$SECURITY_DIR/.venv"

if [[ ! -x "$VENV/bin/python" ]]; then
    python3 -m venv "$VENV" 2>/dev/null || python3 -m venv --without-pip --system-site-packages "$VENV"
fi
"$VENV/bin/python" -m pip install --quiet -r "$SECURITY_DIR/requirements.txt"
"$VENV/bin/semgrep" --version
