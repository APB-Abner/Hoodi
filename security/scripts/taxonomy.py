"""Taxonomia de PII do hoodi e cálculo do indicador "Risco Cidadão".

Usado por ai_remediation.py e build_dashboard.py. Sem dependências externas.

Fórmula (explicável em uma frase):
    risco de um finding = peso do DADO (0-10) x fator de EXPOSIÇÃO da saída (0-1)
    Risco Cidadão (0-100) = 100 x (1 - e^(-soma / 15))

A curva exponencial satura: um único vazamento real já leva o score a "Moderado",
vários vazamentos aproximam de 100, mas o valor nunca passa de 100.
"""
from __future__ import annotations

import json
import math
import re
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[2]

# Peso de cada categoria para a vida do cidadão (0-10) e rótulo para o painel.
DATA_CATEGORIES: dict[str, dict] = {
    "LOCALIZACAO": {"weight": 10, "label": "Localização", "why": "revela onde a pessoa está, mora ou pede ajuda"},
    "SENHA": {"weight": 10, "label": "Senha", "why": "dá acesso total à conta"},
    "SAUDE": {"weight": 9, "label": "Saúde", "why": "dado sensível (LGPD art. 11)"},
    "TOKEN": {"weight": 9, "label": "Token / credencial", "why": "permite agir em nome do cidadão"},
    "CPF": {"weight": 8, "label": "CPF", "why": "usado em fraudes e golpes"},
    "CONTATO": {"weight": 5, "label": "E-mail / contato", "why": "facilita phishing"},
    "DISPOSITIVO": {"weight": 4, "label": "Dispositivo", "why": "permite rastrear o aparelho"},
    "NOME": {"weight": 3, "label": "Nome", "why": "identifica a pessoa"},
}

# Fator de exposição de cada tipo de saída (sink).
SINK_TYPES: dict[str, dict] = {
    "HTTP_TERCEIRO": {"factor": 1.0, "label": "Envio a terceiros (HTTP)"},
    "RESPOSTA_HTTP": {"factor": 1.0, "label": "Resposta da API"},
    "SEGREDO_NO_CODIGO": {"factor": 0.9, "label": "Segredo no código"},
    "ARQUIVO": {"factor": 0.8, "label": "Arquivo em disco"},
    "LOG": {"factor": 0.7, "label": "Log da aplicação"},
    "EXCECAO": {"factor": 0.6, "label": "Mensagem de exceção"},
    "CONSOLE": {"factor": 0.5, "label": "Console / depuração"},
    "URL": {"factor": 0.5, "label": "Parâmetro de URL"},
    "DADO_FIXO": {"factor": 0.4, "label": "Dado pessoal fixo no código"},
    # Risco latente: não vaza hoje, mas vaza se alguém logar o objeto.
    "LATENTE_TOSTRING": {"factor": 0.03, "label": "Risco latente (ToString de record)"},
}

SCORE_SCALE = 15.0
LEVELS = [(75, "Crítico"), (50, "Alto"), (25, "Moderado"), (0, "Baixo")]

# Classificação por nome de campo (mesmos nomes das regras YAML).
NAME_PATTERNS: list[tuple[str, str]] = [
    ("SENHA", r"(?i)^(senha|novasenha|senhaatual|password|newpassword|passwd|pwd)$"),
    ("TOKEN", r"(?i)^(token|accesstoken|refreshtoken|jwt|bearer|pushtoken|expotoken|apikey|clientsecret|secret)$"),
    ("LOCALIZACAO", r"(?i)^(lat|lng|lon|latitude|longitude|coordenadas?|localizacao|location|geolocation|h3cell|h3index|celulah3|trajeto)$"),
    ("SAUDE", r"(?i)^(bpm|batimentos?|heartrate|frequenciacardiaca|spo2|oxigenacao|pressaoarterial|glicemia|saude|health)$"),
    ("CPF", r"(?i)^(cpf|rg|cnh|documento)$"),
    ("CONTATO", r"(?i)^(email|telefone|phone|celular)$"),
    ("DISPOSITIVO", r"(?i)^(deviceid|instalacaoid|installationid|imei|macaddress)$"),
    ("NOME", r"(?i)^(nome|titularnome)$"),
]
# Campos já protegidos (hash) contam no inventário, mas não como dado exposto.
PROTECTED_SUFFIX = re.compile(r"(?i)hash$")


def classify_name(name: str) -> str | None:
    base = PROTECTED_SUFFIX.sub("", name)
    for category, pattern in NAME_PATTERNS:
        if re.match(pattern, base):
            return category
    return None


def classify_sink(code: str) -> str:
    """Descobre o tipo de saída olhando o trecho de código do sink."""
    if re.search(r"\bnew\s+\w*Exception\s*\(", code):
        return "EXCECAO"
    if re.search(r"\.(Get|Post|Put|Patch|Send)\w*Async\s*\(", code):
        return "HTTP_TERCEIRO"
    if re.search(r"\bFile\.\w+\s*\(", code):
        return "ARQUIVO"
    if re.search(r"\b(Console|Debug|Trace)(\.Error)?\.Write", code):
        return "CONSOLE"
    if re.search(r"\b(Ok|Created|Json)\s*\(|Response\.Write", code):
        return "RESPOSTA_HTTP"
    if re.search(r"\.Log\w*\s*\(", code):
        return "LOG"
    return "LOG"


