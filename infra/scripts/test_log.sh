#!/usr/bin/env bash
set -euo pipefail
BASE="$(cd "$(dirname "$0")" && pwd)"
source "$BASE/lib/log.sh"
init_log "test_log"

stage "Listar a pasta /etc" ls /etc
stage "Comando que falha"   ls /pasta/que/nao/existe
stage "Nunca deveria rodar" echo "oi"