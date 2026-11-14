# hoodi — API

Backend em .NET 10. A autenticação, contatos/convites e o registro de tokens Expo estão implementados, incluindo os patches v0.5.2 e v0.5.3 de convites e validação. Grade de risco, pedido de ajuda, rotas e endpoints de compatibilidade de dispositivos continuam pendentes. A decisão vigente da grade v0.6 é H3 resolução 9 (`h3_cell`), cinco faixas (`0–4`) e níveis de risco `0–3`, conforme o fechamento mais recente do Nando; veja [o contrato do snapshot v1](docs/risk-snapshot-v1-v0.6.md).

## Integrantes

| Integrante | RM | Responsabilidade prevista no documento consolidado |
| --- | --- | --- |
| Abner de Paiva Barbosa | RM558468 | API .NET, domínio, EF Core, migrations, contrato e testes |
| Beatriz Vieira de Novais | RM554746 | VM Linux, serviços, monitoramento e evidências de infraestrutura |
| Fernando Luiz Silva Antonio | RM555201 | Grade de risco e firmware do hoodi Pet |
| Mariana Neugebauer Dourado | RM550494 | App Expo, BLE e integração com a API |

## Executar localmente

```powershell
if (!(Test-Path Hoodi.Api/Properties/launchSettings.json)) { Copy-Item Hoodi.Api/Properties/launchSettings.example.json Hoodi.Api/Properties/launchSettings.json }
dotnet restore Hoodi.slnx
dotnet tool restore
dotnet run --project Hoodi.Api -- --environment Development
```

Os arquivos locais `appsettings*.json` e `launchSettings.json` não são versionados; os modelos sem segredos terminam em `.example.json`. As configurações operacionais também podem ser fornecidas por variáveis de ambiente.

Use a URL HTTPS exibida pela aplicação. O banco SQLite local é criado no diretório de trabalho e a migration inicial é aplicada ao iniciar. Os arquivos `*.local.db*` estão ignorados pelo Git.

- Saúde da API e conexão com o banco: `/health`.
- Documento OpenAPI gerado: `/openapi/v1.json`.
- Interface interativa Scalar: `/scalar`, disponível também em Production para a demo, usando o OpenAPI gerado pela API.

O envio de e-mail requer um servidor SMTP local, como Mailpit, em `127.0.0.1:1025`. Restrinja também a interface da caixa de entrada a localhost. O repositório não instala nem inicia esse servidor automaticamente.

## Configuração para VM — Bia

**O banco autorizado é SQLite, não PostgreSQL.** Para esta demo, provisionar uma única instância da API, com banco e chaves fora do diretório de publicação.

| Variável de ambiente | Configuração na VM |
| --- | --- |
| `ASPNETCORE_ENVIRONMENT` | `Production` |
| `ASPNETCORE_URLS` | `http://127.0.0.1:5000` |
| `Database__ConnectionString` | `Data Source=/var/lib/hoodi/hoodi.db` |
| `DataProtection__KeysPath` | `/var/lib/hoodi/keys` |
| `Jwt__SigningKey` | Base64 de pelo menos 32 bytes aleatórios, gerada e guardada privadamente na VM |
| `Https__RedirectEnabled` | `true` por padrão; `false` somente na demo HTTP descrita abaixo |
| `Https__Port` | `443` por padrão; alterável para uma porta HTTPS efetivamente configurada |
| `Smtp__Host` | `127.0.0.1` |
| `Smtp__Port` | `1025` |

Fora de Development, a aplicação recusa iniciar sem chave JWT ou diretório de Data Protection, indicando o nome da configuração ausente. Chave JWT malformada ou curta também é recusada, sem mostrar seu valor.

Em Development, ambas podem ser omitidas para usar chaves efêmeras; nesse caso reiniciar perde a validade dos JWTs e a capacidade de decifrar payloads anteriores. Para testar reinício local, configure as mesmas opções persistentes da VM.

### Arquivos de implantação

A pasta `deploy/` contém modelos para a Bia integrar ao provisionamento:

- `hoodi.service`: API com reinício automático, `EnvironmentFile` e diretório persistente.
- `hoodi.environment.example`: modelo sem credenciais reais; instalar como `/etc/hoodi/hoodi.env`, proprietário `root:root`, permissão `600`.
- `hoodi-mailpit.service`: Mailpit com SMTP e interface apenas em loopback e caixa persistente.
- `nginx.conf.example`: modo HTTPS; exige domínio real e certificado confiável já emitido.
- `nginx.http-demo.conf.example`: alternativa de demo com apenas IP e porta 80, sem redirecionamento para 443.

