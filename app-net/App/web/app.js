import { pedir } from "/ponte.js";

const lista = document.getElementById("lista");
const resumo = document.getElementById("resumo");

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

function cartao(g) {
  const botao = document.createElement("button");
  botao.className = "aa-cartao gravacao";
  botao.type = "button";
  botao.dataset.caminho = g.caminho;

  const esquerda = document.createElement("div");

  const titulo = document.createElement("p");
  titulo.className = "gravacao__titulo";
  // Título da agenda quando existe; senão a data, que é o que o usuário tem
  // para se orientar.
  titulo.textContent = g.titulo || quando(g.nome);
  esquerda.appendChild(titulo);

  const meta = document.createElement("p");
  meta.className = "gravacao__meta";
  const partes = [duracao(g.duracao_s)];
  if (g.titulo) partes.push(quando(g.nome));
  if (g.participantes > 0) partes.push(`${g.participantes} participantes`);
  for (const p of partes) {
    const span = document.createElement("span");
    span.textContent = p;
    meta.appendChild(span);
  }
  esquerda.appendChild(meta);

  if (g.avisos.length > 0) {
    const caixa = document.createElement("div");
    caixa.className = "gravacao__avisos";
    for (const aviso of g.avisos) {
      const alerta = document.createElement("div");
      alerta.className = "aa-alerta aa-alerta--atencao";
      const ponto = document.createElement("span");
      ponto.className = "aa-alerta__ponto";
      alerta.append(ponto, document.createTextNode(aviso));
      caixa.appendChild(alerta);
    }
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

function erro(mensagem) {
  lista.replaceChildren();
  const alerta = document.createElement("div");
  alerta.className = "aa-alerta aa-alerta--erro";
  alerta.textContent = mensagem;
  lista.appendChild(alerta);
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
    erro(e.message);
  }
}

carregar();
