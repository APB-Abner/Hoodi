// hoodi Pet - teste de bancada (OLED + vibracao + botao)
// Placa: "ESP32C3 Dev Module" | Tools > USB CDC On Boot: Enabled (senao o Serial nao aparece)
// Bibliotecas: Adafruit SSD1306 + Adafruit GFX (Library Manager)
// Ligacoes (doc consolidado): OLED SDA=GPIO6, SCL=GPIO7 | Vibracao IN=GPIO3 | Botao GPIO4->GND

#include <Wire.h>
#include <Adafruit_GFX.h>
#include <Adafruit_SSD1306.h>

const int PINO_SDA = 6, PINO_SCL = 7, PINO_VIB = 3, PINO_BOTAO = 4;
Adafruit_SSD1306 tela(128, 64, &Wire, -1);
int nivel = 0;  // 0 tranquila, 1 atenta, 2 preocupada, 3 em alerta

void escanearI2C() {
  Serial.println("Escaneando I2C...");
  int achados = 0;
  for (uint8_t ad = 1; ad < 127; ad++) {
    Wire.beginTransmission(ad);
    if (Wire.endTransmission() == 0) { Serial.printf("  dispositivo em 0x%02X\n", ad); achados++; }
  }
  if (!achados) Serial.println("  nada. Conferir SDA/SCL trocados, VCC em 3V3 e GND.");
}

void desenharCarinha(int n) {
  tela.clearDisplay();
  tela.drawCircle(64, 32, 28, SSD1306_WHITE);
  int olho = (n == 3) ? 5 : 3;                       // olho arregalado no alerta
  tela.fillCircle(52, 25, olho, SSD1306_WHITE);
  tela.fillCircle(76, 25, olho, SSD1306_WHITE);
  if (n == 0) for (int x = -10; x <= 10; x++) tela.drawPixel(64 + x, 46 - (x * x) / 20, SSD1306_WHITE); // sorriso (cantos sobem)
  if (n == 1) tela.drawLine(54, 44, 74, 44, SSD1306_WHITE);                                             // reta
  if (n >= 2) for (int x = -10; x <= 10; x++) tela.drawPixel(64 + x, 42 + (x * x) / 20, SSD1306_WHITE); // triste (cantos descem)
  if (n == 3) tela.fillCircle(64, 47, 4, SSD1306_WHITE);                                                // boca aberta
  tela.setCursor(0, 0); tela.setTextColor(SSD1306_WHITE); tela.print("nivel "); tela.print(n);
  tela.display();
}

void vibrar(int n) {                                   // n pulsos curtos; nivel 0 nao vibra
  for (int i = 0; i < n; i++) { digitalWrite(PINO_VIB, HIGH); delay(200); digitalWrite(PINO_VIB, LOW); delay(150); }
}

void setup() {
  Serial.begin(115200);
  delay(1500);                                         // tempo do USB CDC subir
  pinMode(PINO_VIB, OUTPUT); digitalWrite(PINO_VIB, LOW);
  pinMode(PINO_BOTAO, INPUT_PULLUP);
  Wire.begin(PINO_SDA, PINO_SCL);
  escanearI2C();
  if (!tela.begin(SSD1306_SWITCHCAPVCC, 0x3C)) Serial.println("OLED nao respondeu em 0x3C (tentar 0x3D).");
  desenharCarinha(nivel);
  Serial.println("Aperte o botao (ou mande 0-3 pelo Serial) para trocar o nivel.");
}

void loop() {
  int novo = -1;
  if (Serial.available()) { char c = Serial.read(); if (c >= '0' && c <= '3') novo = c - '0'; }
  if (digitalRead(PINO_BOTAO) == LOW) { delay(30); while (digitalRead(PINO_BOTAO) == LOW) delay(5); novo = (nivel + 1) % 4; }
  if (novo >= 0) {
    nivel = novo;
    Serial.printf("nivel=%d\n", nivel);
    desenharCarinha(nivel);
    vibrar(nivel);
  }
}
