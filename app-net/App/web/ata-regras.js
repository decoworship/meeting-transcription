// A ata lida do Markdown, sem DOM: a mesma leitura desenha a aba Ata
// (atas.js) e monta o HTML que o Copiar leva para o corpo do e-mail.
//
//     node --test tools/web/ata-regras.test.mjs
//
// Uma leitura só, e não duas: o dia em que a ata ganhar uma construção nova, a
// tela e o e-mail precisam aprendê-la juntos.

/**
 * Os blocos da ata, na ordem em que aparecem.
 *
 * São seis construções — título, item de lista, checkbox, negrito, parágrafo e
 * regra — e uma biblioteca custaria mais que isso em bytes e em CSP.
 *
 *   { tipo: "titulo", nivel: 2..5, trechos }
 *   { tipo: "lista", itens: [{ caixa: null | true | false, texto, trechos }] }
 *   { tipo: "paragrafo", rotulo: null | "decidido" | "atencao" | "aberto", trechos }
 *
 * `nivel` já é o do HTML: `#` vira h2, porque o h1 da tela é o título da reunião.
 */
export function blocosDaAta(markdown) {
  const blocos = [];
  let lista = null;

  for (const linha of String(markdown ?? "").split("\n")) {
    const t = linha.trim();

    if (t.length === 0) { lista = null; continue; }

    const titulo = t.match(/^(#{1,4})\s+(.*)$/);
    if (titulo) {
      lista = null;
      blocos.push({ tipo: "titulo", nivel: Math.min(6, titulo[1].length + 1),
                    trechos: trechosComNegrito(titulo[2]) });
      continue;
    }

    const item = t.match(/^[-*]\s+(?:\[( |x)\]\s+)?(.*)$/);
    if (item) {
      if (!lista) {
        lista = { tipo: "lista", itens: [] };
        blocos.push(lista);
      }
      lista.itens.push({
        caixa: item[1] === undefined ? null : item[1] === "x",
        texto: item[2],
        trechos: trechosComNegrito(item[2]),
      });
      continue;
    }

    lista = null;
    // Os rótulos da ata por tema (ATA-5) viram etiqueta colorida.
    const rotulo = t.match(/^\*\*(Decidido|Atenção|Em aberto):\*\*/);
    blocos.push({
      tipo: "paragrafo",
      rotulo: rotulo ? ROTULOS[rotulo[1]] : null,
      trechos: trechosComNegrito(t),
    });
  }
  return blocos;
}

const ROTULOS = { Decidido: "decidido", "Atenção": "atencao", "Em aberto": "aberto" };

/** O texto partido no negrito: `[{ texto, forte }]`. */
export function trechosComNegrito(texto) {
  const trechos = [];
  let resto = texto;
  const negrito = /\*\*(.+?)\*\*/;

  let m;
  while ((m = negrito.exec(resto)) !== null) {
    if (m.index > 0) trechos.push({ texto: resto.slice(0, m.index), forte: false });
    trechos.push({ texto: m[1], forte: true });
    resto = resto.slice(m.index + m[0].length);
  }
  if (resto.length > 0) trechos.push({ texto: resto, forte: false });
  return trechos;
}

// ───────────────────────────────────────── o HTML do e-mail

// Cores fixas, e não os tokens do tema: o corpo do e-mail é branco, e a areia
// do app num fundo branco parece mancha. O escuro levaria texto claro para um
// fundo claro. Os rótulos usam os tons do tema claro do design system
// (assets/ds/colors_and_type.css), que foram feitos para fundo claro.
const COR_TEXTO = "#1F1F1F";
const COR_SUAVE = "#6B6B6B";
const FUNDO = "#FFFFFF";
const ETIQUETA = {
  decidido: { cor: "#4A6746", fundo: "#E7EDE4" },
  atencao: { cor: "#8A5F22", fundo: "#F5EBD8" },
  aberto: { cor: "#3D6189", fundo: "#EAEEF4" },
};

// O Gmail joga fora <style> e classes ao colar; só o style de cada elemento
// sobrevive. Por isso cada um carrega o seu.
const ESTILO = {
  raiz: `background-color:${FUNDO};color:${COR_TEXTO};font-family:Arial,Helvetica,sans-serif;`
    + "font-size:14px;line-height:1.5;",
  h2: `font-size:20px;font-weight:bold;color:${COR_TEXTO};margin:20px 0 8px;`,
  h3: `font-size:16px;font-weight:bold;color:${COR_TEXTO};margin:16px 0 4px;`,
  h4: `font-size:14px;font-weight:bold;color:${COR_SUAVE};margin:12px 0 4px;`,
  p: `color:${COR_TEXTO};margin:0 0 8px;`,
  ul: "margin:0 0 12px;padding-left:24px;",
  li: `color:${COR_TEXTO};margin:0 0 4px;`,
  // A caixinha é um caractere, e não um <input>: o Gmail remove os campos de
  // formulário do que se cola.
  pendencia: `color:${COR_TEXTO};margin:0 0 4px;list-style-type:none;`,
  feita: `color:${COR_SUAVE};text-decoration:line-through;`,
};

/**
 * A ata em HTML para colar no corpo de um e-mail.
 *
 * Fundo branco declarado na raiz: copiar da tela levava a areia do app junto,
 * e ela aparecia como uma faixa no branco do e-mail. O texto vem de um modelo
 * de linguagem, então todo ele passa por `escapar` — o que é tag aqui é só o
 * que este arquivo escreve.
 */
export function htmlParaEmail(markdown) {
  const partes = [];
  for (const b of blocosDaAta(markdown)) {
    if (b.tipo === "titulo") {
      const tag = `h${Math.min(4, b.nivel)}`;
      partes.push(`<${tag} style="${ESTILO[tag]}">${emLinha(b.trechos)}</${tag}>`);
    } else if (b.tipo === "lista") {
      const itens = b.itens.map((i) => {
        if (i.caixa === null) return `<li style="${ESTILO.li}">${emLinha(i.trechos)}</li>`;
        const texto = i.caixa
          ? `<span style="${ESTILO.feita}">${emLinha(i.trechos)}</span>`
          : emLinha(i.trechos);
        return `<li style="${ESTILO.pendencia}">${i.caixa ? "☑" : "☐"}&nbsp;${texto}</li>`;
      });
      partes.push(`<ul style="${ESTILO.ul}">${itens.join("")}</ul>`);
    } else {
      partes.push(`<p style="${ESTILO.p}">${paragrafo(b)}</p>`);
    }
  }
  return `<div style="${ESTILO.raiz}">${partes.join("")}</div>`;
}

function paragrafo(b) {
  if (!b.rotulo) return emLinha(b.trechos);
  // O rótulo é o primeiro negrito; vai em maiúsculas no próprio texto, porque
  // text-transform não é coisa em que se possa confiar num cliente de e-mail.
  const [primeiro, ...resto] = b.trechos;
  const { cor, fundo } = ETIQUETA[b.rotulo];
  const etiqueta = `<strong style="color:${cor};background-color:${fundo};font-size:11px;`
    + `letter-spacing:.05em;padding:1px 6px;border-radius:3px;">`
    + `${escapar(primeiro.texto.toLocaleUpperCase("pt-BR"))}</strong>`;
  return `${etiqueta} ${emLinha(resto).replace(/^\s+/, "")}`;
}

function emLinha(trechos) {
  return trechos.map((t) => (t.forte ? `<strong>${escapar(t.texto)}</strong>` : escapar(t.texto)))
    .join("");
}

export function escapar(texto) {
  return String(texto)
    .replace(/&/g, "&amp;")
    .replace(/</g, "&lt;")
    .replace(/>/g, "&gt;")
    .replace(/"/g, "&quot;")
    .replace(/'/g, "&#39;");
}
