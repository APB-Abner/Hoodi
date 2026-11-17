#!/usr/bin/env bash

set -euo pipefail
BASE_DIR="$(cd "$(dirname "$0")" && pwd)"
source "$BASE_DIR/lib/log.sh"

if [ "$(id -u)" -ne 0 ]; then
  echo "Execute com sudo: sudo bash $0"
  exit 1
fi

init_log "03_monitor"

# --- configurações ---
CONFIG_DIR="/etc/hoodi"
MONITOR_ENV="$CONFIG_DIR/monitor.env"
MONITOR_BIN="/usr/local/bin/hoodi-monitor"
SERVICE_FILE="/etc/systemd/system/hoodi-monitor.service"

install_monitor_script() {
  install -m 0755 "$BASE_DIR/monitor.sh" "$MONITOR_BIN"
}

create_monitor_config() {
  mkdir -p "$CONFIG_DIR"
  cat > "$MONITOR_ENV" <<EOF
CPU_LIMIT=80
MEM_LIMIT=80
DISK_LIMIT=80
INTERVAL=30
LOG_DIR=/var/log/hoodi
EOF
  chmod 644 "$MONITOR_ENV"
}

install_monitor_service() {
  cat > "$SERVICE_FILE" <<EOF
[Unit]
Description=hoodi monitor de CPU, memoria e disco
After=network.target
StartLimitIntervalSec=0

[Service]
Type=simple
EnvironmentFile=$MONITOR_ENV
ExecStart=$MONITOR_BIN
Restart=always
RestartSec=5
SyslogIdentifier=hoodi-monitor

[Install]
WantedBy=multi-user.target
EOF

  systemctl daemon-reload && \
  systemctl enable hoodi-monitor && \
  systemctl restart hoodi-monitor && \
  sleep 3 && \
  systemctl is-active hoodi-monitor
}

configure_logrotate() {
  cat > /etc/logrotate.d/hoodi <<EOF
/var/log/hoodi/*.log {
    daily
    rotate 14
    compress
    missingok
    notifempty
    copytruncate
}
EOF
  logrotate --debug /etc/logrotate.d/hoodi 2>&1 | tail -3
}

stage "Instalar script do monitor em $MONITOR_BIN"           install_monitor_script
stage "Criar configuração de limites ($MONITOR_ENV)"         create_monitor_config
stage "Instalar monitor como serviço systemd (Restart=always)" install_monitor_service
stage "Configurar rotação de logs (logrotate)"               configure_logrotate

log "Monitor ativo. Acompanhe com: sudo tail -f /var/log/hoodi/monitor.log"