#!/usr/bin/env bash

set -euo pipefail
BASE_DIR="$(cd "$(dirname "$0")" && pwd)"
source "$BASE_DIR/lib/log.sh"

if [ "$(id -u)" -ne 0 ]; then
  echo "Execute com sudo: sudo bash $0"
  exit 1
fi

init_log "00_bootstrap"
START_TIME=$(date +%s)

stage "Provisionar infraestrutura (01_infra.sh)"   bash "$BASE_DIR/01_infra.sh"
stage "Provisionar aplicação (02_infra.sh)"        bash "$BASE_DIR/02_infra.sh"
stage "Provisionar monitoramento (03_monitor.sh)"  bash "$BASE_DIR/03_monitor.sh"

ELAPSED=$(( $(date +%s) - START_TIME ))
audit "FIM" "VM provisionada do zero em ${ELAPSED}s"
log "API disponível em: http://$(curl -fsS -4 ifconfig.me 2>/dev/null || hostname -I | awk '{print $1}')/health"