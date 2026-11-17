#!/usr/bin/env bash

set -euo pipefail
BASE_DIR="$(cd "$(dirname "$0")" && pwd)"
source "$BASE_DIR/lib/log.sh"

if [ "$(id -u)" -ne 0 ]; then
  echo "Execute com sudo: sudo bash $0 api|monitor|disk"
  exit 1
fi

TEST_FILE="/var/tmp/hoodi-fault-test.img"
MONITOR_LOG="/var/log/hoodi/monitor.log"
DISK_LIMIT=80
MONITOR_INTERVAL=30

init_log "04_fault_test"

kill_service() {
  local service="$1" pid_before pid_after
  pid_before="$(systemctl show -p MainPID --value "$service")"
  log "$service rodando com PID $pid_before. Enviando kill -9..."
  kill -9 "$pid_before"

  log "Processo morto. Aguardando o systemd reiniciar..."
  sleep 8

  pid_after="$(systemctl show -p MainPID --value "$service")"
  systemctl status "$service" --no-pager | head -5

  if [ "$pid_after" != "0" ] && [ "$pid_after" != "$pid_before" ]; then
    log "RECUPERADO: PID $pid_before -> $pid_after"
    if [ "$service" = "hoodi" ]; then
      curl -fsS http://127.0.0.1/health && echo
    fi
    return 0
  fi
  log "O serviço NÃO voltou"
  return 1
}

fill_disk() {
  local used available target missing wait_time
  trap 'rm -f "$TEST_FILE"' EXIT

  read -r used available <<< "$(df -P --block-size=1M / | awk 'NR == 2 {print $3, $4}')"
  target=$(( (used + available) * (DISK_LIMIT + 3) / 100 ))
  missing=$(( target - used ))

  log "Disco antes do teste:"
  df -h /
  if [ "$missing" -gt 0 ]; then
    log "Criando arquivo de ${missing}MB para passar de ${DISK_LIMIT}%..."
    fallocate -l "${missing}M" "$TEST_FILE"
    df -h /
  else
    log "Disco já está acima de $((DISK_LIMIT + 3))%: não é preciso criar arquivo, só aguardar o alerta"
  fi

  wait_time=$(( MONITOR_INTERVAL + 5 ))
  log "Aguardando ${wait_time}s pela próxima medição do monitor..."
  sleep "$wait_time"

  log "Alertas registrados pelo monitor:"
  grep "ALERTA: Disco" "$MONITOR_LOG" | tail -3 && \
  journalctl -t hoodi-monitor -n 3 --no-pager || return 1

  rm -f "$TEST_FILE"
  log "Arquivo de teste removido. Disco depois da limpeza:"
  df -h /
}

case "${1:-}" in
  api)     stage "Teste de falha: matar a API (kill -9)"       kill_service hoodi ;;
  monitor) stage "Teste de falha: matar o monitor (kill -9)"   kill_service hoodi-monitor ;;
  disk)    stage "Teste de falha: disco acima de ${DISK_LIMIT}%" fill_disk ;;
  *)       echo "Uso: sudo bash $0 api|monitor|disk"; exit 2 ;;
esac