Os modelos pressupõem usuário/grupo `hoodi`, runtime ASP.NET Core 10 em `/usr/bin/dotnet`, publicação em `/opt/hoodi` e Mailpit previamente instalado em `/usr/local/bin/mailpit`. Substitua domínio e caminhos dos certificados nginx. Não execute os modelos sem adaptar ao provisionamento real.

```powershell
dotnet publish Hoodi.Api -c Release -o artifacts/publish
```

A API aplica migrations ao iniciar. Pare a instância anterior ao atualizar; preserve `/var/lib/hoodi`, seu banco e suas chaves. Não coloque esses arquivos junto dos binários substituídos.

Data Protection usa `PersistKeysToFileSystem` com nome de aplicação estável `hoodi`. A chave JWT também deve permanecer igual depois de reiniciar. O diretório de chaves precisa de permissão `700`, os arquivos `600`, acessíveis ao usuário do serviço. No Linux, persistir as chaves em arquivos não as cifra automaticamente em repouso: a proteção desta configuração depende dessas permissões e do armazenamento da VM. Backups precisam incluir banco, key ring e configuração privada, com acesso restrito.

A interface do Mailpit não deve ficar pública. Na máquina que exibirá a demo, abrir o túnel abaixo, substituindo usuário e IP pelos da VM:

```bash
ssh -N -L 8025:127.0.0.1:8025 usuario@IP_DA_VM
```

Manter o SSH aberto e acessar `http://127.0.0.1:8025` no navegador local. A Bia precisa disponibilizar o acesso SSH; não é necessário expor a porta 8025 na internet. Mailpit captura mensagens da demo e não substitui um provedor de e-mail para usuários reais.

### Escolher o modo de transporte antes da demo

