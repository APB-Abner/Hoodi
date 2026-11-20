# Auditoria de PII — Taint Analysis em ação

Gerado em 19/11/2026 10:21 por `security/scripts/run_audit.sh`.

| Alvo | Risco Cidadão | Nível | Exposições | Bloqueantes (ERROR) |
|---|---|---|---|---|
| API hoodi (código real) | 19/100 | Baixo | 11 | 0 |
| Protótipo vulnerável | 100/100 | Crítico | 18 | 10 |
| Protótipo corrigido | 0/100 | Baixo | 0 | 0 |
| Teste de mutação | 28/100 | Moderado | 1 | 1 |

## API hoodi (código real)

_Hoodi.Api, Application, Domain e Infrastructure_

| Arquivo:linha | Regra | Dado (fonte) | Saída (sink) | Pontos | Alerta ao cidadão |
|---|---|---|---|---|---|
| `Hoodi.Application/Auth/Contratos.cs:9` | `hoodi-pii-record-tostring-exposure` | Senha (Nome, Senha, Token) | Risco latente (ToString de record) | 0.3 | Se você compartilhar sua senha com alguém ou ela for interceptada, essa pessoa poderá acessar sua conta e fazer alterações sem sua permissão. |
| `Hoodi.Application/Auth/Contratos.cs:14` | `hoodi-pii-record-tostring-exposure` | Senha (Email, Senha) | Risco latente (ToString de record) | 0.3 | Se a senha da sua conta for acessada por alguém sem permissão, eles podem usar seus dados para roubar dinheiro ou causar danos à sua vida pessoal. |
| `Hoodi.Application/Auth/Contratos.cs:20` | `hoodi-pii-record-tostring-exposure` | Senha (NovaSenha, Token) | Risco latente (ToString de record) | 0.3 | Se a senha for comprometida, alguém poderá acessar sua conta e seus dados pessoais. Isso pode acontecer se o seu dispositivo ou aplicativo for hackeado. |
| `Hoodi.Application/Conta/Contratos.cs:7` | `hoodi-pii-record-tostring-exposure` | Senha (Senha) | Risco latente (ToString de record) | 0.3 | Se alguém tiver acesso a sua conta no Hoodi, ele poderá ver sua senha. Isso é um problema porque a senha é usada para proteger sua conta e dados pessoais. |
| `Hoodi.Application/Panico/Contratos.cs:7` | `hoodi-pii-record-tostring-exposure` | Localização (Latitude, Longitude) | Risco latente (ToString de record) | 0.3 | Se você usar o Hoodi para pedir ajuda em uma área de risco, é possível que a localização seja mostrada por acaso. Isso pode acontecer se alguém acessar informações sobre sua solicitação de ajuda. É importante manter suas informações seguras e evitar compartilhar detalhes sensíveis. |
| `Hoodi.Application/Panico/Contratos.cs:17` | `hoodi-pii-record-tostring-exposure` | Localização (Latitude, Longitude, TitularNome) | Risco latente (ToString de record) | 0.3 | Se você usar a aplicação em um lugar público ou compartilhar sua localização com alguém, é possível que a gente revele onde você está. Isso não acontece agora, mas é uma forma de proteção para o futuro. |
| `Hoodi.Application/Panico/PanicoEntradaConverter.cs:22` | `hoodi-pii-record-tostring-exposure` | Localização (Latitude, Longitude) | Risco latente (ToString de record) | 0.3 | Se você está em uma situação de risco, é importante que sua localização seja protegida para evitar que ela seja compartilhada sem seu consentimento. Se o Hoodi Pet ou o celular forem acessados por alguém mal-intencionado, a pessoa pode ser localizada e exposta a mais perigo. |
| `Hoodi.Application/Rotas/Contratos.cs:10` | `hoodi-pii-record-tostring-exposure` | Localização (Latitude, Longitude) | Risco latente (ToString de record) | 0.3 | Se você usar o Hoodi em um lugar público ou compartilhar sua localização com alguém, é possível que a aplicação revele onde você está. Isso não é uma violação de privacidade agora, mas pode acontecer se houver um problema no futuro. |
| `Hoodi.Infrastructure/Panico/Registros.cs:37` | `hoodi-pii-record-tostring-exposure` | Localização (Latitude, Longitude) | Risco latente (ToString de record) | 0.3 | Se você estiver em uma situação de risco, é importante que sua localização seja protegida para evitar que ela seja compartilhada sem consentimento. Se o app Hoodi precisar mostrar a localização, ele fará isso de forma segura. |
| `Hoodi.Application/Auth/Contratos.cs:18` | `hoodi-pii-record-tostring-exposure` | Token / credencial (RefreshToken) | Risco latente (ToString de record) | 0.27 | Se alguém tiver acesso a seu celular ou computador, eles podem ver sua senha ou token de autenticação se o aplicativo Hoodi for configurado para imprimir informações. Isso não significa que houve vazamento de dados agora, mas é um risco latente que pode ser evitado. |
| `Hoodi.Application/Auth/Contratos.cs:27` | `hoodi-pii-record-tostring-exposure` | Token / credencial (AccessToken, RefreshToken) | Risco latente (ToString de record) | 0.27 | Se você usar um aplicativo que guarda sua senha ou token em uma área de risco, como um parque público, e o aplicativo enviar informações para alguém sem querer, a pessoa poderia descobrir sua senha ou token. Isso pode permitir que ela faça coisas em seu nome. |

