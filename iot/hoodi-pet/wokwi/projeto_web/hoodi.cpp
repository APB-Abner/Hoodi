// hoodi Pet - firmware num arquivo so, gerado por scripts/gerar_wokwi_web.py. Nao edite aqui:
// edite firmware/hoodi_pet/ e gere de novo. Bluetooth desligado (o Wokwi nao simula BLE).
#define HOODI_SEM_BLE 1

// ===== config.h =====
// Configuracao do hoodi Pet. Ligacoes conforme a carcaca v7b (3 botoes + alto-falante).

// 1 = sem Bluetooth (Wokwi nao simula BLE): comandos chegam so pelo Serial.
#ifndef HOODI_SEM_BLE
#define HOODI_SEM_BLE 0
#endif

// 1 = build de simulacao (Wokwi). Por padrao acompanha HOODI_SEM_BLE.
#ifndef HOODI_WOKWI
#define HOODI_WOKWI HOODI_SEM_BLE
#endif

#define HOODI_VERSAO_FIRMWARE "1.1.0"

// Pinos (ESP32-C3 SuperMini). GPIO8 (LED) e GPIO9 (BOOT) ficam livres: afetam o boot. GPIO2 tambem e de boot.
constexpr int PINO_SDA = 6;
constexpr int PINO_SCL = 7;
constexpr int PINO_VIBRACAO = 3;          // IN do modulo de vibracao (VCC no +BAT). Nunca o motor direto.
constexpr int PINO_SOM = 5;               // base do NPN via 1 kOhm. Nunca o alto-falante direto no GPIO.
constexpr int PINO_BOTAO_TELA = 4;        // botao 1: clique acende a tela e avisa o app; segurar ao ligar = pareamento
constexpr int PINO_BOTAO_SILENCIAR = 10;  // botao 2: clique corta vibracao e som
#if HOODI_WOKWI
constexpr int PINO_BOTAO_AJUDA = 1;       // no Wokwi o GPIO20 e o RX do Serial Monitor; la o botao 3 fica no GPIO1
#else
constexpr int PINO_BOTAO_AJUDA = 20;      // botao 3: segurar 3 s = pedido de ajuda. GPIO20 e o RX da UART0:
                                          // exige "USB CDC On Boot: Enabled" (o Serial vai pela USB)
#endif

// Som. Silenciar (botao 2 ou comando do app) corta som e vibracao juntos.
constexpr bool SOM_HABILITADO = true;
// Som no pedido de ajuda (enviado ou falhou por falta de celular). Desligado de proposito: quem pede ajuda
// pode estar perto de quem ameaca, e um bipe chama atencao. Com false, a resposta fica so na vibracao:
// curta = enviado; longa + curta = NAO saiu (sem celular).
constexpr bool SOM_NO_PEDIDO_DE_AJUDA = false;

constexpr unsigned long TELA_APAGA_MS = 10000;          // apaga a tela apos 10 s sem novidade (0 = nunca apaga, bom pra demo)
constexpr unsigned long PRESSAO_LONGA_MS = 3000;        // botao 3 segurado = evento PressaoLonga (pedido de ajuda)
constexpr unsigned long BOOT_PAREAR_MS = 2000;          // segurar o botao 1 ao ligar = modo pareamento
constexpr unsigned long PAREAMENTO_DURACAO_MS = 120000; // modo pareamento fecha sozinho em 2 min
constexpr unsigned long AVISO_DESCONEXAO_MS = 20000;    // vibra 1x se ficar 20 s sem o celular durante um trajeto

// ===== protocolo.h =====
// Protocolo BLE app <-> hoodi Pet, versao 1.
// C++ puro, sem Arduino: compila no PC para os testes (testes/protocolo_test.cpp).
// Especificacao: docs/PROTOCOLO_BLE.md. Qualquer mudanca aqui muda tambem app/hoodiPetProtocolo.ts.

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

