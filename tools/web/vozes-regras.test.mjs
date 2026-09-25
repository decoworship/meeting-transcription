// As regras de Ajustes › Vozes, fora do navegador.
//
//     node --test tools/web/vozes-regras.test.mjs
//
// Saúde errada não dá erro: só manda a pessoa errada para "pouca voz", ou
// esconde de "Para revisar" a amostra que pedia ouvido. Ver o plano 6.

import { test } from "node:test";
import assert from "node:assert/strict";

const R = await import(new URL("../../app-net/App/web/vozes-regras.js", import.meta.url));

const A = (extra = {}) => ({
  indice: 0, criada_em: "2026-09-17T10:00:00Z", duracao_s: 3.1, faixa: "system",
  dispositivo: "Sala Conf B", quarentena: false, outro_modelo: false, regras_antigas: false, ...extra,
});
const P = (nome, amostras) => ({ nome, amostras: amostras.map((a, i) => ({ ...a, indice: i })) });

test("saúde: 4 em uso é boa, 1 a 3 é pouca voz, nenhuma é fora de uso", () => {
  assert.equal(R.saude(P("x", [A(), A(), A(), A()])).rotulo, "boa");
  assert.equal(R.saude(P("x", [A(), A(), A()])).rotulo, "pouca voz");
  // quarentena e inerte não contam como voz em uso
  assert.equal(R.saude(P("x", [A(), A(), A(), A({ quarentena: true })])).rotulo, "pouca voz");
  assert.equal(R.saude(P("x", [A({ regras_antigas: true }), A({ outro_modelo: true })])).rotulo, null);
});

test("amostras e aparelhos: as ativas, dispositivos distintos", () => {
  const p = P("Carol", [A(), A({ dispositivo: "Headset" }), A({ quarentena: true }),
                        A({ dispositivo: null }), A({ regras_antigas: true, dispositivo: "Velho" })]);
  assert.equal(R.resumoDaPessoa(p), "4 amostras · 2 aparelhos");
  assert.equal(R.resumoDaPessoa(P("x", [A()])), "1 amostra · 1 aparelho");
});

test("a fila: só quarentena ativa, a mais nova primeiro, com a pessoa", () => {
  const vozes = [
    P("Carol", [A(), A({ quarentena: true, criada_em: "2026-09-16T00:00:00Z" })]),
    P("Rafael", [A({ quarentena: true, criada_em: "2026-09-17T00:00:00Z" }),
                 A({ quarentena: true, outro_modelo: true })]),
  ];
  const f = R.fila(vozes);
  assert.deepEqual(f.map((x) => [x.pessoa, x.amostra.indice]), [["Rafael", 0], ["Carol", 1]]);
});

test("fora de uso: conta as inertes, e separa quem só tem inerte", () => {
  const vozes = [P("Carol", [A(), A({ regras_antigas: true })]),
                 P("Antigo", [A({ regras_antigas: true }), A({ outro_modelo: true })])];
  const f = R.foraDeUso(vozes);
  assert.equal(f.total, 3);
  assert.deepEqual(f.pessoas, ["Antigo"]);
  assert.deepEqual(R.pessoasEmUso(vozes).map((p) => p.nome), ["Carol"]);
});

test("procedência na voz do app", () => {
  assert.equal(R.procedencia(A()), "17 set · áudio da reunião · 3,1 s · Sala Conf B");
  assert.equal(R.procedencia(A({ faixa: "mic", dispositivo: null, duracao_s: 4 })),
               "17 set · seu microfone · 4,0 s");
});

test("número com vírgula, e o primeiro nome do botão", () => {
  assert.equal(R.numero(0.4), "0,40");
  assert.equal(R.primeiroNome("Carol Souza"), "Carol");
  assert.equal(R.primeiroNome("Você (André)"), "Você");
  assert.equal(R.primeiroNome("  Marcos "), "Marcos");
});

test("parecidos: some o recusado e o par de quem não existe mais", () => {
  const pares = [{ a: "Elio", b: "Élio", semelhanca: 0.93 }, { a: "Ana", b: "Bia", semelhanca: 0.8 }];
  const recusados = new Set([R.chaveDoPar("Bia", "Ana")]);
  assert.deepEqual(R.parecidosVisiveis(pares, recusados, ["Elio", "Élio", "Ana", "Bia"]).map((p) => p.a), ["Elio"]);
  assert.deepEqual(R.parecidosVisiveis(pares, new Set(), ["Elio", "Ana", "Bia"]).map((p) => p.a), ["Ana"]);
});

test("ordem: boa antes de pouca voz, e mais amostras primeiro", () => {
  const vozes = [P("Elio", [A(), A()]), P("Heitor", [A(), A(), A(), A(), A()]),
                 P("Élio", [A(), A(), A(), A()]), P("Rafael", [A(), A(), A()])];
  assert.deepEqual(R.ordenarPessoas(vozes).map((p) => p.nome), ["Heitor", "Élio", "Rafael", "Elio"]);
});
