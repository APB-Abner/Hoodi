// Protocolo BLE app <-> hoodi Pet, versao 1.
// C++ puro, sem Arduino: compila no PC para os testes (testes/protocolo_test.cpp).
// Especificacao: docs/PROTOCOLO_BLE.md. Qualquer mudanca aqui muda tambem app/hoodiPetProtocolo.ts.
#pragma once
#include <stddef.h>
#include <stdint.h>

namespace hoodi {

constexpr uint8_t VERSAO_PROTOCOLO = 1;

constexpr const char* UUID_SERVICO = "a70ab40e-599a-4803-89db-3b1667e7d753";
constexpr const char* UUID_COMANDO = "ad7790b4-bf5d-4ee2-a6b2-5faac8195117";  // write (com resposta), exige pareamento
constexpr const char* UUID_STATUS  = "147dca7f-bc45-4789-85bb-adc57bc40124";  // read + notify, exige pareamento
constexpr const char* UUID_EVENTO  = "6e9d9b1f-3aef-4015-a8ab-162399270a78";  // notify, exige pareamento

constexpr uint8_t NIVEL_DESCONHECIDO = 0xFF;
constexpr uint8_t AUTOMATICO = 0xFF;  // padrao/expressao derivados do nivel

enum class TipoComando : uint8_t { Estado = 1, Identificar = 2, Silenciar = 3, FimTrajeto = 4 };
enum class Padrao : uint8_t { Nenhum = 0, Curto = 1, Duplo = 2, Longo = 3, Identificar = 4, PerdeuConexao = 5 };
enum class Expressao : uint8_t {
  Tranquila = 0, Atenta = 1, Preocupada = 2, Alerta = 3, Desconhecido = 4, Dormindo = 5,
  // Internas: o app nao pode pedir estas.
  Desconectado = 6, Pareamento = 7
};
enum class Resultado : uint8_t { Aplicado = 0, SequenciaAntiga = 1, Invalido = 2, Nenhum = 0xFF };
enum class TipoEvento : uint8_t { Clique = 1, PressaoLonga = 2 };

constexpr size_t TAM_COMANDO = 9;
constexpr size_t TAM_STATUS = 9;
constexpr size_t TAM_EVENTO = 6;

constexpr uint8_t FLAG_VIBRANDO = 0x01;
constexpr uint8_t FLAG_TELA_LIGADA = 0x02;
constexpr uint8_t FLAG_TRAJETO_ATIVO = 0x04;

// Comando ja validado e com os valores AUTOMATICO resolvidos.
struct Comando {
  TipoComando tipo = TipoComando::Estado;
  uint32_t sequencia = 0;
  uint8_t nivel = NIVEL_DESCONHECIDO;
  Padrao padrao = Padrao::Nenhum;
  Expressao expressao = Expressao::Desconhecido;
};

struct Status {
  Resultado resultado = Resultado::Nenhum;
  uint32_t sequencia = 0;           // sequencia do ultimo comando avaliado
  uint8_t nivel = NIVEL_DESCONHECIDO;
  Expressao expressao = Expressao::Dormindo;
  uint8_t flags = 0;
};

inline uint32_t lerU32(const uint8_t* p) {
  return (uint32_t)p[0] | ((uint32_t)p[1] << 8) | ((uint32_t)p[2] << 16) | ((uint32_t)p[3] << 24);
}
inline void escreverU32(uint8_t* p, uint32_t v) {
  p[0] = (uint8_t)v; p[1] = (uint8_t)(v >> 8); p[2] = (uint8_t)(v >> 16); p[3] = (uint8_t)(v >> 24);
}

inline Padrao padraoDoNivel(uint8_t nivel) {
  switch (nivel) {
    case 1: return Padrao::Curto;
    case 2: return Padrao::Duplo;
    case 3: return Padrao::Longo;
    default: return Padrao::Nenhum;  // 0 e desconhecido nao vibram
  }
}

inline Expressao expressaoDoNivel(uint8_t nivel) {
  return nivel <= 3 ? (Expressao)nivel : Expressao::Desconhecido;
}

// Frame do comando (9 bytes, little-endian):
// [0] versao=1 [1] tipo [2..5] sequencia u32 [6] nivel [7] padrao [8] expressao
// Bytes 6..8 so contam no tipo Estado; nos outros tipos sao ignorados (enviar 0).
// Retorna false para frame invalido; nesse caso out.sequencia ainda traz a sequencia lida, se houver.
inline bool decodificarComando(const uint8_t* d, size_t tam, Comando& out) {
  out = Comando{};
  if (d == nullptr || tam != TAM_COMANDO) return false;
  out.sequencia = lerU32(d + 2);
  if (d[0] != VERSAO_PROTOCOLO) return false;
  if (out.sequencia == 0) return false;
  if (d[1] < 1 || d[1] > 4) return false;
  out.tipo = (TipoComando)d[1];

  switch (out.tipo) {
    case TipoComando::Estado: {
      const uint8_t nivel = d[6], padrao = d[7], expressao = d[8];
      if (nivel > 3 && nivel != NIVEL_DESCONHECIDO) return false;
      if (padrao > (uint8_t)Padrao::Longo && padrao != AUTOMATICO) return false;
      if (expressao > (uint8_t)Expressao::Dormindo && expressao != AUTOMATICO) return false;
      out.nivel = nivel;
      out.padrao = padrao == AUTOMATICO ? padraoDoNivel(nivel) : (Padrao)padrao;
      out.expressao = expressao == AUTOMATICO ? expressaoDoNivel(nivel) : (Expressao)expressao;
      // Regra do produto: sem dado nao e seguro e nao vibra.
      if (nivel == NIVEL_DESCONHECIDO) out.padrao = Padrao::Nenhum;
      return true;
    }
    case TipoComando::Identificar:
      out.padrao = Padrao::Identificar;
      return true;
    case TipoComando::Silenciar:
      return true;
    case TipoComando::FimTrajeto:
      out.nivel = NIVEL_DESCONHECIDO;
      out.expressao = Expressao::Dormindo;
      return true;
  }
  return false;
}

// Monta um frame (usado pelo Serial de bancada e pelos testes).
inline void codificarComando(TipoComando tipo, uint32_t sequencia, uint8_t nivel, uint8_t padrao,
                             uint8_t expressao, uint8_t out[TAM_COMANDO]) {
  out[0] = VERSAO_PROTOCOLO; out[1] = (uint8_t)tipo; escreverU32(out + 2, sequencia);
  out[6] = nivel; out[7] = padrao; out[8] = expressao;
}

// Status (9 bytes): [0] versao [1] resultado [2..5] sequencia [6] nivel [7] expressao [8] flags
inline void codificarStatus(const Status& s, uint8_t out[TAM_STATUS]) {
  out[0] = VERSAO_PROTOCOLO; out[1] = (uint8_t)s.resultado; escreverU32(out + 2, s.sequencia);
  out[6] = s.nivel; out[7] = (uint8_t)s.expressao; out[8] = s.flags;
}

// Evento (6 bytes): [0] versao [1] tipo [2..5] contador u32 (cresce a cada evento desde o boot)
inline void codificarEvento(TipoEvento tipo, uint32_t contador, uint8_t out[TAM_EVENTO]) {
  out[0] = VERSAO_PROTOCOLO; out[1] = (uint8_t)tipo; escreverU32(out + 2, contador);
}

// A sequencia zera a cada nova conexao: o app comeca em 1 depois de conectar.
// Comando com sequencia <= ultima aceita e ignorado (retransmissao ou fora de ordem).
struct Sequenciador {
  uint32_t ultima = 0;
  void reiniciar() { ultima = 0; }
  bool aceitar(uint32_t s) {
    if (s <= ultima) return false;
    ultima = s;
    return true;
  }
};

}  // namespace hoodi
