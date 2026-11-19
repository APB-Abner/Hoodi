#include "tela.h"

#include <Adafruit_GFX.h>
#include <Adafruit_SSD1306.h>
#include <Arduino.h>
#include <Wire.h>

#include "carinhas.h"

namespace tela {
namespace {

Adafruit_SSD1306 oled(128, 64, &Wire, -1);
bool ok = false;
bool acesa = false;

// OLED de duas cores: as 16 linhas de cima sao amarelas (texto) e as 48 de baixo azuis (carinha).
// A grade 18 x 12 do app vira blocos de 4 x 4 pixels: 72 x 48, centralizada na parte azul.
// Cada quadradinho aceso e 3 x 3 com 1 pixel de folga, imitando a grade do app.
constexpr int CELULA = 4;
constexpr int GRADE_X = (128 - carinhas::LARGURA * CELULA) / 2;
constexpr int GRADE_Y = 16;

// Estado da ultima carinha desenhada, para a piscada redesenhar igual.
hoodi::Expressao expressaoAtual = hoodi::Expressao::Dormindo;
uint8_t nivelAtual = hoodi::NIVEL_DESCONHECIDO;
bool bleAtual = false;
bool mostrandoCarinha = false;  // false quando a tela mostra PIN ou mensagem
bool piscando = false;
unsigned long proximaPiscada = 0;
constexpr unsigned long DURACAO_PISCADA_MS = 160;

const char* nomeNivel(uint8_t nivel) {
  switch (nivel) {
    case 0: return "tranquilo";
    case 1: return "atencao";
    case 2: return "cuidado";
    case 3: return "alerta";
    default: return "sem dado";
  }
}

const char* const* gradeDa(hoodi::Expressao e, bool fechado) {
  using hoodi::Expressao;
  switch (e) {
    case Expressao::Tranquila: return fechado ? carinhas::TRANQUILA_PISCANDO : carinhas::TRANQUILA;
    case Expressao::Atenta: return fechado ? carinhas::ATENTA_PISCANDO : carinhas::ATENTA;
    case Expressao::Preocupada: return carinhas::PREOCUPADA;
    case Expressao::Alerta: return carinhas::ALERTA;
    case Expressao::Dormindo: return carinhas::DORMINDO;
    case Expressao::Desconectado: return carinhas::DESCONECTADO;
    default: return carinhas::DESCONHECIDO;
  }
}

bool pisca(hoodi::Expressao e) {
  return e == hoodi::Expressao::Tranquila || e == hoodi::Expressao::Atenta;
}

const char* rotulo(hoodi::Expressao e, uint8_t nivel) {
  using hoodi::Expressao;
  if (e == Expressao::Dormindo) return "dormindo";
  if (e == Expressao::Desconectado) return "sem celular";
  if (e == Expressao::Desconhecido) return "sem dado";
  return nomeNivel(nivel);
}

void desenharGrade(const char* const* grade) {
  for (int r = 0; r < carinhas::ALTURA; r++) {
    for (int c = 0; c < carinhas::LARGURA; c++) {
      if (grade[r][c] == '#') {
        oled.fillRect(GRADE_X + c * CELULA, GRADE_Y + r * CELULA, CELULA - 1, CELULA - 1, SSD1306_WHITE);
      }
    }
  }
}

void desenharCarinha() {
  oled.clearDisplay();
  oled.setTextSize(1);
  oled.setTextColor(SSD1306_WHITE);
  oled.setCursor(0, 4);
  oled.print(rotulo(expressaoAtual, nivelAtual));
  // Indicador de conexao no canto: B = celular conectado, x = sem celular.
  oled.setCursor(122, 4);
  oled.print(bleAtual ? "B" : "x");
  desenharGrade(gradeDa(expressaoAtual, piscando));
  oled.display();
}

void agendarPiscada(unsigned long agora) {
  proximaPiscada = agora + 2500 + (esp_random() % 3000);  // a cada 2,5 a 5,5 s, como bicho de verdade
}

}  // namespace

bool iniciar(int sda, int scl) {
  Wire.begin(sda, scl);
  ok = oled.begin(SSD1306_SWITCHCAPVCC, 0x3C);
  if (!ok) ok = oled.begin(SSD1306_SWITCHCAPVCC, 0x3D);
  if (ok) { oled.clearDisplay(); oled.display(); acesa = true; }
  return ok;
}

bool disponivel() { return ok; }

void ligar() {
  if (!ok || acesa) return;
  oled.ssd1306_command(SSD1306_DISPLAYON);
  acesa = true;
}

void apagar() {
  if (!ok || !acesa) return;
  oled.ssd1306_command(SSD1306_DISPLAYOFF);  // economiza bateria; a RAM da tela continua
  acesa = false;
}

bool ligada() { return ok && acesa; }

void expressao(hoodi::Expressao e, uint8_t nivel, bool bleConectado) {
  if (!ok) return;
  expressaoAtual = e;
  nivelAtual = nivel;
  bleAtual = bleConectado;
  mostrandoCarinha = true;
  piscando = false;
  agendarPiscada(millis());
  desenharCarinha();
  ligar();
}

void animar(unsigned long agora) {
  if (!ok || !acesa || !mostrandoCarinha || !pisca(expressaoAtual)) return;
  if (!piscando && (long)(agora - proximaPiscada) >= 0) {
    piscando = true;
    desenharCarinha();
  } else if (piscando && (long)(agora - proximaPiscada) >= (long)DURACAO_PISCADA_MS) {
    piscando = false;
    agendarPiscada(agora);
    desenharCarinha();
  }
}

void pin(uint32_t valor) {
  if (!ok) return;
  mostrandoCarinha = false;
  char texto[8];
  snprintf(texto, sizeof(texto), "%06lu", (unsigned long)(valor % 1000000UL));
  oled.clearDisplay();
  oled.setTextColor(SSD1306_WHITE);
  oled.setTextSize(1);
  oled.setCursor(0, 4);
  oled.print("Digite no celular:");
  oled.setTextSize(3);
  oled.setCursor(10, 28);
  oled.print(texto);
  oled.display();
  ligar();
}

void pareamento(const char* nome) {
  mensagem("Modo pareamento", nome);
}

void mensagem(const char* linha1, const char* linha2) {
  if (!ok) return;
  mostrandoCarinha = false;
  oled.clearDisplay();
  oled.setTextColor(SSD1306_WHITE);
  oled.setTextSize(1);
  oled.setCursor(0, 4);
  oled.print(linha1);
  oled.setCursor(0, 36);
  oled.print(linha2);
  oled.display();
  ligar();
}

}  // namespace tela
