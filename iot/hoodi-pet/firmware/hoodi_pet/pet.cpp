// Logica principal do hoodi Pet (setup/loop). Fica num .cpp para o Arduino nao gerar prototipos
// automaticos dentro de namespace. Comentarios de uso: hoodi_pet.ino.
#include <Arduino.h>

#include "ble_pet.h"
#include "config.h"
#include "protocolo.h"
#include "tela.h"
#include "vibracao.h"

using namespace hoodi;

namespace {

char nomeDispositivo[20];
Vibrador vibrador;
Botao botao(PRESSAO_LONGA_MS);
Sequenciador sequenciador;

Status estado;                        // o que o app le na caracteristica STATUS
bool trajetoAtivo = false;
bool bleConectado = false;
unsigned long ultimaNovidadeMs = 0;   // controla o apagar da tela
unsigned long desconectouEm = 0;
bool avisoDesconexaoPendente = false;
unsigned long pareamentoAbertoEm = 0;
uint32_t contadorEventos = 0;

void mostrarEstado() {
  tela::expressao(estado.expressao, estado.nivel, bleConectado);
  ultimaNovidadeMs = millis();
}

uint8_t flagsAtuais() {
  uint8_t f = 0;
  if (vibrador.ativo()) f |= FLAG_VIBRANDO;
  if (tela::ligada()) f |= FLAG_TELA_LIGADA;
  if (trajetoAtivo) f |= FLAG_TRAJETO_ATIVO;
  return f;
}

void publicarStatus() {
  estado.flags = flagsAtuais();
  uint8_t frame[TAM_STATUS];
  codificarStatus(estado, frame);
  ble::publicarStatus(frame, sizeof(frame));
}

void aplicar(const Comando& c) {
  const unsigned long agora = millis();
  switch (c.tipo) {
    case TipoComando::Estado:
      trajetoAtivo = true;
      estado.nivel = c.nivel;
      estado.expressao = c.expressao;
      if (c.padrao != Padrao::Nenhum) vibrador.iniciar(c.padrao, agora);
      mostrarEstado();
      break;
    case TipoComando::Identificar:
      vibrador.iniciar(Padrao::Identificar, agora);
      mostrarEstado();
      break;
    case TipoComando::Silenciar:
      vibrador.parar();
      break;
    case TipoComando::FimTrajeto:
      trajetoAtivo = false;
      vibrador.parar();
      estado.nivel = NIVEL_DESCONHECIDO;
      estado.expressao = Expressao::Dormindo;
      mostrarEstado();
      break;
  }
}

// Caminho unico para BLE e Serial: valida, confere a sequencia, aplica e publica o status.
void processarFrame(const uint8_t* dados, size_t tam) {
  Comando c;
  const bool valido = tam <= ble::TAM_MAX_FRAME && decodificarComando(dados, tam, c);
  estado.sequencia = c.sequencia;
  if (!valido) {
    estado.resultado = Resultado::Invalido;
  } else if (!sequenciador.aceitar(c.sequencia)) {
    estado.resultado = Resultado::SequenciaAntiga;
  } else {
    estado.resultado = Resultado::Aplicado;
    aplicar(c);
  }
  Serial.printf("[cmd] seq=%lu resultado=%u nivel=%u expressao=%u\n", (unsigned long)c.sequencia,
                (unsigned)estado.resultado, (unsigned)estado.nivel, (unsigned)estado.expressao);
  publicarStatus();
}

void emitirEvento(TipoEvento tipo) {
  uint8_t frame[TAM_EVENTO];
  codificarEvento(tipo, ++contadorEventos, frame);
  ble::publicarEvento(frame, sizeof(frame));
  Serial.printf("[evento] tipo=%u contador=%lu\n", (unsigned)tipo, (unsigned long)contadorEventos);
}

void abrirPareamento() {
  ble::entrarModoPareamento();
  pareamentoAbertoEm = millis();
  tela::pareamento(nomeDispositivo);
  Serial.printf("[ble] modo pareamento por %lu s: procure \"%s\" no app\n", PAREAMENTO_DURACAO_MS / 1000,
                nomeDispositivo);
}

void processarAvisosBle() {
  ble::Aviso a;
  while (ble::proximoAviso(a)) {
    switch (a.tipo) {
      case ble::TipoAviso::Conectou:
        bleConectado = true;
        sequenciador.reiniciar();  // o app recomeca a sequencia em 1 a cada conexao
        avisoDesconexaoPendente = false;
        Serial.println("[ble] conectado");
        if (!ble::modoPareamento()) mostrarEstado();
        break;
      case ble::TipoAviso::Desconectou:
        bleConectado = false;
        desconectouEm = millis();
        avisoDesconexaoPendente = trajetoAtivo;
        // Sem celular o Pet nao sabe o nivel: nao pode continuar mostrando o ultimo.
        estado.nivel = NIVEL_DESCONHECIDO;
        estado.expressao = trajetoAtivo ? Expressao::Desconectado : Expressao::Dormindo;
        Serial.println("[ble] desconectado");
        if (ble::modoPareamento()) tela::pareamento(nomeDispositivo); else mostrarEstado();
        publicarStatus();
        break;
      case ble::TipoAviso::MostrarPin:
        tela::pin(a.valor);
        Serial.println("[ble] PIN na tela do Pet");  // o PIN nao vai para o Serial de proposito
        break;
      case ble::TipoAviso::Pareado:
        Serial.println("[ble] pareado; outros celulares esquecidos");
        tela::mensagem("Pareado!", nomeDispositivo);
        vibrador.iniciar(Padrao::Curto, millis());
        ultimaNovidadeMs = millis();
        break;
      case ble::TipoAviso::PareamentoRecusado:
        Serial.println("[ble] pareamento recusado");
        if (ble::modoPareamento()) tela::pareamento(nomeDispositivo);  // fora do modo, nem acende a tela
        break;
    }
  }
  if (ble::modoPareamento() && millis() - pareamentoAbertoEm >= PAREAMENTO_DURACAO_MS && ble::pareamentosSalvos() > 0) {
    ble::sairModoPareamento();
    Serial.println("[ble] modo pareamento encerrado");
    mostrarEstado();
  }
}

bool hexParaBytes(const String& texto, uint8_t* out, size_t max, size_t& tam) {
  String limpo;
  for (unsigned int i = 0; i < texto.length(); i++) {
    if (!isspace((unsigned char)texto[i])) limpo += texto[i];
  }
  if (limpo.length() % 2 != 0 || limpo.length() / 2 > max) return false;
  tam = limpo.length() / 2;
  for (size_t i = 0; i < tam; i++) {
    char par[3] = {limpo[2 * i], limpo[2 * i + 1], 0};
    char* fim = nullptr;
    out[i] = (uint8_t)strtoul(par, &fim, 16);
    if (*fim != 0) return false;
  }
  return true;
}

// Atalhos do Serial nao mexem na sequencia (senao o app, conectado ao mesmo tempo, teria comandos recusados).
// So o "hex" passa pela regra de sequencia, para testar o protocolo como o app usa.
void aplicarAtalho(TipoComando tipo, uint8_t nivel) {
  uint8_t frame[TAM_COMANDO];
  codificarComando(tipo, 1, nivel, AUTOMATICO, AUTOMATICO, frame);
  Comando c;
  if (!decodificarComando(frame, TAM_COMANDO, c)) return;
  aplicar(c);
  Serial.printf("[serial] nivel=%u expressao=%u\n", (unsigned)estado.nivel, (unsigned)estado.expressao);
  publicarStatus();
}

void comandoSerial(String linha) {
  linha.trim();
  if (linha.length() == 0) return;

  if (linha.length() == 2 && linha[0] == 'n' && (linha[1] == '?' || (linha[1] >= '0' && linha[1] <= '3'))) {
    aplicarAtalho(TipoComando::Estado, linha[1] == '?' ? NIVEL_DESCONHECIDO : (uint8_t)(linha[1] - '0'));
  } else if (linha == "id") {
    aplicarAtalho(TipoComando::Identificar, 0);
  } else if (linha == "sil") {
    aplicarAtalho(TipoComando::Silenciar, 0);
  } else if (linha == "fim") {
    aplicarAtalho(TipoComando::FimTrajeto, 0);
  } else if (linha.startsWith("hex ")) {
    uint8_t frame[ble::TAM_MAX_FRAME];
    size_t tam = 0;
    if (hexParaBytes(linha.substring(4), frame, sizeof(frame), tam)) processarFrame(frame, tam);
    else Serial.println("[serial] hex invalido");
  } else if (linha == "st") {
    Serial.printf("[st] fw=%s nome=%s ble=%s pareamento=%s salvos=%d trajeto=%s nivel=%u expressao=%u ultimaSeq=%lu\n",
                  HOODI_VERSAO_FIRMWARE, nomeDispositivo, bleConectado ? "conectado" : "livre",
                  ble::modoPareamento() ? "aberto" : "fechado", ble::pareamentosSalvos(), trajetoAtivo ? "sim" : "nao",
                  (unsigned)estado.nivel, (unsigned)estado.expressao, (unsigned long)sequenciador.ultima);
  } else if (linha == "parear") {
    abrirPareamento();
  } else if (linha == "esquecer") {
    ble::esquecerPareamentos();
    Serial.println("[ble] pareamentos apagados");
    abrirPareamento();
  } else {
    Serial.println("[serial] comandos: n0 n1 n2 n3 n? id sil fim hex st parear esquecer");
  }
}

void lerSerial() {
  static String buffer;
  while (Serial.available()) {
    const char ch = (char)Serial.read();
    if (ch == '\n' || ch == '\r') {
      comandoSerial(buffer);
      buffer = "";
    } else if (buffer.length() < 64) {
      buffer += ch;
    }
  }
}

void tratarBotao() {
  const unsigned long agora = millis();
  switch (botao.atualizar(digitalRead(PINO_BOTAO) == LOW, agora)) {
    case Botao::Evento::Clique:
      // Clique acorda a tela e avisa o app (o app decide o que fazer).
      if (!ble::modoPareamento()) mostrarEstado();
      emitirEvento(TipoEvento::Clique);
      break;
    case Botao::Evento::PressaoLonga:
      vibrador.iniciar(Padrao::Curto, agora);  // confirma no corpo que a pressao longa foi reconhecida
      emitirEvento(TipoEvento::PressaoLonga);
      break;
    case Botao::Evento::Nenhum:
      break;
  }
}

void tratarDesconexao() {
  if (!avisoDesconexaoPendente || bleConectado) return;
  if (millis() - desconectouEm < AVISO_DESCONEXAO_MS) return;
  avisoDesconexaoPendente = false;
  vibrador.iniciar(Padrao::PerdeuConexao, millis());
  mostrarEstado();
  Serial.println("[ble] aviso de desconexao vibrado");
}

void tratarTela() {
  if (ble::modoPareamento()) return;  // tela fica acesa enquanto espera o celular
  if (tela::ligada() && millis() - ultimaNovidadeMs >= TELA_APAGA_MS) tela::apagar();
}

bool botaoSeguradoNoBoot() {
  if (digitalRead(PINO_BOTAO) != LOW) return false;
  const unsigned long inicio = millis();
  while (millis() - inicio < BOOT_PAREAR_MS) {
    if (digitalRead(PINO_BOTAO) != LOW) return false;
    delay(10);
  }
  return true;
}

}  // namespace

