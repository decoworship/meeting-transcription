import { pedir } from "/ponte.js";
import { telaDeRevisao, abrirPainel } from "/revisao.js";
import { telaDeAjustes } from "/configuracoes.js";
import { telaDoGravador } from "/gravador.js";
import { abrirGaveta, fecharGavetas, alerta, campo, secao,
         campoComSugestoes, preencherSugestoes } from "/pecas.js";
import { transcrever as pedirTranscricao, assinarTranscricoes, emCurso,
         ultimoResultado, sincronizar, cancelar } from "/transcricoes.js";
import { blocoDeNotas } from "/notas.js";
import { telaDeAtas } from "/atas.js";
import { ligarBolinhas } from "/trilho.js";

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

/**
 * Marca no trilho onde estamos.
 *
 * Reuniões, preparo e revisão são o mesmo destino: quem está revisando uma
 * transcrição continua "em Reuniões", e acender outra coisa mentiria sobre
 * onde o ← voltar leva.
 */
function destino(qual) {
  for (const b of document.querySelectorAll(".trilho__item"))
    b.removeAttribute("aria-current");
  document.getElementById(qual)?.setAttribute("aria-current", "page");
}

// ─────────────────────────────────────────────────────────── lista

function cartao(g) {
  const botao = document.createElement("button");
  botao.className = "aa-cartao gravacao";
  botao.type = "button";
  botao.dataset.gravacao = g.caminho;
  botao.addEventListener("click", () => abrirGravacao(g));

  const esquerda = document.createElement("div");

  const t = document.createElement("p");
  t.className = "gravacao__titulo";
  t.textContent = tituloDe(g);
  esquerda.appendChild(t);

  // Cliente e projeto na frente da duração: é por eles que se procura uma
  // reunião de duas semanas atrás, e é o que prova que a escolha feita na tela
  // de preparo ficou guardada — antes ela sumia de vista assim que se saía da
  // tela, e parecia perdida mesmo estando salva.
  const meta = document.createElement("p");
  meta.className = "gravacao__meta";
  const partes = [];
  if (g.cliente || g.projeto)
    partes.push([g.cliente, g.projeto].filter(Boolean).join(" · "));
  partes.push(duracao(g.duracao_s));
  if (g.titulo) partes.push(quando(g.nome));
  // "convidados" e não "participantes": o número vem da lista da agenda, que
  // diz quem foi chamado e não quem apareceu.
  if (g.convidados > 0) partes.push(`${g.convidados} convidados`);
  if (g.com_notas) partes.push("com notas");
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

  botao.append(esquerda, etiquetaDe(g));
  return botao;
}

/**
 * O estado da gravação em uma palavra.
 *
 * "Transcrevendo…" tem precedência sobre "Não transcrita" porque a lista é o
 * lugar onde se procura a reunião de novo depois de sair da tela dela — e ali
 * "Não transcrita" ao lado de uma transcrição que está rodando é mentira.
 */
function etiquetaDe(g) {
  const etiqueta = document.createElement("span");
  const rodando = emCurso(g.caminho);
  if (rodando) {
    etiqueta.className = "aa-etiqueta";
    // A ata usa o mesmo registro da transcrição, e dizer "Transcrevendo…"
    // enquanto se escreve a ata de uma reunião já transcrita é mentira — foi o
    // que o dono do produto viu no primeiro uso.
    etiqueta.textContent = rodando.tarefa === "ata"
      ? "Escrevendo a ata…" : "Transcrevendo…";
  } else {
    etiqueta.className = g.transcrita ? "aa-etiqueta aa-etiqueta--sucesso" : "aa-etiqueta";
    etiqueta.textContent = g.transcrita ? "Transcrita" : "Não transcrita";
  }
  return etiqueta;
}