// ===== vibracao.h =====
// Maquina de estados da vibracao, sem delay(): o loop chama atualizar(millis()).
// C++ puro, testado no PC (testes/vibracao_test.cpp).

#include <stddef.h>
#include <stdint.h>

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

// ===== som.h =====
// Maquina de estados do alto-falante, sem delay(): o loop chama atualizar(millis()) e toca a frequencia
// devolvida (0 = silencio). C++ puro, testado no PC (testes/protocolo_test.cpp).
// Hardware: GPIO5 -> 1 kOhm -> base do NPN; alto-falante entre +BAT (via 33 Ohm) e o coletor; diodo no falante.

#include <stddef.h>
#include <stdint.h>

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

// ===== carinhas.h =====
// GERADO por scripts/gerar_carinhas.py. Nao edite aqui: edite o script e gere de novo.
// Grade 18 x 12 no estilo do app (pixel art). '#' aceso, '.' apagado.

namespace carinhas {

constexpr int LARGURA = 18;
constexpr int ALTURA = 12;

// Mesma ordem do enum hoodi::Expressao (0..5) + Desconectado (6).
constexpr const char* TRANQUILA[ALTURA] = {
  "..................",
  "..................",
  "..................",
  ".....##....##.....",
  ".....##....##.....",
  ".....##....##.....",
  "..................",
  "..................",
  ".......#..#.......",
  "........##........",
  "..................",
  "..................",
};
constexpr const char* ATENTA[ALTURA] = {
  "..................",
  "..................",
  "..................",
  ".....##....##.....",
  ".....##....##.....",
  ".....##....##.....",
  "..................",
  "..................",
  "..................",
  ".......####.......",
  "..................",
  "..................",
};
constexpr const char* PREOCUPADA[ALTURA] = {
  "..................",
  "......#....#......",
  ".....#......#.....",
  "..................",
  ".....##....##.....",
  ".....##....##.....",
  ".....##....##.....",
  "..................",
  "..................",
  "........##........",
  ".......#..#.......",
  "..................",
};
constexpr const char* ALERTA[ALTURA] = {
  "..................",
  "..................",
  "................#.",
  "....###....###..#.",
  "....###....###..#.",
  "....###....###..#.",
  "................#.",
  "........##........",
  ".......#..#.....#.",
  ".......#..#.......",
  "........##........",
  "..................",
};
constexpr const char* DESCONHECIDO[ALTURA] = {
  "..................",
  "........###.......",
  ".......#...#......",
  "...........#......",
  "..........#.......",
  ".........#........",
  ".........#........",
  "..................",
  ".........#........",
  "..................",
  "..................",
  "..................",
};
constexpr const char* DORMINDO[ALTURA] = {
  ".............####.",
  "...............#..",
  "..............#...",
  ".............####.",
  "..................",
  ".....##....##.....",
  "..................",
  "..................",
  "..................",
  "........##........",
  "..................",
  "..................",
};
constexpr const char* DESCONECTADO[ALTURA] = {
  "..............#.#.",
  "...............#..",
  "..............#.#.",
  "..................",
  ".....##....##.....",
  ".....##....##.....",
  "..................",
  "..................",
  "..................",
  "........##........",
  "..................",
  "..................",
};
constexpr const char* TRANQUILA_PISCANDO[ALTURA] = {
  "..................",
  "..................",
  "..................",
  "..................",
  "..................",
  ".....##....##.....",
  "..................",
  "..................",
  ".......#..#.......",
  "........##........",
  "..................",
  "..................",
};
constexpr const char* ATENTA_PISCANDO[ALTURA] = {
  "..................",
  "..................",
  "..................",
  "..................",
  "..................",
  ".....##....##.....",
  "..................",
  "..................",
  "..................",
  ".......####.......",
  "..................",
  "..................",
};

}  // namespace carinhas

// ===== tela.h =====
// OLED SSD1306 128x64 I2C: carinhas por nivel, PIN de pareamento e mensagens curtas.

