// hoodi Pet - teste de bancada (OLED + vibracao + alto-falante + 3 botoes)
// Placa: "ESP32C3 Dev Module" | Tools > USB CDC On Boot: Enabled (senao o Serial nao aparece)
// Bibliotecas: Adafruit SSD1306 + Adafruit GFX (Library Manager)
// Ligacoes (carcaca v7b): OLED SDA=GPIO6, SCL=GPIO7 | Vibracao IN=GPIO3 | Alto-falante: GPIO5 -> 1k -> base do NPN
// Botoes para GND: 1 = GPIO4 (troca o nivel), 2 = GPIO10 (bipe de teste), 3 = GPIO20 (vibra e bipa: "ajuda")

#include <Wire.h>
#include <Adafruit_GFX.h>
#include <Adafruit_SSD1306.h>

const int PINO_SDA = 6, PINO_SCL = 7, PINO_VIB = 3, PINO_SOM = 5;
const int PINO_BOTAO = 4, PINO_BOTAO_2 = 10, PINO_BOTAO_3 = 20;
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
  for (int i = 0; i < n; i++) {
    digitalWrite(PINO_VIB, HIGH); tone(PINO_SOM, 2000); delay(200);
    digitalWrite(PINO_VIB, LOW); noTone(PINO_SOM); delay(150);
  }
}

bool apertou(int pino) {                               // espera soltar (debounce simples)
  if (digitalRead(pino) != LOW) return false;
  delay(30);
  while (digitalRead(pino) == LOW) delay(5);
  return true;
}

void setup() {
  Serial.begin(115200);
  delay(1500);                                         // tempo do USB CDC subir
  pinMode(PINO_VIB, OUTPUT); digitalWrite(PINO_VIB, LOW);
  pinMode(PINO_BOTAO, INPUT_PULLUP);
  pinMode(PINO_BOTAO_2, INPUT_PULLUP);
  pinMode(PINO_BOTAO_3, INPUT_PULLUP);
  Wire.begin(PINO_SDA, PINO_SCL);
  escanearI2C();
  if (!tela.begin(SSD1306_SWITCHCAPVCC, 0x3C)) Serial.println("OLED nao respondeu em 0x3C (tentar 0x3D).");
  desenharCarinha(nivel);
  Serial.println("Botao 1 (ou 0-3 pelo Serial) troca o nivel | botao 2 bipa | botao 3 vibra e bipa.");
}

void loop() {
  int novo = -1;
  if (Serial.available()) { char c = Serial.read(); if (c >= '0' && c <= '3') novo = c - '0'; }
  if (apertou(PINO_BOTAO)) novo = (nivel + 1) % 4;
  if (apertou(PINO_BOTAO_2)) { Serial.println("botao 2 (GPIO10)"); tone(PINO_SOM, 2500, 150); }
  if (apertou(PINO_BOTAO_3)) { Serial.println("botao 3 (GPIO20)"); vibrar(1); }
  if (novo >= 0) {
    nivel = novo;
    Serial.printf("nivel=%d\n", nivel);
    desenharCarinha(nivel);
    vibrar(nivel);
  }
}
