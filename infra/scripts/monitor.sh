#!/usr/bin/env bash

set -uo pipefail

CPU_LIMIT="${CPU_LIMIT:-80}"
MEM_LIMIT="${MEM_LIMIT:-80}"
DISK_LIMIT="${DISK_LIMIT:-80}"
INTERVAL="${INTERVAL:-30}"
LOG_DIR="${LOG_DIR:-/var/log/hoodi}"
MONITOR_LOG="$LOG_DIR/monitor.log"

mkdir -p "$LOG_DIR"

write_log() {
  echo "[$(date '+%d/%m/%Y %H:%M:%S')] $*" >> "$MONITOR_LOG"
}

get_cpu_usage() {
  local user1 nice1 system1 idle1 iowait1 irq1 softirq1 steal1
  local user2 nice2 system2 idle2 iowait2 irq2 softirq2 steal2
  read -r _ user1 nice1 system1 idle1 iowait1 irq1 softirq1 steal1 _ < /proc/stat
  sleep 1
  read -r _ user2 nice2 system2 idle2 iowait2 irq2 softirq2 steal2 _ < /proc/stat

  local idle=$(( (idle2 + iowait2) - (idle1 + iowait1) ))
  local total=$(( (user2 + nice2 + system2 + idle2 + iowait2 + irq2 + softirq2 + steal2) \
                - (user1 + nice1 + system1 + idle1 + iowait1 + irq1 + softirq1 + steal1) ))
  if [ "$total" -gt 0 ]; then
    echo $(( 100 * (total - idle) / total ))
  else
    echo 0
  fi
}

get_memory_usage() {
  awk '/^MemTotal:/ {total = $2} /^MemAvailable:/ {available = $2}
       END {printf "%d\n", (total - available) * 100 / total}' /proc/meminfo
}

get_disk_usage() {
  df -P / | awk 'NR == 2 {gsub("%", "", $5); print $5}'
}

send_alert() {
  local resource="$1" value="$2" limit="$3"
  local message
  message="ALERTA: $resource em ${value}% (limite ${limit}%) no host $(hostname)"
  write_log "$message"
  logger -t hoodi-monitor -p user.warning "$message"
}

trap 'write_log "monitor encerrado (pid $$)"; exit 0' TERM INT

write_log "monitor iniciado (pid $$) | limites CPU=${CPU_LIMIT}% MEM=${MEM_LIMIT}% DISCO=${DISK_LIMIT}% | intervalo ${INTERVAL}s"

while true; do
  cpu="$(get_cpu_usage)"
  memory="$(get_memory_usage)"
  disk="$(get_disk_usage)"

  write_log "cpu=${cpu}% mem=${memory}% disco=${disk}%"

  if [ "$cpu" -ge "$CPU_LIMIT" ];     then send_alert "CPU"     "$cpu"    "$CPU_LIMIT";  fi
  if [ "$memory" -ge "$MEM_LIMIT" ];  then send_alert "Memoria" "$memory" "$MEM_LIMIT";  fi
  if [ "$disk" -ge "$DISK_LIMIT" ];   then send_alert "Disco"   "$disk"   "$DISK_LIMIT"; fi

  sleep "$INTERVAL" &
  wait $!
done