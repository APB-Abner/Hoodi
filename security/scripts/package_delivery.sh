#!/usr/bin/env bash
# Gera o ZIP de entrega da disciplina de Cybersecurity:
#   - código do módulo (regras PII/taint, scripts, protótipos, workflow do CI)
#   - auditoria (evidence/: testes das regras, gate, taint-audit.md, alertas da IA, scores)
#   - dashboard (index.html + prints claro/escuro/celular)
# Uso: bash security/scripts/package_delivery.sh   -> security/dist/hoodi-cybersecurity.zip
set -euo pipefail
SECURITY_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
REPO_ROOT="$(cd "$SECURITY_DIR/.." && pwd)"
DIST="$SECURITY_DIR/dist"
DASHBOARD="$SECURITY_DIR/dashboard/index.html"
mkdir -p "$DIST"

if [[ ! -f "$DASHBOARD" ]]; then
    echo "Dashboard não encontrado. Rode antes: bash security/scripts/run_audit.sh"
    exit 1
fi

# Prints do painel (se houver Chrome instalado).
CHROME="$(command -v google-chrome || command -v chromium || command -v chromium-browser || true)"
if [[ -n "$CHROME" ]]; then
    shoot() {
        "$CHROME" --headless=new --disable-gpu --no-sandbox --hide-scrollbars --window-size="$1" \
            ${3:+--force-dark-mode --blink-settings=preferredColorScheme=0} \
            --screenshot="$SECURITY_DIR/evidence/$2" "file://$DASHBOARD" >/dev/null 2>&1
    }
    shoot 1280,1400 dashboard-light.png
    shoot 1280,1400 dashboard-dark.png dark
    shoot 390,1600 dashboard-mobile.png
    echo "Prints salvos em security/evidence/dashboard-*.png"
fi

python3 -I - "$REPO_ROOT" "$DIST/hoodi-cybersecurity.zip" <<'EOF'
import sys, zipfile
from pathlib import Path
root, out = Path(sys.argv[1]), Path(sys.argv[2])
skip = {".venv", "dist", "__pycache__"}
files = [p for p in (root / "security").rglob("*") if p.is_file() and not skip & set(p.relative_to(root).parts)]
files.append(root / ".github/workflows/security-pii.yml")
with zipfile.ZipFile(out, "w", zipfile.ZIP_DEFLATED) as z:
    for f in sorted(files):
        z.write(f, f.relative_to(root))
print(f"{out.relative_to(root)}: {len(files)} arquivos, {out.stat().st_size // 1024} KB")
EOF
