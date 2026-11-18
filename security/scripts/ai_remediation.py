"""Tradução via IA: finding técnico -> alerta de risco real para o cidadão + correção.

Lê security/reports/semgrep-*.json, envia cada exposição (taint/estrutural) ao
Claude e grava security/evidence/ai-alerts.json.

- Usa saída estruturada (Pydantic) para o JSON vir sempre no mesmo formato.
- Guarda cache por finding: rodar de novo não gera custo para o que já foi traduzido.
- Sem credencial (ou com --offline) usa templates locais, marcados como "template".
- Só código vai para a API; os dados nos protótipos são fictícios.

Uso:
    python security/scripts/ai_remediation.py            # IA (ANTHROPIC_API_KEY)
    python security/scripts/ai_remediation.py --local    # IA local via Ollama (sem custo)
    python security/scripts/ai_remediation.py --offline  # sem IA
    python security/scripts/ai_remediation.py --refresh  # ignora o cache
"""
from __future__ import annotations

import argparse
import hashlib
import json
import os
import sys
import urllib.error
import urllib.request
from pathlib import Path
from typing import Literal

sys.path.insert(0, str(Path(__file__).resolve().parent))
from taxonomy import DATA_CATEGORIES, SINK_TYPES, load_target  # noqa: E402

SECURITY_DIR = Path(__file__).resolve().parents[1]
REPORT_DIR = SECURITY_DIR / "reports"
OUTPUT = SECURITY_DIR / "evidence" / "ai-alerts.json"
TARGETS = ["api", "prototipo-vulneravel", "prototipo-corrigido", "mutacao"]
MODEL = "claude-opus-5-5"

SYSTEM_PROMPT = """Você é analista de privacidade do hoodi, um app brasileiro que avisa pelo relógio \
ou celular quando a pessoa entra em área de risco e permite pedir ajuda a contatos de confiança. \
Os usuários são cidadãos comuns, muitos em situação de vulnerabilidade.

Você recebe um achado de análise estática (Semgrep) sobre dado pessoal chegando a uma saída insegura. \
Traduza o achado para duas audiências:

1. Cidadão: português simples, sem jargão (não use as palavras log, sink, taint, token, endpoint, \
hash, HTTP). Diga o que pode acontecer com a pessoa na vida real e por quê importa. Seja concreto e \
calmo, sem alarmismo nem promessas. 2 a 3 frases.
2. Desenvolvedor: a correção mínima, citando o padrão do projeto quando existir \
(IPasswordHasher para senha, IDataProtector.Protect para cifrar, ILogger com só identificadores \
técnicos, Expo como único terceiro autorizado para push). Inclua um trecho C# corrigido curto.

Se o achado for "risco latente" (record sem ToString próprio), deixe claro que hoje não há vazamento \
e que é prevenção. Use a gravidade proporcional ao dano real ao cidadão."""


def build_prompt(finding: dict) -> str:
    category = DATA_CATEGORIES[finding["category"]]
    sink = SINK_TYPES.get(finding["sink"] or "", {"label": "-"})
    return f"""Achado: {finding['rule']} (severidade Semgrep: {finding['severity']})
Mensagem da regra: {finding['message']}
Categoria do dado: {category['label']} — {category['why']}
Saída onde o dado chega: {sink['label']}
Campos de origem: {', '.join(finding['source_names']) or '-'}
Base legal citada: {finding['lgpd'] or '-'} | {finding['cwe'] or '-'}
Arquivo: {finding['path']}:{finding['line']}

Trecho (com contexto):
```csharp
{finding['context']}
```"""


def fingerprint(finding: dict) -> str:
    raw = f"{finding['rule']}|{finding['code']}|{finding['category']}|{finding['sink']}"
    return hashlib.sha256(raw.encode()).hexdigest()[:16]


