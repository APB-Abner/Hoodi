#!/usr/bin/env bash
LOG_DIR="/var/log/hoodi"
AUDIT_LOG="$LOG_DIR/audit.log"

init_log(){
    SCRIPT_NAME="$1"
    mkdir -p "$LOG_DIR"
    exec > >(tee -a "$LOG_DIR/$SCRIPT_NAME.log") 2>&1
    audit "INICIO" "executado por ${SUDO_USER:-$USER}"
}

log(){
    echo "[$(date '+%d/%m/%Y %H:%M:%S')] $*"
}

audit() {
    local status="$1" message="$2"
    echo "$(date '+%d/%m/%Y %H:%M:%S') | $SCRIPT_NAME | $status | $message" >> "$AUDIT_LOG"
    log "$status: $message"
}

stage(){
    local desc="$1"
    shift
    log ">> $desc"
    if "$@"; then
        audit "OK" "$desc"
    else
        audit "FAIL" "$desc"
        exit 1
    fi
}