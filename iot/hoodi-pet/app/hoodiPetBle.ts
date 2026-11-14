// Cliente BLE do hoodi Pet para o app Expo (development build com react-native-ble-plx 3.x).
// Cuida de: procurar, conectar, pareamento (o sistema pede o PIN que aparece no OLED),
// sequencia por conexao, reconexao com espera crescente e reenvio do ultimo estado.
//
// Permissoes (app.json, plugin do react-native-ble-plx): Android 12+ pede BLUETOOTH_SCAN e
// BLUETOOTH_CONNECT; iOS pede NSBluetoothAlwaysUsageDescription. Ver docs/PROTOCOLO_BLE.md.

import { BleManager, State, type Subscription } from "react-native-ble-plx";

import {
  AUTOMATICO,
  base64ParaBytes,
  bytesParaBase64,
  codificarEstado,
  codificarSimples,
  decodificarEvento,
  decodificarStatus,
  type EventoPet,
  type Nivel,
  Padrao,
  SequenciaPorConexao,
  type StatusPet,
  TipoComando,
  UUID_COMANDO,
  UUID_EVENTO,
  UUID_SERVICO,
  UUID_STATUS,
} from "./hoodiPetProtocolo";

export type EstadoConexao = "desconectado" | "procurando" | "conectando" | "conectado" | "reconectando";

export interface PetEncontrado {
  id: string;
  nome: string;
  rssi: number | null;
}

export interface OuvintesPet {
  conexao?(estado: EstadoConexao): void;
  status?(status: StatusPet): void;
  /** Clique ou pressao longa no botao do Pet. O app decide o que fazer (ex.: pedido de ajuda). */
  evento?(evento: EventoPet): void;
  erro?(mensagem: string, causa?: unknown): void;
}

const ESPERA_MAX_RECONEXAO_MS = 30_000;

export class HoodiPet {
  private readonly manager = new BleManager();
  private readonly sequencia = new SequenciaPorConexao();
  private assinaturas: Subscription[] = [];
  private deviceId: string | null = null;
  private querConectado = false;
  private tentativas = 0;
  private timerReconexao: ReturnType<typeof setTimeout> | null = null;
  private ultimoNivel: Nivel | undefined = undefined; // reenviado (sem vibrar) depois de reconectar
  private ultimoContadorEvento = 0;
  private estado: EstadoConexao = "desconectado";

  constructor(private readonly ouvintes: OuvintesPet = {}) {}

  get conexao(): EstadoConexao {
    return this.estado;
  }

  /** Procura Pets anunciando o servico do hoodi. */
  async procurar(duracaoMs = 10_000): Promise<PetEncontrado[]> {
    await this.esperarBluetoothLigado();
    this.mudar("procurando");
    const achados = new Map<string, PetEncontrado>();
    await this.manager.startDeviceScan([UUID_SERVICO], null, (erro, d) => {
      if (erro) {
        this.ouvintes.erro?.("falha na busca Bluetooth", erro);
        return;
      }
      if (d) achados.set(d.id, { id: d.id, nome: d.localName ?? d.name ?? "hoodi Pet", rssi: d.rssi });
    });
    await new Promise((r) => setTimeout(r, duracaoMs));
    await this.manager.stopDeviceScan();
    if (this.estado === "procurando") this.mudar("desconectado");
    return [...achados.values()].sort((a, b) => (b.rssi ?? -999) - (a.rssi ?? -999));
  }

  /**
   * Conecta e mantem conectado (reconecta sozinho ate desconectar()).
   * Na primeira vez o sistema abre a tela de pareamento: digitar o PIN de 6 digitos do OLED.
   * Guarde o deviceId (AsyncStorage) para reconectar sem procurar de novo.
   */
  async conectar(deviceId: string): Promise<void> {
    this.deviceId = deviceId;
    this.querConectado = true;
    this.tentativas = 0;
    await this.esperarBluetoothLigado();
    await this.tentarConectar();
  }

  async desconectar(): Promise<void> {
    this.querConectado = false;
    this.limparTimer();
    this.limparAssinaturas();
    if (this.deviceId) await this.manager.cancelDeviceConnection(this.deviceId).catch(() => undefined);
    this.mudar("desconectado");
  }

  /**
   * Manda o nivel atual ao Pet. vibrar=true usa o padrao do nivel (1 curto, 2 duplo, 3 longo);
   * use RegraAlerta para decidir. nivel null mostra "?" e nunca vibra.
   */
  async enviarEstado(nivel: Nivel, vibrar: boolean): Promise<void> {
    this.ultimoNivel = nivel;
    await this.escrever(codificarEstado(this.sequencia.proxima(), nivel, vibrar ? AUTOMATICO : Padrao.Nenhum));
  }

  identificar(): Promise<void> {
    return this.escrever(codificarSimples(TipoComando.Identificar, this.sequencia.proxima()));
  }

  silenciar(): Promise<void> {
    return this.escrever(codificarSimples(TipoComando.Silenciar, this.sequencia.proxima()));
  }

  async fimTrajeto(): Promise<void> {
    this.ultimoNivel = undefined;
    await this.escrever(codificarSimples(TipoComando.FimTrajeto, this.sequencia.proxima()));
  }

