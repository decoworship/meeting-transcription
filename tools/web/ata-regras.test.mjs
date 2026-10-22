// A ata lida do Markdown e o HTML do e-mail, fora do navegador.
//
//     node --test tools/web/ata-regras.test.mjs

import { test } from "node:test";
import assert from "node:assert/strict";

const R = await import(new URL("../../app-net/App/web/ata-regras.js", import.meta.url));

const ATA = [
  "# Reunião de alinhamento",
  "",
  "## Decisões",
  "- adiar o **piloto** para março",
  "- manter o NOC",
  "",
  "## Pendências",
  "- [ ] **Ana**: mandar a proposta",
  "- [x] Bruno: revisar o contrato",
  "",
  "### Faturamento",
  "**Decidido:** cobrar por **projeto**.",
  "**Atenção:** o prazo é curto.",
  "**Em aberto:** quem assina.",
  "Texto solto.",
].join("\n");

test("blocosDaAta lê títulos, listas, caixinhas e rótulos", () => {
  const b = R.blocosDaAta(ATA);
  assert.deepEqual(b.map((x) => x.tipo),
    ["titulo", "titulo", "lista", "titulo", "lista", "titulo",
     "paragrafo", "paragrafo", "paragrafo", "paragrafo"]);
  assert.equal(b[0].nivel, 2);    // # vira h2: o h1 é o título da reunião
  assert.equal(b[5].nivel, 4);
  assert.deepEqual(b[4].itens.map((i) => i.caixa), [false, true]);
  assert.equal(b[4].itens[0].texto, "**Ana**: mandar a proposta");
  assert.deepEqual(b[2].itens.map((i) => i.caixa), [null, null]);
  assert.deepEqual(b.slice(6).map((x) => x.rotulo), ["decidido", "atencao", "aberto", null]);
});

test("a linha em branco fecha a lista, e \\r\\n não atrapalha", () => {
  const b = R.blocosDaAta("- um\r\n\r\n- dois\r\n");
  assert.equal(b.length, 2);
  assert.equal(b[0].itens[0].texto, "um");
});

test("trechosComNegrito parte no negrito", () => {
  assert.deepEqual(R.trechosComNegrito("a **b** c **d**"), [
    { texto: "a ", forte: false }, { texto: "b", forte: true },
    { texto: " c ", forte: false }, { texto: "d", forte: true },
  ]);
  assert.deepEqual(R.trechosComNegrito("sem nada"), [{ texto: "sem nada", forte: false }]);
});

test("o HTML do e-mail tem fundo branco na raiz e nenhuma cor de areia", () => {
  const html = R.htmlParaEmail(ATA);
  assert.match(html, /^<div style="background-color:#FFFFFF;/);
  // Os tons de areia do design system, claros e escuros: nenhum vai junto.
  assert.doesNotMatch(html, /#(F4EFE6|DAD0BC|5F584A|3D382F|EDE7DB|1A1714|322D26|B7AC98)/i);
  assert.doesNotMatch(html, /var\(/);
  assert.doesNotMatch(html, /class=/);
});

test("o HTML do e-mail leva títulos, negrito, caixinhas e etiquetas", () => {
  const html = R.htmlParaEmail(ATA);
  assert.match(html, /<h2 style="[^"]*">Reunião de alinhamento<\/h2>/);
  assert.match(html, /<h3 style="[^"]*">Decisões<\/h3>/);
  assert.match(html, /adiar o <strong>piloto<\/strong> para março/);
  assert.match(html, /☐&nbsp;<strong>Ana<\/strong>: mandar a proposta/);
  assert.match(html, /☑&nbsp;<span style="[^"]*line-through[^"]*">Bruno: revisar o contrato<\/span>/);
  assert.doesNotMatch(html, /<input/);
  assert.match(html, /background-color:#E7EDE4;[^"]*">DECIDIDO:<\/strong> cobrar por <strong>projeto<\/strong>\./);
  assert.match(html, />ATENÇÃO:<\/strong> o prazo é curto\./);
  assert.match(html, />EM ABERTO:<\/strong> quem assina\./);
  assert.match(html, /<p style="[^"]*">Texto solto\.<\/p>/);
});

test("o texto do modelo é escapado: o que é tag no HTML é só o que o app escreve", () => {
  const html = R.htmlParaEmail('- <img src=x onerror="alert(1)"> & **<b>**');
  assert.doesNotMatch(html, /<img|<b>/);
  assert.match(html, /&lt;img src=x onerror=&quot;alert\(1\)&quot;&gt; &amp; <strong>&lt;b&gt;<\/strong>/);
});

test("a ata vazia vira só a raiz", () => {
  assert.match(R.htmlParaEmail(""), /^<div style="[^"]*"><\/div>$/);
  assert.match(R.htmlParaEmail(null), /^<div style="[^"]*"><\/div>$/);
});
