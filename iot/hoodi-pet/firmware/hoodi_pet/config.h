// Configuracao do hoodi Pet. Ligacoes conforme o documento consolidado.
#pragma once

// 1 = sem Bluetooth (Wokwi nao simula BLE): comandos chegam so pelo Serial.
#ifndef HOODI_SEM_BLE
#define HOODI_SEM_BLE 0
#endif

#define HOODI_VERSAO_FIRMWARE "1.0.0"

// Pinos (ESP32-C3 SuperMini). GPIO8 (LED) e GPIO9 (BOOT) ficam livres: afetam o boot.
constexpr int PINO_SDA = 6;
constexpr int PINO_SCL = 7;
constexpr int PINO_VIBRACAO = 3;   // IN do modulo de vibracao (com driver). Nunca o motor direto.
constexpr int PINO_BOTAO = 4;      // botao para GND, pull-up interno

constexpr unsigned long TELA_APAGA_MS = 10000;          // apaga a tela apos 10 s sem novidade
constexpr unsigned long PRESSAO_LONGA_MS = 3000;        // evento PressaoLonga (pedido de ajuda, se o time confirmar)
constexpr unsigned long BOOT_PAREAR_MS = 2000;          // segurar o botao ao ligar = modo pareamento
constexpr unsigned long PAREAMENTO_DURACAO_MS = 120000; // modo pareamento fecha sozinho em 2 min
constexpr unsigned long AVISO_DESCONEXAO_MS = 20000;    // vibra 1x se ficar 20 s sem o celular durante um trajeto
