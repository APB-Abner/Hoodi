# hoodi Pet: firmware, protocolo BLE e integração com o app

Bichinho de bolso que vibra e muda de carinha quando a pessoa entra numa área de risco maior. O celular faz GPS, grade e regra de alerta, e manda o resultado ao Pet por Bluetooth.

## Estrutura

| Pasta | O que tem |
|---|---|
| `firmware/hoodi_pet/` | Firmware do ESP32-C3 SuperMini (Arduino IDE ou PlatformIO) |
| `app/` | Arquivos TypeScript para o app Expo: codec, regra de alerta e cliente BLE |
| `docs/PROTOCOLO_BLE.md` | Especificação do protocolo app ↔ Pet |
| `wokwi/` | Simulação: `diagram.json`; `projeto_web/` pronto para o wokwi.com |
| `testes/` | Testes no PC: protocolo, vibração e botão (C++); codec e regra de alerta (TypeScript) |

## Ligações (ESP32-C3 SuperMini)

| De | Para |
|---|---|
| LiPo + / − | TP4056 B+ / B− |
| TP4056 OUT+ | chave → pino **5V** |
| TP4056 OUT− | GND |
| OLED VCC / GND / SDA / SCL | 3V3 / GND / GPIO6 / GPIO7 |
| Módulo de vibração VCC / GND / IN | 3V3 / GND / GPIO3 |
| Botão | GPIO4 e GND (pull-up interno) |

- GPIO8 e GPIO9 ficam livres porque afetam o boot.
- **Desligue a chave antes de plugar o USB no ESP32.**
- O motor de vibração solto (2 fios) **não** vai direto no GPIO: precisa de módulo com driver ou de transistor.

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
2. **Trocar de celular:** segure o botão enquanto liga o Pet (2 s).
3. **Botão:** o clique acende a tela e avisa o app. A pressão longa (3 s) vibra uma vez e manda o evento ao app (pedido de ajuda, se o time confirmar).
4. **Serial de bancada (115200):**
   - `n0`–`n3` e `n?`: muda o nível
   - `id`: identificar (vibra pra achar o Pet)
   - `sil`: silenciar
   - `fim`: fim do trajeto
   - `hex <frame>`: frame bruto do protocolo
   - `st`: estado
   - `parear`: abre o modo pareamento
   - `esquecer`: apaga o celular pareado

## Wokwi (Physical Computing)

O Wokwi não simula Bluetooth. Por isso o build de simulação usa `HOODI_SEM_BLE 1` e os comandos chegam pelo Serial, com a mesma lógica e o mesmo protocolo (`hex`). No diagrama, o motor de vibração é representado por um LED.

- **wokwi.com:** crie um projeto ESP32-C3 e suba todos os arquivos de `wokwi/projeto_web/`. Para gerar de novo depois de mudar o firmware: `python3 scripts/gerar_wokwi_web.py`.
- **VS Code:** rode `pio run -e wokwi` em `firmware/` e abra a simulação com `wokwi/wokwi.toml`.

## Testes

```bash
# Protocolo, vibração e botão (C++, sem placa)
g++ -std=c++17 -Wall -Wextra -Werror -Ifirmware/hoodi_pet testes/protocolo_test.cpp -o /tmp/t && /tmp/t
# Codec e regra de alerta do app (Node 22+)
node --experimental-strip-types --test testes/app.test.ts
```

## Pontos abertos com o time

- **Pressão longa = pedido de ajuda?** O pânico ainda está "a confirmar". O Pet já manda o evento; falta o app decidir o que fazer com ele.
- **Bateria:** o circuito não mede a tensão da bateria (não tem divisor resistivo), então o app não tem como mostrar a carga. A autonomia precisa ser medida com o Pet montado.
