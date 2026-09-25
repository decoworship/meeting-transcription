// As regras da reunião aberta e do preparo, fora do navegador.
//
//     node --test tools/web/reuniao-regras.test.mjs
//
// Ver docs/superpowers/specs/2026-09-23-ui-ux.md §3.3 e o plano 2b.

import { test } from "node:test";
import assert from "node:assert/strict";

const R = await import(new URL("../../app-net/App/web/reuniao-regras.js", import.meta.url));

test("separarTermos aceita vírgula, ponto e vírgula e quebra de linha", () => {
  assert.deepEqual(R.separarTermos("Beegol, NOC;Sherlock\nCCIL"), ["Beegol", "NOC", "Sherlock", "CCIL"]);
});

test("separarTermos descarta vazio e repetido, e fica a primeira grafia", () => {
  assert.deepEqual(R.separarTermos(" NOC ,, noc, Noc ,\n\n Algar "), ["NOC", "Algar"]);
  assert.deepEqual(R.separarTermos(""), []);
  assert.deepEqual(R.separarTermos(null), []);
});

test("juntarTermos é o formato que o motor e o disco sempre leram", () => {
  assert.equal(R.juntarTermos(["Beegol", "NOC"]), "Beegol, NOC");
  assert.equal(R.juntarTermos([]), "");
  // Ida e volta: o vocabulário gravado antes das etiquetas não perde termo.
  assert.equal(R.juntarTermos(R.separarTermos("Beegol,NOC\nCCIL")), "Beegol, NOC, CCIL");
});

test("momentosDasNotas acha a marca de cada linha, e a linha sem ela é o texto", () => {
  const m = R.momentosDasNotas("abertura\n[00:12:34] adiar o piloto\n  [01:02:03]   decidir o NOC ");
  assert.deepEqual(m, [
    { segundos: 754, marca: "00:12:34", texto: "adiar o piloto" },
    { segundos: 3723, marca: "01:02:03", texto: "decidir o NOC" },
  ]);
});

test("momentosDasNotas aceita a marca no meio da linha, e só a primeira conta", () => {
  assert.deepEqual(R.momentosDasNotas("Carol [00:00:04] e [00:00:09]"),
                   [{ segundos: 4, marca: "00:00:04", texto: "Carol  e [00:00:09]" }]);
});

test("momentosDasNotas ignora o que não é relógio", () => {
  assert.deepEqual(R.momentosDasNotas("[00:61:00] não\n[12:34] também não\n[aa:bb:cc]"), []);
  assert.deepEqual(R.momentosDasNotas(null), []);
});

test("estadoDasEtapas marca feitas, a atual e as pendentes", () => {
  assert.deepEqual(R.estadoDasEtapas("diarizacao").map((e) => e.estado),
                   ["feita", "feita", "atual", "pendente"]);
  assert.deepEqual(R.estadoDasEtapas("mix").map((e) => e.estado),
                   ["atual", "pendente", "pendente", "pendente"]);
  assert.deepEqual(R.estadoDasEtapas(null).map((e) => e.estado),
                   ["pendente", "pendente", "pendente", "pendente"]);
  assert.deepEqual(R.estadoDasEtapas("desconhecida").map((e) => e.estado),
                   ["pendente", "pendente", "pendente", "pendente"]);
  assert.deepEqual(R.ETAPAS.map(([id]) => id), ["mix", "asr", "diarizacao", "montagem"]);
  assert.equal(R.estadoDasEtapas("asr")[1].rotulo, "Transcrevendo");
});

test("relogioDoTocador", () => {
  assert.equal(R.relogioDoTocador(0), "00:00");
  assert.equal(R.relogioDoTocador(4.9), "00:04");
  assert.equal(R.relogioDoTocador(900), "15:00");
  assert.equal(R.relogioDoTocador(3723), "1:02:03");
  assert.equal(R.relogioDoTocador(NaN), "00:00");
  assert.equal(R.relogioDoTocador(-3), "00:00");
});

test("resumoDoMotor diz numa linha o que as opções dobradas escondem", () => {
  assert.equal(R.resumoDoMotor({ modelo: "large-v3", idioma: "pt", diarizar: true }),
               "large-v3 · pt · separa os falantes");
  assert.equal(R.resumoDoMotor({ modelo: "small", idioma: " ", diarizar: false }),
               "small · idioma automático · sem separar falantes");
});