#include <stdint.h>

namespace tela {

bool iniciar(int sda, int scl);          // false se o OLED nao respondeu (o Pet continua vibrando)
bool disponivel();
void expressao(hoodi::Expressao e, uint8_t nivel, bool bleConectado);
void pin(uint32_t pin);                  // codigo de 6 digitos para digitar no celular
void pareamento(const char* nomeDispositivo);
void mensagem(const char* linha1, const char* linha2);
void animar(unsigned long agora);       // piscada das carinhas tranquila/atenta (chamar no loop)
void ligar();
void apagar();
bool ligada();

}  // namespace tela

// ===== ble_pet.h =====
// Camada Bluetooth (NimBLE-Arduino 2.5.x). Os callbacks do BLE rodam em outra task:
// eles so enfileiram, e o loop() processa tudo numa task so.

#include <stddef.h>
#include <stdint.h>

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

// ===== pet.h =====
// Etapas do hoodi Pet chamadas pelo setup() e pelo loop() de hoodi_pet.ino.

namespace pet {

void iniciar();            // pinos, tela, nome Bluetooth e modo pareamento

void receberComandos();    // comandos do app que chegaram pelo Bluetooth
void atualizarBle();       // conectou, desconectou, PIN na tela, pareado
void atualizarSerial();    // comandos de bancada (n0..n3, id, fim, st...)
void atualizarBotoes();    // botao 1 tela, botao 2 silenciar, botao 3 ajuda (pressao longa)
void atualizarConexao();   // vibra 1x se o celular sumir durante um trajeto
void atualizarVibracao();  // liga/desliga o motor conforme o padrao em andamento
void atualizarSom();       // toca a nota do toque em andamento no alto-falante (0 = silencio)
void atualizarTela();      // apaga a tela depois de 10 s sem novidade

}  // namespace pet

// ===== tela.cpp =====
#include <Adafruit_GFX.h>
#include <Adafruit_SSD1306.h>
#include <Arduino.h>
#include <Wire.h>

