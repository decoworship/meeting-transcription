// As regras da lista de Reuniões, fora do navegador.
//
//     node --test tools/web/reunioes-regras.test.mjs
//
// O que dói errar aqui não dá erro: um filtro que esconde uma reunião só deixa
// a lista menor. Ver docs/superpowers/specs/2026-09-23-ui-ux.md §3.3.

import { test } from "node:test";
import assert from "node:assert/strict";

const R = await import(new URL("../../app-net/App/web/reunioes-regras.js", import.meta.url));

const G = (nome, extra = {}) => ({
  nome, caminho: `C:\\Rec\\${nome}`, titulo: null, cliente: null, projeto: null,
  transcrita: true, tem_ata: false, ata_velha: false, pendencias: 0, nomes: [], ...extra,
});

test("normalizar tira acento, maiúscula e espaço das pontas", () => {
  assert.equal(R.normalizar("  Agente de CRÉDITO "), "agente de credito");
  assert.equal(R.normalizar(null), "");
});

test("diaDe e horaDe leem o nome da pasta, e nome fora do padrão não tem dia", () => {
  assert.equal(R.diaDe("2026-09-23_14-00-12"), "2026-09-23");
  assert.equal(R.horaDe("2026-09-23_14-00-12"), "14:00");
  assert.equal(R.diaDe("reuniao-importada"), null);
  assert.equal(R.horaDe("reuniao-importada"), "");
});

test("hojeLocal escreve a data local com zeros", () => {
  assert.equal(R.hojeLocal(new Date(2026, 8, 3, 23, 59)), "2026-09-03");
});

test("rotuloDoDia diz hoje, ontem, o dia da semana, e o ano quando não é o de hoje", () => {
  assert.equal(R.rotuloDoDia("2026-09-23", "2026-09-23"), "Hoje · quarta, 23 set");
  assert.equal(R.rotuloDoDia("2026-09-22", "2026-09-23"), "Ontem · terça, 22 set");
  assert.equal(R.rotuloDoDia("2026-09-17", "2026-09-23"), "Quinta, 17 set");
  assert.equal(R.rotuloDoDia("2025-12-31", "2026-09-23"), "Quarta, 31 dez 2025");
  assert.equal(R.rotuloDoDia(null, "2026-09-23"), "Sem data");
});

test("agruparPorDia mantém a ordem e manda o que não tem data para o fim", () => {
  // O núcleo ordena pelo nome, decrescente — e "reuniao-…" vem ANTES dos
  // números nessa ordem. É o caso que a regra existe para consertar.
  const lista = [G("reuniao-importada"), G("2026-09-23_11-02-00"),
                 G("2026-09-23_09-00-00"), G("2026-09-17_13-59-57")];

  const grupos = R.agruparPorDia(lista, "2026-09-23");

  assert.deepEqual(grupos.map((g) => g.rotulo),
                   ["Hoje · quarta, 23 set", "Quinta, 17 set", "Sem data"]);
  assert.deepEqual(grupos[0].itens.map((g) => g.nome),
                   ["2026-09-23_11-02-00", "2026-09-23_09-00-00"]);
});

test("estadoDe segue a escada, e o que roda vem antes de tudo", () => {
  assert.equal(R.estadoDe(G("a", { transcrita: false })).chave, "nao-transcrita");
  assert.equal(R.estadoDe(G("a")).chave, "sem-ata");
  assert.equal(R.estadoDe(G("a", { tem_ata: true })).chave, "ata-pronta");
  assert.equal(R.estadoDe(G("a", { tem_ata: true, ata_velha: true })).chave, "ata-velha");
  assert.deepEqual(
    R.estadoDe(G("a", { transcrita: false }), { tarefa: "transcricao", etapa: "diarizacao" }),
    { chave: "transcrevendo", rotulo: "Separando falantes…", tom: "info" });
  assert.equal(R.estadoDe(G("a"), { tarefa: "ata", etapa: "lendo" }).rotulo, "Escrevendo a ata…");
});

test("estadoDe aguenta o núcleo antigo, que não manda os campos da ata", () => {
  const antiga = { nome: "2026-09-23_11-02-00", caminho: "x", transcrita: true };
  assert.equal(R.estadoDe(antiga).chave, "sem-ata");
});

test("proximoPasso oferece o que a reunião precisa", () => {
  assert.equal(R.proximoPasso(G("a", { transcrita: false })).acao, "transcrever");
  assert.equal(R.proximoPasso(G("a")).acao, "gerar-ata");
  assert.equal(R.proximoPasso(G("a", { tem_ata: true, ata_velha: true })).acao, "refazer-ata");
  assert.equal(R.proximoPasso(G("a", { tem_ata: true })).acao, "abrir-ata");
  assert.equal(R.proximoPasso(G("a"), { tarefa: "transcricao" }).acao, "acompanhar-transcricao");
  assert.equal(R.proximoPasso(G("a"), { tarefa: "ata" }).acao, "acompanhar-ata");
});