def _read_lines(path: str, start: int, end: int, context: int = 0) -> str:
    file = (REPO_ROOT / path) if not Path(path).is_absolute() else Path(path)
    try:
        lines = file.read_text(encoding="utf-8-sig").splitlines()
    except OSError:
        return ""
    lo = max(1, start - context)
    hi = min(len(lines), end + context)
    return "\n".join(lines[lo - 1:hi])


def normalize(semgrep_json: dict, target: str) -> list[dict]:
    """Converte a saída do Semgrep em findings do hoodi (deduplicados por linha)."""
    merged: dict[tuple, dict] = {}
    for result in semgrep_json.get("results", []):
        rule = result["check_id"].split(".")[-1]
        meta = result["extra"].get("metadata", {})
        path = result["path"]
        start, end = result["start"]["line"], result["end"]["line"]
        code = _read_lines(path, start, end)
        message = result["extra"]["message"]
        kind = "inventory" if meta.get("kind") == "inventory" else (
            "taint" if rule.startswith("hoodi-taint") else "structural")

        names: list[str] = []
        if kind == "taint":
            found = re.search(r"\((\w+)\)", message)
            names = [found.group(1)] if found else []
            category = meta.get("pii_category", "NOME")
            sink = "RESPOSTA_HTTP" if rule.endswith("in-response") else classify_sink(code)
        else:
            # Nome do parâmetro/propriedade: seguido de "," ")" "= padrão" ou "{ get".
            names = re.findall(r"\b(\w+)\s*(?:=\s*[^,)]+)?\s*(?:[,)]|\{\s*get)", code)
            names = [n for n in names if classify_name(n)]
            categories = [classify_name(n) for n in names]
            if rule == "hoodi-pii-hardcoded-secret":
                category, sink = "TOKEN", "SEGREDO_NO_CODIGO"
            elif rule == "hoodi-pii-cpf-literal":
                category, sink = "CPF", "DADO_FIXO"
            elif rule == "hoodi-pii-query-string":
                param = re.search(r"(?i)[?&](\w+)=", code)
                category, sink = (classify_name(param.group(1)) if param else None) or "LOCALIZACAO", "URL"
            else:
                # Inventário / ToString: a categoria mais pesada entre os campos encontrados.
                ranked = sorted({c for c in categories if c}, key=lambda c: -DATA_CATEGORIES[c]["weight"])
                category = ranked[0] if ranked else "NOME"
                sink = "LATENTE_TOSTRING" if rule == "hoodi-pii-record-tostring-exposure" else None

        key = (rule, path, start)
        if key in merged:
            merged[key]["source_names"] = sorted(set(merged[key]["source_names"]) | set(names))
            continue
        merged[key] = {
            "id": f"{rule}:{path}:{start}",
            "target": target,
            "rule": rule,
            "kind": kind,
            "severity": result["extra"]["severity"],
            "path": path,
            "line": start,
            "end_line": end,
            "code": code.strip(),
            "context": _read_lines(path, start, end, context=3),
            "message": message,
            "category": category,
            "sink": sink,
            "source_names": sorted(set(names)),
            "lgpd": meta.get("lgpd", ""),
            "cwe": meta.get("cwe", ""),
        }

    findings = list(merged.values())
    for item in findings:
        item["risk_points"] = finding_points(item)
    return findings


def finding_points(item: dict) -> float:
    if item["kind"] == "inventory" or not item.get("sink"):
        return 0.0
    weight = DATA_CATEGORIES[item["category"]]["weight"]
    return round(weight * SINK_TYPES[item["sink"]]["factor"], 2)


def citizen_risk(findings: list[dict]) -> dict:
    exposures = [f for f in findings if f["kind"] != "inventory"]
    raw = sum(f["risk_points"] for f in exposures)
    score = round(100 * (1 - math.exp(-raw / SCORE_SCALE)))
    level = next(label for limit, label in LEVELS if score >= limit)
    by_category: dict[str, float] = {}
    by_sink: dict[str, float] = {}
    for f in exposures:
        by_category[f["category"]] = round(by_category.get(f["category"], 0) + f["risk_points"], 2)
        by_sink[f["sink"]] = round(by_sink.get(f["sink"], 0) + f["risk_points"], 2)
    inventory: dict[str, int] = {}
    for f in findings:
        if f["kind"] == "inventory":
            for name in f["source_names"] or [""]:
                cat = classify_name(name) or f["category"]
                inventory[cat] = inventory.get(cat, 0) + 1
    return {
        "score": score,
        "level": level,
        "raw_points": round(raw, 2),
        "exposures": len(exposures),
        "blocking": sum(1 for f in exposures if f["severity"] == "ERROR"),
        "by_category": by_category,
        "by_sink": by_sink,
        "inventory": inventory,
    }


def load_target(report_dir: Path, target: str) -> list[dict]:
    file = report_dir / f"semgrep-{target}.json"
    if not file.exists():
        return []
    return normalize(json.loads(file.read_text(encoding="utf-8")), target)
