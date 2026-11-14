// Camada Bluetooth (NimBLE-Arduino 2.5.x). Os callbacks do BLE rodam em outra task:
// eles so enfileiram, e o loop() processa tudo numa task so.
#pragma once
#include <stddef.h>
#include <stdint.h>
#include "config.h"

namespace ble {

enum class TipoAviso : uint8_t { Conectou, Desconectou, MostrarPin, Pareado, PareamentoRecusado };
struct Aviso {
  TipoAviso tipo;
  uint32_t valor;  // PIN em MostrarPin
};

constexpr size_t TAM_MAX_FRAME = 20;
struct FrameRecebido {
  uint8_t dados[TAM_MAX_FRAME];
  uint8_t tam;      // 0xFF = chegou maior que TAM_MAX_FRAME (sera tratado como invalido)
};

#if HOODI_SEM_BLE
inline bool iniciar(const char*) { return false; }
inline bool proximoAviso(Aviso&) { return false; }
inline bool proximoComando(FrameRecebido&) { return false; }
inline bool conectado() { return false; }
inline void publicarStatus(const uint8_t*, size_t) {}
inline void publicarEvento(const uint8_t*, size_t) {}
inline void entrarModoPareamento() {}
inline void sairModoPareamento() {}
inline bool modoPareamento() { return false; }
inline int pareamentosSalvos() { return 0; }
inline void esquecerPareamentos() {}
#else
bool iniciar(const char* nomeDispositivo);
bool proximoAviso(Aviso& out);
bool proximoComando(FrameRecebido& out);
bool conectado();
void publicarStatus(const uint8_t* dados, size_t tam);  // atualiza o valor lido e notifica
void publicarEvento(const uint8_t* dados, size_t tam);
void entrarModoPareamento();
void sairModoPareamento();
bool modoPareamento();
int pareamentosSalvos();
void esquecerPareamentos();
#endif

}  // namespace ble