# ---------------------------------------------------------------- modo offline
TEMPLATES = {
    "LOCALIZACAO": "Sua localização exata pode ficar guardada em lugares que não deveriam tê-la. "
                   "Quem tiver acesso a esses registros poderia descobrir onde você mora ou onde pediu ajuda.",
    "SENHA": "Sua senha pode ficar visível para quem não deveria vê-la. "
             "Com ela, outra pessoa conseguiria entrar na sua conta do hoodi.",
    "SAUDE": "Informações sobre a sua saúde, como os batimentos do relógio, podem ficar expostas. "
             "Esse tipo de dado tem proteção especial por lei.",
    "TOKEN": "Uma chave que mantém você conectado pode ficar exposta. "
             "Com ela, alguém poderia usar o app como se fosse você, sem saber sua senha.",
    "CPF": "Seu CPF pode ser enviado ou guardado sem proteção. "
           "CPF vazado é usado em golpes e fraudes em seu nome.",
    "CONTATO": "Seu e-mail ou telefone pode ficar exposto e ser usado para mensagens falsas que parecem do hoodi.",
    "DISPOSITIVO": "O identificador do seu relógio ou celular pode ficar exposto, permitindo acompanhar o aparelho ao longo do tempo.",
    "NOME": "Seu nome pode aparecer em registros internos sem necessidade.",
}
FIXES = {
    "LOG": "Registre só identificadores técnicos (traceId, tipo do erro) e nunca o valor do dado.",
    "CONSOLE": "Remova a saída de depuração; use ILogger sem dados pessoais.",
    "ARQUIVO": "Não grave dado pessoal em arquivo aberto; se precisar, cifre com IDataProtector.Protect.",
    "HTTP_TERCEIRO": "Não envie dado pessoal a terceiros; se for indispensável, envie no corpo, minimizado e com contrato.",
    "EXCECAO": "Não coloque dados do cidadão na mensagem de exceção; use um código de erro.",
    "RESPOSTA_HTTP": "Remova o campo da resposta da API.",
    "SEGREDO_NO_CODIGO": "Mova o segredo para variável de ambiente (/etc/hoodi/hoodi.env) e troque a credencial.",
    "URL": "Envie o dado no corpo de um POST, não na URL.",
    "DADO_FIXO": "Use dados fictícios gerados em teste, nunca CPF real no código.",
    "LATENTE_TOSTRING": "Sobrescreva ToString()/PrintMembers no record para omitir os campos sensíveis.",
}


def offline_alert(finding: dict) -> dict:
    latent = finding["sink"] == "LATENTE_TOSTRING"
    sink_label = SINK_TYPES[finding["sink"]]["label"]
    text = TEMPLATES[finding["category"]]
    if latent:
        text = "Hoje não há vazamento. " + text.split(". ")[0] + " se este objeto for registrado por engano no futuro."
    severity = {"ERROR": "alta", "WARNING": "media", "INFO": "baixa"}[finding["severity"]]
    if finding["category"] in ("SENHA", "LOCALIZACAO") and not latent and finding["severity"] == "ERROR":
        severity = "critica"
    return {
        "titulo": f"{DATA_CATEGORIES[finding['category']]['label']} em {sink_label[0].lower()}{sink_label[1:]}",
        "alerta_cidadao": text,
        "dano_real": (lambda why: why[0].upper() + why[1:] + ".")(DATA_CATEGORIES[finding["category"]]["why"]),
        "gravidade": "baixa" if latent else severity,
        "correcao": FIXES[finding["sink"]],
        "patch_csharp": "",
    }


# ---------------------------------------------------------------- formato do alerta
def alert_schema():
    """Formato único do alerta, usado tanto pelo Claude quanto pelo modelo local."""
    try:
        from pydantic import BaseModel, Field
    except ImportError:
        return None

    class CitizenAlert(BaseModel):
        titulo: str = Field(description="Título curto do alerta, até 70 caracteres, sem jargão")
        alerta_cidadao: str = Field(description="2 a 3 frases para o cidadão, linguagem simples")
        dano_real: str = Field(description="Exemplo concreto do dano na vida da pessoa, 1 frase")
        gravidade: Literal["baixa", "media", "alta", "critica"]
        correcao: str = Field(description="Correção para o desenvolvedor, 1 a 2 frases")
        patch_csharp: str = Field(description="Trecho C# corrigido, até 12 linhas")

    return CitizenAlert


# ---------------------------------------------------------------- modo IA local (Ollama)
# Sem custo e sem internet: o modelo roda na própria máquina. Nada sai do computador.
OLLAMA_URL = os.environ.get("OLLAMA_URL", "http://127.0.0.1:11434")
OLLAMA_MODEL = os.environ.get("OLLAMA_MODEL", "llama3.1:8b")

