// Testes do codec do app e da regra de alerta (Node 22+):
//   node --experimental-strip-types --test testes/app.test.ts
// Os vetores de bytes sao os mesmos de testes/protocolo_test.cpp: app e firmware falam igual.
import assert from "node:assert/strict";
import { test } from "node:test";

import {
  AUTOMATICO,
  base64ParaBytes,
  bytesParaBase64,
  codificarEstado,
  codificarSimples,
  decodificarEvento,
  decodificarStatus,
  Padrao,
  Resultado,
  SequenciaPorConexao,
  TipoComando,
  TipoEvento,
} from "../app/hoodiPetProtocolo.ts";
import { RegraAlerta, distanciaMetros, type Leitura } from "../app/regraAlerta.ts";

const hex = (b: Uint8Array) => Buffer.from(b).toString("hex");

test("estado: mesmo frame que o firmware espera (little-endian)", () => {
  assert.equal(hex(codificarEstado(0x01020304, 1)), "010104030201" + "01ffff");
  assert.equal(hex(codificarEstado(1, 2, Padrao.Duplo, 2)), "010101000000020202"); // exemplo do 'hex' no Serial
  assert.equal(hex(codificarEstado(5, null)), "010105000000ffffff");
  assert.equal(hex(codificarEstado(5, 3, Padrao.Nenhum)), "01010500000003" + "00ff");
});

test("comandos simples", () => {
  assert.equal(hex(codificarSimples(TipoComando.Identificar, 4)), "010204000000000000");
  assert.equal(hex(codificarSimples(TipoComando.FimTrajeto, 6)), "010406000000000000");
});

test("validacoes do app", () => {
  assert.throws(() => codificarEstado(0, 1));
  assert.throws(() => codificarEstado(1.5, 1));
  assert.throws(() => codificarEstado(2 ** 32, 1));
  assert.throws(() => codificarEstado(1, 1, AUTOMATICO, 6 as never)); // expressao interna
});

test("status e evento: mesmos bytes do teste C++", () => {
  const s = decodificarStatus(Uint8Array.from([0x01, 0x01, 0xdd, 0xcc, 0xbb, 0xaa, 0x02, 0x02, 0x05]));
  assert.deepEqual(s, {
    resultado: Resultado.SequenciaAntiga, sequencia: 0xaabbccdd, nivel: 2, expressao: 2,
    vibrando: true, telaLigada: false, trajetoAtivo: true,
  });
  assert.equal(decodificarStatus(Uint8Array.from([1, 0, 1, 0, 0, 0, 0xff, 5, 0])).nivel, null);
  assert.deepEqual(decodificarEvento(Uint8Array.from([0x01, 0x02, 0x03, 0, 0, 0])), { tipo: TipoEvento.PressaoLonga, contador: 3 });
  assert.throws(() => decodificarStatus(Uint8Array.from([2, 0, 0, 0, 0, 0, 0, 0, 0])));
  assert.throws(() => decodificarEvento(Uint8Array.from([1, 9, 0, 0, 0, 0])));
});

test("sequencia por conexao", () => {
  const s = new SequenciaPorConexao();
  assert.equal(s.proxima(), 1);
  assert.equal(s.proxima(), 2);
  s.reiniciar();
  assert.equal(s.proxima(), 1);
});

test("base64 igual ao do Node, ida e volta", () => {
  for (let n = 0; n <= 12; n++) {
    const b = Uint8Array.from({ length: n }, (_, i) => (i * 73 + 11) & 0xff);
    assert.equal(bytesParaBase64(b), Buffer.from(b).toString("base64"));
    assert.deepEqual(base64ParaBytes(bytesParaBase64(b)), b);
  }
});

// ---- Regra de alerta -------------------------------------------------------------------------
// ~0,00009 grau de latitude = ~10 m
const passo = (metros: number) => metros * 0.000009;
const L = (celula: string | null, nivel: 0 | 1 | 2 | 3 | null, metrosAoNorte: number, agoraMs: number): Leitura => ({
  celula, nivel, lat: -23.5613 + passo(metrosAoNorte), lon: -46.6565, agoraMs,
});

test("distancia aproximada", () => {
  const d = distanciaMetros(-23.5613, -46.6565, -23.5613 + passo(30), -46.6565);
  assert.ok(d > 29 && d < 31, `d=${d}`);
});

test("inicio: alerta so se ja comecar em 2 ou 3", () => {
  const r = new RegraAlerta();
  assert.deepEqual(r.avaliar(L("a", 1, 0, 0)), { nivel: 1, vibrar: false, motivo: "inicio" });
  r.iniciarTrajeto();
  assert.deepEqual(r.avaliar(L("a", 2, 0, 0)), { nivel: 2, vibrar: true, motivo: "inicio" });
  r.iniciarTrajeto();
  assert.deepEqual(r.avaliar(L(null, null, 0, 0)), { nivel: null, vibrar: false, motivo: "sem_dado" });
});

