# Implantação

A API roda como instância única, com banco SQLite e key ring fora do diretório de publicação. O provisionamento completo da VM por shell script está em [infra/README.md](../infra/README.md).

## Variáveis de ambiente

| Variável | Valor na VM |
| --- | --- |
| `ASPNETCORE_ENVIRONMENT` | `Production` |
| `ASPNETCORE_URLS` | `http://127.0.0.1:5000` |
| `Database__ConnectionString` | `Data Source=/var/lib/hoodi/hoodi.db` |
| `DataProtection__KeysPath` | `/var/lib/hoodi/keys` |
| `Jwt__SigningKey` | Base64 de pelo menos 32 bytes aleatórios, gerada e guardada apenas na VM |
| `Https__RedirectEnabled` | `true` por padrão; `false` somente no modo HTTP por IP |
| `Https__Port` | `443` por padrão |
| `Smtp__Host` / `Smtp__Port` | `127.0.0.1` / `1025` |
| `Rotas__ApiKey` | Chave privada do HeiGIT |
| `Risk__ImporterUserIds__0` | Id da conta autorizada a importar a grade |

- **Fora de Development:** a aplicação se recusa a iniciar sem chave JWT ou sem diretório de Data Protection, e indica o nome da configuração ausente. Chave malformada ou curta também é recusada, sem exibir seu valor.
- **Em Development:** as duas podem ser omitidas e são geradas em memória. Reiniciar invalida os JWTs emitidos e impede decifrar dados gravados antes.

## Arquivos de implantação

A pasta [`deploy`](../deploy) contém modelos que precisam ser adaptados ao ambiente real:

| Arquivo | Conteúdo |
| --- | --- |
| `hoodi.service` | Unidade systemd com reinício automático e `EnvironmentFile` |
| `hoodi.environment.example` | Modelo sem credenciais. Instalar como `/etc/hoodi/hoodi.env`, dono `root:root`, permissão `600` |
| `hoodi-mailpit.service` | Mailpit com SMTP e interface apenas em loopback |
| `nginx.conf.example` | Modo HTTPS, que exige domínio e certificado confiável |
| `nginx.http-demo.conf.example` | Modo de demonstração por IP na porta 80, sem redirecionamento para 443 |

Os modelos pressupõem:
- usuário e grupo `hoodi`;
- runtime ASP.NET Core 10 em `/usr/bin/dotnet`;
- publicação em `/opt/hoodi`;
- Mailpit em `/usr/local/bin/mailpit`.

```bash
dotnet publish Hoodi.Api -c Release -o artifacts/publish
```

A API aplica as migrations ao iniciar. Para atualizar, pare a instância anterior e preserve `/var/lib/hoodi`, com o banco e as chaves.

## Chaves e backups

Data Protection usa `PersistKeysToFileSystem` com o nome de aplicação estável `hoodi`. A chave JWT também precisa permanecer a mesma entre reinícios.

- **Permissões:** o diretório de chaves usa `700` e os arquivos `600`, acessíveis apenas ao usuário do serviço.
- **Cifragem em repouso:** no Linux, persistir as chaves em arquivo não as cifra automaticamente. A proteção depende dessas permissões e do armazenamento da VM.
- **Backups:** devem incluir banco, key ring e configuração privada, com acesso restrito.

## Mailpit

A interface do Mailpit não deve ser pública. Para consultá-la a partir de outra máquina, use um túnel SSH:

```bash
ssh -N -L 8025:127.0.0.1:8025 usuario@IP_DA_VM
```

Com o túnel aberto, acesse `http://127.0.0.1:8025`. O Mailpit apenas captura mensagens de demonstração e não substitui um provedor de e-mail.

## Modo de transporte

**HTTPS (recomendado).**
1. Aponte um domínio para a VM.
2. Emita um certificado confiável com um cliente ACME, como o [Let's Encrypt](https://letsencrypt.org/getting-started/).
3. Instale `nginx.conf.example`, ajustando domínio e caminhos dos certificados.
4. Mantenha `Https__RedirectEnabled=true`.

Certificado autoassinado não resolve a confiança do aplicativo.

**HTTP por IP (demonstração).**
1. Instale apenas `nginx.http-demo.conf.example`.
2. Defina `Https__RedirectEnabled=false` no EnvironmentFile.
3. Reinicie a API.

A base do app passa a ser `http://IP_DA_VM`, e o `ASPNETCORE_URLS` interno continua em loopback. HTTP não protege credenciais nem tokens em trânsito, então use apenas dados sintéticos. A build do aplicativo também precisa permitir tráfego HTTP para esse destino ([rede no React Native](https://reactnative.dev/docs/network)).

Não instale os dois modelos do nginx ao mesmo tempo.

## Proxy reverso e limites por cliente

`UseForwardedHeaders` executa antes do redirecionamento HTTPS, da autenticação e do rate limiter. Apenas `127.0.0.1` e `::1` são proxies confiáveis, com limite de um salto. O nginx deve sobrescrever `X-Forwarded-For` com o IP do cliente e `X-Forwarded-Proto` com o esquema externo.

Não exponha a porta interna 5000 nem habilite a opção que confia em todos os proxies. Cabeçalhos vindos de origens não confiáveis são ignorados, e assim os limites continuam aplicados por cliente.

Referências: [proxy e forwarded headers](https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/proxy-load-balancer?view=aspnetcore-10.0) e [persistência de chaves do Data Protection](https://learn.microsoft.com/en-us/aspnet/core/security/data-protection/configuration/overview?view=aspnetcore-10.0).
