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

test("momentosDasNotas tira a imagem do texto e a devolve à parte", () => {
  assert.deepEqual(
    R.momentosDasNotas("[00:12:34] ![captura de tela](capturas/tela-00-12-34.png)\n"
                       + "[00:13:00] slide do orçamento ![imagem](capturas/imagem-00-13-00.png)"),
    [
      { segundos: 754, marca: "00:12:34", texto: "", imagem: "capturas/tela-00-12-34.png" },
      { segundos: 780, marca: "00:13:00", texto: "slide do orçamento", imagem: "capturas/imagem-00-13-00.png" },
    ]);
});

test("imagensDasNotas acha as imagens da reunião, com o momento quando há", () => {
  assert.deepEqual(
    R.imagensDasNotas("abertura\n[00:12:34] ![captura de tela](capturas/tela-00-12-34.png)\n"
                      + "![imagem](capturas/imagem.png) e ![b](capturas/b.jpg)"),
    [
      { caminho: "capturas/tela-00-12-34.png", legenda: "captura de tela", segundos: 754, marca: "00:12:34" },
      { caminho: "capturas/imagem.png", legenda: "imagem", segundos: null, marca: null },
      { caminho: "capturas/b.jpg", legenda: "b", segundos: null, marca: null },
    ]);
});

test("imagensDasNotas ignora imagem de fora da reunião e o que não é imagem", () => {
  assert.deepEqual(R.imagensDasNotas("![x](https://exemplo.com/a.png)\n[link](capturas/a.png)\n"
                                     + "![y](capturas/../../fora.png)"), []);
  assert.deepEqual(R.imagensDasNotas(null), []);
});

test("urlDaImagem monta o endereço pela pasta da gravação, com barra de Windows ou não", () => {
  assert.equal(R.urlDaImagem("C:\\Users\\a\\Reuniões\\2026-10-21_10-00-00", "capturas/tela-00-00-05.png"),
               "https://gravacoes.local/2026-10-21_10-00-00/capturas/tela-00-00-05.png");
  assert.equal(R.urlDaImagem("/tmp/x/2026 out", "capturas/a b.png"),
               "https://gravacoes.local/2026%20out/capturas/a%20b.png");
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

const CATALOGO = [
  { pacote: { id: "large-v3", nome: "Large v3", familia: "asr" }, estado: "instalado", em_uso: false },
  { pacote: { id: "parakeet-ultra", nome: "Parakeet Ultra", familia: "asr" }, estado: "instalado", em_uso: true },
  { pacote: { id: "medium", nome: "Medium", familia: "asr" }, estado: "ausente", em_uso: false },
  { pacote: { id: "qwen3-4b-instruct", nome: "Qwen3 4B", familia: "ata" }, estado: "instalado", em_uso: true },
];

test("opcoesDeModelo vem do catálogo: só transcrição, nome bonito, id como valor", () => {
  const o = R.opcoesDeModelo(CATALOGO);
  assert.deepEqual(o.map((x) => x.valor), ["large-v3", "parakeet-ultra", "medium"]);
  assert.equal(o[1].rotulo, "Parakeet Ultra");
  assert.equal(o[2].rotulo, "Medium (não baixado)");
});

test("opcoesDeModelo marca o padrão de Ajustes, e não o primeiro da lista", () => {
  assert.deepEqual(R.opcoesDeModelo(CATALOGO).filter((x) => x.padrao).map((x) => x.valor),
                   ["parakeet-ultra"]);
});

test("opcoesDeModelo guarda o valor salvo que o catálogo não conhece", () => {
  const o = R.opcoesDeModelo(CATALOGO, "tiny");
  assert.equal(o.at(-1).valor, "tiny");
  assert.equal(R.opcoesDeModelo(CATALOGO, "large-v3").length, 3);   // conhecido não duplica
  assert.deepEqual(R.opcoesDeModelo(null), []);
});

test("resumoDoMotor diz numa linha o que as opções dobradas escondem", () => {
  assert.equal(R.resumoDoMotor({ modelo: "large-v3", idioma: "pt", diarizar: true }),
               "large-v3 · pt · separa os falantes");
  assert.equal(R.resumoDoMotor({ modelo: "small", idioma: " ", diarizar: false }),
               "small · idioma automático · sem separar falantes");
});

test("pedacosDaFala deixa a fala curta inteira, com o tempo dela", () => {
  assert.deepEqual(R.pedacosDaFala({ start: 10, end: 13, text: " vamos começar " }),
    [{ start: 10, end: 13, text: "vamos começar" }]);
  assert.deepEqual(R.pedacosDaFala({ start: 0, end: 1, text: "  " }), []);
});

test("pedacosDaFala corta a fala longa em pedaços parecidos, e o tempo segue as letras", () => {
  const texto = Array.from({ length: 30 }, (_, i) => `p${i}`).join(" ");
  const p = R.pedacosDaFala({ start: 100, end: 130, text: texto }, 14);
  // 30 palavras em no máximo 14: três de 10, e não 14 + 14 + 2.
  assert.deepEqual(p.map((x) => x.text.split(" ").length), [10, 10, 10]);
  assert.equal(p[0].start, 100);
  assert.equal(p.at(-1).end, 130);
  for (let i = 1; i < p.length; i++) assert.equal(p[i].start, p[i - 1].end);
  // O primeiro tem menos letras ("p0".."p9"), então fica com menos tempo.
  assert.ok(p[0].end - p[0].start < p[1].end - p[1].start);
});

test("legendaNoInstante acha a fala do instante, com folga, sem passar da próxima", () => {
  const segs = [
    { start: 0, end: 2, text: "bom dia", speaker: "Ana" },
    { start: 2.3, end: 4, text: "oi", speaker: "Unknown" },
    { start: 10, end: 12, text: "voltando", speaker: "Bia" },
  ];
  assert.deepEqual(R.legendaNoInstante(segs, 1), { falante: "Ana", texto: "bom dia", indice: 0 });
  // A folga segura "bom dia" até a próxima começar, e não além.
  assert.equal(R.legendaNoInstante(segs, 2.2).texto, "bom dia");
  assert.deepEqual(R.legendaNoInstante(segs, 2.5), { falante: null, texto: "oi", indice: 1 });
  assert.equal(R.legendaNoInstante(segs, 4.5).texto, "oi");
  assert.equal(R.legendaNoInstante(segs, 6), null);
  assert.equal(R.legendaNoInstante(segs, -1), null);
  assert.equal(R.legendaNoInstante([], 1), null);
  assert.equal(R.legendaNoInstante(segs, NaN), null);
});

test("legendaNoInstante mostra o pedaço da fala longa que corresponde ao instante", () => {
  const texto = Array.from({ length: 28 }, (_, i) => `p${i}`).join(" ");
  const segs = [{ start: 0, end: 28, text: texto, speaker: "Ana" }];
  assert.ok(R.legendaNoInstante(segs, 1).texto.startsWith("p0 "));
  assert.ok(R.legendaNoInstante(segs, 27).texto.endsWith(" p27"));
});

test("tempoNoVideo desconta onde o vídeo começou", () => {
  assert.equal(R.tempoNoVideo(10, 2.25), 7.75);
  assert.equal(R.tempoNoVideo(1, 2.25), -1.25);
  assert.equal(R.tempoNoVideo(5, undefined), 5);
});