test("subida vibra; descida so troca a carinha; repetir nao manda nada", () => {
  const r = new RegraAlerta();
  r.avaliar(L("a", 0, 0, 0));
  assert.equal(r.avaliar(L("a", 0, 5, 1_000)), null);
  // entra na celula b (nivel 2) e anda 40 m
  assert.equal(r.avaliar(L("b", 2, 10, 2_000)), null); // ainda dentro da histerese
  assert.deepEqual(r.avaliar(L("b", 2, 45, 3_000)), { nivel: 2, vibrar: true, motivo: "subiu" });
  assert.equal(r.avaliar(L("b", 2, 50, 4_000)), null);
  // volta para nivel 1 numa celula c, depois de andar 30 m
  assert.equal(r.avaliar(L("c", 1, 60, 200_000)), null);
  assert.deepEqual(r.avaliar(L("c", 1, 95, 201_000)), { nivel: 1, vibrar: false, motivo: "mudou" });
});

test("histerese: GPS oscilando na divisa nao vibra", () => {
  const r = new RegraAlerta();
  r.avaliar(L("a", 1, 0, 0));
  for (let i = 1; i <= 20; i++) {
    const celula = i % 2 === 0 ? "a" : "b";
    const nivel = celula === "a" ? 1 : 3;
    assert.equal(r.avaliar(L(celula, nivel, (i % 3) * 3, i * 1_000)), null, `passo ${i}`);
  }
});

test("mudanca de faixa horaria na mesma celula vale na hora", () => {
  const r = new RegraAlerta();
  r.avaliar(L("a", 1, 0, 0));
  assert.deepEqual(r.avaliar(L("a", 3, 0, 300_000)), { nivel: 3, vibrar: true, motivo: "subiu" });
});

test("sem dado aparece na hora e nunca vibra", () => {
  const r = new RegraAlerta();
  r.avaliar(L("a", 1, 0, 0));
  assert.deepEqual(r.avaliar(L(null, null, 0, 1_000)), { nivel: null, vibrar: false, motivo: "sem_dado" });
  assert.equal(r.avaliar(L(null, null, 0, 2_000)), null);
});

test("voltar do sem dado compara com o ultimo nivel conhecido", () => {
  const r = new RegraAlerta();
  r.avaliar(L("a", 1, 0, 0));
  r.avaliar(L(null, null, 0, 1_000));
  // Perdeu o GPS no 1 e voltou no 3: e subida, vibra.
  assert.deepEqual(r.avaliar(L("b", 3, 0, 500_000)), { nivel: 3, vibrar: true, motivo: "subiu" });
  r.avaliar(L(null, null, 0, 501_000));
  // Voltou no mesmo nivel: so tira o "?".
  assert.deepEqual(r.avaliar(L("b", 3, 0, 800_000)), { nivel: 3, vibrar: false, motivo: "mudou" });
});

test("trajeto que comeca sem GPS usa a regra do inicio no primeiro nivel", () => {
  const r = new RegraAlerta();
  assert.deepEqual(r.avaliar(L(null, null, 0, 0)), { nivel: null, vibrar: false, motivo: "sem_dado" });
  assert.deepEqual(r.avaliar(L("a", 2, 0, 5_000)), { nivel: 2, vibrar: true, motivo: "inicio" });
  r.iniciarTrajeto();
  r.avaliar(L(null, null, 0, 0));
  assert.deepEqual(r.avaliar(L("a", 1, 0, 5_000)), { nivel: 1, vibrar: false, motivo: "inicio" });
});

test("intervalo minimo: subida bloqueada fica pendente e vibra quando o intervalo acaba", () => {
  const r = new RegraAlerta();
  r.avaliar(L("a", 0, 0, 0));
  assert.deepEqual(r.avaliar(L("a", 1, 0, 10_000)), { nivel: 1, vibrar: true, motivo: "subiu" });
  // 30 s depois sobe para 3: bloqueado pelo intervalo, so a carinha muda
  assert.deepEqual(r.avaliar(L("a", 3, 0, 40_000)), { nivel: 3, vibrar: false, motivo: "mudou" });
  assert.equal(r.avaliar(L("a", 3, 0, 100_000)), null);
  // 2 min depois do ultimo alerta: vibra o pendente
  assert.deepEqual(r.avaliar(L("a", 3, 0, 130_000)), { nivel: 3, vibrar: true, motivo: "pendente" });
  assert.equal(r.avaliar(L("a", 3, 0, 140_000)), null);
});

test("pendente cancela se o nivel voltar ao do ultimo alerta", () => {
  const r = new RegraAlerta();
  r.avaliar(L("a", 0, 0, 0));
  r.avaliar(L("a", 2, 0, 10_000)); // alerta em 2
  r.avaliar(L("a", 3, 0, 20_000)); // pendente
  assert.deepEqual(r.avaliar(L("a", 2, 0, 30_000)), { nivel: 2, vibrar: false, motivo: "mudou" });
  assert.equal(r.avaliar(L("a", 2, 0, 200_000)), null);
});