namespace tela {
namespace {

Adafruit_SSD1306 oled(128, 64, &Wire, -1);
bool ok = false;
bool acesa = false;

// OLED de duas cores: as 16 linhas de cima sao amarelas (texto) e as 48 de baixo azuis (carinha).
// A grade 18 x 12 do app vira blocos de 4 x 4 pixels: 72 x 48, centralizada na parte azul.
// Cada quadradinho aceso e 3 x 3 com 1 pixel de folga, imitando a grade do app.
constexpr int CELULA = 4;
constexpr int GRADE_X = (128 - carinhas::LARGURA * CELULA) / 2;
constexpr int GRADE_Y = 16;

// Estado da ultima carinha desenhada, para a piscada redesenhar igual.
hoodi::Expressao expressaoAtual = hoodi::Expressao::Dormindo;
uint8_t nivelAtual = hoodi::NIVEL_DESCONHECIDO;
bool bleAtual = false;
bool mostrandoCarinha = false;  // false quando a tela mostra PIN ou mensagem
bool piscando = false;
unsigned long proximaPiscada = 0;
constexpr unsigned long DURACAO_PISCADA_MS = 160;

const char* nomeNivel(uint8_t nivel) {
  switch (nivel) {
    case 0: return "tranquilo";
    case 1: return "atencao";
    case 2: return "cuidado";
    case 3: return "alerta";
    default: return "sem dado";
  }
}

const char* const* gradeDa(hoodi::Expressao e, bool fechado) {
  using hoodi::Expressao;
  switch (e) {
    case Expressao::Tranquila: return fechado ? carinhas::TRANQUILA_PISCANDO : carinhas::TRANQUILA;
    case Expressao::Atenta: return fechado ? carinhas::ATENTA_PISCANDO : carinhas::ATENTA;
    case Expressao::Preocupada: return carinhas::PREOCUPADA;
    case Expressao::Alerta: return carinhas::ALERTA;
    case Expressao::Dormindo: return carinhas::DORMINDO;
    case Expressao::Desconectado: return carinhas::DESCONECTADO;
    default: return carinhas::DESCONHECIDO;
  }
}

bool pisca(hoodi::Expressao e) {
  return e == hoodi::Expressao::Tranquila || e == hoodi::Expressao::Atenta;
}

const char* rotulo(hoodi::Expressao e, uint8_t nivel) {
  using hoodi::Expressao;
  if (e == Expressao::Dormindo) return "dormindo";
  if (e == Expressao::Desconectado) return "sem celular";
  if (e == Expressao::Desconhecido) return "sem dado";
  return nomeNivel(nivel);
}

void desenharGrade(const char* const* grade) {
  for (int r = 0; r < carinhas::ALTURA; r++) {
    for (int c = 0; c < carinhas::LARGURA; c++) {
      if (grade[r][c] == '#') {
        oled.fillRect(GRADE_X + c * CELULA, GRADE_Y + r * CELULA, CELULA - 1, CELULA - 1, SSD1306_WHITE);
      }
    }
  }
}

void desenharCarinha() {
  oled.clearDisplay();
  oled.setTextSize(1);
  oled.setTextColor(SSD1306_WHITE);
  oled.setCursor(0, 4);
  oled.print(rotulo(expressaoAtual, nivelAtual));
  // Indicador de conexao no canto: B = celular conectado, x = sem celular.
  oled.setCursor(122, 4);
  oled.print(bleAtual ? "B" : "x");
  desenharGrade(gradeDa(expressaoAtual, piscando));
  oled.display();
}

void agendarPiscada(unsigned long agora) {
  proximaPiscada = agora + 2500 + (esp_random() % 3000);  // a cada 2,5 a 5,5 s, como bicho de verdade
}

}  // namespace

bool iniciar(int sda, int scl) {
  Wire.begin(sda, scl);
  ok = oled.begin(SSD1306_SWITCHCAPVCC, 0x3C);
  if (!ok) ok = oled.begin(SSD1306_SWITCHCAPVCC, 0x3D);
  if (ok) { oled.clearDisplay(); oled.display(); acesa = true; }
  return ok;
}

bool disponivel() { return ok; }

void ligar() {
  if (!ok || acesa) return;
  oled.ssd1306_command(SSD1306_DISPLAYON);
  acesa = true;
}

void apagar() {
  if (!ok || !acesa) return;
  oled.ssd1306_command(SSD1306_DISPLAYOFF);  // economiza bateria; a RAM da tela continua
  acesa = false;
}

bool ligada() { return ok && acesa; }

void expressao(hoodi::Expressao e, uint8_t nivel, bool bleConectado) {
  if (!ok) return;
  expressaoAtual = e;
  nivelAtual = nivel;
  bleAtual = bleConectado;
  mostrandoCarinha = true;
  piscando = false;
  agendarPiscada(millis());
  desenharCarinha();
  ligar();
}

void animar(unsigned long agora) {
  if (!ok || !acesa || !mostrandoCarinha || !pisca(expressaoAtual)) return;
  if (!piscando && (long)(agora - proximaPiscada) >= 0) {
    piscando = true;
    desenharCarinha();
  } else if (piscando && (long)(agora - proximaPiscada) >= (long)DURACAO_PISCADA_MS) {
    piscando = false;
    agendarPiscada(agora);
    desenharCarinha();
  }
}

void pin(uint32_t valor) {
  if (!ok) return;
  mostrandoCarinha = false;
  char texto[8];
  snprintf(texto, sizeof(texto), "%06lu", (unsigned long)(valor % 1000000UL));
  oled.clearDisplay();
  oled.setTextColor(SSD1306_WHITE);
  oled.setTextSize(1);
  oled.setCursor(0, 4);
  oled.print("Digite no celular:");
  oled.setTextSize(3);
  oled.setCursor(10, 28);
  oled.print(texto);
  oled.display();
  ligar();
}

void pareamento(const char* nome) {
  mensagem("Modo pareamento", nome);
}

void mensagem(const char* linha1, const char* linha2) {
  if (!ok) return;
  mostrandoCarinha = false;
  oled.clearDisplay();
  oled.setTextColor(SSD1306_WHITE);
  oled.setTextSize(1);
  oled.setCursor(0, 4);
  oled.print(linha1);
  oled.setCursor(0, 36);
  oled.print(linha2);
  oled.display();
  ligar();
}

}  // namespace tela

