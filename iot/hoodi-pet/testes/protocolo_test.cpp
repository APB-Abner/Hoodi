// Testes no PC do protocolo, da vibracao e do botao (sem placa).
// Rodar: g++ -std=c++17 -Wall -Wextra -Werror -I../firmware/hoodi_pet protocolo_test.cpp -o t && ./t
#include <cstdio>
#include <cstdlib>
#include <cstring>

#include "protocolo.h"
#include "vibracao.h"

using namespace hoodi;

static int falhas = 0, total = 0;
#define CHECA(cond)                                                     \
  do {                                                                  \
    total++;                                                            \
    if (!(cond)) { falhas++; std::printf("FALHOU %s:%d  %s\n", __FILE__, __LINE__, #cond); } \
  } while (0)

static bool decod(const uint8_t* f, size_t n, Comando& c) { return decodificarComando(f, n, c); }

static void testeEstadoAutomatico() {
  uint8_t f[TAM_COMANDO];
  Comando c;
  for (uint8_t n = 0; n <= 3; n++) {
    codificarComando(TipoComando::Estado, 7, n, AUTOMATICO, AUTOMATICO, f);
    CHECA(decod(f, sizeof f, c));
    CHECA(c.tipo == TipoComando::Estado && c.sequencia == 7 && c.nivel == n);
    CHECA(c.expressao == (Expressao)n);
  }
  codificarComando(TipoComando::Estado, 1, 0, AUTOMATICO, AUTOMATICO, f); decod(f, 9, c); CHECA(c.padrao == Padrao::Nenhum);
  codificarComando(TipoComando::Estado, 1, 1, AUTOMATICO, AUTOMATICO, f); decod(f, 9, c); CHECA(c.padrao == Padrao::Curto);
  codificarComando(TipoComando::Estado, 1, 2, AUTOMATICO, AUTOMATICO, f); decod(f, 9, c); CHECA(c.padrao == Padrao::Duplo);
  codificarComando(TipoComando::Estado, 1, 3, AUTOMATICO, AUTOMATICO, f); decod(f, 9, c); CHECA(c.padrao == Padrao::Longo);
}

static void testeEstadoExplicito() {
  uint8_t f[TAM_COMANDO];
  Comando c;
  // App atualiza a carinha sem vibrar (nivel caiu): padrao 0 explicito.
  codificarComando(TipoComando::Estado, 2, 3, 0, AUTOMATICO, f);
  CHECA(decod(f, 9, c) && c.padrao == Padrao::Nenhum && c.expressao == Expressao::Alerta);
  // Expressao Dormindo e permitida; Desconectado/Pareamento (internas) nao.
  codificarComando(TipoComando::Estado, 2, 1, 1, (uint8_t)Expressao::Dormindo, f);
  CHECA(decod(f, 9, c) && c.expressao == Expressao::Dormindo);
  codificarComando(TipoComando::Estado, 2, 1, 1, (uint8_t)Expressao::Desconectado, f);
  CHECA(!decod(f, 9, c));
  codificarComando(TipoComando::Estado, 2, 1, 1, (uint8_t)Expressao::Pareamento, f);
  CHECA(!decod(f, 9, c));
  // Padroes internos (Identificar/PerdeuConexao) nao podem vir no Estado.
  codificarComando(TipoComando::Estado, 2, 1, (uint8_t)Padrao::Identificar, AUTOMATICO, f);
  CHECA(!decod(f, 9, c));
}

static void testeDesconhecidoNaoVibra() {
  uint8_t f[TAM_COMANDO];
  Comando c;
  codificarComando(TipoComando::Estado, 3, NIVEL_DESCONHECIDO, (uint8_t)Padrao::Longo, AUTOMATICO, f);
  CHECA(decod(f, 9, c));
  CHECA(c.nivel == NIVEL_DESCONHECIDO && c.padrao == Padrao::Nenhum && c.expressao == Expressao::Desconhecido);
}

static void testeOutrosTipos() {
  uint8_t f[TAM_COMANDO];
  Comando c;
  codificarComando(TipoComando::Identificar, 4, 0x77, 0x77, 0x77, f);  // bytes 6..8 ignorados
  CHECA(decod(f, 9, c) && c.tipo == TipoComando::Identificar && c.padrao == Padrao::Identificar);
  codificarComando(TipoComando::Silenciar, 5, 0, 0, 0, f);
  CHECA(decod(f, 9, c) && c.tipo == TipoComando::Silenciar);
  codificarComando(TipoComando::FimTrajeto, 6, 0, 0, 0, f);
  CHECA(decod(f, 9, c) && c.expressao == Expressao::Dormindo && c.nivel == NIVEL_DESCONHECIDO);
}

static void testeInvalidos() {
  uint8_t f[TAM_COMANDO];
  Comando c;
  codificarComando(TipoComando::Estado, 1, 1, AUTOMATICO, AUTOMATICO, f);
  CHECA(!decod(f, 8, c));            // tamanho errado
  CHECA(!decod(f, 10, c));
  CHECA(!decod(nullptr, 9, c));
  f[0] = 2; CHECA(!decod(f, 9, c));  // versao desconhecida
  f[0] = 1; f[1] = 0; CHECA(!decod(f, 9, c));
  f[1] = 5; CHECA(!decod(f, 9, c));
  codificarComando(TipoComando::Estado, 0, 1, AUTOMATICO, AUTOMATICO, f);
  CHECA(!decod(f, 9, c));            // sequencia 0 e invalida
  codificarComando(TipoComando::Estado, 1, 4, AUTOMATICO, AUTOMATICO, f);
  CHECA(!decod(f, 9, c));            // nivel 4 nao existe
  // Frame invalido com sequencia legivel devolve a sequencia (o status informa qual foi recusado).
  codificarComando(TipoComando::Estado, 42, 9, AUTOMATICO, AUTOMATICO, f);
  CHECA(!decod(f, 9, c) && c.sequencia == 42);
}

static void testeLittleEndian() {
  uint8_t f[TAM_COMANDO];
  codificarComando(TipoComando::Estado, 0x01020304, 1, AUTOMATICO, AUTOMATICO, f);
  CHECA(f[2] == 0x04 && f[3] == 0x03 && f[4] == 0x02 && f[5] == 0x01);
  const uint8_t esperado[] = {0x01, 0x01, 0x04, 0x03, 0x02, 0x01, 0x01, 0xFF, 0xFF};
  CHECA(std::memcmp(f, esperado, 9) == 0);
}

static void testeStatusEEvento() {
  Status s;
  s.resultado = Resultado::SequenciaAntiga;
  s.sequencia = 0xAABBCCDD;
  s.nivel = 2;
  s.expressao = Expressao::Preocupada;
  s.flags = FLAG_VIBRANDO | FLAG_TRAJETO_ATIVO;
  uint8_t out[TAM_STATUS];
  codificarStatus(s, out);
  const uint8_t esperado[] = {0x01, 0x01, 0xDD, 0xCC, 0xBB, 0xAA, 0x02, 0x02, 0x05};
  CHECA(std::memcmp(out, esperado, 9) == 0);

  uint8_t ev[TAM_EVENTO];
  codificarEvento(TipoEvento::PressaoLonga, 3, ev);
  const uint8_t esperadoEv[] = {0x01, 0x02, 0x03, 0x00, 0x00, 0x00};
  CHECA(std::memcmp(ev, esperadoEv, 6) == 0);
}

static void testeSequenciador() {
  Sequenciador s;
  CHECA(s.aceitar(1));
  CHECA(!s.aceitar(1));  // retransmissao
  CHECA(s.aceitar(5));   // pular numeros e permitido
  CHECA(!s.aceitar(3));  // fora de ordem
  s.reiniciar();         // nova conexao
  CHECA(s.aceitar(1));
  CHECA(s.aceitar(0xFFFFFFFFu));
  CHECA(!s.aceitar(0xFFFFFFFFu));
}

static void testeVibrador() {
  Vibrador v;
  CHECA(!v.atualizar(0) && !v.ativo());
  v.iniciar(Padrao::Duplo, 1000);  // 180 on, 160 off, 180 on
  CHECA(v.ativo());
  CHECA(v.atualizar(1000) == true);
  CHECA(v.atualizar(1179) == true);
  CHECA(v.atualizar(1180) == false);
  CHECA(v.atualizar(1339) == false);
  CHECA(v.atualizar(1340) == true);
  CHECA(v.atualizar(1519) == true);
  CHECA(v.atualizar(1520) == false && !v.ativo());
  // Loop atrasado (pulou varios passos de uma vez) termina sem travar.
  v.iniciar(Padrao::Longo, 0);
  CHECA(v.atualizar(100000) == false && !v.ativo());
  // Novo padrao substitui o atual; Nenhum nao vibra.
  v.iniciar(Padrao::Longo, 0);
  v.iniciar(Padrao::Curto, 10);
  CHECA(v.atualizar(10) == true && v.atualizar(190) == false);
  v.iniciar(Padrao::Nenhum, 0);
  CHECA(!v.ativo());
  // millis() dando a volta no u32.
  v.iniciar(Padrao::Curto, 0xFFFFFFF0u);
  CHECA(v.atualizar(0x00000010u) == true);
  CHECA(v.atualizar(0x000000A5u) == false);
  // Silenciar.
  v.iniciar(Padrao::Identificar, 0);
  v.parar();
  CHECA(!v.atualizar(1) && !v.ativo());
}

static void testeBotao() {
  using E = Botao::Evento;
  Botao b(3000, 30);
  // Ruido de 10 ms nao conta.
  CHECA(b.atualizar(true, 0) == E::Nenhum);
  CHECA(b.atualizar(false, 10) == E::Nenhum);
  CHECA(b.atualizar(false, 100) == E::Nenhum);
  // Clique: aperta 200 ms e solta.
  CHECA(b.atualizar(true, 1000) == E::Nenhum);
  CHECA(b.atualizar(true, 1040) == E::Nenhum);
  CHECA(b.atualizar(false, 1200) == E::Nenhum);
  CHECA(b.atualizar(false, 1240) == E::Clique);
  CHECA(b.atualizar(false, 1300) == E::Nenhum);
  // Pressao longa dispara uma vez, ainda apertado, e soltar depois nao gera clique.
  CHECA(b.atualizar(true, 2000) == E::Nenhum);
  CHECA(b.atualizar(true, 2040) == E::Nenhum);
  CHECA(b.atualizar(true, 5039) == E::Nenhum);
  CHECA(b.atualizar(true, 5040) == E::PressaoLonga);
  CHECA(b.atualizar(true, 9000) == E::Nenhum);
  CHECA(b.atualizar(false, 9100) == E::Nenhum);
  CHECA(b.atualizar(false, 9200) == E::Nenhum);
}

int main() {
  testeEstadoAutomatico();
  testeEstadoExplicito();
  testeDesconhecidoNaoVibra();
  testeOutrosTipos();
  testeInvalidos();
  testeLittleEndian();
  testeStatusEEvento();
  testeSequenciador();
  testeVibrador();
  testeBotao();
  std::printf("%d/%d verificacoes passaram\n", total - falhas, total);
  return falhas == 0 ? EXIT_SUCCESS : EXIT_FAILURE;
}
