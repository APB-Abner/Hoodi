# Integração com o aplicativo

Regras que o cliente móvel precisa seguir para consumir a API.

## Deep links

O aplicativo registra o scheme `hoodi`. Em todos os links, o token ou o código vai no **fragmento** (`#`), nunca na query string.

| Link | Origem | Ação no app |
| --- | --- | --- |
| `hoodi://confirmar#token=…` | E-mail de confirmação | Pedir nome e senha e chamar `POST /v1/auth/confirmar-email` |
| `hoodi://redefinir-senha#token=…` | E-mail de recuperação | Pedir a nova senha e chamar `POST /v1/auth/redefinir-senha` |
| `hoodi://convite#codigo=…` | Convite compartilhado ou QR code | Autenticar e chamar `POST /v1/contatos/convites/resgatar` com `codigo` e `decisao` |

## Convites de contato

O código canônico tem 64 caracteres hexadecimais em maiúsculas. O app o compartilha como link ou QR code e não pede digitação manual. O servidor aplica `Trim().ToUpperInvariant()` antes de validar e calcular o hash.

| Situação | Resposta |
| --- | --- |
| Código malformado | 400 `DADOS_INVALIDOS` |
| Código bem formado, mas inexistente ou encerrado | 409 |
| Emissor resgatando o próprio convite | 422 `VINCULO_PROPRIO` |
| Rejeição | 204, sem vínculo e sem checar duplicidade |
| Aceite | Checagem de duplicidade e 200 com o vínculo |

Convites rejeitados deixam de aparecer na lista de pendentes do emissor. A validade padrão é de 60 minutos (`Contatos__ValidadeConviteMinutos`). Só vínculos aceitos autorizam o relacionamento titular → contato. Veja o [contrato de contatos e push](contacts-push-contract.md).

## Tokens push e logout

- **Registro:** `PUT /v1/push/tokens` registra, atualiza ou reativa o token Expo de uma instalação (`instalacaoId`).
- **Escopo:** os endpoints de token não enviam notificações nem consultam receipts. O envio acontece no despacho do pânico.
- **Logout:** antes de apagar as credenciais locais, o app chama `POST /v1/auth/sair` com JWT válido e corpo `{"instalacaoId":"…"}`. A resposta 204 confirma, na mesma transação, a revogação da sessão atual e a desativação do push dessa instalação. As outras sessões e instalações continuam válidas, e o endpoint funciona mesmo sem push registrado.
- **Repetição do logout:** repetir com o JWT já revogado retorna 401, e esse retorno sozinho não comprova a desativação do push. Clientes da v0.5.3 usam o fluxo anterior, com `DELETE /v1/push/tokens/{id}` antes de limpar os tokens. Veja o [contrato do logout](../contracts/v0.6/logout.openapi.yaml).

## Exclusão de conta

`DELETE /v1/conta` exige JWT e corpo `{"senha":"…"}`. Por isso, a tela de confirmação precisa de um campo de senha.

| Situação | Resposta |
| --- | --- |
| Senha errada | 403 `SENHA_INVALIDA` |
| Senha correta | 204, com exclusão transacional |

Depois da exclusão, os contatos mantêm o histórico de pedidos como "Conta excluída", sem posição nem observação. Roteiro de teste: [exclusão pelo transporte real](account-delete-transport-test.md).

## Pedido de ajuda pelo hoodi Pet

O botão de ajuda do Pet chega ao app como evento BLE tipo 2 ([protocolo](../iot/hoodi-pet/docs/PROTOCOLO_BLE.md)). O app então chama `POST /v1/panico` com:

- uma `idempotencyKey` nova por pedido;
- a origem do pedido;
- a posição atual, quando disponível.

Repetir a mesma chave devolve o mesmo pedido com 200, sem duplicar o despacho. Veja [pânico e exclusão de conta](panic-account-v0.6.md).

## Grade de risco

O app consome a grade pelo [contrato H3](../contracts/v0.6/risk.openapi.yaml), que traz o CSV histórico completo em `GET /v1/risco/grade` e a consulta por célula em `GET /v1/risco/celulas/{h3Cell}?faixa=`. Antes da primeira importação, as leituras retornam 404 `SNAPSHOT_INDISPONIVEL`. Procedimento: [grade v2](risk-snapshot-v2-integration.md).

## Avaliação de rotas

`POST /v1/rotas/avaliar` recebe `origem`, `destino` e `saidaEm` (ISO 8601 com fuso). A geometria volta como lista de `{latitude, longitude}`, não como polyline codificada.

A resposta muda em relação à v0.5:
- a faixa horária é a da partida e vale para o trajeto inteiro;
- `tipoDia` não existe mais;
- o ranking usa a pontuação acumulada, e a resposta traz os campos de cobertura.

Veja o [contrato de rotas](../contracts/v0.6/routes.openapi.yaml) e os [critérios](routes-v0.6.md).
