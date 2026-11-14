// Protocolo BLE app <-> hoodi Pet, versao 1. Espelho de firmware/hoodi_pet/protocolo.h.
// Sem dependencias: funciona no Expo, no Node (testes) e em qualquer lib BLE.
// Especificacao: docs/PROTOCOLO_BLE.md

export const VERSAO_PROTOCOLO = 1;

export const UUID_SERVICO = "a70ab40e-599a-4803-89db-3b1667e7d753";
export const UUID_COMANDO = "ad7790b4-bf5d-4ee2-a6b2-5faac8195117";
export const UUID_STATUS = "147dca7f-bc45-4789-85bb-adc57bc40124";
export const UUID_EVENTO = "6e9d9b1f-3aef-4015-a8ab-162399270a78";

export const NIVEL_DESCONHECIDO = 0xff;
export const AUTOMATICO = 0xff;

export const TipoComando = { Estado: 1, Identificar: 2, Silenciar: 3, FimTrajeto: 4 } as const;
export type TipoComando = (typeof TipoComando)[keyof typeof TipoComando];

export const Padrao = { Nenhum: 0, Curto: 1, Duplo: 2, Longo: 3 } as const;
export type Padrao = (typeof Padrao)[keyof typeof Padrao];

export const Expressao = {
  Tranquila: 0, Atenta: 1, Preocupada: 2, Alerta: 3, Desconhecido: 4, Dormindo: 5,
  Desconectado: 6, Pareamento: 7, // internas: aparecem no status, o app nao pode pedir
} as const;
export type Expressao = (typeof Expressao)[keyof typeof Expressao];

export const Resultado = { Aplicado: 0, SequenciaAntiga: 1, Invalido: 2, Nenhum: 0xff } as const;
export type Resultado = (typeof Resultado)[keyof typeof Resultado];

export const TipoEvento = { Clique: 1, PressaoLonga: 2 } as const;
export type TipoEvento = (typeof TipoEvento)[keyof typeof TipoEvento];

/** nivel 0..3 ou null (sem dado / sem GPS / fora da cobertura). */
export type Nivel = 0 | 1 | 2 | 3 | null;

export interface StatusPet {
  resultado: Resultado;
  sequencia: number;
  nivel: Nivel;
  expressao: number;
  vibrando: boolean;
  telaLigada: boolean;
  trajetoAtivo: boolean;
}

export interface EventoPet {
  tipo: TipoEvento;
  contador: number; // cresce a cada evento desde que o Pet ligou; serve para ignorar repetidos
}

function escreverU32(b: Uint8Array, pos: number, v: number): void {
  b[pos] = v & 0xff;
  b[pos + 1] = (v >>> 8) & 0xff;
  b[pos + 2] = (v >>> 16) & 0xff;
  b[pos + 3] = (v >>> 24) & 0xff;
}

function lerU32(b: Uint8Array, pos: number): number {
  return (b[pos] | (b[pos + 1] << 8) | (b[pos + 2] << 16) | (b[pos + 3] << 24)) >>> 0;
}

function validarSequencia(sequencia: number): void {
  if (!Number.isInteger(sequencia) || sequencia < 1 || sequencia > 0xffffffff) {
    throw new RangeError("sequencia deve ser inteiro de 1 a 4294967295");
  }
}

/** Frame de 9 bytes: [versao][tipo][sequencia u32 LE][nivel][padrao][expressao]. */
export function codificarEstado(
  sequencia: number,
  nivel: Nivel,
  padrao: Padrao | typeof AUTOMATICO = AUTOMATICO,
  expressao: Expressao | typeof AUTOMATICO = AUTOMATICO,
): Uint8Array {
  validarSequencia(sequencia);
  if (expressao !== AUTOMATICO && expressao > Expressao.Dormindo) {
    throw new RangeError("expressao interna nao pode ser pedida pelo app");
  }
  const f = new Uint8Array(9);
  f[0] = VERSAO_PROTOCOLO;
  f[1] = TipoComando.Estado;
  escreverU32(f, 2, sequencia);
  f[6] = nivel === null ? NIVEL_DESCONHECIDO : nivel;
  f[7] = padrao;
  f[8] = expressao;
  return f;
}

export function codificarSimples(
  tipo: typeof TipoComando.Identificar | typeof TipoComando.Silenciar | typeof TipoComando.FimTrajeto,
  sequencia: number,
): Uint8Array {
  validarSequencia(sequencia);
  const f = new Uint8Array(9);
  f[0] = VERSAO_PROTOCOLO;
  f[1] = tipo;
  escreverU32(f, 2, sequencia);
  return f;
}

export function decodificarStatus(b: Uint8Array): StatusPet {
  if (b.length !== 9 || b[0] !== VERSAO_PROTOCOLO) throw new Error("status invalido");
  return {
    resultado: b[1] as Resultado,
    sequencia: lerU32(b, 2),
    nivel: b[6] <= 3 ? (b[6] as 0 | 1 | 2 | 3) : null,
    expressao: b[7],
    vibrando: (b[8] & 0x01) !== 0,
    telaLigada: (b[8] & 0x02) !== 0,
    trajetoAtivo: (b[8] & 0x04) !== 0,
  };
}

export function decodificarEvento(b: Uint8Array): EventoPet {
  if (b.length !== 6 || b[0] !== VERSAO_PROTOCOLO) throw new Error("evento invalido");
  if (b[1] !== TipoEvento.Clique && b[1] !== TipoEvento.PressaoLonga) throw new Error("tipo de evento desconhecido");
  return { tipo: b[1] as TipoEvento, contador: lerU32(b, 2) };
}

/** A sequencia zera no Pet a cada conexao: chame reiniciar() logo depois de conectar. */
export class SequenciaPorConexao {
  private ultima = 0;
  reiniciar(): void {
    this.ultima = 0;
  }
  proxima(): number {
    this.ultima = this.ultima >= 0xffffffff ? 1 : this.ultima + 1;
    return this.ultima;
  }
}

// react-native-ble-plx troca valores em base64. Implementacao propria para nao depender de Buffer.
const ALFABETO = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/";

export function bytesParaBase64(bytes: Uint8Array): string {
  let s = "";
  for (let i = 0; i < bytes.length; i += 3) {
    const a = bytes[i], b = bytes[i + 1], c = bytes[i + 2];
    const n = (a << 16) | ((b ?? 0) << 8) | (c ?? 0);
    s += ALFABETO[(n >> 18) & 63] + ALFABETO[(n >> 12) & 63];
    s += b === undefined ? "=" : ALFABETO[(n >> 6) & 63];
    s += c === undefined ? "=" : ALFABETO[n & 63];
  }
  return s;
}

export function base64ParaBytes(b64: string): Uint8Array {
  const limpo = b64.replace(/[^A-Za-z0-9+/]/g, "");
  const out: number[] = [];
  for (let i = 0; i < limpo.length; i += 4) {
    const v = [0, 1, 2, 3].map((k) => (i + k < limpo.length ? ALFABETO.indexOf(limpo[i + k]) : -1));
    const n = (v[0] << 18) | (v[1] << 12) | ((v[2] < 0 ? 0 : v[2]) << 6) | (v[3] < 0 ? 0 : v[3]);
    out.push((n >> 16) & 0xff);
    if (v[2] >= 0) out.push((n >> 8) & 0xff);
    if (v[3] >= 0) out.push(n & 0xff);
  }
  return Uint8Array.from(out);
}
