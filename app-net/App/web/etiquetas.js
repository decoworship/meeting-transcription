// O campo de etiquetas do vocabulário (preparo, spec 2026-09-23-ui-ux.md §3.3).
//
// A caixa de texto de antes deixava ler o vocabulário como um parágrafo e
// escondia o repetido; cada termo como etiqueta se lê, se confere e se tira um
// a um. O disco continua vendo o texto de sempre (`juntarTermos`).
//
// Pôr e tirar etiqueta nunca recria o campo de digitar: quem escreve não perde
// o cursor (F-2 da docs/FASE7-FRONTEND.md).

import { separarTermos, juntarTermos } from "/reuniao-regras.js";

export function campoDeEtiquetas({ id, rotulo, aoMudar = () => {} }) {
  const raiz = document.createElement("div");
  raiz.className = "campo etiquetas";
  raiz.id = id;

  const nome = document.createElement("label");
  nome.className = "campo__rotulo";
  nome.htmlFor = `${id}-novo`;
  nome.textContent = rotulo;

  const caixa = document.createElement("div");
  caixa.className = "aa-entrada etiquetas__caixa";

  const lista = document.createElement("span");
  lista.className = "etiquetas__lista";

  const entrada = document.createElement("input");
  entrada.className = "etiquetas__novo";
  entrada.id = `${id}-novo`;
  entrada.type = "text";
  entrada.placeholder = "Acrescentar termo…";
  entrada.autocomplete = "off";

  caixa.append(lista, entrada);
  raiz.append(nome, caixa);
  // Clicar no vazio da caixa é querer digitar.
  caixa.addEventListener("click", (e) => { if (e.target === caixa) entrada.focus(); });

  let termos = [];

  function desenhar() {
    lista.replaceChildren(...termos.map((t) => {
      const e = document.createElement("span");
      e.className = "aa-etiqueta etiquetas__termo";
      e.appendChild(document.createTextNode(t));
      const x = document.createElement("button");
      x.type = "button";
      x.className = "etiquetas__tirar";
      x.textContent = "×";
      x.setAttribute("aria-label", `Tirar ${t}`);
      x.addEventListener("click", () => { tirar(t); entrada.focus(); });
      e.appendChild(x);
      return e;
    }));
  }

  /** Põe os termos de um texto; devolve se algum entrou. */
  function por(texto) {
    const antes = termos.length;
    termos = separarTermos(juntarTermos([...termos, ...separarTermos(texto)]));
    return termos.length !== antes;
  }

  function tirar(t) {
    termos = termos.filter((x) => x !== t);
    desenhar();
    aoMudar();
  }

  function porDigitado() {
    const texto = entrada.value;
    entrada.value = "";
    if (por(texto)) { desenhar(); aoMudar(); }
  }

  entrada.addEventListener("keydown", (e) => {
    if (e.key === "Enter" || e.key === ",") { e.preventDefault(); porDigitado(); }
    else if (e.key === "Backspace" && entrada.value === "" && termos.length) {
      e.preventDefault();
      tirar(termos[termos.length - 1]);
    }
  });
  entrada.addEventListener("paste", (e) => {
    const colado = e.clipboardData?.getData("text/plain") ?? "";
    if (!/[,;\n]/.test(colado)) return;
    e.preventDefault();
    entrada.value = `${entrada.value} ${colado}`;
    porDigitado();
  });
  // Sair com texto no campo é terminar o termo, e não jogá-lo fora.
  entrada.addEventListener("blur", () => { if (entrada.value.trim()) porDigitado(); });

  return {
    raiz,
    valor: () => juntarTermos(termos),
    /** Troca a lista inteira, sem avisar: é o disco chegando, e não a pessoa. */
    definir(texto) { termos = separarTermos(texto); desenhar(); },
    acrescentar(termo) { if (por(termo)) { desenhar(); aoMudar(); } },
  };
}