export async function telaDeLista() {
  fecharGavetas();
  destino("ir-reunioes");
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
      // Não fala mais em "o MeetingRecorder": desde a Fase 2.5 o gravador é
      // este mesmo app, e mandar a pessoa procurar outro programa era mandá-la
      // procurar algo que não existe mais.
      vazio.textContent = "Nenhuma gravação ainda. Comece uma em Gravador.";
      const ir = document.createElement("button");
      ir.className = "aa-btn aa-btn-primario";
      ir.type = "button";
      ir.textContent = "Ir para o Gravador";
      ir.addEventListener("click", abrirGravador);
      tela.append(vazio, ir);
      return;
    }

    cabecalho("Reuniões",
      gravacoes.length === 1 ? "1 gravação" : `${gravacoes.length} gravações`, false);
    for (const g of gravacoes) tela.appendChild(cartao(g));

    // A etiqueta acompanha: quem fica parado na lista enquanto uma transcrição
    // termina vê "Transcrita" aparecer sozinha, sem precisar recarregar nada.
    const cancelar = assinarTranscricoes(() => {
      if (!tela.isConnected || !tela.querySelector(".gravacao")) { cancelar(); return; }
      for (const g of gravacoes) {
        const cartaoDela = tela.querySelector(`[data-gravacao="${CSS.escape(g.caminho)}"]`);
        if (!cartaoDela) continue;
        const fim = ultimoResultado(g.caminho);
        if (!emCurso(g.caminho) && fim && !fim.erro) g.transcrita = true;
        cartaoDela.lastElementChild.replaceWith(etiquetaDe(g));
      }
    });
  } catch (e) {
    tela.setAttribute("aria-busy", "false");
    tela.replaceChildren(alerta(e.message, "erro"));
  }
}

/**
 * Apaga a gravação inteira — áudio, metadados e transcrição.
 *
 * Mora <b>dentro</b> da gravação aberta, e não na lista. Um botão de apagar em
 * cada cartão põe a ação mais destrutiva do app a um clique errado de distância,
 * numa tela que se percorre rápido; abrir a gravação primeiro custa um clique e
 * garante que quem apaga está olhando para o que apaga.
 *
 * A confirmação nomeia a gravação e diz o que não volta. O áudio original não se
 * refaz: não há lixeira, e o gravador não guarda cópia.
 */
