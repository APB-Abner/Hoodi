// hoodi Pet - firmware 1.0.0 (ESP32-C3 SuperMini)
//
// O Pet so executa: quem decide quando alertar e o app (regra de subida de nivel, intervalo, histerese).
// GPS, grade de risco e API ficam no celular. O Pet nunca chama a API.
//
// Arduino IDE: placa "ESP32C3 Dev Module", USB CDC On Boot: Enabled.
// Bibliotecas: NimBLE-Arduino 2.5.x, Adafruit SSD1306, Adafruit GFX.
// Wokwi: HOODI_SEM_BLE 1 em config.h (o Wokwi nao simula Bluetooth); comandos pelo Serial.
//
// Serial (115200), uma linha por comando:
//   n0 n1 n2 n3 n?   estado com nivel 0..3 ou desconhecido (padrao e expressao automaticos)
//   id               identificar (vibra para achar o Pet)
//   sil              silenciar a vibracao atual
//   fim              fim do trajeto (carinha dormindo)
//   hex <18 hex>     frame bruto, passa pela regra de sequencia. Ex.: hex 010101000000020202
//   st               imprime o estado
//   parear           abre o modo pareamento por 2 min
//   esquecer         apaga o celular pareado

// O codigo fica em pet.cpp (setup e loop estao la). Este arquivo existe porque o Arduino IDE
// exige um .ino com o nome da pasta.