**Preferido — domínio e HTTPS confiável:** apontar um subdomínio (por exemplo, um subdomínio DuckDNS) para a VM, emitir um certificado confiável com um cliente ACME/Let's Encrypt e só então instalar `nginx.conf.example`, substituindo domínio e caminhos dos certificados. `hoodi.example.invalid` é um placeholder e não funciona como domínio da demo. Manter `Https__RedirectEnabled=true`. Consulte o [guia oficial do Let's Encrypt](https://letsencrypt.org/getting-started/). Certificado autoassinado não resolve a confiança do app por si só.

**Alternativa — demo isolada apenas por IP:** instalar somente `nginx.http-demo.conf.example`, definir `Https__RedirectEnabled=false` no EnvironmentFile e reiniciar a API. A base do app será `http://IP_DA_VM`; não há certificado nem listener 443 nesse modo. O `ASPNETCORE_URLS` interno continua em loopback. Usar dados sintéticos: HTTP não protege credenciais e tokens em trânsito. Não instalar os dois modelos nginx simultaneamente.

Desligar o redirect no backend não altera as políticas de rede do aplicativo: a build de demonstração precisa permitir HTTP no alvo. A configuração fica com a Mari e requer teste no aparelho; veja a [documentação de rede do React Native](https://reactnative.dev/docs/network). A opção preferida continua sendo HTTPS com certificado confiável.

### nginx, HTTPS e limite por IP

`UseForwardedHeaders` executa antes do redirecionamento HTTPS, autenticação e rate limiter. Apenas `127.0.0.1` e `::1` são proxies confiáveis, com limite de um salto. O nginx deve sobrescrever `X-Forwarded-For` com o IP do cliente e `X-Forwarded-Proto` com o esquema externo.

Não exponha a porta interna 5000 nem habilite a variável que confia automaticamente em todos os proxies. Cabeçalhos recebidos diretamente de origens não confiáveis são ignorados. Assim a aplicação reconhece o HTTPS encerrado no nginx e o limite permanece por cliente.

## Fluxo de autenticação

| Operação | Comportamento |
| --- | --- |
| `POST /v1/auth/registrar` | Recebe apenas e-mail; retorna 202 genérico e enfileira confirmação se elegível. |
| `POST /v1/auth/confirmar-email` | Recebe token, nome e senha; cria conta após prova de posse do e-mail. |
| `POST /v1/auth/login` | Verifica senha e retorna access JWT e refresh token. |
| `POST /v1/auth/renovar` | Rotaciona refresh, preservando replay por 45 segundos. |
| `POST /v1/auth/solicitar-redefinicao` | Retorna 202 genérico; enfileira recuperação para conta elegível. |
| `POST /v1/auth/redefinir-senha` | Consome token de recuperação, altera senha e invalida sessões anteriores. |

Confirmação e recuperação usam tokens distintos, de uso único e validade de 30 minutos. Solicitar recuperação não modifica senha nem revoga sessão. O cadastro pendente pode reemitir token após intervalo de um minuto, invalidando o anterior. As senhas passam pelo `PasswordHasher` do framework e os tokens ficam como hashes nas tabelas de autenticação.

A mensagem de confirmação contém `hoodi://confirmar#token=…`; a de recuperação contém `hoodi://redefinir-senha#token=…`. O token está no fragmento, nunca na query string. A Mari precisa registrar o scheme `hoodi` no Expo e tratar os links; a abertura no aparelho ainda exige validação no app.

O e-mail fica numa outbox cifrada, gravada na mesma transação do pedido. O worker tenta enviar a cada cinco segundos. Falhas de decifragem preservam a mensagem e registram um erro genérico; restaurar as chaves corretas permite nova tentativa. Mensagens vencidas são removidas. SMTP pode entregar a mesma mensagem novamente se houver falha entre envio e confirmação no banco.

### Como explicar o replay na banca

O refresh token é trocado a cada renovação. Se a rede perder a resposta, o app repetirá o token antigo sem saber que ele já foi usado. Durante 45 segundos, a API devolve **o mesmo par de tokens** do primeiro sucesso, sem uma nova rotação. Isso também vale para duas chamadas concorrentes.

Depois dos 45 segundos, reutilizar o token antigo revoga sua família e exige novo login. O replay fica cifrado e é limpo pelo worker após a janela, na próxima varredura. A sessão expira após sete dias sem renovação e nunca ultrapassa 30 dias desde o login. O JWT dura 60 minutos, mas a API também verifica no banco se a sessão e o security stamp continuam válidos.

### Erros e limites

- Entrada malformada: 400; autenticação inválida: 401; excesso de tentativas: 429.
- `DomainException`: 409 com o código de domínio; a exceção explícita `VINCULO_PROPRIO` retorna 422.
- Falha inesperada: 500 com código genérico; detalhes da exceção não são enviados ao cliente.
- Falha de saúde da dependência: 503 em `/health`.
- Autenticação: 30 requisições por minuto por IP; cadastro, login e solicitação de recuperação também limitam cinco por minuto por e-mail e operação.

Emissão de convites permite cinco pedidos por minuto por usuário; resgate permite dez. Os limites em memória são para uma instância e reiniciam com o processo. Os prazos são parâmetros operacionais atuais, sujeitos ao fechamento do contrato. Cadastro e recuperação têm resposta genérica com piso de 300 ms; isso reduz diferenças básicas de tempo, sem comprovar resistência a análise sob carga.

### SQLite e datas

O provedor SQLite não traduz ordenação/comparações de `DateTimeOffset` como outros bancos. A autenticação mantém os filtros por identificadores e compara prazos em memória. Os novos convites convertem datas para ticks UTC inteiros, permitindo filtrar expiração diretamente no SQL. Para novas consultas por prazo, especialmente convites e grade, definir antes uma representação UTC traduzível pelo provedor, como ticks inteiros, com sua migration e testes. Não acrescentar `Where(x => x.ExpiraEmUtc < agora)` sem testar no SQLite real.

`Usuario` e `VinculoContato` agora recebem `agoraUtc` explicitamente; o serviço fornece o horário pelo `TimeProvider`. Os campos do domínio agora usam `DateTimeOffset` em UTC. Para `Usuario` e `VinculoContato`, conversores EF mantêm a representação SQLite legada como `DateTime`/`TEXT`, interpretando os valores antigos explicitamente como UTC, sem reescrever os dados.

## Validação e pendências

Última execução: 78 testes passaram. O modelo EF continua consistente com as migrations, e o validador do contrato passou em modo normal e com `python -O`. Os testes cobrem os módulos e os patches, incluindo normalização dos códigos e os modos HTTP/HTTPS. Evidência local: `artifacts/test-results/utc-refactor.trx` (ignorada pelo Git).

```powershell
dotnet test Hoodi.slnx --no-restore
dotnet ef migrations has-pending-model-changes --project Hoodi.Infrastructure --startup-project Hoodi.Api -- --environment Development
```

Os testes usam dados sintéticos, arquivos SQLite isolados, migration real e relógio controlável. Os testes adicionais recriam o host em Production com o mesmo banco/chaves, verificam JWT e replay após reinício, preservação de e-mail pendente, forwarded headers, limites por cliente e respostas 409/500.

A recriação de host automatizada não prova o teste de falha Linux. Ainda falta executar na VM o encerramento do processo e reinício pelo systemd, validar nginx/TLS real, permissões e envio pelo Mailpit. A integração dos deep links no app também permanece pendente.

A baseline v0.5.1 foi preservada. O patch v0.5.2 definiu o resgate de convites; a v0.5.3 acrescenta normalização e respostas 400. A v0.6 ainda precisa implementar o snapshot H3 resolução 9 definido no [registro do produtor](docs/risk-snapshot-v1-v0.6.md), usando o CSV completo, manifesto e fixture reais.

Referências de configuração: [proxy e forwarded headers](https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/proxy-load-balancer?view=aspnetcore-10.0) e [persistência de chaves do Data Protection](https://learn.microsoft.com/en-us/aspnet/core/security/data-protection/configuration/overview?view=aspnetcore-10.0).




## Contatos e tokens Expo

A migration incremental `ContactsAndPush` cria convites, vínculos e tokens push sem substituir a base de autenticação. O resgate segue a ordem aprovada na v0.5.2: código inválido/encerrado → 409; emissor do próprio código → 422 VINCULO_PROPRIO; rejeição → 204 sem vínculo, sem checar duplicidade; aceite → checagem de duplicidade e 200 com vínculo. Detalhes em [contrato dos módulos](docs/contacts-push-contract.md).

A validade padrão do convite é de 60 minutos, configurável por `Contatos__ValidadeConviteMinutos`. Emitir convites tem limite de cinco por minuto por usuário. Apenas vínculos aceitos autorizam o relacionamento A → B; revogados ficam no histórico.

Os endpoints de tokens Expo registram, atualizam, reativam e desativam associações autenticadas. Eles ainda não enviam notificações nem consultam receipts do Expo.


O [OpenAPI v0.5.3](contracts/openapi.yaml) preserva a [baseline v0.5.1](contracts/baseline/openapi.v0.5.1.yaml) e a [v0.5.2](contracts/baseline/openapi.v0.5.2.yaml), acrescentando normalização dos códigos e respostas 400 conforme o [changelog v0.5.3](docs/CHANGELOG_v0.5.3.md). O contrato completo inclui operações ainda pendentes; o Scalar mostra as implementadas.

Convites rejeitados deixam de aparecer na lista de pendentes do emissor. O contrato não promete esconder o encerramento do convite.

Validação do patch, com PyYAML e jsonschema disponíveis no ambiente: `python scripts/validate_contract_patch.py` (também executável com `python -O`).




### Compartilhar convites — Mari

O código canônico tem 64 caracteres hexadecimais em maiúsculas. O app monta `hoodi://convite#codigo=CODIGO_RECEBIDO`, compartilha o link ou o representa em QR. Não pedir digitação manual dos 64 caracteres. O receptor lê o fragmento, autentica-se e envia `codigo` e `decisao` no corpo do POST de resgate. O servidor aplica `Trim().ToUpperInvariant()` antes da validação e do hash; códigos malformados retornam 400 `DADOS_INVALIDOS`, enquanto código bem formado mas inexistente/encerrado continua retornando 409. O app ainda precisa implementar e testar esse deep link.

### Sair do app — integração atual e v0.6

Não há endpoint de logout nesta versão. Ao registrar o push, o app guarda o `id` retornado. Antes de apagar JWT, refresh e demais dados locais, chama `DELETE /v1/push/tokens/{id}` com o JWT ainda disponível e espera a desativação. Se apagar o JWT antes, não consegue autenticar esse DELETE. Sem confirmação do servidor, a saída local não garante desativar o push remoto. A troca de conta continua desativando a associação anterior do mesmo token Expo, mas não substitui esse passo de saída.

Para a v0.6, fica planejado `POST /v1/auth/sair`, revogando a sessão e desativando o push da instalação informada. Esse endpoint não foi implementado nem adicionado ao contrato v0.5.3. A validação ponta a ponta no app permanece com a Mari.




## Fluxo Git

A branch `develop` recebe as alterações e os testes. Após validação, integrar em `main` por fast-forward quando possível e publicar as duas branches. Os commits devem separar mudanças funcionais e manutenção, sem incluir configurações locais, segredos, bancos ou artefatos de build/teste.

Repositório da equipe: https://github.com/APB-Abner/Hoodi. Nunca usar force push para substituir um histórico remoto existente.

A padronização de datas e a compatibilidade com registros existentes estão descritas em [UTC no domínio](docs/utc-timestamps.md).