export function botaoApagarGravacao(g) {
  const b = document.createElement("button");
  b.className = "aa-btn aa-btn-texto";
  b.type = "button";
  b.textContent = "Apagar gravação";
  b.addEventListener("click", async () => {
    if (!confirm(
      `Apagar "${tituloDe(g)}"?\n\n`
      + "Isto apaga o áudio, os metadados e a transcrição desta reunião. "
      + "O áudio original não pode ser recuperado.")) return;

    b.disabled = true;
    try {
      await pedir("apagar-gravacao", { gravacao: g.caminho });
    } catch (e) {
      alert(`não apagou: ${e.message}`);
      b.disabled = false;
      return;
    }
    telaDeLista();
  });
  return b;
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

  // O vínculo vem junto: cliente e projeto escolhidos antes sobrevivem a sair
  // da tela, porque moram em reuniao.json na pasta da gravação e não dentro da
  // transcrição — que, na tela de preparo, ainda não existe.
  const [{ clientes }, vinculo] = await Promise.all([
    pedir("clientes"), pedir("reuniao", { gravacao: g.caminho }),
  ]);

  const forma = document.createElement("div");
  forma.className = "secao";

  // ---- reunião: cliente e projeto aceitam nome novo digitado
  const reuniao = secao("Reunião");
  const linha1 = document.createElement("div");
  linha1.className = "linha";
  linha1.append(
    campoComSugestoes("Cliente", "cliente", Object.keys(clientes), vinculo.cliente ?? ""),
    campoComSugestoes("Projeto", "projeto", clientes[vinculo.cliente] ?? [],
                      vinculo.projeto ?? ""),
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
    // Sim ou não, e nada de escolher o modelo de diarização: o "3.1" era opção
    // de tela que o pipeline ignorava — o motor sempre usou o community-1, que
    // a Fase 0 mediu 6,7 pontos de DER melhor. Oferecer o pior, e não aplicar
    // nem isso, era duas mentiras na mesma linha (FASE0-RESULTADOS).
    campo("Separar falantes", "select", { id: "diarizacao", opcoes: ["sim", "não"] }),
  );
  motor.appendChild(linha2);

  // ---- notas escritas durante a reunião
  //
  // Antes do vocabulário de propósito: é delas que saem os nomes próprios e as
  // siglas que o vocabulário quer, e lê-las primeiro é a ordem em que a pessoa
  // vai querer copiar.
  const blocoNotas = secao("Notas");
  const notas = blocoDeNotas(g.caminho, { linhas: 5, aoMudar: (t) => sugerirTermos(t) });
  blocoNotas.appendChild(notas.raiz);

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
  // Os termos que as notas revelaram, oferecidos um a um.
  //
  // Sugestão e não injeção: o nome vai para o vocabulário quando a pessoa
  // clica, porque quem escreveu a nota sabe o que é nome de sistema e o que é
  // a primeira palavra de uma frase (FASE3.md §3).
  const sugestoes = document.createElement("div");
  sugestoes.className = "sugestoes";
  vocab.append(caixa, dica, sugestoes);

  function sugerirTermos(termos) {
    sugestoes.replaceChildren();
    const caixaVocab = document.getElementById("vocabulario");
    if (!caixaVocab) return;

    const jaTem = new Set(caixaVocab.value.split(",").map((t) => t.trim()).filter(Boolean));
    const novos = termos.filter((t) => !jaTem.has(t));
    if (novos.length === 0) return;

    const rotulo = document.createElement("span");
    rotulo.className = "campo__dica";
    rotulo.textContent = "Das suas notas:";
    sugestoes.appendChild(rotulo);

    for (const termo of novos.slice(0, 12)) {
      const b = document.createElement("button");
      b.className = "aa-etiqueta sugestao";
      b.type = "button";
      b.textContent = `+ ${termo}`;
      b.title = "Acrescentar ao vocabulário";
      b.addEventListener("click", () => {
        const atual = caixaVocab.value.trim();
        caixaVocab.value = atual ? `${atual}, ${termo}` : termo;
        b.remove();
        if (sugestoes.querySelectorAll(".sugestao").length === 0) sugestoes.replaceChildren();
      });
      sugestoes.appendChild(b);
    }
  }

  const acoes = document.createElement("div");
  acoes.className = "acoes";
  const botao = document.createElement("button");
  botao.className = "aa-btn aa-btn-primario aa-btn--grande";
  botao.type = "button";
  botao.textContent = "Transcrever";

  const aviso = document.createElement("span");
  aviso.className = "campo__dica";
  acoes.append(botao, aviso, botaoApagarGravacao(g));

  const painel = document.createElement("div");
  forma.append(reuniao, motor, blocoNotas, vocab, acoes, painel);
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

    // A tela pode ter sido trocada enquanto a resposta vinha — escolher o
    // projeto e sair no mesmo segundo é um caminho normal. Sem esta guarda, o
    // preenchimento cai num getElementById que devolve null e o erro sobe no
    // console sem ninguém ver.
    if (!campoCliente.isConnected) return;

    if (!prefs) {
      aviso.textContent = "projeto novo — será criado ao transcrever";
      return;
    }
    aviso.textContent = "preferências do projeto carregadas";
    if (prefs.model_size) document.getElementById("modelo").value = prefs.model_size;
    if (prefs.language) document.getElementById("idioma").value = prefs.language;
    document.getElementById("diarizacao").value = prefs.diarization === false ? "não" : "sim";
    document.getElementById("vocabulario").value = prefs.initial_prompt ?? "";
  }

  /**
   * Guarda o vínculo assim que ele muda.
   *
   * Na hora da escolha, e não só ao transcrever: quem escolhe o projeto e sai
   * da tela — para ouvir um trecho, para conferir outra reunião — voltava e
   * encontrava os campos vazios.
   */
  async function guardarVinculo() {
    try {
      await pedir("salvar-reuniao", {
        gravacao: g.caminho,
        cliente: campoCliente.value.trim(),
        projeto: campoProjeto.value.trim(),
      });
    } catch {
      // Não vale interromper o preparo por causa disto: o vínculo é gravado de
      // novo ao transcrever, que é quando ele passa a importar de verdade.
    }
  }

  /**
   * Guarda o vocabulário no projeto, que é de quem ele é.
   *
   * O vocabulário é preferência de cliente/projeto, não desta reunião: os nomes
   * e siglas de um projeto valem para todas as reuniões dele. Até 14/08 só era
   * gravado ao clicar em Transcrever — quem digitava um termo e saía da tela
   * perdia o que escreveu, e a tela seguinte já mostrava o vocabulário velho.
   */
  async function guardarVocabulario() {
    const cliente = campoCliente.value.trim();
    const projeto = campoProjeto.value.trim();
    // Sem projeto não há onde guardar: o vocabulário mora no par, e inventar um
    // projeto "sem nome" só para ter onde salvar criaria lixo no cadastro.
    if (!cliente || !projeto) return;

    const caixaVocab = document.getElementById("vocabulario");
    if (!caixaVocab) return;

    try {
      await pedir("salvar-projeto", {
        cliente, projeto,
        prefs: {
          language: document.getElementById("idioma").value.trim(),
          model_size: document.getElementById("modelo").value,
          engine: "faster-whisper",
          diarization: document.getElementById("diarizacao").value === "sim",
          diar_model: "community-1",
          condition_on_previous_text: false,
          initial_prompt: caixaVocab.value.trim(),
        },
      });
    } catch {
      // O vocabulário é gravado de novo ao transcrever; falhar aqui não pode
      // interromper quem está preparando a reunião.
    }
  }

  campoCliente.addEventListener("change", () => {
    atualizarProjetos(); carregarPreferencias(); guardarVinculo();
  });
  campoCliente.addEventListener("input", atualizarProjetos);
  campoProjeto.addEventListener("change", () => { carregarPreferencias(); guardarVinculo(); });
  // O blur é a rede: grava de novo quando o campo perde o foco, inclusive nos
  // caminhos em que o change não chega a disparar. Gravar duas vezes o mesmo
  // valor não custa nada — são dois campos num JSON pequeno.
  campoCliente.addEventListener("blur", guardarVinculo);
  campoProjeto.addEventListener("blur", guardarVinculo);

  // As preferências do motor e o vocabulário seguem o mesmo caminho do vínculo:
  // gravam ao sair do campo, e não só ao transcrever.
  caixa.querySelector("textarea").addEventListener("blur", guardarVocabulario);
  for (const id of ["modelo", "idioma", "diarizacao"])
    document.getElementById(id).addEventListener("change", guardarVocabulario);

  botao.addEventListener("click", () => transcrever(g, botao, painel));

  // Reencontrar uma transcrição já em curso é o motivo de esta tela existir do
  // jeito que existe: quem saiu no meio e voltou cai aqui, e o que ele precisa
  // ver é a barra onde ela está — não um botão "Transcrever" que começaria tudo
  // de novo. O erro da última tentativa aparece pelo mesmo caminho.
  if (emCurso(g.caminho)) acompanhar(g, botao, painel);
  else if (ultimoResultado(g.caminho)?.erro)
    painel.replaceChildren(alerta(ultimoResultado(g.caminho).erro, "erro"));
}

