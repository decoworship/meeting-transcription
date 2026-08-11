import { pedir } from "/ponte.js";
import { telaDeRevisao, abrirPainel } from "/revisao.js";
import { abrirGaveta, fecharGavetas, alerta, campo, secao,
         campoComSugestoes, preencherSugestoes } from "/pecas.js";

const tela = document.getElementById("tela");
const titulo = document.getElementById("titulo");
const subtitulo = document.getElementById("subtitulo");
const voltar = document.getElementById("voltar");

/** "1h 02min" ou "3min 20s" — a duração é para dar noção, não para cronometrar. */
export function duracao(segundos) {
  const s = Math.round(segundos);
  const h = Math.floor(s / 3600);
  const m = Math.floor((s % 3600) / 60);
  if (h > 0) return `${h}h ${String(m).padStart(2, "0")}min`;
  if (m > 0) return `${m}min ${String(s % 60).padStart(2, "0")}s`;
  return `${s}s`;
}

/** "2026-08-10_08-08-10" -> "10/08/2026 às 08:08". */
export function quando(nome) {
  const m = nome.match(/^(\d{4})-(\d{2})-(\d{2})_(\d{2})-(\d{2})/);
  if (!m) return nome;
  const [, ano, mes, dia, hora, min] = m;
  return `${dia}/${mes}/${ano} às ${hora}:${min}`;
}

export const tituloDe = (g) => g.titulo || quando(g.nome);

function cabecalho(t, sub, comVoltar) {
  titulo.textContent = t;
  subtitulo.textContent = sub ?? "";
  voltar.hidden = !comVoltar;
}

// ─────────────────────────────────────────────────────────── lista

function cartao(g) {
  const botao = document.createElement("button");
  botao.className = "aa-cartao gravacao";
  botao.type = "button";
  botao.addEventListener("click", () => abrirGravacao(g));

  const esquerda = document.createElement("div");

  const t = document.createElement("p");
  t.className = "gravacao__titulo";
  t.textContent = tituloDe(g);
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
  etiqueta.className = g.transcrita ? "aa-etiqueta aa-etiqueta--sucesso" : "aa-etiqueta";
  etiqueta.textContent = g.transcrita ? "Transcrita" : "Não transcrita";

  botao.append(esquerda, etiqueta);
  return botao;
}

export async function telaDeLista() {
  fecharGavetas();
  cabecalho("Reuniões", "", false);
  tela.setAttribute("aria-busy", "true");
  tela.replaceChildren();

  try {
    const { gravacoes } = await pedir("gravacoes");
    tela.setAttribute("aria-busy", "false");
    tela.replaceChildren();

    if (gravacoes.length === 0) {
      cabecalho("Reuniões", "Nenhuma gravação encontrada", false);
      const vazio = document.createElement("p");
      vazio.className = "vazio";
      vazio.textContent = "Grave uma reunião com o MeetingRecorder e ela aparece aqui.";
      tela.appendChild(vazio);
      return;
    }

    cabecalho("Reuniões",
      gravacoes.length === 1 ? "1 gravação" : `${gravacoes.length} gravações`, false);
    for (const g of gravacoes) tela.appendChild(cartao(g));
  } catch (e) {
    tela.setAttribute("aria-busy", "false");
    tela.replaceChildren(alerta(e.message, "erro"));
  }
}

// ───────────────────────────────────────────── preparar / transcrever

/**
 * A tela de antes da transcrição.
 *
 * Reproduz o formulário do app Python, mas já preenchido com o que a gravação
 * sabe de si: título e convidados vêm da agenda, e cliente/projeto vêm do
 * último uso. O que o usuário faz aqui é conferir, não digitar do zero.
 */
