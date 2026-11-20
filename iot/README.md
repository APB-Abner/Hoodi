# hoodi Pet — imported source

Source: the supplied `hoodi-pet.zip` archive. Files under `hoodi-pet/` retain their original contents. This import brings the firmware, BLE protocol helpers, alert rules, tests and documentation into the team repository.

The `app/` directory contains integration modules, not a complete Expo application. Fernando owns firmware/risk integration and Mariana owns mobile/BLE integration; their original source documentation remains available in [the package README](hoodi-pet/README.md).

Validation performed: `node --experimental-strip-types --test iot/hoodi-pet/testes/app.test.ts` — 16 tests passed. These test protocol encoding and alert rules without native BLE or hardware.

Firmware compilation, flashing, native device BLE, battery/TP4056 assembly and physical enclosure decisions were not validated by this import. No toolchain or dependencies were installed. Historical assembly instructions in the source are not new hardware approvals.

## Firmware 1.1.0 (carcaça v7b)

Atualização posterior ao import, feita sobre a versão de 14/11 16:52 do firmware (carinhas em pixel art de `carinhas.h`, geradas por `scripts/gerar_carinhas.py` e iguais às do app, com piscada animada e loop em etapas via `pet.h`): 3 botões (GPIO4 tela, GPIO10 silenciar, GPIO20 ajuda com pressão longa de 3 s), alto-falante no GPIO5 via transistor NPN e módulo de vibração alimentado pelo +BAT. O protocolo BLE não mudou (versão 1, mesmos bytes). O Wokwi ganhou os 3 botões e um buzzer; lá o botão 3 fica no GPIO1, porque o GPIO20 é o RX do Serial Monitor.

Validação deste incremento:

- **Testes:** C++ de protocolo, vibração, som e botão (103/103) e TypeScript do app (16/16).
- **Compilação e simulação:** o arquivo único `wokwi/projeto_web/hoodi.cpp` compilou com o core ESP32 do Wokwi e foi executado no ESP32-C3 simulado em 19/11/2026. Foram exercitados os níveis n0 a n3, `n?`, `fim`, a tela apagando após 10 s, o botão silenciar e a pressão longa do botão de ajuda, que gerou `[evento] tipo=2`.
- **Fora desta validação:** a gravação desta versão na placa física.

Projeto no Wokwi: https://wokwi.com/projects/480684111115222017