function dataDe(nome) {
  const m = nome.match(/^(\d{4}-\d{2}-\d{2})/);
  return m ? m[1] : "";
}

const ETAPAS = {
  mix: "Somando as faixas",
  asr: "Transcrevendo",
  diarizacao: "Separando os falantes",
  montagem: "Montando o resultado",
};

/**
 * Desenha a transcrição desta gravação enquanto ela roda, esteja ela recém
 * pedida ou já a meio caminho quando esta tela montou.
 *
 * O andamento vem do registro do núcleo, e não de uma promessa: é o que permite
 * sair da tela e voltar sem perder a barra, e é o que faz o resultado chegar
 * mesmo que ninguém estivesse olhando quando ele ficou pronto.
 */
function acompanhar(g, botao, painel) {
  botao.disabled = true;
  botao.textContent = "Transcrevendo…";

  const barra = document.createElement("div");
  barra.className = "aa-progresso";
  const preenchimento = document.createElement("div");
  barra.appendChild(preenchimento);

  const estado = document.createElement("p");
  estado.className = "campo__dica";
  estado.textContent = "preparando…";

  // Parar mora ao lado da barra, e não entre os botões da tela: é a ação de
  // quem está olhando a transcrição correr e mudou de ideia. Some junto com ela.
  const parar = document.createElement("button");
  parar.className = "aa-btn aa-btn-texto";
  parar.type = "button";
  parar.textContent = "Parar transcrição";
  parar.addEventListener("click", async () => {
    parar.disabled = true;
    parar.textContent = "parando…";
    try {
      await cancelar(g.caminho);
    } catch (e) {
      parar.disabled = false;
      parar.textContent = "Parar transcrição";
      painel.appendChild(alerta(e.message, "erro"));
    }
  });

  const linha = document.createElement("div");
  linha.className = "progresso__linha";
  linha.append(estado, parar);
  painel.replaceChildren(barra, linha);

  function pintar(t) {
    estado.textContent = `${ETAPAS[t.etapa] ?? t.etapa}: ${t.texto}`;
    preenchimento.style.width = `${t.fracao >= 0 ? Math.round(t.fracao * 100) : 0}%`;
  }

  const atual = emCurso(g.caminho);
  if (atual) pintar(atual);

  const cancelarAssinatura = assinarTranscricoes(() => {
    // A tela saiu do documento (trocou-se de destino): largar a assinatura e
    // deixar o trabalho seguir. Quem voltar a esta gravação monta outra.
    if (!painel.isConnected) { cancelarAssinatura(); return; }

    const rodando = emCurso(g.caminho);
    if (rodando) { pintar(rodando); return; }

    const fim = ultimoResultado(g.caminho);
    if (!fim) return;              // é outra gravação que mudou de estado

    cancelarAssinatura();
    if (fim.cancelada) {
      // Parou a pedido: o app obedeceu, então nada de alerta vermelho. O botão
      // volta a convidar, porque recomeçar é o próximo passo provável.
      botao.disabled = false;
      botao.textContent = "Transcrever";
      const nota = document.createElement("p");
      nota.className = "campo__dica";
      nota.textContent = "Transcrição interrompida. A placa foi liberada.";
      painel.replaceChildren(nota);
      return;
    }
    if (fim.erro) {
      botao.disabled = false;
      botao.textContent = "Tentar de novo";
      painel.replaceChildren(alerta(fim.erro, "erro"));
      return;
    }
    abrirResultado(g);
  });
}

