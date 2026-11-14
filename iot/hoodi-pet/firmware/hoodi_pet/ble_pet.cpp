#include "ble_pet.h"

#if !HOODI_SEM_BLE

#include <Arduino.h>
#include <NimBLEDevice.h>
#include <string.h>

#include "protocolo.h"

namespace ble {
namespace {

QueueHandle_t filaAvisos = nullptr;
QueueHandle_t filaComandos = nullptr;
NimBLEServer* servidor = nullptr;
NimBLECharacteristic* chrStatus = nullptr;
NimBLECharacteristic* chrEvento = nullptr;

volatile bool emPareamento = false;
volatile bool pareamentoNovoEmCurso = false;  // true entre onPassKeyDisplay e onAuthenticationComplete
volatile uint8_t conexoes = 0;

void avisar(TipoAviso tipo, uint32_t valor = 0) {
  Aviso a{tipo, valor};
  xQueueSend(filaAvisos, &a, 0);
}

class CallbacksServidor : public NimBLEServerCallbacks {
  void onConnect(NimBLEServer* s, NimBLEConnInfo& info) override {
    // Um celular por vez.
    if (s->getConnectedCount() > 1) {
      s->disconnect(info);
      return;
    }
    conexoes = 1;
    pareamentoNovoEmCurso = false;
    avisar(TipoAviso::Conectou);
    // Pede criptografia ja na conexao: celular pareado so recifra; celular novo cai no PIN.
    NimBLEDevice::startSecurity(info.getConnHandle());
  }

  void onDisconnect(NimBLEServer* s, NimBLEConnInfo& info, int motivo) override {
    (void)info; (void)motivo;
    conexoes = s->getConnectedCount();
    pareamentoNovoEmCurso = false;
    if (conexoes == 0) avisar(TipoAviso::Desconectou);
    NimBLEDevice::startAdvertising();  // no NimBLE 2.x o anuncio nao volta sozinho
  }

  // So e chamado num pareamento NOVO (celular ja pareado so recifra).
  uint32_t onPassKeyDisplay() override {
    const uint32_t pin = esp_random() % 1000000UL;
    pareamentoNovoEmCurso = true;
    // Fora do modo pareamento o PIN nao aparece na tela, entao ninguem consegue digitar.
    if (emPareamento) avisar(TipoAviso::MostrarPin, pin);
    return pin;
  }

  void onAuthenticationComplete(NimBLEConnInfo& info) override {
    const bool novo = pareamentoNovoEmCurso;
    pareamentoNovoEmCurso = false;
    if (!info.isEncrypted() || !info.isAuthenticated()) {
      if (novo) avisar(TipoAviso::PareamentoRecusado);
      servidor->disconnect(info);
      return;
    }
    if (!novo) return;  // celular ja pareado voltou
    if (!emPareamento) {
      // Defesa extra: pareamento fora do modo pareamento nao fica salvo.
      NimBLEDevice::deleteBond(info.getIdAddress());
      avisar(TipoAviso::PareamentoRecusado);
      servidor->disconnect(info);
      return;
    }
    // Fica so o celular que acabou de parear.
    const NimBLEAddress atual = info.getIdAddress();
    for (int i = NimBLEDevice::getNumBonds() - 1; i >= 0; i--) {
      const NimBLEAddress salvo = NimBLEDevice::getBondedAddress(i);
      if (!(salvo == atual)) NimBLEDevice::deleteBond(salvo);
    }
    emPareamento = false;
    avisar(TipoAviso::Pareado);
  }
};

class CallbacksComando : public NimBLECharacteristicCallbacks {
  void onWrite(NimBLECharacteristic* c, NimBLEConnInfo& info) override {
    (void)info;
    const NimBLEAttValue valor = c->getValue();
    FrameRecebido f{};
    if (valor.size() > TAM_MAX_FRAME) {
      f.tam = 0xFF;
    } else {
      f.tam = (uint8_t)valor.size();
      memcpy(f.dados, valor.data(), valor.size());
    }
    xQueueSend(filaComandos, &f, 0);  // fila cheia: o comando se perde e o app nao recebe status desta sequencia
  }
};

CallbacksServidor callbacksServidor;
CallbacksComando callbacksComando;

}  // namespace

bool iniciar(const char* nome) {
  filaAvisos = xQueueCreate(8, sizeof(Aviso));
  filaComandos = xQueueCreate(8, sizeof(FrameRecebido));
  if (!filaAvisos || !filaComandos) return false;

  if (!NimBLEDevice::init(nome)) return false;
  // Pareamento com bonding + protecao MITM + Secure Connections.
  // O Pet so tem tela (DISPLAY_ONLY): o celular pede o PIN que aparece no OLED.
  NimBLEDevice::setSecurityAuth(true, true, true);
  NimBLEDevice::setSecurityIOCap(BLE_HS_IO_DISPLAY_ONLY);

  servidor = NimBLEDevice::createServer();
  servidor->setCallbacks(&callbacksServidor, false);  // objeto estatico: o NimBLE nao pode dar delete

  NimBLEService* servico = servidor->createService(hoodi::UUID_SERVICO);
  NimBLECharacteristic* comando = servico->createCharacteristic(
      hoodi::UUID_COMANDO, NIMBLE_PROPERTY::WRITE | NIMBLE_PROPERTY::WRITE_ENC | NIMBLE_PROPERTY::WRITE_AUTHEN,
      hoodi::TAM_COMANDO);
  comando->setCallbacks(&callbacksComando);

  chrStatus = servico->createCharacteristic(
      hoodi::UUID_STATUS,
      NIMBLE_PROPERTY::READ | NIMBLE_PROPERTY::READ_ENC | NIMBLE_PROPERTY::READ_AUTHEN | NIMBLE_PROPERTY::NOTIFY,
      hoodi::TAM_STATUS);
  chrEvento = servico->createCharacteristic(
      hoodi::UUID_EVENTO,
      NIMBLE_PROPERTY::READ | NIMBLE_PROPERTY::READ_ENC | NIMBLE_PROPERTY::READ_AUTHEN | NIMBLE_PROPERTY::NOTIFY,
      hoodi::TAM_EVENTO);

  servidor->start();

  NimBLEAdvertising* anuncio = NimBLEDevice::getAdvertising();
  anuncio->enableScanResponse(true);  // nome vai na resposta: UUID de 128 bits + nome nao cabem em 31 bytes
  anuncio->setName(nome);
  anuncio->addServiceUUID(hoodi::UUID_SERVICO);
  return NimBLEDevice::startAdvertising();
}

bool proximoAviso(Aviso& out) { return filaAvisos && xQueueReceive(filaAvisos, &out, 0) == pdTRUE; }
bool proximoComando(FrameRecebido& out) { return filaComandos && xQueueReceive(filaComandos, &out, 0) == pdTRUE; }
bool conectado() { return conexoes > 0; }

void publicarStatus(const uint8_t* dados, size_t tam) {
  if (!chrStatus) return;
  chrStatus->setValue(dados, tam);
  if (conectado()) chrStatus->notify();
}

void publicarEvento(const uint8_t* dados, size_t tam) {
  if (!chrEvento) return;
  chrEvento->setValue(dados, tam);
  if (conectado()) chrEvento->notify();
}

void entrarModoPareamento() { emPareamento = true; }
void sairModoPareamento() { emPareamento = false; }
bool modoPareamento() { return emPareamento; }
int pareamentosSalvos() { return NimBLEDevice::getNumBonds(); }
void esquecerPareamentos() { NimBLEDevice::deleteAllBonds(); }

}  // namespace ble

#endif  // !HOODI_SEM_BLE