async function telaDePreparo(g) {
  cabecalho(tituloDe(g), `${duracao(g.duracao_s)} · ${quando(g.nome)}`, true);
  tela.replaceChildren();

  const { clientes } = await pedir("clientes");

  const forma = document.createElement("div");
  forma.className = "secao";

  // ---- reunião: cliente e projeto aceitam nome novo digitado
  const reuniao = secao("Reunião");
  const linha1 = document.createElement("div");
  linha1.className = "linha";
  linha1.append(
    campoComSugestoes("Cliente", "cliente", Object.keys(clientes)),
    campoComSugestoes("Projeto", "projeto", []),
    campo("Data", "input", { id: "data", tipo: "date", valor: dataDe(g.nome) }),
  );
  reuniao.appendChild(linha1);

  // ---- motor
  const motor = secao("Motor");
  const linha2 = document.createElement("div");
  linha2.className = "linha";
  linha2.append(
    campo("Modelo", "select", {
      id: "modelo",
      opcoes: ["large-v3", "medium", "small", "base", "tiny"],
    }),
    campo("Idioma", "input", { id: "idioma", valor: "pt" }),
    campo("Separar falantes", "select", {
      id: "diarizacao",
      opcoes: ["community-1", "3.1", "não separar"],
    }),
  );
  motor.appendChild(linha2);

  // ---- vocabulário
  const vocab = secao("Vocabulário");
  const caixa = campo("Termos do projeto", "textarea", { id: "vocabulario", linhas: 4 });
  const dica = document.createElement("p");
  dica.className = "campo__dica";
  // O aviso de 224 tokens do app antigo morreu de propósito: a correção
  // fonética a jusante recupera o termo mesmo quando o modelo erra a grafia,
  // então a lista não tem mais teto (FASE0 5-A).
  dica.textContent =
    "Nomes de pessoas, jargão, nomes de sistemas. Sem limite de tamanho — "
    + "o que o modelo escrever parecido é corrigido depois.";
  vocab.append(caixa, dica);

  const acoes = document.createElement("div");
  acoes.className = "acoes";
  const botao = document.createElement("button");
  botao.className = "aa-btn aa-btn-primario aa-btn--grande";
  botao.type = "button";
  botao.textContent = "Transcrever";

  const aviso = document.createElement("span");
  aviso.className = "campo__dica";
  acoes.append(botao, aviso);

  const painel = document.createElement("div");
  forma.append(reuniao, motor, vocab, acoes, painel);
  tela.appendChild(forma);

  // ---- ligações entre os campos
  const campoCliente = document.getElementById("cliente");
  const campoProjeto = document.getElementById("projeto");

  function atualizarProjetos() {
    const projetos = clientes[campoCliente.value] ?? [];
    preencherSugestoes(document.getElementById("projeto-lista"), projetos);
    aviso.textContent = clientes[campoCliente.value]
      ? "" : campoCliente.value ? "cliente novo — será criado ao transcrever" : "";
  }

  /** Ao escolher um projeto conhecido, suas preferências voltam. */
  async function carregarPreferencias() {
    if (!campoCliente.value || !campoProjeto.value) return;
    const { prefs } = await pedir("prefs", {
      cliente: campoCliente.value,
      projeto: campoProjeto.value,
    });
    if (!prefs) {
      aviso.textContent = "projeto novo — será criado ao transcrever";
      return;
    }
    aviso.textContent = "preferências do projeto carregadas";
    if (prefs.model_size) document.getElementById("modelo").value = prefs.model_size;
    if (prefs.language) document.getElementById("idioma").value = prefs.language;
    document.getElementById("diarizacao").value =
      prefs.diarization === false ? "não separar" : (prefs.diar_model ?? "community-1");
    document.getElementById("vocabulario").value = prefs.initial_prompt ?? "";
  }

  campoCliente.addEventListener("change", () => { atualizarProjetos(); carregarPreferencias(); });
  campoCliente.addEventListener("input", atualizarProjetos);
  campoProjeto.addEventListener("change", carregarPreferencias);

  botao.addEventListener("click", () => transcrever(g, botao, painel));
}

function dataDe(nome) {
  const m = nome.match(/^(\d{4}-\d{2}-\d{2})/);
  return m ? m[1] : "";
}

async function transcrever(g, botao, painel) {
  botao.disabled = true;
  botao.textContent = "Transcrevendo…";

  const barra = document.createElement("div");
  barra.className = "aa-progresso";
  const preenchimento = document.createElement("div");
  barra.appendChild(preenchimento);

  const estado = document.createElement("p");
  estado.className = "campo__dica";
  estado.textContent = "preparando…";
  painel.replaceChildren(barra, estado);

  const nomes = {
    mix: "Somando as faixas",
    asr: "Transcrevendo",
    diarizacao: "Separando os falantes",
    montagem: "Montando o resultado",
  };

  try {
    const vocabulario = document.getElementById("vocabulario").value.trim();
    const diar = document.getElementById("diarizacao").value;

    // Guardar antes de transcrever, e não depois: se a transcrição falhar, o
    // que foi digitado aqui não pode se perder junto.
    const cliente = document.getElementById("cliente").value.trim();
    const projeto = document.getElementById("projeto").value.trim();
    if (cliente && projeto) {
      await pedir("salvar-projeto", {
        cliente, projeto,
        prefs: {
          language: document.getElementById("idioma").value.trim(),
          model_size: document.getElementById("modelo").value,
          engine: "faster-whisper",
          diarization: diar !== "não separar",
          diar_model: diar === "não separar" ? "community-1" : diar,
          condition_on_previous_text: false,
          initial_prompt: vocabulario,
        },
      });
    }

    const r = await pedir("transcrever", { gravacao: g.caminho, vocabulario }, (p) => {
      estado.textContent = `${nomes[p.etapa] ?? p.etapa}: ${p.texto}`;
      preenchimento.style.width = `${p.fracao >= 0 ? Math.round(p.fracao * 100) : 0}%`;
    });
    g.transcrita = true;
    telaDeRevisao(g, JSON.parse(r.transcricao), { cabecalho, tela });
  } catch (e) {
    botao.disabled = false;
    botao.textContent = "Tentar de novo";
    painel.replaceChildren(alerta(e.message, "erro"));
  }
}

