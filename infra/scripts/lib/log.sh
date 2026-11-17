#!/usr/bin/env bash
LOG_DIR="/var/log/hoodi"
AUDIT_LOG="$LOG_DIR/audit.log"

init_log(){
    SCRIPT_NAME="$1"
    mkdir -p "$LOG_DIR"
    exec > >(tee -a "$LOG_DIR/$SCRIPT_NAME.log") 2>&1
    audit "INICIO" "executado por ${SUDO_USER:-${USER:-$(whoami)}}"
}

log(){
    echo "[$(date '+%d/%m/%Y %H:%M:%S')] $*"
}

audit() {
    local status="$1" message="$2"
    echo "$(date '+%d/%m/%Y %H:%M:%S') | $SCRIPT_NAME | $status | $message" >> "$AUDIT_LOG"
    log "$status: $message"
}

# stage "descrição" comando args... -> roda, registra OK/FAIL, para se falhar
# A etapa roda num subshell com set -e próprio, FORA de um if: dentro de um if
# o Bash ignora o set -e, e uma função poderia falhar no meio e ainda dar OK.
stage(){
    local desc="$1" rc
    shift
    log ">> $desc"
    set +e
    ( set -e; "$@" )
    rc=$?
    set -e
    if [ "$rc" -eq 0 ]; then
        audit "OK" "$desc"
    else
        audit "FAIL" "$desc (código $rc)"
        exit "$rc"
    fi
}