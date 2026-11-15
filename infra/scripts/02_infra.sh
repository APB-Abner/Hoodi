#!/usr/bin/env bash

set -euo pipefail
BASE_DIR="$(cd "$(dirname "$0")" && pwd)"
source "$BASE_DIR/lib/log.sh"

if [ "$(id -u)" -ne 0 ]; then
  echo "Execute com sudo: sudo bash $0"
  exit 1
fi

init_log "02_app"

# --- configurações ---
REPO_URL="https://github.com/APB-Abner/Hoodi.git"
REPO_BRANCH="develop"
SRC_DIR="/opt/hoodi-src"            # código-fonte (git clone)
APP_DIR="/opt/hoodi"                # API compilada (dotnet publish)
APP_USER="hoodi"
CONFIG_DIR="/etc/hoodi"
ENV_FILE="$CONFIG_DIR/hoodi.env"
DOTNET_DIR="/usr/share/dotnet"

install_dependencies() {
  DEBIAN_FRONTEND=noninteractive apt-get install -y nginx sqlite3 libicu74 ca-certificates openssl
}

install_dotnet() {
  if [ -x "$DOTNET_DIR/dotnet" ] && "$DOTNET_DIR/dotnet" --list-sdks | grep -q "^10\."; then
    log ".NET 10 já instalado, pulando"
  else
    curl -fsSL https://dot.net/v1/dotnet-install.sh -o /tmp/dotnet-install.sh && \
    bash /tmp/dotnet-install.sh --channel 10.0 --install-dir "$DOTNET_DIR" || return 1
  fi
  ln -sf "$DOTNET_DIR/dotnet" /usr/bin/dotnet && \
  dotnet --version
}

install_mailpit() {
  if [ -x /usr/local/bin/mailpit ]; then
    log "Mailpit já instalado, pulando"
  else
    curl -fsSL https://raw.githubusercontent.com/axllent/mailpit/develop/install.sh -o /tmp/mailpit-install.sh && \
    bash /tmp/mailpit-install.sh || return 1
  fi
  /usr/local/bin/mailpit version
}

create_app_user() {
  if id "$APP_USER" &>/dev/null; then
    log "Usuário $APP_USER já existe, pulando"
  else
    useradd --system --no-create-home --shell /usr/sbin/nologin "$APP_USER"
  fi
}

fetch_source() {
  if [ -d "$SRC_DIR/.git" ]; then
    git -C "$SRC_DIR" fetch origin "$REPO_BRANCH" && \
    git -C "$SRC_DIR" reset --hard "origin/$REPO_BRANCH"
  else
    git clone --branch "$REPO_BRANCH" "$REPO_URL" "$SRC_DIR"
  fi
  git -C "$SRC_DIR" log -1 --oneline
}

build_api() {
  # pausa a API antes de trocar os binários (o banco e as chaves ficam em /var/lib/hoodi)
  systemctl stop hoodi 2>/dev/null || true
  export HOME=/root DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
  rm -rf "$APP_DIR" && \
  dotnet publish "$SRC_DIR/Hoodi.Api" -c Release -o "$APP_DIR" && \
  test -f "$APP_DIR/Hoodi.Api.dll"
}

create_env_file() {
  mkdir -p "$CONFIG_DIR"
  chmod 700 "$CONFIG_DIR"

  # a chave JWT precisa continuar IGUAL entre execuções: só gera se o arquivo ainda não existir
  if [ -f "$ENV_FILE" ]; then
    log "$ENV_FILE já existe, mantendo a chave JWT atual"
  else
    local jwt_key
    jwt_key="$(openssl rand -base64 32)"
    cat > "$ENV_FILE" <<EOF
Database__ConnectionString="Data Source=/var/lib/hoodi/hoodi.db"
DataProtection__KeysPath=/var/lib/hoodi/keys
Jwt__SigningKey=$jwt_key
Smtp__Host=127.0.0.1
Smtp__Port=1025
Https__RedirectEnabled=false
Https__Port=443
EOF
  fi

  chown root:root "$ENV_FILE" && \
  chmod 600 "$ENV_FILE"
}

# copia um arquivo removendo o BOM (caractere invisível que editores do Windows colocam no início)
copy_without_bom() {
  sed '1s/^\xEF\xBB\xBF//' "$1" > "$2"
}

install_services() {
  copy_without_bom "$SRC_DIR/deploy/hoodi.service"         /etc/systemd/system/hoodi.service && \
  copy_without_bom "$SRC_DIR/deploy/hoodi-mailpit.service" /etc/systemd/system/hoodi-mailpit.service || return 1

  # override: troca Restart=on-failure por Restart=always sem editar o arquivo do time
  mkdir -p /etc/systemd/system/hoodi.service.d
  cat > /etc/systemd/system/hoodi.service.d/override.conf <<EOF
[Unit]
StartLimitIntervalSec=0

[Service]
Restart=always
RestartSec=5
EOF

  systemctl daemon-reload && \
  systemctl enable --now hoodi-mailpit && \
  systemctl enable hoodi && \
  systemctl restart hoodi
}

configure_nginx() {
  copy_without_bom "$SRC_DIR/deploy/nginx.http-demo.conf.example" /etc/nginx/sites-available/hoodi && \
  ln -sf /etc/nginx/sites-available/hoodi /etc/nginx/sites-enabled/hoodi && \
  rm -f /etc/nginx/sites-enabled/default && \
  nginx -t && \
  systemctl enable nginx && \
  systemctl reload nginx
}

check_health() {
  local attempt
  for attempt in $(seq 1 30); do
    if curl -fsS http://127.0.0.1/health; then
      echo
      log "API respondendo pelo nginx (tentativa $attempt)"
      return 0
    fi
    sleep 2
  done
  log "API não respondeu. Últimos logs do serviço:"
  journalctl -u hoodi -n 30 --no-pager
  return 1
}

stage "Instalar dependências (nginx, sqlite, openssl)"  install_dependencies
stage "Instalar .NET 10"                                install_dotnet
stage "Instalar Mailpit"                                install_mailpit
stage "Criar usuário de serviço '$APP_USER'"            create_app_user
stage "Baixar código da API ($REPO_BRANCH)"             fetch_source
stage "Compilar e publicar a API em $APP_DIR"           build_api
stage "Criar arquivo de configuração e segredos"        create_env_file
stage "Instalar serviços systemd (API e Mailpit)"       install_services
stage "Configurar nginx (proxy porta 80 -> 5000)"       configure_nginx
stage "Verificar saúde da API (/health)"                check_health