// Maquina de estados da vibracao, sem delay(): o loop chama atualizar(millis()).
// C++ puro, testado no PC (testes/vibracao_test.cpp).
#pragma once
#include <stddef.h>
#include <stdint.h>
#include "protocolo.h"

namespace hoodi {

struct Passo {
  uint16_t ms;
  bool ligado;
};

inline const Passo* passosDoPadrao(Padrao p, size_t& n) {
  static const Passo curto[] = {{180, true}};
  static const Passo duplo[] = {{180, true}, {160, false}, {180, true}};
  static const Passo longo[] = {{450, true}, {180, false}, {450, true}, {180, false}, {450, true}};
  static const Passo identificar[] = {{80, true}, {80, false}, {80, true}, {80, false}, {80, true},
                                      {80, false}, {80, true}, {80, false}, {80, true}};
  static const Passo perdeuConexao[] = {{700, true}, {250, false}, {150, true}};
  switch (p) {
    case Padrao::Curto: n = sizeof(curto) / sizeof(Passo); return curto;
    case Padrao::Duplo: n = sizeof(duplo) / sizeof(Passo); return duplo;
    case Padrao::Longo: n = sizeof(longo) / sizeof(Passo); return longo;
    case Padrao::Identificar: n = sizeof(identificar) / sizeof(Passo); return identificar;
    case Padrao::PerdeuConexao: n = sizeof(perdeuConexao) / sizeof(Passo); return perdeuConexao;
    default: n = 0; return nullptr;
  }
}

class Vibrador {
 public:
  // Um padrao novo substitui o que estiver tocando.
  void iniciar(Padrao p, uint32_t agora) {
    passos_ = passosDoPadrao(p, total_);
    indice_ = 0;
    inicioPasso_ = agora;
  }
  void parar() { passos_ = nullptr; total_ = 0; }
  bool ativo() const { return passos_ != nullptr; }

  // Retorna o estado desejado do motor neste instante.
  bool atualizar(uint32_t agora) {
    while (passos_ != nullptr) {
      const Passo& passo = passos_[indice_];
      if ((uint32_t)(agora - inicioPasso_) < passo.ms) return passo.ligado;
      inicioPasso_ += passo.ms;
      if (++indice_ >= total_) parar();
    }
    return false;
  }

 private:
  const Passo* passos_ = nullptr;
  size_t total_ = 0;
  size_t indice_ = 0;
  uint32_t inicioPasso_ = 0;
};

// Botao com debounce: clique curto ou pressao longa (dispara enquanto ainda esta apertado).
class Botao {
 public:
  enum class Evento { Nenhum, Clique, PressaoLonga };
  explicit Botao(uint32_t msLonga = 3000, uint32_t msDebounce = 30) : msLonga_(msLonga), msDebounce_(msDebounce) {}

  Evento atualizar(bool apertado, uint32_t agora) {
    if (apertado != bruto_) { bruto_ = apertado; mudouEm_ = agora; }
    if ((uint32_t)(agora - mudouEm_) < msDebounce_) return Evento::Nenhum;
    if (bruto_ != estavel_) {
      estavel_ = bruto_;
      if (estavel_) { apertouEm_ = agora; longaDisparada_ = false; return Evento::Nenhum; }
      return longaDisparada_ ? Evento::Nenhum : Evento::Clique;  // soltou
    }
    if (estavel_ && !longaDisparada_ && (uint32_t)(agora - apertouEm_) >= msLonga_) {
      longaDisparada_ = true;
      return Evento::PressaoLonga;
    }
    return Evento::Nenhum;
  }

 private:
  uint32_t msLonga_, msDebounce_;
  bool bruto_ = false, estavel_ = false, longaDisparada_ = false;
  uint32_t mudouEm_ = 0, apertouEm_ = 0;
};

}  // namespace hoodi
