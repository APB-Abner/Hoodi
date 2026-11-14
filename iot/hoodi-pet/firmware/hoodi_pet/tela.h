// OLED SSD1306 128x64 I2C: carinhas por nivel, PIN de pareamento e mensagens curtas.
#pragma once
#include <stdint.h>
#include "protocolo.h"

namespace tela {

bool iniciar(int sda, int scl);          // false se o OLED nao respondeu (o Pet continua vibrando)
bool disponivel();
void expressao(hoodi::Expressao e, uint8_t nivel, bool bleConectado);
void pin(uint32_t pin);                  // codigo de 6 digitos para digitar no celular
void pareamento(const char* nomeDispositivo);
void mensagem(const char* linha1, const char* linha2);
void ligar();
void apagar();
bool ligada();

}  // namespace tela
