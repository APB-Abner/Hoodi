# Segurança de dados pessoais — hoodi (Cybersecurity, GS 2026.2)

Módulo que mapeia dados pessoais (PII) no código do hoodi, rastreia o fluxo desses
dados até saídas inseguras (Taint Analysis), bloqueia vazamentos no CI, traduz cada
achado em um alerta claro para o cidadão com IA e consolida tudo no indicador
**Risco Cidadão** (0–100).

Responsável: Bia (RM554746). Todos os dados nos protótipos são **fictícios**.

## Mapa dos critérios

| Critério | Pts | Onde está |
|---|---|---|
| Robustez do motor de parsing PII | 3,0 | `rules/pii-detection.yml` (6 regras) + testes unitários `rules/*.cs` (`semgrep --test`, 14/14) |
| Eficácia do Taint Analysis | 3,0 | `rules/taint-pii-leak.yml` (8 regras) · protótipo vulnerável 10/10 vazamentos detectados · protótipo corrigido 0 · teste de mutação no código real · gate no CI |
| Inovação com IA na remediação | 2,0 | `scripts/ai_remediation.py` (Claude ou Llama local via Ollama, saída estruturada, cache, fallback) |
| Dashboard executivo (Risco Cidadão) | 2,0 | `dashboard/index.html` · prints em `evidence/dashboard-*.png` |

## Como funciona

```
código C#  ──►  Semgrep + regras do hoodi  ──►  findings (JSON)
                  │  pii-detection.yml   (ONDE o dado pessoal está)
                  │  taint-pii-leak.yml  (PARA ONDE ele flui)
                  ▼
           taxonomy.py  ── peso do dado × fator da saída ──►  Risco Cidadão 0–100
                  │
                  ├──► ai_remediation.py ── Claude ──► alerta ao cidadão + correção
                  └──► build_dashboard.py ──► dashboard/index.html + evidence/
CI (.github/workflows/security-pii.yml): severidade ERROR na API = build falha
```

### 1. Mapeamento de PII (`rules/pii-detection.yml`)

| Regra | O que detecta | Severidade |
|---|---|---|
| `hoodi-pii-inventory-property` | propriedades de entidades com dado pessoal (`Cpf`, `Email`, `SenhaHash`…) | INFO (inventário) |
| `hoodi-pii-inventory-record` | parâmetros de records/DTOs com dado pessoal | INFO (inventário) |
| `hoodi-pii-record-tostring-exposure` | record com senha/localização/CPF **sem `ToString()` próprio**: o C# gera um `ToString()` que imprime todos os campos, então logar o objeto vaza o dado | WARNING |
| `hoodi-pii-cpf-literal` | CPF formatado escrito em código/configuração | WARNING |
| `hoodi-pii-hardcoded-secret` | chave/senha/segredo fixo no código | ERROR |
| `hoodi-pii-query-string` | dado pessoal em parâmetro de URL (`?lat=`, `&cpf=`) | WARNING |

Categorias de dado, com peso para o cidadão (0–10): localização 10, senha 10, saúde 9,
token 9, CPF 8, contato 5, dispositivo 4, nome 3. A localização tem peso máximo porque,
no hoodi, ela revela onde a pessoa está no momento em que pede ajuda.

### 2. Taint Analysis (`rules/taint-pii-leak.yml`)

Uma regra por categoria de dado, todas com os mesmos sinks e sanitizadores (âncoras YAML):

- **Sources:** campos e variáveis cujo nome indica dado pessoal (`entrada.Senha`, `latitude`,
  `refreshToken`, `PetId`…), inclusive dentro de lambdas (`rota.Select(p => p.Latitude)`).
- **Sinks (saídas inseguras):** `ILogger.Log*`, `Console/Debug/Trace.Write*`, `File.Write*/Append*`,
  `HttpClient.*Async` (exceto `https://exp.host/`, o Expo, único terceiro autorizado para push),
  `new *Exception(...)` e, para senha, a resposta HTTP (`Ok(...)`).
- **Sanitizers:** funções de hash, máscara, criptografia ou redução de precisão (`HashPassword`,
  `IDataProtector.Protect`, `MaskCpf`, `CoarseLocation`…) e comparações, que só devolvem
  verdadeiro ou falso.

O protótipo vulnerável simula os **eventos do Hoodi Pet** (o bichinho de bolso que vibra e muda de
carinha): o celular repassa ao servidor o botão de ajuda do Pet junto com a posição. O Pet não tem
GPS nem sensor de saúde; as regras de saúde continuam ativas como prevenção (LGPD art. 11).

Resultados (`evidence/taint-audit.md`):

| Alvo | Risco Cidadão | Exposições | Bloqueantes |
|---|---|---|---|
| Protótipo vulnerável (`fixtures/LeakyPrototype`) | 100 Crítico | 18 | 10 |
| Protótipo corrigido (`fixtures/SafePrototype`) | 0 Baixo | 0 | 0 |
| API hoodi (código real) | 19 Baixo | 11 latentes | 0 |
| Teste de mutação (cópia do `PanicoService` + 1 debug com localização) | 28 Moderado | 1 | 1 |

