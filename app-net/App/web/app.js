import { pedir } from "/ponte.js";

const lista = document.getElementById("lista");
const resumo = document.getElementById("resumo");
const cabecalho = document.getElementById("cabecalho");

/** "1h 02min" ou "3min 20s" — a duração é para dar noção, não para cronometrar. */
function duracao(segundos) {
  const s = Math.round(segundos);
  const h = Math.floor(s / 3600);
  const m = Math.floor((s % 3600) / 60);
  if (h > 0) return `${h}h ${String(m).padStart(2, "0")}min`;
  if (m > 0) return `${m}min ${String(s % 60).padStart(2, "0")}s`;
  return `${s}s`;
}

/** "2026-08-10_08-08-10" -> "10/08/2026 às 08:08". */
function quando(nome) {
  const m = nome.match(/^(\d{4})-(\d{2})-(\d{2})_(\d{2})-(\d{2})/);
  if (!m) return nome;
  const [, ano, mes, dia, hora, min] = m;
  return `${dia}/${mes}/${ano} às ${hora}:${min}`;
}

function titulo(g) {
  return g.titulo || quando(g.nome);
}

function alerta(texto, variacao = "atencao") {
  const el = document.createElement("div");
  el.className = `aa-alerta aa-alerta--${variacao}`;
  const ponto = document.createElement("span");
  ponto.className = "aa-alerta__ponto";
  el.append(ponto, document.createTextNode(texto));
  return el;
}

// ─────────────────────────────────────────────────────────── lista

function cartao(g) {
  const botao = document.createElement("button");
  botao.className = "aa-cartao gravacao";
  botao.type = "button";
  botao.addEventListener("click", () => abrir(g));

  const esquerda = document.createElement("div");

  const t = document.createElement("p");
  t.className = "gravacao__titulo";
  t.textContent = titulo(g);
  esquerda.appendChild(t);

  const meta = document.createElement("p");
  meta.className = "gravacao__meta";
  const partes = [duracao(g.duracao_s)];
  if (g.titulo) partes.push(quando(g.nome));
  // "convidados" e não "participantes": o número vem da lista da agenda, que
  // diz quem foi chamado e não quem apareceu.
  if (g.convidados > 0) partes.push(`${g.convidados} convidados`);
  for (const p of partes) {
    const span = document.createElement("span");
    span.textContent = p;
    meta.appendChild(span);
  }
  esquerda.appendChild(meta);

  if (g.avisos.length > 0) {
    const caixa = document.createElement("div");
    caixa.className = "gravacao__avisos";
    for (const aviso of g.avisos) caixa.appendChild(alerta(aviso));
    esquerda.appendChild(caixa);
  }

  const etiqueta = document.createElement("span");
  etiqueta.className = g.transcrita
    ? "aa-etiqueta aa-etiqueta--sucesso"
    : "aa-etiqueta";
  etiqueta.textContent = g.transcrita ? "Transcrita" : "Não transcrita";

  botao.append(esquerda, etiqueta);
  return botao;
}

async function carregar() {
  try {
    const { gravacoes } = await pedir("gravacoes");
    lista.setAttribute("aria-busy", "false");
    lista.replaceChildren();

    if (gravacoes.length === 0) {
      resumo.textContent = "Nenhuma gravação encontrada.";
      const vazio = document.createElement("p");
      vazio.className = "vazio";
      vazio.textContent =
        "Grave uma reunião com o MeetingRecorder e ela aparece aqui.";
      lista.appendChild(vazio);
      return;
    }

    resumo.textContent =
      gravacoes.length === 1 ? "1 gravação" : `${gravacoes.length} gravações`;
    for (const g of gravacoes) lista.appendChild(cartao(g));
  } catch (e) {
    lista.setAttribute("aria-busy", "false");
    resumo.textContent = "Não foi possível listar as gravações.";
    lista.replaceChildren(alerta(e.message, "erro"));
  }
}

// ────────────────────────────────────────────────────────── detalhe

/** Estado mínimo: qual gravação está aberta. A lista é recarregada ao voltar. */
let aberta = null;