## Protótipo vulnerável

_Eventos do Hoodi Pet com vazamentos propositais_

| Arquivo:linha | Regra | Dado (fonte) | Saída (sink) | Pontos | Alerta ao cidadão |
|---|---|---|---|---|---|
| `security/fixtures/LeakyPrototype/LeakyTelemetryController.cs:46` | `hoodi-taint-location-leak` | Localização (Latitude, Longitude) | Envio a terceiros (HTTP) | 10.0 | Se a sua localização for enviada para um serviço externo sem reduzir a precisão, alguém poderá saber onde você mora ou trabalha. Isso é perigoso porque pode levar a situações de violência contra você. |
| `security/fixtures/LeakyPrototype/LeakyTelemetryController.cs:68` | `hoodi-taint-password-in-response` | Senha (-) | Resposta da API | 10.0 | Se a senha for compartilhada em uma resposta HTTP, ela pode ser vista por outros usuários ou até mesmo por hackers que estejam monitorando as comunicações. Isso significa que alguém poderia usar sua conta sem precisar saber sua senha real. |
| `security/fixtures/LeakyPrototype/LeakyRouteExporter.cs:13` | `hoodi-pii-hardcoded-secret` | Token / credencial (-) | Segredo no código | 8.1 | Se alguém tiver acesso ao seu repositório de código, eles poderão usar a sua credencial para agir em nome seu. Isso é um problema sério porque permite que essa pessoa faça coisas como pedir ajuda sem autorização ou acessar áreas restritas do app. |
| `security/fixtures/LeakyPrototype/LeakyRouteExporter.cs:19` | `hoodi-taint-location-leak` | Localização (H3Cell) | Arquivo em disco | 8.0 | Se a localização exata do seu celular for vazada, alguém poderá descobrir onde você mora ou trabalha. Isso pode aumentar o risco de segurança para você e sua família. |
| `security/fixtures/LeakyPrototype/LeakyTelemetryController.cs:49` | `hoodi-taint-cpf-leak` | CPF (Cpf, cpf) | Envio a terceiros (HTTP) | 8.0 | Seu CPF foi enviado para um terceiro sem segurança. Isso significa que alguém pode usar seu nome e número de identificação para abrir contas, fazer compras ou até mesmo pedir dinheiro emprestado em seu nome. É importante cuidar com esses dados sensíveis. |
| `security/fixtures/LeakyPrototype/LeakyTelemetryController.cs:38` | `hoodi-taint-location-leak` | Localização (Latitude, Longitude) | Log da aplicação | 7.0 | Se a localização exata do seu celular for gravada em um registro, alguém poderá saber onde você mora ou trabalha. Isso pode colocar sua segurança em risco. |
| `security/fixtures/LeakyPrototype/LeakyTelemetryController.cs:61` | `hoodi-taint-password-leak` | Senha (Senha) | Log da aplicação | 7.0 | Se alguém tiver acesso a esses logs, ele poderá usar sua senha para entrar na conta. Isso é um problema sério porque você está compartilhando informações importantes sobre si mesmo. |
| `security/fixtures/LeakyPrototype/LeakyRouteExporter.cs:22` | `hoodi-taint-token-leak` | Token / credencial (refreshToken) | Log da aplicação | 6.3 | Se alguém ler o log da aplicação, ele poderá usar seu token de sessão para acessar suas informações e até mesmo realizar ações em seu nome sem precisar da sua senha. Isso é um problema de segurança que pode colocá-lo em risco. |
| `security/fixtures/LeakyPrototype/LeakyTelemetryController.cs:45` | `hoodi-pii-query-string` | Localização (-) | Parâmetro de URL | 5.0 | Se você estiver compartilhando sua localização com um serviço externo, ela pode ser capturada por terceiros e usada para fins não autorizados. Isso pode incluir a identificação de seu endereço ou localização atual. |
| `security/fixtures/LeakyPrototype/LeakyTelemetryController.cs:65` | `hoodi-taint-cpf-leak` | CPF (Cpf) | Mensagem de exceção | 4.8 | Seu CPF foi enviado para uma saída insegura. Isso significa que alguém pode usar seu número de CPF para abrir contas ou cometer fraudes em seu nome. |
| `security/fixtures/LeakyPrototype/LeakyTelemetryController.cs:49` | `hoodi-taint-device-leak` | Dispositivo (PetId) | Envio a terceiros (HTTP) | 4.0 | Seu identificador do dispositivo (PetId) está sendo enviado para terceiros. Isso significa que é possível rastrear seu Hoodi Pet/celular ao longo do tempo, o que pode colocá-lo em situação de risco. |
| `security/fixtures/LeakyPrototype/LeakyTelemetryController.cs:52` | `hoodi-taint-cpf-leak` | CPF (Cpf) | Console / depuração | 4.0 | Seu CPF foi enviado para a saída de depuração do console. Isso significa que alguém pode acessar seu número de CPF, o que é usado em golpes e fraudes. Por isso, é importante proteger sua identidade online. |
| `security/fixtures/LeakyPrototype/LeakyTelemetryController.cs:61` | `hoodi-taint-contact-leak` | E-mail / contato (Email) | Log da aplicação | 3.5 | Seu email foi enviado para um log da aplicação sem proteção. Isso significa que alguém poderia usar seu endereço de email para enviar mensagens não solicitadas, como spam ou phishing. |
| `security/fixtures/LeakyPrototype/LeakyRouteExporter.cs:25` | `hoodi-pii-cpf-literal` | CPF (-) | Dado pessoal fixo no código | 3.2 | Seu CPF está escrito diretamente em um arquivo do repositório. Isso pode ser considerado como vazamento de dados pessoais. Se for apenas para testes, prefira usar geradores de CPF fictício. |
| `security/fixtures/LeakyPrototype/LeakyTelemetryController.cs:42` | `hoodi-taint-device-leak` | Dispositivo (PetId) | Log da aplicação | 2.8 | Seu identificador de dispositivo pode ser usado para rastrear sua localização ao longo do tempo. Isso significa que alguém pode saber onde você está e quando, o que pode ser preocupante se você estiver em situação de vulnerabilidade. |
| `security/fixtures/LeakyPrototype/LeakyRouteExporter.cs:8` | `hoodi-pii-record-tostring-exposure` | Localização (H3Cell, Latitude, Longitude) | Risco latente (ToString de record) | 0.3 | Se você usar o Hoodi em um lugar público, alguém poderia descobrir onde você mora ou pede ajuda se houver um problema. Isso acontece porque a localização é impressa quando o objeto for logado ou interpolado. |
| `security/fixtures/LeakyPrototype/LeakyTelemetryController.cs:14` | `hoodi-pii-record-tostring-exposure` | Localização (Cpf, Latitude, Longitude, PetId) | Risco latente (ToString de record) | 0.3 | Se houver um problema no app, sua localização pode ser mostrada para outras pessoas sem querer. Isso pode acontecer se o seu celular tiver algum problema ou se alguém tentar acessar informações que não devem ter acesso. |
| `security/fixtures/LeakyPrototype/LeakyTelemetryController.cs:20` | `hoodi-pii-record-tostring-exposure` | Senha (Cpf, Email, Nome, Senha) | Risco latente (ToString de record) | 0.3 | Se a sua senha for comprometida, você pode perder acesso à conta e ter problemas financeiros. |

## Protótipo corrigido

_Mesmo fluxo com sanitizadores_

Nenhuma exposição encontrada: os sanitizadores interromperam todos os fluxos.

## Teste de mutação

_Cópia do PanicoService com um debug esquecido_

| Arquivo:linha | Regra | Dado (fonte) | Saída (sink) | Pontos | Alerta ao cidadão |
|---|---|---|---|---|---|
| `security/reports/mutation/PanicoService.mutated.cs:18` | `hoodi-taint-location-leak` | Localização (Latitude, Longitude) | Console / depuração | 5.0 | Se a localização exata do seu celular for revelada, você pode estar exposto a riscos como assaltos ou sequestros. Isso acontece porque o app está enviando sua posição para a saída de depuração. |
