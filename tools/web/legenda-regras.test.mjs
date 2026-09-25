// A régua que quebra a legenda na pausa, fora do navegador.
//
//     node --test tools/web/legenda-regras.test.mjs
//
// Ver docs/superpowers/specs/2026-09-23-ui-ux.md §2 ("a legenda dos outros em
// blocos grandes demais") e o plano 3, T2.

import { test } from "node:test";
import assert from "node:assert/strict";
import { novaLinha, mostraDono, contarPalavras } from "../../app-net/App/web/legenda-regras.js";

const linha = (dono, ate_s, palavras = 5) => ({ dono, ate_s, palavras });

test("a primeira fala abre linha", () => {
  assert.equal(novaLinha(null, { dono: false, t: 10 }), true);
});

test("troca de dono quebra, mesmo sem pausa", () => {
  assert.equal(novaLinha(linha(false, 10), { dono: true, t: 10.2 }), true);
});

test("um quadro sem fala (1,12 s) não quebra: picotaria a fala", () => {
  assert.equal(novaLinha(linha(false, 10), { dono: false, t: 11.12 }), false);
});

test("dois quadros sem fala (3,36 s) quebram", () => {
  assert.equal(novaLinha(linha(false, 10), { dono: false, t: 13.36 }), true);
  assert.equal(novaLinha(linha(false, 10), { dono: false, t: 13.3 }), false);
});

test("2,24 s quebra só depois de 40 palavras", () => {
  assert.equal(novaLinha(linha(false, 10, 39), { dono: false, t: 12.24 }), false);
  assert.equal(novaLinha(linha(false, 10, 40), { dono: false, t: 12.24 }), true);
  assert.equal(novaLinha(linha(false, 10, 40), { dono: false, t: 12.0 }), false);
});

test("vale para os dois donos", () => {
  assert.equal(novaLinha(linha(true, 10), { dono: true, t: 14 }), true);
});

test("o dono só aparece na primeira linha da sequência", () => {
  assert.equal(mostraDono(null, false), true);
  assert.equal(mostraDono(linha(false, 1), false), false);
  assert.equal(mostraDono(linha(true, 1), false), true);
});

test("palavras contam por espaço", () => {
  assert.equal(contarPalavras("  a gente  pode usar "), 4);
  assert.equal(contarPalavras(""), 0);
});
