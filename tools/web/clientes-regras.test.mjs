// As regras de Ajustes › Clientes e projetos, fora do navegador.
//
//     node --test tools/web/clientes-regras.test.mjs
//
// Uma contagem errada não dá erro: só diz a quem escolhe um cliente que ele
// tem menos reuniões do que tem. Ver o plano 4a do redesenho de UI.

import { test } from "node:test";
import assert from "node:assert/strict";

const R = await import(new URL("../../app-net/App/web/clientes-regras.js", import.meta.url));

const G = (nome, cliente, projeto) => ({ nome, cliente, projeto });

const gravacoes = [
  G("2026-09-17_14-00-00", "Algar", "Agentes"),
  G("2026-09-10_09-00-00", "Algar", "Agentes"),
  G("2026-09-16_10-00-00", "Algar", "Agente de Crédito"),
  G("2026-09-15_10-00-00", "Vivo", "Sherlock"),
  G("2026-09-14_10-00-00", null, null),
  G("reuniao-importada", "Algar", "Agentes"),
];

test("contar soma reuniões por cliente e por projeto, e guarda a mais recente", () => {
  const c = R.contar(gravacoes);
  assert.equal(c.cliente("Algar"), 4);
  assert.equal(c.cliente("Vivo"), 1);
  assert.equal(c.cliente("Ninguém"), 0);
  assert.deepEqual(c.projeto("Algar", "Agentes"), { reunioes: 3, ultima: "2026-09-17" });
  assert.deepEqual(c.projeto("Algar", "Nada"), { reunioes: 0, ultima: null });
});

test("reunião sem cliente não conta para ninguém", () => {
  const c = R.contar(gravacoes);
  assert.equal(c.cliente(""), 0);
  assert.equal(c.cliente(null), 0);
});

test("o rótulo do cliente diz projetos e reuniões, no singular quando é um", () => {
  assert.equal(R.rotuloDoCliente(2, 9), "2 projetos · 9 reuniões");
  assert.equal(R.rotuloDoCliente(1, 1), "1 projeto · 1 reunião");
  assert.equal(R.rotuloDoCliente(1, 0), "1 projeto · nenhuma reunião");
});

test("o rótulo do projeto diz quantas e quando foi a última", () => {
  assert.equal(R.rotuloDoProjeto({ reunioes: 5, ultima: "2026-09-17" }), "5 reuniões · última em 17 set");
  assert.equal(R.rotuloDoProjeto({ reunioes: 1, ultima: "2026-01-02" }), "1 reunião · última em 2 jan");
  assert.equal(R.rotuloDoProjeto({ reunioes: 2, ultima: null }), "2 reuniões");
  assert.equal(R.rotuloDoProjeto({ reunioes: 0, ultima: null }), "nenhuma reunião ainda");
});

test("a busca de clientes ignora acento e caixa, e vazia devolve todos", () => {
  const nomes = ["Algar", "Coca-Cola — CCIL", "Crédito Fácil"];
  assert.deepEqual(R.filtrarClientes(nomes, ""), nomes);
  assert.deepEqual(R.filtrarClientes(nomes, "credito"), ["Crédito Fácil"]);
  assert.deepEqual(R.filtrarClientes(nomes, "  CCIL "), ["Coca-Cola — CCIL"]);
});

test("ordenar segue o português", () => {
  assert.deepEqual(R.ordenar(["Vivo", "Álvaro", "algar"]), ["algar", "Álvaro", "Vivo"]);
});