void setup() {
  setCpuFrequencyMhz(80);  // BLE funciona a 80 MHz e a bateria agradece
  Serial.begin(115200);
  delay(300);

  pinMode(PINO_VIBRACAO, OUTPUT);
  digitalWrite(PINO_VIBRACAO, LOW);
  pinMode(PINO_BOTAO, INPUT_PULLUP);

  const uint64_t mac = ESP.getEfuseMac();
  snprintf(nomeDispositivo, sizeof(nomeDispositivo), "hoodi Pet %02X%02X", (unsigned)((mac >> 32) & 0xFF),
           (unsigned)((mac >> 40) & 0xFF));
  Serial.printf("\nhoodi Pet %s (%s)%s\n", HOODI_VERSAO_FIRMWARE, nomeDispositivo, HOODI_SEM_BLE ? " sem BLE" : "");

  if (!tela::iniciar(PINO_SDA, PINO_SCL)) Serial.println("[tela] OLED nao respondeu em 0x3C/0x3D; seguindo so com vibracao");

  estado.nivel = NIVEL_DESCONHECIDO;
  estado.expressao = Expressao::Dormindo;

  const bool forcarPareamento = botaoSeguradoNoBoot();
  if (!HOODI_SEM_BLE) {
    if (!ble::iniciar(nomeDispositivo)) Serial.println("[ble] falha ao iniciar o Bluetooth");
    if (forcarPareamento || ble::pareamentosSalvos() == 0) abrirPareamento();
  }
  if (!ble::modoPareamento()) mostrarEstado();
  publicarStatus();
  Serial.println("Digite 'st' para o estado ou qualquer coisa para a lista de comandos.");
}

void loop() {
  ble::FrameRecebido f;
  while (ble::proximoComando(f)) processarFrame(f.dados, f.tam == 0xFF ? ble::TAM_MAX_FRAME + 1 : f.tam);

  processarAvisosBle();
  lerSerial();
  tratarBotao();
  tratarDesconexao();
  digitalWrite(PINO_VIBRACAO, vibrador.atualizar(millis()) ? HIGH : LOW);
  tratarTela();
  delay(5);
}
