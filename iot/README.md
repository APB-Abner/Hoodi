# hoodi Pet — imported source

Source: the original `hoodi-pet.zip` import, updated from the supplied `hoodi-iot-1.1.0.zip` archive. Files under `hoodi-pet/` retain their original contents. This import brings the firmware, BLE protocol helpers, alert rules, tests and documentation into the team repository.

The `app/` directory contains integration modules, not a complete Expo application. Fernando owns firmware/risk integration and Mariana owns mobile/BLE integration; their original source documentation remains available in [the package README](hoodi-pet/README.md).

Validation performed: `node --experimental-strip-types --test iot/hoodi-pet/testes/app.test.ts` — 16 tests passed. These test protocol encoding and alert rules without native BLE or hardware.

Firmware compilation, flashing, native device BLE, battery/TP4056 assembly and physical enclosure decisions were not validated by this import. No toolchain or dependencies were installed. Historical assembly instructions in the source are not new hardware approvals.

## Firmware 1.1.0 (carcaça v7b)

Atualização posterior ao import: 3 botões (GPIO4 tela, GPIO10 silenciar, GPIO20 ajuda com pressão longa de 3 s), alto-falante no GPIO5 via transistor NPN e módulo de vibração alimentado pelo +BAT. O protocolo BLE não mudou (versão 1, mesmos bytes). O Wokwi ganhou os 3 botões e um buzzer; lá o botão 3 fica no GPIO1, porque o GPIO20 é o RX do Serial Monitor.

Validação deste incremento: testes C++ de protocolo, vibração, som e botão (103/103), testes TypeScript do app (16/16) e checagem de sintaxe do `pet.cpp` contra stubs do Arduino, nos modos Pet e Wokwi. **Não** houve compilação com o core ESP32 nem gravação na placa: o ambiente usado não alcançava os servidores de toolchain. Antes da entrega, compile com `pio run -e pet` (ou Arduino IDE) e rode o projeto no Wokwi.

## Repository validation of 1.1.0

Supplied archive SHA-256: `462175b91e97e42912c577082bdfde5b228db42489ccad29f7084b5b339aef45`.

- Native C++: **103/103 checks passed**, compiled with Clang, C++17, `-Wall -Wextra -Werror`.
- TypeScript: **16/16 tests passed**, using Node's type stripping and test runner.
- Wokwi: all four web bundle files reproduced from source in an isolated staging directory; both diagram JSON files parsed and matched.

The protocol codec and BLE implementation retain the supplied version-1 format. The mobile helper change only documents the button event; this does not implement or validate the native app's call to `/v1/panico`.

No dependencies were installed. The installed PlatformIO cache lacks ESP32 packages; compilation with the ESP32 core, flashing, Wokwi execution and physical BLE/hardware testing remain pending.
