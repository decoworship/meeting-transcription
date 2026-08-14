// As notas da reunião: o mesmo editor, em dois lugares.
//
// No Gravador enquanto se grava — que é quando a nota de fato acontece — e na
// reunião depois, porque quem toma nota corre atrás do que perdeu assim que a
// reunião acaba (FASE3.md §3). Um editor só, para o comportamento de salvar não
// divergir entre as duas telas.
//
// Salva sozinho. Nota de reunião perdida por falta de um clique em "salvar" é a
// falha mais boba que este bloco pode ter.

import { pedir } from "/ponte.js";

const ATRASO_MS = 800;

/** "00:12:34" — o mesmo relógio da tela do Gravador. */
function relogio(segundos) {
  const s = Math.max(0, Math.round(segundos));
  const p = (n) => String(n).padStart(2, "0");
  return `${p(Math.floor(s / 3600))}:${p(Math.floor((s % 3600) / 60))}:${p(s % 60)}`;
}

/**
 * Monta o bloco de notas de uma gravação.
 *
 * @param gravacao caminho da pasta; pode chegar nulo e ser definido depois
 *        (a tela do Gravador só sabe qual é a pasta quando a gravação começa).
 * @param opcoes.tempo função que devolve os segundos decorridos, quando há
 *        gravação correndo. Sem ela, não há botão de marcar momento.
 * @param opcoes.aoMudar chamado depois de cada gravação bem-sucedida, com os
 *        termos que o núcleo achou no texto.
 */
export function blocoDeNotas(gravacao, opcoes = {}) {
  const raiz = document.createElement("div");
  raiz.className = "bloco notas";

  const topo = document.createElement("div");
  topo.className = "notas__topo";

  const titulo = document.createElement("h2");
  titulo.className = "bloco__titulo";
  titulo.textContent = "Notas da reunião";

  const estado = document.createElement("span");
  estado.className = "campo__dica notas__estado";

  topo.append(titulo, estado);

  const campo = document.createElement("textarea");
  campo.className = "aa-entrada notas__texto";
  campo.rows = opcoes.linhas ?? 8;
  campo.placeholder = "Decisões, pendências, nomes. Salva sozinho.";
  campo.spellcheck = true;

  const acoes = document.createElement("div");
  acoes.className = "notas__acoes";

  // Marcar o momento é o que liga a nota ao áudio depois. Só existe com
  // gravação correndo, porque fora dela não há tempo decorrido que signifique
  // alguma coisa.
  let marcar = null;
  if (opcoes.tempo) {
    marcar = document.createElement("button");
    marcar.className = "aa-btn aa-btn-secundario";
    marcar.type = "button";
    marcar.textContent = "Marcar momento";
    marcar.addEventListener("click", () => {
      const marca = `\n[${relogio(opcoes.tempo())}] `;
      const pos = campo.selectionStart ?? campo.value.length;
      campo.value = campo.value.slice(0, pos) + marca + campo.value.slice(pos);
      campo.focus();
      campo.selectionStart = campo.selectionEnd = pos + marca.length;
      agendar();
    });
    acoes.appendChild(marcar);
  }

  raiz.append(topo, campo, acoes);

  // ─────────────────────────────────────────────────────── salvar

  let alvo = gravacao ?? null;
  let relogioDeEspera = null;
  let ultimoSalvo = "";
  let carregado = false;
  let trocando = false;

  function agendar() {
    if (!alvo) return;
    estado.textContent = "…";
    clearTimeout(relogioDeEspera);
    relogioDeEspera = setTimeout(salvar, ATRASO_MS);
  }

  async function salvar() {
    clearTimeout(relogioDeEspera);
    if (!alvo || !carregado) return;

    const texto = campo.value;
    if (texto === ultimoSalvo) { estado.textContent = "salvo"; return; }

    try {
      const r = await pedir("salvar-notas", { gravacao: alvo, conteudo: texto });
      ultimoSalvo = texto;
      estado.textContent = "salvo";
      opcoes.aoMudar?.(r.termos ?? []);
    } catch (e) {
      // Não some com o texto nem desabilita o campo: o que está na tela é a
      // única cópia do que a pessoa escreveu, e ela pode continuar escrevendo
      // enquanto o disco não responde.
      estado.textContent = `não salvou: ${e.message}`;
    }
  }

  campo.addEventListener("input", agendar);
  // Sair do campo grava na hora: esperar o atraso quando já se está indo embora
  // é apostar que a tela sobrevive mais 800 ms.
  campo.addEventListener("blur", salvar);

  async function carregar(caminho) {
    alvo = caminho ?? alvo;
    if (!alvo) return;
    try {
      const r = await pedir("notas", { gravacao: alvo });
      // Não sobrescreve o que está sendo digitado: numa gravação que acabou de
      // começar, a resposta pode chegar depois da primeira tecla.
      if (campo.value === "" || campo.value === ultimoSalvo) campo.value = r.notas ?? "";
      ultimoSalvo = campo.value;
      carregado = true;
      estado.textContent = campo.value ? "salvo" : "";
      opcoes.aoMudar?.(r.termos ?? []);
    } catch {
      carregado = true;      // sem notas anteriores é o caso normal
    }
  }

  carregar(alvo);

  return {
    raiz,
    campo,
    /** A tela do Gravador chama quando a gravação começa, para saber onde escrever. */
    async apontarPara(caminho) {
      // A tela do Gravador chama isto a cada evento — cinco vezes por segundo
      // enquanto grava. Sem a trava, a segunda chamada entraria enquanto a
      // primeira ainda espera o disco, e as duas carregariam a mesma nota.
      if (caminho === alvo || trocando) return;
      trocando = true;
      try {
        await trocarAlvo(caminho);
      } finally {
        trocando = false;
      }
    },
    /** Grava agora, sem esperar o atraso. Para quem está saindo da tela. */
    salvarAgora: salvar,
    definirHabilitado(ligado, aviso = "") {
      campo.disabled = !ligado;
      if (marcar) marcar.disabled = !ligado;
      if (!ligado && aviso) estado.textContent = aviso;
    },
  };

  /**
   * Troca a gravação que este bloco edita.
   *
   * Grava o que estava escrito ANTES de trocar: parar a gravação troca o alvo
   * para nulo, e limpar o campo primeiro jogaria fora a última frase —
   * justamente a que se escreve quando a reunião está acabando.
   */
  async function trocarAlvo(caminho) {
    await salvar();
    alvo = caminho;
    campo.value = "";
    ultimoSalvo = "";
    carregado = false;
    if (caminho) await carregar(caminho);
  }
}