const acervo = [
  G("2026-09-23_11-02-00", { titulo: "Reunião de lideranças", cliente: "Beegol (interno)", projeto: "Gestão" }),
  G("2026-09-23_10-30-00", { titulo: "Semanal — Uberlândia", transcrita: false }),
  G("2026-09-17_13-59-57", { titulo: "Comunicação Beegol + App", cliente: "Algar", projeto: "Agentes",
                             nomes: ["Rafael Prado"], tem_ata: true, pendencias: 3 }),
  G("2026-09-16_15-29-00", { titulo: "Kickoff", cliente: "Algar", projeto: "Agente de Crédito", tem_ata: true }),
  G("2026-08-01_09-00-00", { titulo: "Antiga", cliente: "Vivo", projeto: "Sherlock" }),
  G("reuniao-importada", { titulo: "Importada" }),
];
const titulos = (l) => l.map((g) => g.titulo);
const opcoes = { hoje: "2026-09-23" };

test("sem critério, nada some", () => {
  assert.equal(R.filtrar(acervo, {}, opcoes).length, acervo.length);
});

test("a busca ignora acento, casa todas as palavras, e olha projeto, convidado e data", () => {
  assert.deepEqual(titulos(R.filtrar(acervo, { texto: "credito" }, opcoes)), ["Kickoff"]);
  assert.deepEqual(titulos(R.filtrar(acervo, { texto: "algar rafael" }, opcoes)),
                   ["Comunicação Beegol + App"]);
  assert.deepEqual(titulos(R.filtrar(acervo, { texto: "17/09" }, opcoes)),
                   ["Comunicação Beegol + App"]);
});

test("o filtro de cliente separa, e 'sem cliente' pega os que não têm", () => {
  assert.deepEqual(titulos(R.filtrar(acervo, { cliente: "Algar" }, opcoes)),
                   ["Comunicação Beegol + App", "Kickoff"]);
  assert.deepEqual(titulos(R.filtrar(acervo, { cliente: R.SEM_CLIENTE }, opcoes)),
                   ["Semanal — Uberlândia", "Importada"]);
});

test("a semana vai de segunda a domingo, inclusive quando atravessa o mês", () => {
  // 23/09/2026 é uma quarta: esta semana vai de 21 a 27, a passada de 14 a 20.
  assert.deepEqual(R.intervaloDoPeriodo("esta-semana", "", "2026-09-23"), ["2026-09-21", "2026-09-27"]);
  assert.deepEqual(R.intervaloDoPeriodo("semana-passada", "", "2026-09-23"), ["2026-09-14", "2026-09-20"]);
  // 06/09 é domingo: a semana dele começou na segunda, 31 de agosto.
  assert.deepEqual(R.intervaloDoPeriodo("semana", "2026-09-06", "2026-09-23"), ["2026-08-31", "2026-09-06"]);
  assert.deepEqual(R.intervaloDoPeriodo("dia", "2026-09-17", "2026-09-23"), ["2026-09-17", "2026-09-17"]);
  assert.deepEqual(R.intervaloDoPeriodo("30", "", "2026-09-23"), ["2026-08-25", "2026-09-23"]);
  assert.equal(R.intervaloDoPeriodo("tudo", "", "2026-09-23"), null);
});

test("'um dia' sem o dia escolhido ainda não filtra nada", () => {
  assert.equal(R.intervaloDoPeriodo("dia", "", "2026-09-23"), null);
  assert.equal(R.filtrar(acervo, { periodo: "dia", data: "" }, opcoes).length, acervo.length);
});

test("a data filtra pelo dia da pasta, e o sem data só aparece em 'qualquer data'", () => {
  assert.deepEqual(titulos(R.filtrar(acervo, { periodo: "esta-semana" }, opcoes)),
                   ["Reunião de lideranças", "Semanal — Uberlândia"]);
  assert.deepEqual(titulos(R.filtrar(acervo, { periodo: "semana-passada" }, opcoes)),
                   ["Comunicação Beegol + App", "Kickoff"]);
  assert.deepEqual(titulos(R.filtrar(acervo, { periodo: "dia", data: "2026-09-17" }, opcoes)),
                   ["Comunicação Beegol + App"]);
  assert.equal(R.filtrar(acervo, { periodo: "30" }, opcoes).length, 4);
  assert.equal(R.filtrar(acervo, { periodo: "tudo" }, opcoes).length, acervo.length);
});

test("o estado filtra pela escada, e 'com pendências' é ata com item aberto", () => {
  assert.deepEqual(titulos(R.filtrar(acervo, { estado: "nao-transcrita" }, opcoes)),
                   ["Semanal — Uberlândia"]);
  assert.deepEqual(titulos(R.filtrar(acervo, { estado: "com-pendencias" }, opcoes)),
                   ["Comunicação Beegol + App"]);
  assert.deepEqual(titulos(R.filtrar(acervo, { estado: "ata-pronta" }, opcoes)),
                   ["Comunicação Beegol + App", "Kickoff"]);
});

test("o estado usa o que está rodando: transcrição em curso não é 'não transcrita'", () => {
  const rodandoDe = (c) => (c === acervo[1].caminho ? { tarefa: "transcricao", etapa: "asr" } : null);
  assert.deepEqual(R.filtrar(acervo, { estado: "nao-transcrita" }, { ...opcoes, rodandoDe }), []);
});

test("clientesDe conta e ordena em português", () => {
  assert.deepEqual(R.clientesDe(acervo),
                   [{ nome: "Algar", n: 2 }, { nome: "Beegol (interno)", n: 1 }, { nome: "Vivo", n: 1 }]);
});
