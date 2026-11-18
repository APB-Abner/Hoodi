# hoodi Pet: firmware, protocolo BLE e integração com o app

Bichinho de bolso que vibra, bipa e muda de carinha quando a pessoa entra numa área de risco maior. O celular faz GPS, grade e regra de alerta, e manda o resultado ao Pet por Bluetooth.

Firmware 1.1.0: 3 botões e alto-falante, conforme a carcaça v7b.

## Estrutura

| Pasta | O que tem |
|---|---|
| `firmware/hoodi_pet/` | Firmware do ESP32-C3 SuperMini (Arduino IDE ou PlatformIO) |
| `app/` | Arquivos TypeScript para o app Expo: codec, regra de alerta e cliente BLE |
| `docs/PROTOCOLO_BLE.md` | Especificação do protocolo app ↔ Pet |
| `wokwi/` | Simulação: `diagram.json`; `projeto_web/` pronto para o wokwi.com |
| `testes/` | Testes no PC: protocolo, vibração, som e botão (C++); codec e regra de alerta (TypeScript) |

## Ligações (ESP32-C3 SuperMini)

| De | Para |
|---|---|
| LiPo + / − | TP4056 B+ / B− |
| TP4056 OUT+ | chave → pino **5V** do ESP (esse trilho é o **+BAT**) |
| TP4056 OUT− | GND (**nunca** o B−) |
| OLED VCC / GND / SDA / SCL | 3V3 / GND / GPIO6 / GPIO7 |
| Módulo de vibração (V919) VCC / GND / IN | +BAT / GND / GPIO3 |
| Botão 1, tela | GPIO4 e GND |
| Botão 2, silenciar | GPIO10 e GND |
| Botão 3, ajuda | GPIO20 e GND |
| Alto-falante | +BAT → resistor 33 Ω → alto-falante → coletor do NPN; emissor → GND; base ← resistor 1 kΩ ← GPIO5; diodo em paralelo com o alto-falante (catodo no lado do +BAT) |

- Os botões usam o pull-up interno; não precisa de resistor.
- GPIO2, GPIO8 e GPIO9 ficam livres porque afetam o boot.
- **GPIO20 é o RX da UART0.** O botão 3 só funciona com **USB CDC On Boot: Enabled** (o Serial vai pela USB). Sem isso, o firmware nem compila: tem um `#error` avisando.
- **Desligue a chave antes de plugar o USB no ESP32.**
- O motor de vibração solto (2 fios) e o alto-falante **não** vão direto no GPIO: precisam de driver ou transistor.

## Gravar (Arduino IDE)

1. Instale as placas ESP32 da Espressif (3.x).
2. No Library Manager, instale:
   - **NimBLE-Arduino** 2.5.x (não a 3.0-dev)
   - **Adafruit SSD1306**
   - **Adafruit GFX**
3. Abra `firmware/hoodi_pet/hoodi_pet.ino`. As outras abas abrem junto.
4. Configure a placa **ESP32C3 Dev Module** com **USB CDC On Boot: Enabled**.
5. Se o upload falhar: segure BOOT, aperte RESET e solte BOOT.

Com PlatformIO: `cd firmware && pio run -e pet -t upload && pio device monitor`.

## Usar

1. **Primeiro uso:** o Pet abre o modo pareamento sozinho. Procure "hoodi Pet XXXX" pelo app e digite o PIN que aparece no OLED.
2. **Trocar de celular:** segure o botão 1 enquanto liga o Pet (2 s).
3. **Botões:**
   - **1, tela:** o clique acende a tela e avisa o app.
   - **2, silenciar:** corta a vibração e o som na hora e acende a tela.
   - **3, ajuda:** segure 3 s. O clique é ignorado, para não pedir ajuda sem querer.
     - Com celular conectado: vibra 1 vez curta e manda o evento de pressão longa ao app, que faz o `POST /v1/panico`.
     - Sem celular: vibra longo + curto e mostra "Sem celular / ajuda nao enviada". O pedido **não** saiu.
4. **Som:** o alto-falante acompanha o alerta (1, 2 ou 3 bipes conforme o nível), o "identificar", a perda de conexão, o PIN e o pareamento. O pedido de ajuda **não bipa** por padrão, porque quem pede ajuda pode estar perto de quem ameaça. Para mudar, edite `SOM_NO_PEDIDO_DE_AJUDA` em `config.h`. Para desligar todo o som, use `SOM_HABILITADO`.
5. **Serial de bancada (115200):**
   - `n0`–`n3` e `n?`: muda o nível
   - `id`: identificar (vibra pra achar o Pet)
   - `sil`: silenciar (vibração e som)
   - `fim`: fim do trajeto
   - `hex <frame>`: frame bruto do protocolo
   - `st`: estado
   - `parear`: abre o modo pareamento
   - `esquecer`: apaga o celular pareado

## Wokwi (Physical Computing)

O Wokwi não simula Bluetooth. Por isso o build de simulação usa `HOODI_SEM_BLE 1` e os comandos chegam pelo Serial, com a mesma lógica e o mesmo protocolo (`hex`).

Diferenças do diagrama para o Pet real:

| Pet real | Wokwi | Por quê |
|---|---|---|
| Motor de vibração | LED roxo | O Wokwi não tem motor |
| Alto-falante com transistor | Buzzer direto no GPIO5 | Só na simulação; no real, nunca direto |
| Botão 3 no GPIO20 | Botão 3 no GPIO1 | No Wokwi o GPIO20 é o RX do Serial Monitor |
| Pedido de ajuda vai pelo BLE | Sai no Serial como `[evento] tipo=2` | Sem BLE na simulação |

- **wokwi.com:** crie um projeto ESP32-C3 e suba todos os arquivos de `wokwi/projeto_web/`. Para gerar de novo depois de mudar o firmware: `python3 scripts/gerar_wokwi_web.py`.
- **VS Code:** rode `pio run -e wokwi` em `firmware/` e abra a simulação com `wokwi/wokwi.toml`.

## Testes

```bash
# Protocolo, vibração, som e botão (C++, sem placa)
g++ -std=c++17 -Wall -Wextra -Werror -Ifirmware/hoodi_pet testes/protocolo_test.cpp -o /tmp/t && /tmp/t
# Codec e regra de alerta do app (Node 22+)
node --experimental-strip-types --test testes/app.test.ts
```

## Pontos abertos com o time

- **App:** o evento de pressão longa agora vem do botão 3. O código e o protocolo não mudaram (continua o evento `2`), então o app não precisa de alteração para receber. Confirmar com a Mari que o app liga esse evento ao `POST /v1/panico`.
- **Firmware não compilado neste incremento:** a lógica nova (som, 3 botões) passou nos testes no PC e na checagem de sintaxe, mas precisa ser compilada e gravada na placa (`pio run -e pet`) e testada no Wokwi.
- **Bateria:** o circuito não mede a tensão da bateria (não tem divisor resistivo), então o app não tem como mostrar a carga. A autonomia precisa ser medida com o Pet montado.