# Modelos pequenos precisam de regras mais explícitas que o Claude para manter o tom.
LOCAL_EXTRA_RULES = """

Regras de formato (obrigatórias):
- Responda sempre em português do Brasil.
- titulo: frase curta que cita o tipo de dado do achado e o que pode acontecer com a pessoa. \
Nunca comece com "Achado", "Alerta" ou "Análise".
- alerta_cidadao e dano_real: também não use as palavras API, HTTP, código, ToString, record, \
análise estática.
- patch_csharp: apenas código C# válido, sem crases e sem markdown."""


# Trava contra alucinação: o texto precisa falar do mesmo dado que a regra encontrou.
CATEGORY_KEYWORDS = {
    "LOCALIZACAO": ("localiza", "onde você", "endereço"),
    "SENHA": ("senha",),
    "SAUDE": ("saúde", "batimento", "cardíac"),
    "TOKEN": ("credencial", "chave", "token", "acesso", "sessão", "conectado"),
    "CPF": ("cpf",),
    "CONTATO": ("e-mail", "email", "telefone", "contato"),
    "DISPOSITIVO": ("dispositivo", "aparelho", "relógio", "celular", "identificador"),
    "NOME": ("nome",),
}


def mentions_category(text: str, category: str) -> bool:
    return any(word in text.lower() for word in CATEGORY_KEYWORDS[category])


def strip_fences(code: str) -> str:
    lines = [line for line in code.strip().splitlines() if not line.strip().startswith("```")]
    return "\n".join(lines).strip()


def local_available() -> bool:
    try:
        with urllib.request.urlopen(f"{OLLAMA_URL}/api/tags", timeout=5) as response:
            models = [m["name"] for m in json.load(response).get("models", [])]
    except (urllib.error.URLError, TimeoutError, ValueError):
        print(f"  Ollama não respondeu em {OLLAMA_URL}; usando templates.", file=sys.stderr)
        return False
    if OLLAMA_MODEL not in models:
        print(f"  modelo {OLLAMA_MODEL} não está baixado no Ollama; usando templates.", file=sys.stderr)
        return False
    return True


def ask_local(schema, finding: dict) -> dict | None:
    from pydantic import ValidationError

    body = json.dumps({
        "model": OLLAMA_MODEL,
        "messages": [
            {"role": "system", "content": SYSTEM_PROMPT + LOCAL_EXTRA_RULES},
            {"role": "user", "content": build_prompt(finding) + (
                f"\n\nO dado pessoal deste achado é: {DATA_CATEGORIES[finding['category']]['label']}. "
                "O título e o alerta ao cidadão devem falar desse dado, e não de outro.")},
        ],
        # Saída estruturada: o Ollama força a resposta a seguir o mesmo JSON Schema do Claude.
        "format": schema.model_json_schema(),
        "stream": False,
        "options": {"temperature": 0.2},
    }).encode()
    request = urllib.request.Request(f"{OLLAMA_URL}/api/chat", data=body,
                                     headers={"Content-Type": "application/json"})
    try:
        # Na CPU um alerta pode levar alguns minutos.
        with urllib.request.urlopen(request, timeout=900) as response:
            content = json.load(response)["message"]["content"]
        alert = schema.model_validate_json(content).model_dump()
    except (urllib.error.URLError, TimeoutError, ValueError, KeyError, ValidationError) as error:
        print(f"  modelo local falhou ({type(error).__name__}); usando template neste finding", file=sys.stderr)
        return None
    alert["patch_csharp"] = strip_fences(alert["patch_csharp"])
    category = finding["category"]
    if not mentions_category(alert["alerta_cidadao"], category):
        print("  modelo local falou de outro dado; usando template neste finding", file=sys.stderr)
        return None
    if not mentions_category(alert["titulo"], category):
        alert["titulo"] = offline_alert(finding)["titulo"]
    alert["_model"] = OLLAMA_MODEL
    return alert


# ---------------------------------------------------------------- modo IA (Claude)
def ai_client():
    try:
        import anthropic
    except ImportError:
        return None, None
    return anthropic, alert_schema()