async function abrir(g) {
  aberta = g;
  cabecalho.hidden = true;
  lista.replaceChildren();

  const voltar = document.createElement("button");
  voltar.className = "aa-btn aa-btn-texto voltar";
  voltar.type = "button";
  voltar.textContent = "← Todas as reuniões";
  voltar.addEventListener("click", fechar);

  const topo = document.createElement("div");
  const h = document.createElement("h1");
  h.textContent = titulo(g);
  const sub = document.createElement("p");
  sub.className = "sub";
  sub.textContent = [duracao(g.duracao_s), quando(g.nome)].join(" · ");
  topo.append(h, sub);

  const acoes = document.createElement("div");
  acoes.className = "acoes";

  const painel = document.createElement("div");
  painel.className = "painel";

  lista.append(voltar, topo, acoes, painel);

  if (g.transcrita) {
    mostrarTranscricao(painel, g);
    return;
  }

  const transcrever = document.createElement("button");
  transcrever.className = "aa-btn aa-btn-primario aa-btn--grande";
  transcrever.type = "button";
  transcrever.textContent = "Transcrever";
  transcrever.addEventListener("click", () =>
    executar(g, transcrever, painel),
  );
  acoes.appendChild(transcrever);
}

function fechar() {
  aberta = null;
  cabecalho.hidden = false;
  lista.setAttribute("aria-busy", "true");
  lista.replaceChildren();
  carregar();
}

async function executar(g, botao, painel) {
  botao.disabled = true;
  botao.textContent = "Transcrevendo…";

  const barra = document.createElement("div");
  barra.className = "aa-progresso";
  const preenchimento = document.createElement("div");
  barra.appendChild(preenchimento);

  const estado = document.createElement("p");
  estado.className = "sub";
  estado.textContent = "preparando…";

  painel.replaceChildren(barra, estado);

  try {
    const r = await pedir(
      "transcrever",
      { gravacao: g.caminho },
      (p) => {
        // As etapas não têm a mesma duração; mostrar a fração dentro da etapa
        // com o nome dela é mais honesto que inventar um total.
        const nomes = {
          mix: "Somando as faixas",
          asr: "Transcrevendo",
          diarizacao: "Separando os falantes",
          montagem: "Montando o resultado",
        };
        estado.textContent = `${nomes[p.etapa] ?? p.etapa}: ${p.texto}`;
        const pct = p.fracao >= 0 ? Math.round(p.fracao * 100) : 0;
        preenchimento.style.width = `${pct}%`;
      },
    );

    g.transcrita = true;
    botao.remove();
    painel.replaceChildren();
    mostrarTranscricao(painel, g, r.transcricao);
  } catch (e) {
    botao.disabled = false;
    botao.textContent = "Tentar de novo";
    painel.replaceChildren(alerta(e.message, "erro"));
  }
}

function mostrarTranscricao(painel, g, json) {
  const render = (texto) => {
    if (!texto) {
      painel.replaceChildren(alerta("A transcrição não foi encontrada.", "erro"));
      return;
    }
    const dados = JSON.parse(texto);
    painel.replaceChildren();

    const resumoLinha = document.createElement("p");
    resumoLinha.className = "sub";
    const falantes = new Set(
      dados.segments.map((s) => s.speaker).filter(Boolean),
    );
    resumoLinha.textContent =
      `${dados.segments.length} trechos · ${falantes.size} falantes` +
      (dados.language ? ` · ${dados.language}` : "");
    painel.appendChild(resumoLinha);

    const corpo = document.createElement("div");
    corpo.className = "transcricao";
    for (const s of dados.segments) {
      const linha = document.createElement("p");
      linha.className = "segmento";

      const quem = document.createElement("span");
      quem.className = "segmento__falante";
      quem.textContent = s.speaker ?? "—";

      const fala = document.createElement("span");
      fala.textContent = s.text.trim();

      linha.append(quem, fala);
      corpo.appendChild(linha);
    }
    painel.appendChild(corpo);
  };

  if (json) render(json);
  else pedir("transcricao", { gravacao: g.caminho }).then((r) => render(r.transcricao));
}

carregar();
