#!/usr/bin/env bash
# Smoke test contra uma API em execução (local ou na VM). Uso: infra/scripts/06_smoke_api.sh [BASE_URL]
# Só leitura: não cria conta nem grava dados. Sai com código 1 se qualquer verificação falhar.
set -u
BASE="${1:-http://127.0.0.1:5000}"
falhas=0

ok()   { printf 'ok    %s\n' "$1"; }
falha() { printf 'FALHA %s\n' "$1"; falhas=$((falhas + 1)); }

codigo() { curl -s -o /dev/null -w '%{http_code}' --max-time 15 "$@"; }

[ "$(codigo "$BASE/health")" = "200" ] && ok "GET /health = 200" || falha "GET /health"

cab="$(curl -s -D - -o /dev/null --max-time 15 "$BASE/health" | tr -d '\r')"
for h in "x-content-type-options: nosniff" "x-frame-options: DENY" "referrer-policy: no-referrer" "cache-control: no-store"; do
  printf '%s\n' "$cab" | grep -qi "^$h" && ok "cabeçalho $h" || falha "cabeçalho $h ausente"
done

corpo_grade="$(curl -s -w '\n%{http_code}' --max-time 15 "$BASE/v1/risco/grade")"
cod_grade="${corpo_grade##*$'\n'}"
if [ "$cod_grade" = "200" ]; then ok "GET /v1/risco/grade = 200 (snapshot publicado)"
elif [ "$cod_grade" = "404" ] && printf '%s' "$corpo_grade" | grep -q SNAPSHOT_INDISPONIVEL; then
  printf 'aviso nenhum snapshot publicado: o app receberá SNAPSHOT_INDISPONIVEL até a importação (/v1/admin/risco/importar)\n'
else falha "GET /v1/risco/grade = $cod_grade"; fi
[ "$(codigo -X POST -H 'Content-Type: application/json' -d '{}' "$BASE/v1/rotas/avaliar")" = "401" ] && ok "POST /v1/rotas/avaliar sem token = 401" || falha "POST /v1/rotas/avaliar sem token deveria dar 401"
[ "$(codigo -X POST -H 'Content-Type: application/json' -d '{}' "$BASE/v1/panico")" = "401" ] && ok "POST /v1/panico sem token = 401" || falha "POST /v1/panico sem token deveria dar 401"
[ "$(codigo -H 'Origin: https://malicioso.invalid' "$BASE/health")" = "200" ] && ! curl -s -D - -o /dev/null -H 'Origin: https://malicioso.invalid' "$BASE/health" | grep -qi '^access-control-allow-origin' && ok "CORS fechado para origem desconhecida" || falha "CORS aberto para origem desconhecida"

if [ "$falhas" -eq 0 ]; then echo "smoke ok"; else echo "smoke com $falhas falha(s)"; exit 1; fi