def ask_claude(anthropic, schema, client, finding: dict) -> dict | None:
    try:
        response = client.beta.messages.parse(
            model=MODEL,
            max_tokens=4000,
            system=[{"type": "text", "text": SYSTEM_PROMPT, "cache_control": {"type": "ephemeral"}}],
            messages=[{"role": "user", "content": build_prompt(finding)}],
            output_format=schema,
            output_config={"effort": "medium"},
            # Se o classificador de segurança recusar (conteúdo "cyber"), o próprio
            # servidor tenta de novo num modelo de fallback.
            betas=["server-side-fallback-2026-07-01"],
            fallbacks="default",
        )
    except anthropic.AuthenticationError:
        print("  credencial inválida; usando templates", file=sys.stderr)
        raise
    except anthropic.RateLimitError:
        print("  limite de requisições atingido; usando template neste finding", file=sys.stderr)
        return None
    except anthropic.APIStatusError as error:
        print(f"  erro da API ({error.status_code}); usando template neste finding", file=sys.stderr)
        return None
    except anthropic.APIConnectionError:
        print("  sem conexão com a API; usando template neste finding", file=sys.stderr)
        return None

    if response.stop_reason == "refusal" or response.parsed_output is None:
        print(f"  resposta recusada/incompleta ({response.stop_reason}); usando template", file=sys.stderr)
        return None
    alert = response.parsed_output.model_dump()
    alert["_model"] = response.model
    return alert


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--offline", action="store_true", help="não chama a IA; usa templates")
    parser.add_argument("--local", action="store_true",
                        help="usa um modelo local via Ollama (sem custo), em vez do Claude")
    parser.add_argument("--refresh", action="store_true", help="ignora o cache e traduz tudo de novo")
    args = parser.parse_args()

    cache: dict[str, dict] = {}
    if OUTPUT.exists() and not args.refresh:
        for item in json.loads(OUTPUT.read_text(encoding="utf-8")).get("alerts", []):
            cache[item["fingerprint"]] = item

    findings = [f for target in TARGETS for f in load_target(REPORT_DIR, target) if f["kind"] != "inventory"]

    anthropic, schema, client, local = None, None, None, False
    model_used = "template"
    if args.local:
        schema = alert_schema()
        local = schema is not None and local_available()
        model_used = f"{OLLAMA_MODEL} (local, Ollama)" if local else model_used
    elif not args.offline:
        anthropic, schema = ai_client()
        has_credentials = any(os.environ.get(v) for v in ("ANTHROPIC_API_KEY", "ANTHROPIC_AUTH_TOKEN", "ANTHROPIC_PROFILE"))
        if anthropic and has_credentials:
            client = anthropic.Anthropic()
            model_used = MODEL
        else:
            print("  ANTHROPIC_API_KEY não definida: alertas gerados por template (sem IA).", file=sys.stderr)
    use_ai = local or client is not None

    alerts, used_ai, reused = [], 0, 0
    for finding in findings:
        key = fingerprint(finding)
        cached = cache.get(key)
        if cached and (cached["source"] == "ia" or not use_ai):
            alerts.append({**cached, "finding_id": finding["id"], "target": finding["target"]})
            reused += 1
            continue
        alert, source = None, "template"
        if local:
            alert = ask_local(schema, finding)
            if alert:
                source, used_ai = "ia", used_ai + 1
        elif client is not None:
            try:
                alert = ask_claude(anthropic, schema, client, finding)
            except anthropic.AuthenticationError:
                client = None
            if alert:
                source, used_ai = "ia", used_ai + 1
        alert = alert or offline_alert(finding)
        alerts.append({
            "fingerprint": key,
            "finding_id": finding["id"],
            "target": finding["target"],
            "source": source,
            **alert,
        })
        print(f"  [{source}] {finding['target']}: {finding['path']}:{finding['line']} -> {alert['titulo']}")

    OUTPUT.parent.mkdir(parents=True, exist_ok=True)
    OUTPUT.write_text(json.dumps({"model": model_used, "alerts": alerts}, ensure_ascii=False, indent=2) + "\n",
                      encoding="utf-8")
    print(f"  {len(alerts)} alertas ({used_ai} novos via IA, {reused} do cache) -> {OUTPUT.relative_to(SECURITY_DIR.parent)}")


if __name__ == "__main__":
    main()
