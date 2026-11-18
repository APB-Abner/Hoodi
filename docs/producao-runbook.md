# Runbook de produção da API hoodi

Complementa `infra/README.md`. Origem: revisão de segurança de 2026-11-17 (achados H-07 a H-14).

## Configuração de produção (`/etc/hoodi/hoodi.env`, modelo em `deploy/hoodi.environment.example`)

| Variável | Padrão | Produção real |
|---|---|---|
| `Docs__Enabled` | `true` (demo da GS usa o Scalar) | `false`: remove `/scalar` e `/openapi/*` |
| `Cors__AllowedOrigins__N` | vazio (CORS fechado) | só se houver cliente web; uma origem por linha |
| `Https__RedirectEnabled` | `true` | `true` |
| `Panico__RetencaoLocalizacaoHoras` | 24 | revisar com a equipe (1 a 720) |
| `Panico__RetencaoTitularHoras` | 72 | revisar com a equipe (1 a 720) |

O app nativo não usa CORS. O CORS só importa para a versão web.

## Limites de requisição

`/v1/auth/*`: 30/min por IP. `/v1/auth/renovar`: 15/min por IP (política `auth-renovar`). `/v1/rotas/*`: 15/min por usuário.
Atrás de NAT de operadora, vários usuários dividem o IP: se houver 429 legítimo, ajuste em `Program.cs`.

## Chaves: o que perder significa

| Segredo | Onde | Se for perdido |
|---|---|---|
| `Jwt__SigningKey` | `/etc/hoodi/hoodi.env` | todas as sessões caem; usuários precisam entrar de novo |
| Key ring do DataProtection | `/var/lib/hoodi/keys` | e-mails pendentes e tokens cifrados deixam de abrir |
| Banco SQLite | `/var/lib/hoodi/hoodi.db` (+ `-wal`, `-shm`) | perde contas, vínculos, tokens push |

Backup mínimo (diário, fora da VM, permissão 0600, cifrado):

```bash
sudo systemctl stop hoodi    # ou use sqlite3 ".backup" com a API no ar
sudo tar czf hoodi-backup-$(date +%F).tgz /var/lib/hoodi /etc/hoodi/hoodi.env
sudo systemctl start hoodi
```

Restauração: parar o serviço, extrair no mesmo caminho, conferir dono/permissões, subir o serviço e rodar `infra/scripts/06_smoke_api.sh`.
Esse procedimento ainda não foi ensaiado numa VM: teste a restauração antes de depender dele.

## Snapshot de risco

Num banco novo, `GET /v1/risco/grade` responde `404 SNAPSHOT_INDISPONIVEL` até a importação (`POST /v1/admin/risco/importar`).
O smoke test avisa quando isso acontece. Os hashes aprovados padrão são os do contrato v3 (`data/risk/v3/SOURCE.md`); os do v2 (`data/risk/v2/SOURCE.md`) servem para rollback via `Risk__Approved*`.

## Verificação após cada deploy

```bash
infra/scripts/06_smoke_api.sh https://SEU_HOST
```

Confere saúde, cabeçalhos de segurança, endpoints protegidos (401 sem token) e CORS fechado.

A versão da API vem de `<Version>` no `Hoodi.Api.csproj`, lida dos metadados do assembly por `ApiInfo`. `/health`, OpenAPI gerado e o contrato atual `contracts/openapi.yaml` usam essa versão. Validar a consolidação com `python scripts/validate_api_contract.py`; a v0.5.3 está preservada em `contracts/baseline/openapi.v0.5.3.yaml`.

## Pendências conhecidas (não resolvidas por código)

- Licença e termos de uso do dado SSP-SP: confirmar se a redistribuição agregada em H3 é permitida (dono: Fernando).
- Autenticação do frame BLE do Pet: protótipo, sem assinatura (dono: hardware/Fernando).
- Backup e restauração: procedimento descrito, não ensaiado.
