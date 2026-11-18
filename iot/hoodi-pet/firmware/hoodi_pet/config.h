// Configuracao do hoodi Pet. Ligacoes conforme a carcaca v7b (3 botoes + alto-falante).
#pragma once

// 1 = sem Bluetooth (Wokwi nao simula BLE): comandos chegam so pelo Serial.
#ifndef HOODI_SEM_BLE
#define HOODI_SEM_BLE 0
#endif

// 1 = build de simulacao (Wokwi). Por padrao acompanha HOODI_SEM_BLE.
#ifndef HOODI_WOKWI
#define HOODI_WOKWI HOODI_SEM_BLE
#endif

#define HOODI_VERSAO_FIRMWARE "1.1.0"

// Pinos (ESP32-C3 SuperMini). GPIO8 (LED) e GPIO9 (BOOT) ficam livres: afetam o boot. GPIO2 tambem e de boot.
constexpr int PINO_SDA = 6;
constexpr int PINO_SCL = 7;
constexpr int PINO_VIBRACAO = 3;          // IN do modulo de vibracao (VCC no +BAT). Nunca o motor direto.
constexpr int PINO_SOM = 5;               // base do NPN via 1 kOhm. Nunca o alto-falante direto no GPIO.
constexpr int PINO_BOTAO_TELA = 4;        // botao 1: clique acende a tela e avisa o app; segurar ao ligar = pareamento
constexpr int PINO_BOTAO_SILENCIAR = 10;  // botao 2: clique corta vibracao e som
#if HOODI_WOKWI
constexpr int PINO_BOTAO_AJUDA = 1;       // no Wokwi o GPIO20 e o RX do Serial Monitor; la o botao 3 fica no GPIO1
#else
constexpr int PINO_BOTAO_AJUDA = 20;      // botao 3: segurar 3 s = pedido de ajuda. GPIO20 e o RX da UART0:
                                          // exige "USB CDC On Boot: Enabled" (o Serial vai pela USB)
#endif

// Som. Silenciar (botao 2 ou comando do app) corta som e vibracao juntos.
constexpr bool SOM_HABILITADO = true;
// Som no pedido de ajuda (enviado ou falhou por falta de celular). Desligado de proposito: quem pede ajuda
// pode estar perto de quem ameaca, e um bipe chama atencao. Com false, a resposta fica so na vibracao:
// curta = enviado; longa + curta = NAO saiu (sem celular).
constexpr bool SOM_NO_PEDIDO_DE_AJUDA = false;

constexpr unsigned long TELA_APAGA_MS = 10000;          // apaga a tela apos 10 s sem novidade
constexpr unsigned long PRESSAO_LONGA_MS = 3000;        // botao 3 segurado = evento PressaoLonga (pedido de ajuda)
constexpr unsigned long BOOT_PAREAR_MS = 2000;          // segurar o botao 1 ao ligar = modo pareamento
constexpr unsigned long PAREAMENTO_DURACAO_MS = 120000; // modo pareamento fecha sozinho em 2 min
constexpr unsigned long AVISO_DESCONEXAO_MS = 20000;    // vibra 1x se ficar 20 s sem o celular durante um trajeto