O código real da API **não tem vazamento ativo**: os logs registram só o tipo da exceção e o
`traceId`, a localização do pânico é cifrada com `IDataProtector.Protect` e o push ao Expo leva
só o ID do pedido. As 11 exposições são riscos latentes de `ToString()` em records como
`LoginEntrada`, `PanicoEntrada` e `PontoRota` (rotas). A correção recomendada é sobrescrever `PrintMembers`
nesses records.

O **teste de mutação** prova que as regras protegem o código real. O script copia o
`PanicoService.cs`, injeta um `Console.WriteLine` com a localização e roda o scan. O vazamento
é detectado e bloquearia o merge. O arquivo original não é alterado.

### 3. Bloqueio

`.github/workflows/security-pii.yml` roda em todo PR e em todo push na `develop`/`main`:
1. Instala o Semgrep na versão fixa.
2. Confirma que o protótipo vulnerável é bloqueado (prova de que o gate funciona).
3. Roda a auditoria completa e **falha o build se houver finding ERROR na API**.
4. Publica o dashboard e as evidências como artefato.

A evidência local do bloqueio está em `evidence/gate-blocks-leaky-prototype.txt`.

### 4. Tradução via IA (`scripts/ai_remediation.py`)

Para cada exposição, o script envia ao Claude (`claude-opus-5-5`) a regra, a categoria, a saída e
o trecho de código, e recebe um JSON validado (Pydantic):

- `titulo` e `alerta_cidadao`: português simples, sem jargão ("Sua localização exata pode
  ficar guardada…").
- `dano_real` e `gravidade`: o impacto concreto na vida da pessoa.
- `correcao` e `patch_csharp`: a correção para o time, usando os padrões do projeto.

Detalhes de engenharia:
- **Saída estruturada:** o formato vem sempre igual.
- **Cache por finding:** rodar de novo não gera custo.
- **Fallback no servidor:** se o classificador de segurança recusar o pedido, outro modelo
  responde.
- **Modo `--local` (sem custo):** o mesmo prompt e o mesmo JSON Schema vão para um modelo
  aberto (`llama3.1:8b`) rodando na própria máquina via [Ollama](https://ollama.com). Nada sai
  do computador. Foi o modo usado na entrega; o painel mostra o nome do modelo usado.
- **Modo `--offline`:** usa templates locais, marcados como "template (sem IA)" no painel.
- **Privacidade:** só código vai para a API (no modo local, nem isso sai da máquina).

### 5. Dashboard Risco Cidadão (`dashboard/index.html`)

- **Fórmula:** `pontos = peso do dado × fator da saída` e
  `Risco Cidadão = 100 × (1 − e^(−soma/15))`.
- **Níveis:** Baixo < 25, Moderado 25–49, Alto 50–74, Crítico ≥ 75.
- **O que o painel mostra:**
  - Um cartão por alvo (clique para trocar).
  - Risco por tipo de dado e por saída.
  - Os alertas da IA para o cidadão, com a correção técnica.
  - A tabela de achados.
  - O inventário de PII.
- **Formato:** abre direto no navegador, sem internet, com modo claro e escuro.

## Como rodar

```bash
bash security/scripts/setup.sh                 # cria security/.venv (Semgrep + SDK)
export ANTHROPIC_API_KEY=...                   # opcional: alertas via Claude
bash security/scripts/run_audit.sh             # ou --local (Ollama) ou --offline (sem IA)

# IA local sem custo (Ollama no Docker, só em 127.0.0.1):
docker run -d --name hoodi-ollama -p 127.0.0.1:11434:11434 -v hoodi-ollama:/root/.ollama ollama/ollama:0.12.6
docker exec hoodi-ollama ollama pull llama3.1:8b
bash security/scripts/run_audit.sh --local --refresh
xdg-open security/dashboard/index.html
bash security/scripts/package_delivery.sh      # ZIP de entrega em security/dist/
```

## Limitações conhecidas

- **Primary constructors:** o parser C# do Semgrep 1.185 não entende primary constructors
  (C# 12). Ele faz parse parcial: o corpo dos métodos é analisado, como prova o protótipo
  `LeakyRouteExporter`, mas a declaração da classe gera aviso de sintaxe.
- **Fontes pelo nome:** as fontes são identificadas pelo nome do campo. Um campo com nome
  genérico (`valor`, `dados`) não é reconhecido. Por isso existe o inventário: ele mostra os
  nomes usados no projeto.
- **Taint dentro do método:** no Semgrep Community, o taint é intraprocedural. Um dado que
  passa por vários métodos até chegar ao log pode não ser rastreado.
- **Sem trilha completa:** o JSON do Semgrep Community não traz a trilha completa
  fonte → sink. O relatório indica a fonte (nome do campo) e a linha do sink.