/** Abre a revisão do que acabou de ficar pronto, lendo o que foi salvo em disco. */
async function abrirResultado(g) {
  try {
    const r = await pedir("transcricao", { gravacao: g.caminho });
    if (!r.transcricao) throw new Error("a transcrição não foi encontrada");
    g.transcrita = true;
    telaDeRevisao(g, JSON.parse(r.transcricao), { cabecalho, tela });
  } catch (e) {
    tela.replaceChildren(alerta(e.message, "erro"));
  }
}

async function transcrever(g, botao, painel) {
  botao.disabled = true;

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
          diarization: diar === "sim",
          // Guardado como estava para o projeto não perder o campo, mas quem
          // decide é o motor: o community-1 é o único que existe no sidecar.
          diar_model: "community-1",
          condition_on_previous_text: false,
          initial_prompt: vocabulario,
        },
      });
    }

    // Volta na hora: daqui em diante o trabalho é do núcleo, e a tela passa a
    // desenhar o que ele empurra. Recusa quando já há outra transcrição em
    // curso, e a mensagem nomeia qual.
    await pedirTranscricao({
      gravacao: g.caminho,
      vocabulario,
      // Sem estes dois, escolher modelo e idioma na tela não tinha efeito
      // nenhum: o motor caía no padrão e detectava o idioma sozinho.
      idioma: document.getElementById("idioma").value.trim(),
      modelo: document.getElementById("modelo").value,
      // Sem isto a escolha de separar falantes era colhida na tela, salva nas
      // preferências do projeto e ignorada pelo pipeline.
      diarizar: diar === "sim",
      // Guardados com a transcrição: sem isto o cabeçalho do arquivo exportado
      // saía sem dizer de que cliente e projeto era a reunião.
      cliente: document.getElementById("cliente").value.trim(),
      projeto: document.getElementById("projeto").value.trim(),
    });
    acompanhar(g, botao, painel);
  } catch (e) {
    botao.disabled = false;
    botao.textContent = "Tentar de novo";
    painel.replaceChildren(alerta(e.message, "erro"));
  }
}

