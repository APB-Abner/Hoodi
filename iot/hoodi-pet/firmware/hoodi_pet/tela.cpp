#include "tela.h"

#include <Adafruit_GFX.h>
#include <Adafruit_SSD1306.h>
#include <Arduino.h>
#include <Wire.h>

namespace tela {
namespace {

Adafruit_SSD1306 oled(128, 64, &Wire, -1);
bool ok = false;
bool acesa = false;

constexpr int CX = 64, CY = 34, R = 26;

const char* nomeNivel(uint8_t nivel) {
  switch (nivel) {
    case 0: return "tranquilo";
    case 1: return "atencao";
    case 2: return "cuidado";
    case 3: return "alerta";
    default: return "sem dado";
  }
}

// Arco simples: y = base + curva * x^2 / 100, x em [-meio, meio]. Na tela, y cresce para baixo.
void boca(int base, int curva, int meio) {
  for (int x = -meio; x <= meio; x++) {
    int y = base + (curva * x * x) / 100;
    oled.drawPixel(CX + x, y, SSD1306_WHITE);
    oled.drawPixel(CX + x, y + 1, SSD1306_WHITE);
  }
}

void olhos(int raio) {
  oled.fillCircle(CX - 10, CY - 6, raio, SSD1306_WHITE);
  oled.fillCircle(CX + 10, CY - 6, raio, SSD1306_WHITE);
}

void rodape(const char* texto, bool bleConectado) {
  oled.setTextSize(1);
  oled.setTextColor(SSD1306_WHITE);
  oled.setCursor(0, 0);
  oled.print(texto);
  // Indicador de conexao no canto: B = celular conectado, x = sem celular.
  oled.setCursor(122, 0);
  oled.print(bleConectado ? "B" : "x");
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
  using hoodi::Expressao;
  oled.clearDisplay();

  if (e == Expressao::Desconhecido) {
    oled.drawCircle(CX, CY, R, SSD1306_WHITE);
    oled.setTextSize(3);
    oled.setCursor(CX - 8, CY - 11);
    oled.print("?");
    rodape("sem dado", bleConectado);
  } else if (e == Expressao::Dormindo) {
    oled.drawCircle(CX, CY, R, SSD1306_WHITE);
    oled.drawLine(CX - 15, CY - 6, CX - 5, CY - 6, SSD1306_WHITE);
    oled.drawLine(CX + 5, CY - 6, CX + 15, CY - 6, SSD1306_WHITE);
    boca(CY + 10, 0, 4);
    oled.setTextSize(1);
    oled.setCursor(CX + 22, CY - 24);
    oled.print("z z");
    rodape("dormindo", bleConectado);
  } else if (e == Expressao::Desconectado) {
    oled.drawCircle(CX, CY, R, SSD1306_WHITE);
    olhos(2);
    boca(CY + 10, 0, 7);
    rodape("sem celular", bleConectado);
  } else {
    oled.drawCircle(CX, CY, R, SSD1306_WHITE);
    switch (e) {
      case Expressao::Tranquila:
        olhos(3);
        boca(CY + 12, -6, 10);  // sorriso: cantos sobem (y menor)
        break;
      case Expressao::Atenta:
        olhos(3);
        boca(CY + 10, 0, 8);  // boca reta
        break;
      case Expressao::Preocupada:
        olhos(3);
        oled.drawLine(CX - 15, CY - 14, CX - 6, CY - 11, SSD1306_WHITE);  // sobrancelhas
        oled.drawLine(CX + 15, CY - 14, CX + 6, CY - 11, SSD1306_WHITE);
        boca(CY + 9, 5, 9);  // tristinha: cantos descem
        break;
      case Expressao::Alerta:
        olhos(5);
        oled.drawCircle(CX, CY + 11, 5, SSD1306_WHITE);  // boca aberta
        oled.setTextSize(2);
        oled.setCursor(CX + 28, CY - 12);
        oled.print("!");
        break;
      default:
        break;
    }
    rodape(nomeNivel(nivel), bleConectado);
  }
  oled.display();
  ligar();
}

void pin(uint32_t valor) {
  if (!ok) return;
  char texto[8];
  snprintf(texto, sizeof(texto), "%06lu", (unsigned long)(valor % 1000000UL));
  oled.clearDisplay();
  oled.setTextColor(SSD1306_WHITE);
  oled.setTextSize(1);
  oled.setCursor(0, 0);
  oled.print("Digite no celular:");
  oled.setTextSize(3);
  oled.setCursor(10, 26);
  oled.print(texto);
  oled.display();
  ligar();
}

void pareamento(const char* nome) {
  mensagem("Modo pareamento", nome);
}

void mensagem(const char* linha1, const char* linha2) {
  if (!ok) return;
  oled.clearDisplay();
  oled.setTextColor(SSD1306_WHITE);
  oled.setTextSize(1);
  oled.setCursor(0, 16);
  oled.print(linha1);
  oled.setCursor(0, 36);
  oled.print(linha2);
  oled.display();
  ligar();
}

}  // namespace tela
