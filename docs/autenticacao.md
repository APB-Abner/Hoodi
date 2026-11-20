# Autenticação

## Operações

| Operação | Comportamento |
| --- | --- |
| `POST /v1/auth/registrar` | Recebe apenas o e-mail. Retorna 202 genérico e enfileira a confirmação se o e-mail for elegível. |
| `POST /v1/auth/confirmar-email` | Recebe token, nome e senha. Cria a conta após a prova de posse do e-mail. |
| `POST /v1/auth/login` | Verifica a senha e retorna access JWT e refresh token. |
| `POST /v1/auth/renovar` | Rotaciona o refresh token, com janela de replay de 45 segundos. |
| `POST /v1/auth/solicitar-redefinicao` | Retorna 202 genérico e enfileira a recuperação para conta elegível. |
| `POST /v1/auth/redefinir-senha` | Consome o token de recuperação, altera a senha e invalida as sessões anteriores. |
| `POST /v1/auth/sair` | Revoga a sessão atual e desativa o push da instalação informada, na mesma transação. |

## Tokens de e-mail

Confirmação e recuperação usam tokens distintos, de uso único e com validade de 30 minutos. Solicitar recuperação não altera a senha nem revoga sessões. O cadastro pendente pode reemitir o token após um minuto, invalidando o anterior. As senhas passam pelo `PasswordHasher` do framework, e os tokens ficam apenas como hash.

A mensagem de confirmação contém `hoodi://confirmar#token=…` e a de recuperação contém `hoodi://redefinir-senha#token=…`. O token vai no fragmento, nunca na query string.

O e-mail é gravado numa outbox cifrada, na mesma transação do pedido. O `EmailOutboxWorker` tenta o envio a cada cinco segundos. Falhas de decifragem preservam a mensagem e registram um erro genérico; restaurar as chaves corretas permite nova tentativa. Mensagens vencidas são removidas. O SMTP pode entregar a mesma mensagem duas vezes se houver falha entre o envio e a confirmação no banco.

## Refresh token e replay

O refresh token é trocado a cada renovação. Se a rede perder a resposta, o cliente repete o token antigo sem saber que ele já foi usado. Durante 45 segundos, a API devolve o mesmo par de tokens do primeiro sucesso, sem nova rotação. O mesmo vale para duas chamadas concorrentes.

Depois dos 45 segundos, reutilizar o token antigo revoga toda a família e exige novo login. A resposta de replay fica cifrada e é removida pelo worker na varredura seguinte ao fim da janela.

A sessão expira após sete dias sem renovação e nunca ultrapassa 30 dias desde o login. O JWT dura 60 minutos, mas a API também confere no banco se a sessão e o security stamp continuam válidos.

## Erros e limites

| Situação | Resposta |
| --- | --- |
| Entrada malformada | 400 |
| Autenticação inválida | 401 |
| `DomainException` | 409 com o código de domínio (`VINCULO_PROPRIO`: 422) |
| Excesso de tentativas | 429 |
| Falha inesperada | 500 com código genérico, sem detalhes da exceção |
| Dependência indisponível em `/health` | 503 |

- **Autenticação:** 30 requisições por minuto por IP. Cadastro, login e solicitação de recuperação também limitam a 5 por minuto por e-mail e operação.
- **Respostas genéricas:** cadastro e recuperação respondem em pelo menos 300 ms, o que reduz diferenças básicas de tempo, mas não comprova resistência a análise sob carga.
- **Escopo dos limites:** ficam em memória, valem para uma instância e reiniciam com o processo.

## SQLite e datas

O provedor SQLite não traduz ordenação nem comparações de `DateTimeOffset`. A autenticação filtra por identificadores e compara prazos em memória. Os convites gravam prazos como ticks UTC inteiros, o que permite filtrar a expiração no SQL.

Novas consultas por prazo devem usar uma representação UTC traduzível pelo provedor, com migration e testes no SQLite real.

`Usuario` e `VinculoContato` recebem `agoraUtc` explicitamente, e o serviço fornece o horário pelo `TimeProvider`. Conversores do EF mantêm a representação SQLite legada (`DateTime`/`TEXT`) desses dois tipos, interpretando os valores antigos como UTC sem reescrever os dados. Veja [UTC no domínio](utc-timestamps.md).
