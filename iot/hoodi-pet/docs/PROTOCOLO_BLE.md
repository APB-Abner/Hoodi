# Protocolo Bluetooth app ↔ hoodi Pet (versão 1)

O Pet só executa. A decisão de quando alertar fica no app (`app/regraAlerta.ts`). GPS, grade de risco e API ficam no celular, e o Pet nunca chama a API.

## Serviço GATT

| Item | UUID | Propriedades | Segurança |
|---|---|---|---|
| Serviço hoodi Pet | `a70ab40e-599a-4803-89db-3b1667e7d753` | anunciado | — |
| COMANDO | `ad7790b4-bf5d-4ee2-a6b2-5faac8195117` | write com resposta | cifrado + autenticado |
| STATUS | `147dca7f-bc45-4789-85bb-adc57bc40124` | read + notify | cifrado + autenticado |
| EVENTO | `6e9d9b1f-3aef-4015-a8ab-162399270a78` | read + notify | cifrado + autenticado |

O nome anunciado é `hoodi Pet XXXX`, em que XXXX vem do MAC. O nome vai na resposta de scan, porque o UUID de 128 bits ocupa o anúncio quase todo.

## Pareamento

- O Pet tem tela, mas não tem teclado (IO capability `DISPLAY_ONLY`). Usa bonding, proteção MITM e LE Secure Connections.
- Na primeira conexão, o sistema do celular pede um PIN de 6 dígitos. O PIN aparece no OLED e é aleatório a cada pareamento.
- O PIN só aparece com o Pet em **modo pareamento**. Esse modo abre:
  - sozinho, quando o Pet ainda não tem celular salvo;
  - quando a pessoa segura o botão 1 (tela) por 2 s ao ligar;
  - pelos comandos `parear` ou `esquecer` no Serial.

  Fora desse modo, ninguém vê o PIN, então nenhum aparelho por perto consegue parear.
- Quando um pareamento novo termina, o Pet apaga os outros celulares salvos (**um celular por Pet**) e o modo pareamento fecha.
- O modo pareamento também fecha sozinho após 2 min, desde que já exista um celular salvo.
- O Pet aceita uma conexão por vez.

## Frames (little-endian)

### COMANDO, app → Pet (9 bytes)

| Byte | Campo | Valores |
|---|---|---|
| 0 | versão | `1` |
| 1 | tipo | `1` Estado, `2` Identificar, `3` Silenciar, `4` FimTrajeto |
| 2–5 | sequência | u32, de 1 a 4294967295 |
| 6 | nível | `0`–`3`, ou `0xFF` desconhecido (só no Estado) |
| 7 | padrão | `0` nenhum, `1` curto, `2` duplo, `3` longo, `0xFF` automático (só no Estado) |
| 8 | expressão | `0` tranquila, `1` atenta, `2` preocupada, `3` alerta, `4` desconhecido, `5` dormindo, `0xFF` automática (só no Estado) |

- **Automático:**
  - padrão: nível 0 = nenhum, 1 = curto, 2 = duplo, 3 = longo;
  - expressão: segue o nível.
- **Nível desconhecido nunca vibra**, mesmo que o padrão peça vibração. A tela mostra "?".
- **Nos tipos 2 a 4**, os bytes 6–8 são ignorados (mandar 0).
  - Identificar vibra rápido 5 vezes, pra achar o Pet.
  - Silenciar corta a vibração e o som em andamento.
  - FimTrajeto mostra a carinha dormindo.
- **Para só trocar a carinha sem vibrar** (por exemplo, quando o nível desce), mandar Estado com padrão `0`.

Exemplo: `01 01 01000000 02 02 02` é Estado, sequência 1, nível 2, vibração dupla, carinha preocupada.

### STATUS, Pet → app (9 bytes; notify depois de cada comando e quando o celular desconecta)

| Byte | Campo |
|---|---|
| 0 | versão (`1`) |
| 1 | resultado do último comando: `0` aplicado, `1` sequência antiga (ignorado), `2` inválido, `0xFF` nenhum ainda |
| 2–5 | sequência do comando avaliado (no frame inválido, a sequência lida, se o tamanho estava certo) |
| 6 | nível exibido (`0xFF` = desconhecido) |
| 7 | expressão exibida (inclui as internas: `6` sem celular, `7` pareamento) |
| 8 | flags: bit0 vibrando, bit1 tela ligada, bit2 trajeto ativo |