export async function abrirGravacao(g) {
  fecharGavetas();
  if (!g.transcrita) return telaDePreparo(g);

  // Refazer é ação de exceção: some da tela a menos que tenha sido ligada nas
  // configurações. Ver ConfiguracoesDoApp.PermitirRetranscrever.
  const { config } = await pedir("config");
  if (config?.permitir_retranscrever) {
    const r = await pedir("transcricao", { gravacao: g.caminho });
    if (r.transcricao) {
      cabecalho(tituloDe(g), "carregando…", true);
      tela.replaceChildren();
      telaDeRevisao(g, JSON.parse(r.transcricao), {
        cabecalho, tela,
        aoRefazer: () => { g.transcrita = false; telaDePreparo(g); },
        aoApagar: botaoApagarGravacao(g),
      });
      return;
    }
  }

  cabecalho(tituloDe(g), "carregando a transcrição…", true);
  tela.replaceChildren();
  const r = await pedir("transcricao", { gravacao: g.caminho });
  if (!r.transcricao) {
    tela.replaceChildren(alerta("A transcrição não foi encontrada.", "erro"));
    return;
  }
  telaDeRevisao(g, JSON.parse(r.transcricao), {
    cabecalho, tela, aoApagar: botaoApagarGravacao(g),
  });
}

// ──────────────────────────────────────────────────────── ajustes

/**
 * A tela de ajustes mora em configuracoes.js.
 *
 * Ela precisa do cabeçalho e do <main> daqui, e não os importa: passar os dois
 * como contexto mantém o app.js como o único lugar que sabe da moldura.
 */
export function abrirAjustes(aba) {
  fecharGavetas();
  destino("ir-config");
  return telaDeAjustes({ cabecalho, tela }, aba);
}

// ─────────────────────────────────────────────────────── gravador

/**
 * A tela do Gravador mora em gravador.js, pelo mesmo motivo dos ajustes: o
 * app.js é o único lugar que sabe da moldura, e as telas recebem o que
 * precisam dela.
 */
export function abrirGravador() {
  fecharGavetas();
  destino("ir-gravador");
  return telaDoGravador({ cabecalho, tela });
}

/** O destino Atas mora em atas.js, pelo mesmo motivo dos outros dois. */
export function abrirAtas() {
  fecharGavetas();
  destino("ir-atas");
  return telaDeAtas({ cabecalho, tela });
}

// ─────────────────────────────────────────────────────────── ligação

document.getElementById("ir-config").addEventListener("click", () => abrirAjustes());
document.getElementById("ir-gravador").addEventListener("click", abrirGravador);
document.getElementById("ir-atas").addEventListener("click", abrirAtas);
document.getElementById("ir-reunioes").addEventListener("click", telaDeLista);
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
  // Antes de qualquer tela: se já havia uma transcrição rodando quando esta
  // página subiu, a bolinha tem que acender agora. Esperar o próximo evento
  // pode custar minutos — as etapas longas do pipeline não reportam progresso
  // contínuo.
  await sincronizar().catch(() => {});
  ligarBolinhas();

  const hash = location.hash.slice(1);
  if (!hash) return telaDeLista();

  // "revisao=1&falantes" — a parte depois do & abre um painel por cima, que é
  // o que não dá para alcançar sem clique.
  const [principal, extra] = hash.split("&");
  const [tela, arg] = principal.split("=");

  // Os ajustes não dependem de gravação nenhuma, e pedir a lista antes atrasaria
  // a tela por nada. "#config=vozes" cai direto na aba.
  if (tela === "config") return abrirAjustes(arg || "geral");
  if (tela === "gravador") return abrirGravador();
  if (tela === "atas") return abrirAtas();

  const { gravacoes } = await pedir("gravacoes");
  const g = gravacoes[Number(arg) || 0];
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