  async lerStatus(): Promise<StatusPet | null> {
    if (!this.deviceId || this.estado !== "conectado") return null;
    const c = await this.manager.readCharacteristicForDevice(this.deviceId, UUID_SERVICO, UUID_STATUS);
    return c.value ? decodificarStatus(base64ParaBytes(c.value)) : null;
  }

  async destruir(): Promise<void> {
    await this.desconectar();
    await this.manager.destroy();
  }

  // ---------------------------------------------------------------------------------------------

  private async tentarConectar(): Promise<void> {
    if (!this.deviceId || !this.querConectado) return;
    const id = this.deviceId;
    this.mudar(this.tentativas === 0 ? "conectando" : "reconectando");
    try {
      await this.manager.connectToDevice(id, { autoConnect: false, timeout: 10_000 });
      await this.manager.discoverAllServicesAndCharacteristicsForDevice(id);
      this.sequencia.reiniciar(); // o Pet zera a sequencia a cada conexao
      this.limparAssinaturas();
      this.assinaturas.push(
        this.manager.onDeviceDisconnected(id, () => this.aoDesconectar()),
        this.manager.monitorCharacteristicForDevice(id, UUID_SERVICO, UUID_STATUS, (erro, c) => {
          if (erro || !c?.value) return;
          try {
            this.ouvintes.status?.(decodificarStatus(base64ParaBytes(c.value)));
          } catch (e) {
            this.ouvintes.erro?.("status do Pet ilegivel", e);
          }
        }),
        this.manager.monitorCharacteristicForDevice(id, UUID_SERVICO, UUID_EVENTO, (erro, c) => {
          if (erro || !c?.value) return;
          try {
            const ev = decodificarEvento(base64ParaBytes(c.value));
            // O contador zera quando o Pet reinicia; so ignora repeticao exata do ultimo.
            if (ev.contador === this.ultimoContadorEvento) return;
            this.ultimoContadorEvento = ev.contador;
            this.ouvintes.evento?.(ev);
          } catch (e) {
            this.ouvintes.erro?.("evento do Pet ilegivel", e);
          }
        }),
      );
      // Ler o status exige pareamento: na primeira vez isso dispara o pedido de PIN no celular.
      await this.lerComRetentativa(id);
      this.tentativas = 0;
      this.mudar("conectado");
      if (this.ultimoNivel !== undefined) {
        await this.enviarEstado(this.ultimoNivel, false); // restaura a carinha sem vibrar de novo
      }
    } catch (e) {
      this.ouvintes.erro?.("nao conectou ao Pet", e);
      await this.manager.cancelDeviceConnection(id).catch(() => undefined);
      this.agendarReconexao();
    }
  }

  private async lerComRetentativa(id: string): Promise<void> {
    // Enquanto a pessoa digita o PIN, a leitura falha por falta de autenticacao. Tenta por ~60 s.
    for (let i = 0; i < 20; i++) {
      try {
        const c = await this.manager.readCharacteristicForDevice(id, UUID_SERVICO, UUID_STATUS);
        if (c.value) this.ouvintes.status?.(decodificarStatus(base64ParaBytes(c.value)));
        return;
      } catch (e) {
        if (i === 19) throw e;
        await new Promise((r) => setTimeout(r, 3_000));
      }
    }
  }

  private aoDesconectar(): void {
    this.limparAssinaturas();
    if (!this.querConectado) {
      this.mudar("desconectado");
      return;
    }
    // O app deve avisar no proprio celular enquanto o Pet estiver fora (doc: "Sem Bluetooth").
    this.agendarReconexao();
  }

  private agendarReconexao(): void {
    if (!this.querConectado) return;
    this.limparTimer();
    this.tentativas++;
    const espera = Math.min(1_000 * 2 ** (this.tentativas - 1), ESPERA_MAX_RECONEXAO_MS);
    this.mudar("reconectando");
    this.timerReconexao = setTimeout(() => void this.tentarConectar(), espera);
  }

  private async escrever(frame: Uint8Array): Promise<void> {
    if (!this.deviceId || this.estado !== "conectado") {
      throw new Error("Pet desconectado");
    }
    await this.manager.writeCharacteristicWithResponseForDevice(
      this.deviceId,
      UUID_SERVICO,
      UUID_COMANDO,
      bytesParaBase64(frame),
    );
  }

  private async esperarBluetoothLigado(): Promise<void> {
    await new Promise<void>((resolve) => {
      const sub = this.manager.onStateChange((s) => {
        if (s === State.PoweredOn) {
          sub.remove();
          resolve();
        }
      }, true);
    });
  }

  private mudar(e: EstadoConexao): void {
    if (this.estado === e) return;
    this.estado = e;
    this.ouvintes.conexao?.(e);
  }

  private limparAssinaturas(): void {
    for (const s of this.assinaturas) s.remove();
    this.assinaturas = [];
  }

  private limparTimer(): void {
    if (this.timerReconexao) clearTimeout(this.timerReconexao);
    this.timerReconexao = null;
  }
}
