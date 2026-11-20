// hoodi Pet - firmware 1.1.0 (ESP32-C3 SuperMini, carcaca v7b)
//
// O Pet so executa: quem decide quando alertar e o app (regra de subida de nivel, intervalo, histerese).
// GPS, grade de risco e API ficam no celular. O Pet nunca chama a API.
//
// Botoes: 1 (GPIO4) clique acende a tela | 2 (GPIO10) silencia vibracao e som | 3 (GPIO20) segurar 3 s = ajuda.
// Segurar o botao 1 ao ligar abre o pareamento. Alto-falante no GPIO5 via transistor NPN.
//
// Arduino IDE: placa "ESP32C3 Dev Module", USB CDC On Boot: Enabled.
// Bibliotecas: NimBLE-Arduino 2.5.x, Adafruit SSD1306, Adafruit GFX.
// Wokwi: HOODI_SEM_BLE 1 em config.h (o Wokwi nao simula Bluetooth); comandos pelo Serial.
//
// Serial (115200), uma linha por comando:
//   n0 n1 n2 n3 n?   estado com nivel 0..3 ou desconhecido (padrao e expressao automaticos)
//   id               identificar (vibra para achar o Pet)
//   sil              silenciar a vibracao e o som
//   fim              fim do trajeto (carinha dormindo)
//   hex <18 hex>     frame bruto, passa pela regra de sequencia. Ex.: hex 010101000000020202
//   st               imprime o estado
//   parear           abre o modo pareamento por 2 min
//   esquecer         apaga o celular pareado
//
// Arquivos:
//   pet.cpp       logica do Pet (cada etapa abaixo)
//   protocolo.h   formato das mensagens Bluetooth app <-> Pet
//   vibracao.h    padroes de vibracao e leitura dos botoes
//   som.h         toques do alto-falante
//   tela.cpp      carinhas (pixel art de carinhas.h) e mensagens no OLED
//   ble_pet.cpp   Bluetooth (pareamento com PIN, comandos, status, eventos)
//   config.h      pinos e tempos

#include "pet.h"

void setup() {
  pet::iniciar();
}

// Nada aqui usa delay() longo: cada etapa faz um pouco e devolve o controle,
// entao o Pet vibra, bipa, le os botoes e atende o Bluetooth "ao mesmo tempo".
void loop() {
  pet::receberComandos();    // 1. o que o app mandou
  pet::atualizarBle();       // 2. conexao e pareamento
  pet::atualizarSerial();    // 3. comandos de bancada
  pet::atualizarBotoes();    // 4. tela / silenciar / ajuda
  pet::atualizarConexao();   // 5. aviso se o celular sumiu
  pet::atualizarVibracao();  // 6. motor
  pet::atualizarSom();       // 7. alto-falante
  pet::atualizarTela();      // 8. piscar e apagar a tela
  delay(5);
}
