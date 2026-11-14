// Regra de alerta do hoodi (doc consolidado, "Regras que valem em tudo"):
// - alerta ao iniciar o trajeto se o nivel ja for 2 ou 3;
// - depois, so quando o nivel sobe;
// - intervalo minimo entre alertas (2 min); subida bloqueada pelo intervalo fica pendente e
//   vibra quando o intervalo acaba, se o nivel ainda estiver acima do ultimo alerta;
// - histerese de ~30 m: ao trocar de celula, o nivel novo so vale depois de andar 30 m dentro dela
//   (parado na divisa, o GPS oscilando entre duas celulas nao faz o Pet vibrar a cada passo);
// - sem dado (sem GPS, fora da cobertura, celula sem_dado) mostra "?" na hora e nunca vibra;
//   ao voltar do "sem dado", compara com o ultimo nivel conhecido (perder o GPS no nivel 1 e voltar
//   no 3 vibra); se nunca houve nivel conhecido no trajeto, vale a regra do inicio.
// Mudanca de faixa horaria na mesma celula vale na hora.
//
// Puro (sem BLE, sem GPS): o app chama avaliar() a cada posicao e manda o resultado ao Pet.

import type { Nivel } from "./hoodiPetProtocolo";

export interface Leitura {
  celula: string | null; // h3_cell da posicao, ou null sem GPS
  nivel: Nivel;          // nivel da celula na faixa atual, ou null sem dado
  lat: number;
  lon: number;
  agoraMs: number;
}

export type Motivo = "inicio" | "subiu" | "pendente" | "mudou" | "sem_dado";

export interface Decisao {
  nivel: Nivel;
  vibrar: boolean; // true: padrao automatico do nivel; false: so troca a carinha
  motivo: Motivo;
}

export interface OpcoesRegra {
  intervaloMinimoMs: number;
  histereseMetros: number;
  nivelMinimoAlertaInicio: number;
}

const PADRAO: OpcoesRegra = { intervaloMinimoMs: 120_000, histereseMetros: 30, nivelMinimoAlertaInicio: 2 };

export function distanciaMetros(lat1: number, lon1: number, lat2: number, lon2: number): number {
  const R = 6_371_000;
  const rad = Math.PI / 180;
  const dLat = (lat2 - lat1) * rad;
  const dLon = (lon2 - lon1) * rad;
  const a = Math.sin(dLat / 2) ** 2 + Math.cos(lat1 * rad) * Math.cos(lat2 * rad) * Math.sin(dLon / 2) ** 2;
  return 2 * R * Math.asin(Math.sqrt(a));
}

export class RegraAlerta {
  private readonly op: OpcoesRegra;
  private iniciado = false;
  private nivelExibido: Nivel = null;
  private ultimoConhecido: Nivel = null;
  private celulaExibida: string | null = null;
  private celulaCandidata: string | null = null;
  private entrada = { lat: 0, lon: 0 };
  private ultimoAlertaMs = Number.NEGATIVE_INFINITY;
  private nivelUltimoAlerta = -1;
  private pendente = false;

  constructor(opcoes: Partial<OpcoesRegra> = {}) {
    this.op = { ...PADRAO, ...opcoes };
  }

  /** Chamar ao comecar cada trajeto. */
  iniciarTrajeto(): void {
    this.iniciado = false;
    this.nivelExibido = null;
    this.ultimoConhecido = null;
    this.celulaExibida = null;
    this.celulaCandidata = null;
    this.ultimoAlertaMs = Number.NEGATIVE_INFINITY;
    this.nivelUltimoAlerta = -1;
    this.pendente = false;
  }

  /** Retorna o que mandar ao Pet, ou null se nada mudou. */
  avaliar(l: Leitura): Decisao | null {
    if (!this.iniciado) {
      this.iniciado = true;
      this.aceitar(l);
      this.ultimoConhecido = l.nivel;
      const vibrar = l.nivel !== null && l.nivel >= this.op.nivelMinimoAlertaInicio;
      if (vibrar) this.registrarAlerta(l);
      return { nivel: l.nivel, vibrar, motivo: l.nivel === null ? "sem_dado" : "inicio" };
    }

    const anterior = this.nivelExibido;
    this.atualizarNivelExibido(l);
    const atual = this.nivelExibido;

    if (atual === null) {
      this.pendente = false;
      return anterior === null ? null : { nivel: null, vibrar: false, motivo: "sem_dado" };
    }

    const intervaloOk = l.agoraMs - this.ultimoAlertaMs >= this.op.intervaloMinimoMs;
    const referencia = anterior ?? this.ultimoConhecido;
    this.ultimoConhecido = atual;
    if (atual !== anterior) {
      if (referencia === null) {
        // Primeiro nivel conhecido do trajeto (comecou sem GPS): regra do inicio.
        const vibrar = atual >= this.op.nivelMinimoAlertaInicio && intervaloOk;
        if (vibrar) this.registrarAlerta(l);
        return { nivel: atual, vibrar, motivo: "inicio" };
      }
      const subiu = atual > referencia;
      if (subiu && intervaloOk) {
        this.registrarAlerta(l);
        return { nivel: atual, vibrar: true, motivo: "subiu" };
      }
      if (subiu) this.pendente = true;
      if (atual <= this.nivelUltimoAlerta) this.pendente = false;
      return { nivel: atual, vibrar: false, motivo: "mudou" };
    }

    if (this.pendente && intervaloOk) {
      this.pendente = false;
      if (atual > this.nivelUltimoAlerta) {
        this.registrarAlerta(l);
        return { nivel: atual, vibrar: true, motivo: "pendente" };
      }
    }
    return null;
  }

  private registrarAlerta(l: Leitura): void {
    this.ultimoAlertaMs = l.agoraMs;
    this.nivelUltimoAlerta = this.nivelExibido ?? -1;
    this.pendente = false;
  }

  private aceitar(l: Leitura): void {
    this.nivelExibido = l.nivel;
    this.celulaExibida = l.celula;
    this.celulaCandidata = l.celula;
    this.entrada = { lat: l.lat, lon: l.lon };
  }

  private atualizarNivelExibido(l: Leitura): void {
    // Sem dado vale na hora; sair de "sem dado" tambem; mudanca na mesma celula (faixa horaria) tambem.
    if (l.nivel === null || this.nivelExibido === null || l.celula === this.celulaExibida || l.nivel === this.nivelExibido) {
      this.aceitar(l);
      return;
    }
    if (l.celula !== this.celulaCandidata) {
      this.celulaCandidata = l.celula;
      this.entrada = { lat: l.lat, lon: l.lon };
    }
    if (distanciaMetros(this.entrada.lat, this.entrada.lon, l.lat, l.lon) >= this.op.histereseMetros) {
      this.nivelExibido = l.nivel;
      this.celulaExibida = l.celula;
    }
  }
}