export async function abrirGravacao(g) {
  fecharGavetas();
  if (!g.transcrita) return telaDePreparo(g);

  cabecalho(tituloDe(g), "carregando a transcrição…", true);
  tela.replaceChildren();
  const r = await pedir("transcricao", { gravacao: g.caminho });
  if (!r.transcricao) {
    tela.replaceChildren(alerta("A transcrição não foi encontrada.", "erro"));
    return;
  }
  telaDeRevisao(g, JSON.parse(r.transcricao), { cabecalho, tela });
}

// ──────────────────────────────────────────────────── configurações

async function telaDeConfiguracoes() {
  const corpo = document.getElementById("corpo-config");
  corpo.replaceChildren();

  const pastas = secao("Gravações");
  pastas.append(
    campo("Pasta das gravações", "input", {
      id: "cfg-pasta",
      valor: "%USERPROFILE%\\Documents\\MeetingRecordings",
    }),
  );
  const dica = document.createElement("p");
  dica.className = "campo__dica";
  dica.textContent = "É a mesma pasta que o gravador usa — mudar aqui muda nos dois.";
  pastas.appendChild(dica);

  const motores = secao("Motores");
  motores.append(
    campo("Modelo padrão", "select", {
      id: "cfg-modelo",
      opcoes: ["large-v3", "medium", "small"],
    }),
    campo("Modelo de diarização", "select", {
      id: "cfg-diar",
      opcoes: ["community-1", "3.1"],
    }),
    campo("Token do HuggingFace", "input", {
      id: "cfg-hf",
      tipo: "password",
      valor: "",
    }),
  );
  const dicaHf = document.createElement("p");
  dicaHf.className = "campo__dica";
  dicaHf.textContent = "Só é necessário na primeira execução, para baixar o modelo de falantes.";
  motores.appendChild(dicaHf);

  const { clientes } = await pedir("clientes");
  const nClientes = Object.keys(clientes).length;
  const nProjetos = Object.values(clientes).reduce((s, p) => s + p.length, 0);

  const listas = secao("Cadastros");
  for (const [rotulo, texto] of [
    ["Clientes e projetos",
     `${nClientes} ${nClientes === 1 ? "cliente" : "clientes"}, `
     + `${nProjetos} ${nProjetos === 1 ? "projeto" : "projetos"}`],
    ["Vozes conhecidas", "nenhuma voz salva"],
  ]) {
    const linha = document.createElement("div");
    linha.className = "acoes";
    const b = document.createElement("button");
    b.className = "aa-btn aa-btn-secundario";
    b.type = "button";
    b.textContent = rotulo;
    const t = document.createElement("span");
    t.className = "campo__dica";
    t.textContent = texto;
    linha.append(b, t);
    listas.appendChild(linha);
  }

  corpo.append(pastas, motores, listas);
  abrirGaveta("gaveta-config");
}

// ─────────────────────────────────────────────────────────── ligação

document.getElementById("abrir-config").addEventListener("click", telaDeConfiguracoes);
voltar.addEventListener("click", telaDeLista);

for (const b of document.querySelectorAll("[data-fechar]"))
  b.addEventListener("click", fecharGavetas);
document.getElementById("veu").addEventListener("click", fecharGavetas);
document.addEventListener("keydown", (e) => { if (e.key === "Escape") fecharGavetas(); });

/**
 * Abre direto numa tela quando o app foi iniciado com --tela.
 *
 * Existe para desenhar e fotografar cada estado sem depender de clique — e
 * clique automatizado, quando tentado, acertou a janela errada.
 */
async function inicio() {
  const hash = location.hash.slice(1);
  if (!hash) return telaDeLista();

  // "revisao=1&falantes" — a parte depois do & abre um painel por cima, que é
  // o que não dá para alcançar sem clique.
  const [principal, extra] = hash.split("&");
  const [tela, arg] = principal.split("=");
  const { gravacoes } = await pedir("gravacoes");
  const g = gravacoes[Number(arg) || 0];

  if (tela === "config") { telaDeLista(); telaDeConfiguracoes(); return; }
  if (!g) return telaDeLista();

  if (tela === "preparo") { g.transcrita = false; return abrirGravacao(g); }
  if (tela === "revisao") {
    await abrirGravacao(g);
    if (extra) abrirPainel(extra);
    return;
  }
  return telaDeLista();
}

inicio();
