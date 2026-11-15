#!/usr/bin/env bash
# 01_infra.sh — provisiona a infraestrutura: pacotes, hostname, fuso, DNS e firewall
set -euo pipefail
BASE_DIR="$(cd "$(dirname "$0")" && pwd)"
source "$BASE_DIR/lib/log.sh"

if [ "$(id -u)" -ne 0 ]; then
  echo "Execute com sudo: sudo bash $0"
  exit 1
fi

init_log "01_infra"

# --- configurações ---
NEW_HOSTNAME="hoodi-srv"
TIMEZONE="America/Sao_Paulo"
DNS_SERVERS="168.63.129.16 1.1.1.1"   # DNS interno do Azure + Cloudflare

install_packages() {
  apt-get update && \
  DEBIAN_FRONTEND=noninteractive apt-get install -y curl git ufw unzip
}

configure_hostname() {
  hostnamectl set-hostname "$NEW_HOSTNAME" && \
  { grep -q "$NEW_HOSTNAME" /etc/hosts || echo "127.0.1.1 $NEW_HOSTNAME" >> /etc/hosts; }
}

configure_timezone() {
  timedatectl set-timezone "$TIMEZONE"
}

configure_dns() {
  mkdir -p /etc/systemd/resolved.conf.d

  cat > /etc/systemd/resolved.conf.d/hoodi.conf <<EOF
[Resolve]
DNS=$DNS_SERVERS
EOF

  systemctl restart systemd-resolved && \
  grep -q "^\[Resolve\]" /etc/systemd/resolved.conf.d/hoodi.conf && \
  getent hosts github.com
}

configure_firewall() {
  ufw default deny incoming && \
  ufw default allow outgoing && \
  ufw allow 22/tcp comment 'ssh' && \
  ufw allow 80/tcp comment 'nginx' && \
  ufw --force enable && \
  ufw status verbose
}

stage "Instalar pacotes base"       install_packages
stage "Configurar hostname"         configure_hostname
stage "Configurar fuso horário"     configure_timezone
stage "Configurar DNS"              configure_dns
stage "Configurar firewall (ufw)"   configure_firewall