// ===== pet.cpp =====
// Logica do hoodi Pet. O roteiro (setup e loop) fica em hoodi_pet.ino; aqui ficam os detalhes.
// Fica num .cpp porque o Arduino so gera prototipos automaticos nos .ino, e aqui ha namespaces.
#include <Arduino.h>







using namespace hoodi;

#if !HOODI_WOKWI && defined(ARDUINO_USB_CDC_ON_BOOT) && !ARDUINO_USB_CDC_ON_BOOT
#error "O botao de ajuda usa o GPIO20 (RX da UART0). Ative Tools > USB CDC On Boot: Enabled."
#endif

namespace pet {

namespace {

char nomeDispositivo[20];
Vibrador vibrador;
Som som;
uint16_t frequenciaTocando = 0;
Botao botaoTela(PRESSAO_LONGA_MS);
Botao botaoSilenciar(PRESSAO_LONGA_MS);
Botao botaoAjuda(PRESSAO_LONGA_MS);
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

// Alerta no corpo e no som. O som acompanha o padrao da vibracao.
void alertar(Padrao p, unsigned long agora) {
  vibrador.iniciar(p, agora);
  if (SOM_HABILITADO) som.iniciar(toqueDoPadrao(p), agora);
}

void tocar(Toque t) {
  if (SOM_HABILITADO) som.iniciar(t, millis());
}

void silenciar() {
  vibrador.parar();
  som.parar();
}

void aplicar(const Comando& c) {
  const unsigned long agora = millis();
  switch (c.tipo) {
    case TipoComando::Estado:
      trajetoAtivo = true;
      estado.nivel = c.nivel;
      estado.expressao = c.expressao;
      if (c.padrao != Padrao::Nenhum) alertar(c.padrao, agora);
      mostrarEstado();
      break;
    case TipoComando::Identificar:
      alertar(Padrao::Identificar, agora);
      mostrarEstado();
      break;
    case TipoComando::Silenciar:
      silenciar();
      break;
    case TipoComando::FimTrajeto:
      trajetoAtivo = false;
      silenciar();
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
        tocar(Toque::Pin);
        Serial.println("[ble] PIN na tela do Pet");  // o PIN nao vai para o Serial de proposito
        break;
      case ble::TipoAviso::Pareado:
        Serial.println("[ble] pareado; outros celulares esquecidos");
        tela::mensagem("Pareado!", nomeDispositivo);
        vibrador.iniciar(Padrao::Curto, millis());
        tocar(Toque::Pareado);
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
    Serial.printf("[st] fw=%s nome=%s ble=%s pareamento=%s salvos=%d trajeto=%s nivel=%u expressao=%u ultimaSeq=%lu som=%s\n",
                  HOODI_VERSAO_FIRMWARE, nomeDispositivo, bleConectado ? "conectado" : "livre",
                  ble::modoPareamento() ? "aberto" : "fechado", ble::pareamentosSalvos(), trajetoAtivo ? "sim" : "nao",
                  (unsigned)estado.nivel, (unsigned)estado.expressao, (unsigned long)sequenciador.ultima,
                  SOM_HABILITADO ? "ligado" : "desligado");
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

// Botao 1 (tela): clique acende a tela e avisa o app. Segurar nao faz nada (so no boot: pareamento).
// Botao 2 (silenciar): clique ou segurar corta vibracao e som, e acende a tela.
// Botao 3 (ajuda): so a pressao longa (3 s) conta, para nao pedir ajuda sem querer. Clique e ignorado.
void tratarBotoes() {
  const unsigned long agora = millis();

  if (botaoTela.atualizar(digitalRead(PINO_BOTAO_TELA) == LOW, agora) == Botao::Evento::Clique) {
    if (!ble::modoPareamento()) mostrarEstado();
    emitirEvento(TipoEvento::Clique);
  }

  if (botaoSilenciar.atualizar(digitalRead(PINO_BOTAO_SILENCIAR) == LOW, agora) != Botao::Evento::Nenhum) {
    silenciar();
    if (!ble::modoPareamento()) mostrarEstado();
    Serial.println("[botao] silenciado");
  }

  if (botaoAjuda.atualizar(digitalRead(PINO_BOTAO_AJUDA) == LOW, agora) == Botao::Evento::PressaoLonga) {
    // Sem BLE (Wokwi) o evento sai pelo Serial; no Pet real, sem celular conectado ninguem recebe.
    const bool entregue = HOODI_SEM_BLE || bleConectado;
    som.parar();  // discricao: um alerta tocando para na hora em que a pessoa pede ajuda
    if (entregue) {
      vibrador.iniciar(Padrao::Curto, agora);  // confirma no corpo que o pedido saiu
      if (SOM_NO_PEDIDO_DE_AJUDA) tocar(Toque::Ajuda);
      emitirEvento(TipoEvento::PressaoLonga);
    } else {
      vibrador.iniciar(Padrao::PerdeuConexao, agora);  // longo + curto: a ajuda NAO saiu
      if (SOM_NO_PEDIDO_DE_AJUDA) tocar(Toque::AjudaSemCelular);
      tela::mensagem("Sem celular", "ajuda nao enviada");
      ultimaNovidadeMs = agora;
      Serial.println("[botao] pedido de ajuda sem celular conectado: nao enviado");
    }
  }
}

// So mexe no PWM quando a frequencia muda (tone() reconfigura o LEDC a cada chamada).
void tocarFrequencia(uint16_t hz) {
  if (hz == frequenciaTocando) return;
  if (hz == 0) {
    noTone(PINO_SOM);
    // noTone solta o pino do LEDC (fica flutuando). Volta a ser GPIO em LOW: transistor cortado, falante mudo.
    pinMode(PINO_SOM, OUTPUT);
    digitalWrite(PINO_SOM, LOW);
  } else {
    tone(PINO_SOM, hz);
  }
  frequenciaTocando = hz;
}

void tratarDesconexao() {
  if (!avisoDesconexaoPendente || bleConectado) return;
  if (millis() - desconectouEm < AVISO_DESCONEXAO_MS) return;
  avisoDesconexaoPendente = false;
  alertar(Padrao::PerdeuConexao, millis());
  mostrarEstado();
  Serial.println("[ble] aviso de desconexao vibrado");
}

void tratarTela() {
  tela::animar(millis());
  if (TELA_APAGA_MS == 0) return;     // configurado para nunca apagar
  if (ble::modoPareamento()) return;  // tela fica acesa enquanto espera o celular
  if (tela::ligada() && millis() - ultimaNovidadeMs >= TELA_APAGA_MS) tela::apagar();
}

bool botaoSeguradoNoBoot() {
  if (digitalRead(PINO_BOTAO_TELA) != LOW) return false;
  const unsigned long inicio = millis();
  while (millis() - inicio < BOOT_PAREAR_MS) {
    if (digitalRead(PINO_BOTAO_TELA) != LOW) return false;
    delay(10);
  }
  return true;
}

}  // namespace

void iniciar() {
  setCpuFrequencyMhz(80);  // BLE funciona a 80 MHz e a bateria agradece
  Serial.begin(115200);
  delay(300);

  pinMode(PINO_VIBRACAO, OUTPUT);
  digitalWrite(PINO_VIBRACAO, LOW);
  pinMode(PINO_SOM, OUTPUT);
  digitalWrite(PINO_SOM, LOW);
  pinMode(PINO_BOTAO_TELA, INPUT_PULLUP);
  pinMode(PINO_BOTAO_SILENCIAR, INPUT_PULLUP);
  pinMode(PINO_BOTAO_AJUDA, INPUT_PULLUP);

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

void receberComandos() {
  ble::FrameRecebido f;
  while (ble::proximoComando(f)) processarFrame(f.dados, f.tam == 0xFF ? ble::TAM_MAX_FRAME + 1 : f.tam);
}

void atualizarBle() { processarAvisosBle(); }
void atualizarSerial() { lerSerial(); }
void atualizarBotoes() { tratarBotoes(); }
void atualizarConexao() { tratarDesconexao(); }
void atualizarVibracao() { digitalWrite(PINO_VIBRACAO, vibrador.atualizar(millis()) ? HIGH : LOW); }
void atualizarSom() { tocarFrequencia(som.atualizar(millis())); }
void atualizarTela() { tratarTela(); }

}  // namespace pet

// ===== hoodi_pet.ino =====
// hoodi Pet - firmware 1.1.0 (ESP32-C3 SuperMini, carcaca v7b)
//
// O Pet so executa: quem decide quando alertar e o app (regra de subida de nivel, intervalo, histerese).
// GPS, grade de risco e API ficam no celular. O Pet nunca chama a API.
//
// Botoes: 1 (GPIO4) clique acende a tela | 2 (GPIO10) silencia vibracao e som | 3 (GPIO20) segurar 3 s = ajuda.
// Segurar o botao 1 ao ligar abre o pareamento. Alto-falante no GPIO5 via transistor NPN.
//
// Arduino IDE: placa "ESP32C3 Dev Module", USB CDC On Boot: Enabled.
// Bibliotecas: NimBLE-Arduino 2.5.x, Adafruit SSD1306, Adafruit GFX.
// Wokwi: HOODI_SEM_BLE 1 em config.h (o Wokwi nao simula Bluetooth); comandos pelo Serial.
//
// Serial (115200), uma linha por comando:
//   n0 n1 n2 n3 n?   estado com nivel 0..3 ou desconhecido (padrao e expressao automaticos)
//   id               identificar (vibra para achar o Pet)
//   sil              silenciar a vibracao e o som
//   fim              fim do trajeto (carinha dormindo)
//   hex <18 hex>     frame bruto, passa pela regra de sequencia. Ex.: hex 010101000000020202
//   st               imprime o estado
//   parear           abre o modo pareamento por 2 min
//   esquecer         apaga o celular pareado
//
// Arquivos:
//   pet.cpp       logica do Pet (cada etapa abaixo)
//   protocolo.h   formato das mensagens Bluetooth app <-> Pet
//   vibracao.h    padroes de vibracao e leitura dos botoes
//   som.h         toques do alto-falante
//   tela.cpp      carinhas (pixel art de carinhas.h) e mensagens no OLED
//   ble_pet.cpp   Bluetooth (pareamento com PIN, comandos, status, eventos)
//   config.h      pinos e tempos

void setup() {
  pet::iniciar();
}

// Nada aqui usa delay() longo: cada etapa faz um pouco e devolve o controle,
// entao o Pet vibra, bipa, le os botoes e atende o Bluetooth "ao mesmo tempo".
void loop() {
  pet::receberComandos();    // 1. o que o app mandou
  pet::atualizarBle();       // 2. conexao e pareamento
  pet::atualizarSerial();    // 3. comandos de bancada
  pet::atualizarBotoes();    // 4. tela / silenciar / ajuda
  pet::atualizarConexao();   // 5. aviso se o celular sumiu
  pet::atualizarVibracao();  // 6. motor
  pet::atualizarSom();       // 7. alto-falante
  pet::atualizarTela();      // 8. piscar e apagar a tela
  delay(5);
}
