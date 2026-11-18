#!/usr/bin/env bash
# =============================================================================
# hoodi — auditoria de PII completa (Cybersecurity, GS 2026.2)
# Etapas:
#   1. Testes unitários das regras (semgrep --test)
#   2. Scan de 3 alvos: API real, protótipo vulnerável, protótipo corrigido
#   3. Teste de mutação: injeta um vazamento numa CÓPIA do PanicoService real
#   4. Tradução dos findings via IA (ai_remediation.py)
#   5. Dashboard "Risco Cidadão" (build_dashboard.py)
# Uso:  bash security/scripts/run_audit.sh            (IA se houver ANTHROPIC_API_KEY)
#       bash security/scripts/run_audit.sh --offline  (sem IA, alertas por template)
# =============================================================================
set -euo pipefail

SECURITY_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
REPO_ROOT="$(cd "$SECURITY_DIR/.." && pwd)"
REPORT_DIR="$SECURITY_DIR/reports"
EVIDENCE_DIR="$SECURITY_DIR/evidence"
VENV="$SECURITY_DIR/.venv"
SEMGREP="$VENV/bin/semgrep"
PYTHON="$VENV/bin/python"
RULES="$SECURITY_DIR/rules"

log() { printf '[%s] %s\n' "$(date '+%H:%M:%S')" "$*"; }

if [[ ! -x "$SEMGREP" ]]; then
    log "Ambiente não encontrado. Rode antes: bash security/scripts/setup.sh"
    exit 1
fi

mkdir -p "$REPORT_DIR" "$EVIDENCE_DIR"
cd "$REPO_ROOT"

# Semgrep sem telemetria e sem login.
SEMGREP_ARGS=(--config "$RULES" --metrics=off --disable-version-check --json --quiet)

log "1/5 Testes unitários das regras"
"$SEMGREP" --test "$RULES" --metrics=off 2>&1 | tail -3 | tee "$EVIDENCE_DIR/rule-tests.txt"

scan() {
    local target="$1"; shift
    "$SEMGREP" scan "${SEMGREP_ARGS[@]}" --exclude '**/Migrations/**' "$@" \
        > "$REPORT_DIR/semgrep-$target.json" 2>/dev/null || true
    log "    $target: $("$PYTHON" -c "import json,sys;print(len(json.load(open(sys.argv[1]))['results']))" \
        "$REPORT_DIR/semgrep-$target.json") resultados"
}

log "2/5 Scan dos alvos"
scan api Hoodi.Api Hoodi.Application Hoodi.Domain Hoodi.Infrastructure
scan prototipo-vulneravel security/fixtures/LeakyPrototype
scan prototipo-corrigido security/fixtures/SafePrototype

# Evidência do bloqueio: o mesmo comando do CI, rodado no protótipo vulnerável.
{
    echo "\$ semgrep scan --config security/rules --severity ERROR --error security/fixtures/LeakyPrototype"
    "$SEMGREP" scan --config "$RULES" --metrics=off --disable-version-check --severity ERROR --error \
        --text security/fixtures/LeakyPrototype 2>&1 | grep -E "❯❱|❱|┆|findings|blocking" || true
    "$SEMGREP" scan --config "$RULES" --metrics=off --disable-version-check --severity ERROR --error \
        --quiet security/fixtures/LeakyPrototype > /dev/null 2>&1 && echo "exit code: 0" || echo "exit code: $? (pipeline BLOQUEADO)"
} > "$EVIDENCE_DIR/gate-blocks-leaky-prototype.txt"

log "3/5 Teste de mutação no código real (cópia, o original não é alterado)"
MUTATION_DIR="$REPORT_DIR/mutation"
rm -rf "$MUTATION_DIR" && mkdir -p "$MUTATION_DIR"
cp Hoodi.Infrastructure/Panico/PanicoService.cs "$MUTATION_DIR/PanicoService.mutated.cs"
# Insere, logo no início de RegistrarAsync, um "debug esquecido" com a localização.
sed -i '/public async Task<PanicoRegistrado> RegistrarAsync/{n;a\
        Console.WriteLine($"[debug] panico de {usuarioId} em {entrada.Latitude},{entrada.Longitude}"); // MUTAÇÃO
}' "$MUTATION_DIR/PanicoService.mutated.cs"
scan mutacao "security/reports/mutation/PanicoService.mutated.cs"

log "4/5 Tradução dos findings via IA"
"$PYTHON" -I "$SECURITY_DIR/scripts/ai_remediation.py" "$@"

log "5/5 Dashboard Risco Cidadão"
"$PYTHON" -I "$SECURITY_DIR/scripts/build_dashboard.py"

# Gate de bloqueio: ERROR no código real = falha (o mesmo critério do CI).
BLOCKING=$("$PYTHON" -c "
import json,sys
d=json.load(open(sys.argv[1]))
print(sum(1 for r in d['results'] if r['extra']['severity']=='ERROR'))" "$REPORT_DIR/semgrep-api.json")
if [[ "$BLOCKING" -gt 0 ]]; then
    log "BLOQUEADO: $BLOCKING vazamento(s) de severidade ERROR no código da API."
    exit 2
fi
log "OK: nenhum vazamento bloqueante no código da API. Abra security/dashboard/index.html"
