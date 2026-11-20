// hoodi Pet - diagnostico das ligacoes na protoboard.
// Descobre sozinho em quais pinos estao o OLED, os 3 botoes, o motor e o alto-falante, e compara com o
// esperado (OLED SDA=6 SCL=7, botoes 4/10/20, motor=3, som=5). Placa: ESP32C3 Dev Module, USB CDC On Boot: Enabled
// (obrigatorio: o GPIO20 e o RX da UART0).
// Abra o Serial Monitor (115200) e siga as instrucoes. Nao precisa de biblioteca extra.

#include <Arduino.h>
#include <Wire.h>

const int CANDIDATOS[] = {0, 1, 2, 3, 4, 5, 6, 7, 10, 20};  // 8 e 9 ficam de fora: mexem no boot
const int N = sizeof(CANDIDATOS) / sizeof(CANDIDATOS[0]);
bool ocupado[22] = {false};

void esperarEnter(const char* msg) {
  Serial.println(msg);
  while (Serial.available()) Serial.read();
  while (!Serial.available()) delay(10);
  while (Serial.available()) Serial.read();
}

void acharOled() {
  Serial.println("\n== 1) OLED ==");
  for (int i = 0; i < N; i++) {
    for (int j = 0; j < N; j++) {
      if (i == j || ocupado[CANDIDATOS[i]] || ocupado[CANDIDATOS[j]]) continue;
      const int sda = CANDIDATOS[i], scl = CANDIDATOS[j];
      Wire.begin(sda, scl);
      const uint8_t enderecos[] = {0x3C, 0x3D};
      for (uint8_t ad : enderecos) {
        Wire.beginTransmission(ad);
        if (Wire.endTransmission() == 0) {
          Serial.printf("OLED achado: SDA=%d SCL=%d endereco=0x%02X  %s\n", sda, scl, ad,
                        (sda == 6 && scl == 7) ? "OK" : "<-- ESPERADO SDA=6 SCL=7");
          ocupado[sda] = ocupado[scl] = true;
          Wire.end();
          return;
        }
      }
      Wire.end();
    }
  }
  Serial.println("OLED NAO respondeu em nenhum par de pinos: conferir VCC no 3.3, GND e os fios SDA/SCL.");
}

void acharBotao(int numero, int esperado) {
  Serial.printf("\n== 2.%d) BOTAO %d (esperado no GPIO%d) ==\n", numero, numero, esperado);
  for (int i = 0; i < N; i++) if (!ocupado[CANDIDATOS[i]]) pinMode(CANDIDATOS[i], INPUT_PULLUP);
  delay(50);
  for (int i = 0; i < N; i++) {
    const int p = CANDIDATOS[i];
    if (!ocupado[p] && digitalRead(p) == LOW) {
      Serial.printf("Pino %d esta em GND SEM apertar nada: botao sempre fechado ou fio no GND. "
                    "Gire o botao 90 graus ou use pernas na diagonal.\n", p);
      ocupado[p] = true;  // nunca vai ser ligado como saida (evita curto)
    }
  }
  Serial.printf("Aperte e SEGURE o botao %d, e com ele apertado aperte Enter no Serial.\n", numero);
  esperarEnter("");
  bool achou = false;
  for (int i = 0; i < N; i++) {
    const int p = CANDIDATOS[i];
    if (!ocupado[p] && digitalRead(p) == LOW) {
      Serial.printf("Botao %d achado no pino %d  %s\n", numero, p, p == esperado ? "OK" : "<-- ESPERADO OUTRO PINO");
      ocupado[p] = true;
      achou = true;
    }
  }
  if (!achou) Serial.println("Nenhum pino foi a GND: o outro lado do botao precisa ir na trilha - (GND).");
}

void acharMotor() {
  Serial.println("\n== 3) MOTOR ==");
  esperarEnter("Solte o botao, preste atencao no motor e aperte Enter. Cada pino livre vai vibrar 1 vez.");
  for (int i = 0; i < N; i++) {
    const int p = CANDIDATOS[i];
    if (ocupado[p]) continue;
    Serial.printf("  testando pino %d ...\n", p);
    pinMode(p, OUTPUT);
    digitalWrite(p, HIGH);
    delay(500);
    digitalWrite(p, LOW);
    pinMode(p, INPUT);
    delay(1200);
  }
  Serial.println("O motor deve ter vibrado SO no 'pino 3'. Se vibrou em outro, o IN esta nesse pino.");
  Serial.println("Se nao vibrou em nenhum: conferir VCC do modulo no +BAT (chave ligada) e GND.");
}

void acharSom() {
  Serial.println("\n== 4) ALTO-FALANTE ==");
  esperarEnter("Preste atencao no som e aperte Enter. Cada pino livre vai bipar 1 vez.");
  for (int i = 0; i < N; i++) {
    const int p = CANDIDATOS[i];
    if (ocupado[p]) continue;
    Serial.printf("  testando pino %d ...\n", p);
    tone(p, 2000, 400);
    delay(1200);
    noTone(p);
    pinMode(p, INPUT);
  }
  Serial.println("Deve ter bipado SO no 'pino 5'. Se nao bipou: conferir transistor (base no 1k), 33 Ohm e +BAT.");
}

void setup() {
  Serial.begin(115200);
  delay(1500);
  Serial.println("\nhoodi Pet - diagnostico das ligacoes");
  acharOled();
  acharBotao(1, 4);
  acharBotao(2, 10);
  acharBotao(3, 20);
  acharMotor();
  acharSom();
  Serial.println("\nFim. Manda esse texto pro Abner :)");
}

void loop() {}