### EVENTO, Pet → app (6 bytes, notify)

| Byte | Campo |
|---|---|
| 0 | versão (`1`) |
| 1 | `1` clique no botão 1 (tela); `2` pressão longa de 3 s no botão 3 (ajuda), com 1 vibração curta de confirmação |
| 2–5 | contador u32 (cresce a cada evento; zera quando o Pet reinicia) |

O app decide o que cada evento faz. O evento `2` é o pedido de ajuda: botão 3 → BLE → app → `POST /v1/panico`, com a autenticação e a idempotência do app.

- O botão 2 (silenciar) **não** gera evento. Ele corta a vibração e o som no próprio Pet, igual ao comando Silenciar.
- Sem celular conectado, o Pet não manda o evento `2`. Ele vibra longo + curto e mostra "Sem celular / ajuda nao enviada", para a pessoa saber que o pedido não saiu.
- Os bytes não mudaram em relação à versão anterior do firmware: o protocolo continua na versão 1.

## Regras

1. **Sequência por conexão.** O Pet zera a sequência a cada conexão, e o app recomeça em 1. O Pet ignora o comando cuja sequência for menor ou igual à última aceita; pular números é permitido.
2. **Celular desconectou.** O Pet não sabe mais o nível, então mostra "sem celular" (ou dormindo, se não havia trajeto). Se a conexão caiu durante um trajeto e ficou 20 s sem voltar, vibra uma vez no padrão de perda de conexão (longo e depois curto).
3. **Reconectou.** O app reenvia o último nível **sem vibrar** (o `hoodiPetBle.ts` já faz isso).
4. **Tela.** A tela apaga 10 s depois da última novidade. Um clique no botão 1 ou no 2 acende de novo. No modo pareamento, a tela fica acesa.
5. **Regra de alerta, no app** (`regraAlerta.ts`):
   - alerta no início do trajeto se o nível já for 2 ou 3;
   - depois, só quando o nível sobe, com intervalo mínimo de 2 min. Se a subida for bloqueada pelo intervalo, ela fica pendente e vibra quando o intervalo acaba;
   - histerese de 30 m ao trocar de célula;
   - mudança de faixa horária na mesma célula vale na hora;
   - "sem dado" aparece na hora e nunca vibra. Ao voltar dele, a comparação é com o último nível conhecido.

## App (Expo)

- **Ambiente:** precisa de development build (`npx expo prebuild` ou EAS), porque o Expo Go não tem Bluetooth.
- **Biblioteca:** `react-native-ble-plx` 3.x, com o plugin no `app.json`:
  ```json
  ["react-native-ble-plx", { "isBackgroundEnabled": true, "modes": ["central"],
    "bluetoothAlwaysPermission": "O hoodi usa Bluetooth para avisar o hoodi Pet." }]
  ```
- **Permissões:** no Android 12+, pedir em tempo de execução `BLUETOOTH_SCAN` e `BLUETOOTH_CONNECT` (e localização no Android ≤ 11).
- **Arquivos:**
  - `hoodiPetProtocolo.ts`: codec, sem dependências.
  - `regraAlerta.ts`: decide quando vibrar.
  - `hoodiPetBle.ts`: conexão, pareamento e reconexão.
- **Teste obrigatório:** o fluxo inteiro com o celular bloqueado. No iPhone, o segundo plano não é garantido, então a demo é mais segura no Android.

Uso mínimo:

```ts
const pet = new HoodiPet({ conexao: setConexao, evento: (e) => { /* clique / pressão longa */ } });
const [primeiro] = await pet.procurar();
await pet.conectar(primeiro.id);             // 1ª vez: digitar o PIN do OLED
const regra = new RegraAlerta();
regra.iniciarTrajeto();
// a cada posição do GPS:
const d = regra.avaliar({ celula, nivel, lat, lon, agoraMs: Date.now() });
if (d) await pet.enviarEstado(d.nivel, d.vibrar);
// fim:
await pet.fimTrajeto();
```

Qualquer mudança no formato muda os dois lados juntos: `firmware/hoodi_pet/protocolo.h` e `app/hoodiPetProtocolo.ts`. Os testes dos dois usam os mesmos bytes.
