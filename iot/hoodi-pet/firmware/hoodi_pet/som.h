// Maquina de estados do alto-falante, sem delay(): o loop chama atualizar(millis()) e toca a frequencia
// devolvida (0 = silencio). C++ puro, testado no PC (testes/protocolo_test.cpp).
// Hardware: GPIO5 -> 1 kOhm -> base do NPN; alto-falante entre +BAT (via 33 Ohm) e o coletor; diodo no falante.
#pragma once
#include <stddef.h>
#include <stdint.h>
#include "protocolo.h"

namespace hoodi {

enum class Toque : uint8_t {
  Nenhum,
  Nivel1, Nivel2, Nivel3,   // acompanham a vibracao do alerta (curto, duplo, longo)
  Identificar,              // ajuda a achar o Pet
  PerdeuConexao,
  Pin,                      // PIN de pareamento apareceu na tela
  Pareado,
  Ajuda,                    // pedido de ajuda enviado (desligado por padrao: ver SOM_NO_PEDIDO_DE_AJUDA)
  AjudaSemCelular           // pedido de ajuda NAO saiu: Pet sem celular conectado
};

struct Nota {
  uint16_t ms;
  uint16_t hz;  // 0 = silencio
};

inline Toque toqueDoPadrao(Padrao p) {
  switch (p) {
    case Padrao::Curto: return Toque::Nivel1;
    case Padrao::Duplo: return Toque::Nivel2;
    case Padrao::Longo: return Toque::Nivel3;
    case Padrao::Identificar: return Toque::Identificar;
    case Padrao::PerdeuConexao: return Toque::PerdeuConexao;
    default: return Toque::Nenhum;
  }
}

inline const Nota* notasDoToque(Toque t, size_t& n) {
  static const Nota nivel1[] = {{120, 2000}};
  static const Nota nivel2[] = {{120, 2000}, {100, 0}, {120, 2000}};
  static const Nota nivel3[] = {{150, 2600}, {80, 0}, {150, 2600}, {80, 0}, {300, 2600}};
  static const Nota identificar[] = {{70, 2400}, {50, 0}, {70, 3000}, {50, 0}, {70, 2400}, {50, 0}, {70, 3000}};
  static const Nota perdeuConexao[] = {{250, 1600}, {60, 0}, {350, 1000}};
  static const Nota pin[] = {{90, 1800}};
  static const Nota pareado[] = {{100, 1500}, {40, 0}, {160, 2200}};
  static const Nota ajuda[] = {{100, 2200}, {40, 0}, {100, 2200}};
  static const Nota ajudaSemCelular[] = {{300, 900}, {80, 0}, {300, 900}};
  switch (t) {
    case Toque::Nivel1: n = sizeof(nivel1) / sizeof(Nota); return nivel1;
    case Toque::Nivel2: n = sizeof(nivel2) / sizeof(Nota); return nivel2;
    case Toque::Nivel3: n = sizeof(nivel3) / sizeof(Nota); return nivel3;
    case Toque::Identificar: n = sizeof(identificar) / sizeof(Nota); return identificar;
    case Toque::PerdeuConexao: n = sizeof(perdeuConexao) / sizeof(Nota); return perdeuConexao;
    case Toque::Pin: n = sizeof(pin) / sizeof(Nota); return pin;
    case Toque::Pareado: n = sizeof(pareado) / sizeof(Nota); return pareado;
    case Toque::Ajuda: n = sizeof(ajuda) / sizeof(Nota); return ajuda;
    case Toque::AjudaSemCelular: n = sizeof(ajudaSemCelular) / sizeof(Nota); return ajudaSemCelular;
    default: n = 0; return nullptr;
  }
}

class Som {
 public:
  // Um toque novo substitui o que estiver tocando. Toque::Nenhum nao interrompe nada.
  void iniciar(Toque t, uint32_t agora) {
    size_t n = 0;
    const Nota* notas = notasDoToque(t, n);
    if (notas == nullptr) return;
    notas_ = notas;
    total_ = n;
    indice_ = 0;
    inicioNota_ = agora;
  }
  void parar() { notas_ = nullptr; total_ = 0; }
  bool ativo() const { return notas_ != nullptr; }

  // Frequencia desejada neste instante (0 = silencio).
  uint16_t atualizar(uint32_t agora) {
    while (notas_ != nullptr) {
      const Nota& nota = notas_[indice_];
      if ((uint32_t)(agora - inicioNota_) < nota.ms) return nota.hz;
      inicioNota_ += nota.ms;
      if (++indice_ >= total_) parar();
    }
    return 0;
  }

 private:
  const Nota* notas_ = nullptr;
  size_t total_ = 0;
  size_t indice_ = 0;
  uint32_t inicioNota_ = 0;
};

}  // namespace hoodi
