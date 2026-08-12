// A tela de ajustes: cinco abas numa coluna à esquerda.
//
// Era uma gaveta com quatro seções empilhadas. Virou tela por uma razão
// concreta: a gestão de vozes precisa de uma lista de pessoas, as amostras de
// cada uma e um play por linha — nada disso cabe em 28rem de gaveta.
//
// As abas que ainda não têm funcionalidade completa mostram o que já existe e
// dizem o que falta, em vez de exibirem um botão desabilitado. O pedido foi
// montar a UI para validar o desenho antes das funcionalidades; um lugar vazio
// não se valida, mas um lugar que mostra dados reais e admite o que não faz,
// sim.

import { pedir } from "/ponte.js";
import { alerta, campo } from "/pecas.js";

/** "3,1 GB", "148 MB" — tamanho para uma pessoa decidir, não para conferir. */
function tamanho(bytes) {
  if (!bytes) return "—";
  const gb = bytes / 1e9;
  if (gb >= 1) return `${gb.toFixed(1).replace(".", ",")} GB`;
  return `${Math.round(bytes / 1e6)} MB`;
}

/** "4,2s" — a duração de uma amostra de voz. */
const segundos = (s) => `${s.toFixed(1).replace(".", ",")}s`;

/** "10/08/2026" a partir do ISO que o núcleo grava. */
function dia(iso) {
  const d = new Date(iso);
  return Number.isNaN(d.getTime()) ? iso : d.toLocaleDateString("pt-BR");
}

function bloco(titulo, texto) {
  const b = document.createElement("section");
  b.className = "bloco";
  const h = document.createElement("h2");
  h.className = "bloco__titulo";
  h.textContent = titulo;
  b.appendChild(h);
  if (texto) {
    const p = document.createElement("p");
    p.className = "bloco__texto";
    p.textContent = texto;
    b.appendChild(p);
  }
  return b;
}

/** O aviso de obra: o que ainda não existe, dito com todas as letras. */
function obra(oQueFalta, oQueDaParaFazer) {
  const el = document.createElement("div");
  el.className = "obra";
  const t = document.createElement("strong");
  t.textContent = oQueFalta;
  el.appendChild(t);
  if (oQueDaParaFazer) {
    const p = document.createElement("span");
    p.textContent = oQueDaParaFazer;
    el.appendChild(p);
  }
  return el;
}

function chave(ligada, aoMudar) {
  const caixa = document.createElement("input");
  caixa.type = "checkbox";
  caixa.checked = ligada === true;
  caixa.addEventListener("change", (e) => aoMudar(e.target.checked));
  return caixa;
}

/**
 * Campo de pasta com o botão que abre o diálogo do Windows.
 *
 * O campo de texto continua editável: colar um caminho de rede que o diálogo
 * não navega bem é um caso real, e tirar o teclado do usuário para forçar o
 * diálogo resolveria um problema criando outro.
 */
function campoDePasta(rotulo, valor, dica, aoMudar) {
  const raiz = document.createElement("div");
  raiz.className = "campo-pasta";

  const c = campo(rotulo, "input", { valor, dica });
  const entrada = c.querySelector("input");
  entrada.addEventListener("change", (e) => aoMudar(e.target.value.trim()));

  const botao = document.createElement("button");
  botao.className = "aa-btn aa-btn-secundario";
  botao.type = "button";
  botao.textContent = "Escolher…";
  botao.addEventListener("click", async () => {
    const r = await pedir("escolher-pasta", { pasta: entrada.value.trim() });
    // Cancelar o diálogo devolve nulo, e cancelar tem que ser inócuo: sem esta
    // guarda, desistir da escolha limparia a pasta já configurada.
    if (!r.pasta) return;
    entrada.value = r.pasta;
    aoMudar(r.pasta);
  });

  const limpar = document.createElement("button");
  limpar.className = "aa-btn aa-btn-texto";
  limpar.type = "button";
  limpar.textContent = "Limpar";
  limpar.title = "Voltar ao padrão";
  limpar.addEventListener("click", () => {
    entrada.value = "";
    aoMudar("");
  });

  const acoes = document.createElement("div");
  acoes.className = "acoes";
  acoes.append(botao, limpar);

  raiz.append(c, acoes);
  return raiz;
}

// ─────────────────────────────────────────────────────────── aba Geral

function abaGeral(config, gravar) {
  const painel = document.createElement("div");
  painel.className = "painel";

  const pastas = bloco("Pastas",
    "Onde o app procura as gravações e para onde ele copia o que você exporta.");

  const campoPasta = campoDePasta("Pasta das gravações",
    config.pasta_das_gravacoes ?? "", "vazio = a mesma do gravador",
    (v) => gravar({ pasta_das_gravacoes: v }));

  const campoExport = campoDePasta("Pasta de exportação",
    config.pasta_de_exportacao ?? "", "vazio = só ao lado da gravação",
    (v) => gravar({ pasta_de_exportacao: v }));

  const dica = document.createElement("p");
  dica.className = "campo__dica";
  dica.textContent = "A exportação sempre grava ao lado da gravação. "
    + "A pasta acima é a cópia extra, quando você pedir.";

  pastas.append(campoPasta, campoExport, dica);
  painel.append(pastas);
  return painel;
}

// ───────────────────────────────────────────────────────── aba Modelos

function cartaoDeModelo(item) {
  const { pacote, estado, bytes_em_disco, em_uso } = item;

  const cartao = document.createElement("div");
  cartao.className = "modelo";
  cartao.dataset.emUso = String(em_uso);

  const esquerda = document.createElement("div");

  const nome = document.createElement("p");
  nome.className = "modelo__nome";
  nome.textContent = pacote.nome;
  if (em_uso) {
    const et = document.createElement("span");
    et.className = "aa-etiqueta aa-etiqueta--sucesso";
    et.textContent = "em uso";
    nome.appendChild(et);
  }

  const desc = document.createElement("p");
  desc.className = "modelo__desc";
  desc.textContent = pacote.descricao;

  const fatos = document.createElement("p");
  fatos.className = "modelo__fatos";

  // O tamanho é o primeiro fato porque é o que decide: são 3 GB ou 150 MB, e
  // quem está numa conexão ruim precisa saber disso antes de clicar.
  const t = document.createElement("span");
  const forte = document.createElement("b");
  forte.textContent = estado === "instalado"
    ? tamanho(bytes_em_disco) : tamanho(pacote.tamanho_esperado_bytes);
  t.append(forte);
  // Tamanho publicado e tamanho medido não valem o mesmo, e a tela não finge
  // que valem.
  if (!pacote.tamanho_medido && estado !== "instalado") t.append(" aprox.");
  fatos.appendChild(t);

  const est = document.createElement("span");
  est.textContent = { instalado: "baixado", parcial: "baixado pela metade",
                      ausente: "ainda não baixado" }[estado] ?? estado;
  fatos.appendChild(est);

  if (pacote.nota) {
    const n = document.createElement("span");
    n.textContent = pacote.nota;
    fatos.appendChild(n);
  }

  esquerda.append(nome, desc, fatos);

  const direita = document.createElement("div");
  direita.className = "modelo__acao";

  const botao = document.createElement("button");
  botao.type = "button";
  botao.className = estado === "instalado"
    ? "aa-btn aa-btn-texto" : "aa-btn aa-btn-primario";
  botao.textContent = estado === "instalado" ? "Remover"
                    : estado === "parcial" ? "Retomar" : "Baixar";

  // Remover o que está em uso deixaria o app sem motor, e o erro só apareceria
  // na próxima transcrição. Melhor não deixar chegar lá.
  if (estado === "instalado" && em_uso) {
    botao.disabled = true;
    botao.title = "Este é o modelo em uso. Escolha outro acima antes de remover.";
  }

  const barra = document.createElement("div");
  barra.className = "aa-progresso";
  barra.hidden = true;
  barra.appendChild(document.createElement("div"));

  const andamento = document.createElement("span");
  andamento.className = "campo__dica";

  botao.addEventListener("click", async () => {
    if (estado === "instalado") {
      if (!confirm(`Remover ${pacote.nome} do disco? `
                   + `São ${tamanho(bytes_em_disco)} que voltam a ser baixados se precisar.`))
        return;
      botao.disabled = true;
      andamento.textContent = "removendo…";
      try {
        await pedir("remover-pacote", { modelo: pacote.id });
      } catch (e) {
        andamento.textContent = `não removeu: ${e.message}`;
        botao.disabled = false;
        return;
      }
      return recarregar();
    }

    botao.disabled = true;
    barra.hidden = false;
    andamento.textContent = "começando…";
    try {
      await pedir("baixar-pacote", { modelo: pacote.id }, (p) => {
        barra.firstChild.style.width = `${Math.round((p.fracao ?? 0) * 100)}%`;
        andamento.textContent = p.texto ?? "";
      });
    } catch (e) {
      // O motor continua vivo depois de uma falha, então tentar de novo é só
      // clicar — daí o botão voltar em vez de a tela travar.
      andamento.textContent = `não baixou: ${e.message}`;
      botao.disabled = false;
      barra.hidden = true;
      return;
    }
    recarregar();
  });

  direita.append(botao, barra, andamento);
  cartao.append(esquerda, direita);
  return cartao;
}

function abaModelos(catalogo, config, gravar) {
  const painel = document.createElement("div");
  painel.className = "painel";

  for (const [familia, titulo, texto, chaveConfig, padrao] of [
    ["asr", "Transcrição", "Qual modelo transforma áudio em texto.",
     "modelo_padrao", "large-v3"],
    ["diarizacao", "Diarização", "Qual modelo separa quem falou.",
     "diarizacao_padrao", "community-1"],
  ]) {
    const b = bloco(titulo, texto);
    const itens = catalogo.filter((i) => i.pacote.familia === familia);

    const escolha = campo("Usar por padrão", "select",
      { opcoes: itens.map((i) => i.pacote.nome) });
    const sel = escolha.querySelector("select");
    // O valor visível é o nome bonito; o que vai para a configuração é o id que
    // o motor entende. Sem esta separação a tela dita o vocabulário do motor.
    itens.forEach((i, n) => { sel.options[n].value = i.pacote.id; });
    sel.value = config[chaveConfig] ?? padrao;
    sel.addEventListener("change", async (e) => {
      await gravar({ [chaveConfig]: e.target.value });
      recarregar();
    });
    b.appendChild(escolha);

    for (const i of itens) b.appendChild(cartaoDeModelo(i));
    painel.appendChild(b);
  }

  const nota = document.createElement("p");
  nota.className = "campo__dica";
  nota.textContent = "O modelo escolhido também é baixado sozinho na primeira "
    + "transcrição que precisar dele. Baixar por aqui só evita a espera na hora "
    + "errada.";
  painel.appendChild(nota);

  return painel;
}

// ────────────────────────────────────────────────────── aba Transcrição

function abaTranscricao(config, gravar) {
  const painel = document.createElement("div");
  painel.className = "painel";

  const refazer = bloco("Transcrever de novo");
  refazer.classList.add("bloco--chave");
  const texto = document.createElement("p");
  texto.className = "bloco__texto";
  // O porquê fica na tela, não só no código: quem liga isto precisa saber o
  // que arrisca.
  texto.textContent = "Mostra o botão de refazer numa reunião já transcrita. "
    + "Refazer descarta os nomes de falante e as correções de texto daquela "
    + "reunião, e reprocessa o áudio inteiro.";
  refazer.append(texto, chave(config.permitir_retranscrever,
    (v) => gravar({ permitir_retranscrever: v })));
  painel.appendChild(refazer);

  const fonetica = bloco("Correção fonética",
    "Os termos do vocabulário do projeto corrigem o texto: 'Dimi' que o modelo "
    + "escreveu 'Jimmy' volta a ser 'Dimi'.");
  const comoVer = document.createElement("p");
  comoVer.className = "campo__dica";
  comoVer.textContent = "Na transcrição, cada trecho corrigido ganha uma marca "
    + "✎, e o filtro no topo mostra só os corrigidos. Clicar na marca desfaz a "
    + "troca daquele trecho — a correção é um palpite, e palpite que ninguém "
    + "confere é um defeito esperando.";
  fonetica.appendChild(comoVer);
  painel.appendChild(fonetica);

  painel.appendChild(obra(
    "O filtro de silêncio ainda não tem chave aqui.",
    "Ele existe no núcleo e só liga por linha de comando."));

  return painel;
}

// ──────────────────────────────────────────────────────── aba Clientes

function abaClientes(clientes, catalogo) {
  const painel = document.createElement("div");
  painel.className = "painel";

  const nomes = Object.keys(clientes).sort((a, b) => a.localeCompare(b, "pt-BR"));

  const b = bloco("Clientes e projetos",
    "Cada projeto guarda o vocabulário, o idioma e o modelo usados nas "
    + "reuniões dele.");

  if (nomes.length === 0) {
    const vazio = document.createElement("p");
    vazio.className = "campo__dica";
    vazio.textContent = "Nenhum cliente ainda. Eles nascem ao preparar uma "
      + "transcrição: digitar um nome novo ali já o cria.";
    b.appendChild(vazio);
  }

  for (const nome of nomes) {
    const pessoa = document.createElement("div");
    pessoa.className = "pessoa";

    const topo = document.createElement("div");
    topo.className = "pessoa__topo";
    const h = document.createElement("p");
    h.className = "pessoa__nome";
    h.textContent = nome;
    const quantos = document.createElement("span");
    quantos.className = "campo__dica";
    const n = clientes[nome].length;
    quantos.textContent = `${n} ${n === 1 ? "projeto" : "projetos"}`;

    const acoes = document.createElement("span");
    acoes.className = "amostra__acoes";
    acoes.append(
      botaoRenomear("cliente", nome, (novo) =>
        pedir("renomear-cliente", { cliente: nome, nome: novo })),
      botaoApagar(
        `Apagar o cliente "${nome}" e os ${n} ${n === 1 ? "projeto" : "projetos"} dele?\n\n`
        + "Isto esquece o vocabulário e as preferências. As transcrições já "
        + "feitas continuam onde estão.",
        () => pedir("apagar-cliente", { cliente: nome })),
    );

    topo.append(h, quantos, acoes);
    pessoa.appendChild(topo);

    for (const projeto of [...clientes[nome]].sort((a, b) => a.localeCompare(b, "pt-BR")))
      pessoa.appendChild(linhaDeProjeto(nome, projeto, catalogo));

    b.appendChild(pessoa);
  }

  painel.appendChild(b);
  painel.appendChild(obra(
    "Renomear e apagar por aqui ainda não existe.",
    "Cliente e projeto novos continuam nascendo na tela de preparo, digitando "
    + "um nome que ainda não existe."));

  return painel;
}

/** O par de botões que renomeia, com o nome atual já no campo. */
function botaoRenomear(oQue, atual, aoConfirmar) {
  const b = document.createElement("button");
  b.className = "aa-btn aa-btn-texto";
  b.type = "button";
  b.textContent = "Renomear";
  b.addEventListener("click", async (e) => {
    e.stopPropagation();
    const novo = prompt(`Novo nome do ${oQue}:`, atual);
    if (!novo || novo.trim() === atual) return;
    try {
      await aoConfirmar(novo.trim());
    } catch (err) {
      alert(`não renomeou: ${err.message}`);
      return;
    }
    recarregar();
  });
  return b;
}

/**
 * O botão que apaga, sempre com confirmação que diz o que se perde.
 *
 * O texto da confirmação nomeia o alvo e a consequência, em vez de perguntar
 * "tem certeza?" — quem lê "tem certeza" clica em sim por reflexo.
 */
function botaoApagar(pergunta, aoConfirmar) {
  const b = document.createElement("button");
  b.className = "aa-btn aa-btn-texto";
  b.type = "button";
  b.textContent = "Apagar";
  b.addEventListener("click", async (e) => {
    e.stopPropagation();
    if (!confirm(pergunta)) return;
    try {
      await aoConfirmar();
    } catch (err) {
      alert(`não apagou: ${err.message}`);
      return;
    }
    recarregar();
  });
  return b;
}

/**
 * Um projeto que abre nos parâmetros dele.
 *
 * Sanfona, e não navegação para outra tela: comparar dois projetos do mesmo
 * cliente é o gesto que se faz aqui — "o outro está usando qual modelo?" — e
 * trocar de tela a cada um transformaria a comparação em ida e volta.
 */
function linhaDeProjeto(cliente, projeto, catalogo) {
  const caixa = document.createElement("div");
  caixa.className = "projeto";

  const topo = document.createElement("button");
  topo.className = "projeto__topo";
  topo.type = "button";
  topo.setAttribute("aria-expanded", "false");

  const seta = document.createElement("span");
  seta.className = "projeto__seta";
  seta.textContent = "▸";

  const nome = document.createElement("span");
  nome.textContent = projeto;

  const resumo = document.createElement("span");
  resumo.className = "campo__dica";

  topo.append(seta, nome, resumo);

  // As ações ficam fora do <button> do topo: botão dentro de botão é HTML
  // inválido, e o clique de um acabaria disparando o outro.
  const acoes = document.createElement("span");
  acoes.className = "amostra__acoes projeto__acoes";
  acoes.append(
    botaoRenomear("projeto", projeto, (novo) =>
      pedir("renomear-projeto", { cliente, projeto, nome: novo })),
    botaoApagar(
      `Apagar o projeto "${projeto}" de ${cliente}?\n\n`
      + "Isto esquece o vocabulário e as preferências dele. As transcrições já "
      + "feitas continuam onde estão.",
      () => pedir("apagar-projeto", { cliente, projeto })),
  );

  const corpo = document.createElement("div");
  corpo.className = "projeto__corpo";
  corpo.hidden = true;

  let prefs = null;

  /** "large-v3 · pt · com falantes" — o que dá para saber sem abrir. */
  function resumir() {
    if (!prefs) return;
    const partes = [
      prefs.model_size || "modelo padrão",
      prefs.language || "idioma automático",
      prefs.diarization === false ? "sem falantes" : "com falantes",
    ];
    resumo.textContent = partes.join(" · ");
  }

  async function gravar(mudanca) {
    Object.assign(prefs, mudanca);
    estadoDoProjeto.textContent = "salvando…";
    try {
      await pedir("salvar-projeto", { cliente, projeto, prefs });
      estadoDoProjeto.textContent = "salvo";
      resumir();
    } catch (e) {
      estadoDoProjeto.textContent = `não salvou: ${e.message}`;
    }
  }

  const estadoDoProjeto = document.createElement("p");
  estadoDoProjeto.className = "campo__dica";

  async function montar() {
    if (!prefs) prefs = (await pedir("prefs", { cliente, projeto })).prefs ?? {};
    corpo.replaceChildren();
    resumir();

    const asr = catalogo.filter((i) => i.pacote.familia === "asr");
    const diar = catalogo.filter((i) => i.pacote.familia === "diarizacao");

    // O modelo do projeto pode ser um que não está mais no catálogo — projeto
    // criado no app Python, ou pacote removido. Mostrar "(padrão)" e não a
    // primeira opção da lista: escolher por conta própria mudaria em silêncio
    // como as reuniões deste cliente são transcritas.
    const campoModelo = campo("Modelo de transcrição", "select", {
      opcoes: ["(usar o padrão do app)", ...asr.map((i) => i.pacote.nome)],
    });
    const selModelo = campoModelo.querySelector("select");
    selModelo.options[0].value = "";
    asr.forEach((i, n) => { selModelo.options[n + 1].value = i.pacote.id; });
    selModelo.value = prefs.model_size ?? "";
    if (selModelo.selectedIndex === -1) {
      const solto = document.createElement("option");
      solto.value = prefs.model_size;
      solto.textContent = `${prefs.model_size} (não instalado)`;
      selModelo.appendChild(solto);
      selModelo.value = prefs.model_size;
    }
    selModelo.addEventListener("change", (e) =>
      gravar({ model_size: e.target.value || null }));

    const campoIdioma = campo("Idioma", "input", {
      valor: prefs.language ?? "",
      dica: "vazio = detectar sozinho",
    });
    campoIdioma.querySelector("input").addEventListener("change", (e) =>
      gravar({ language: e.target.value.trim() || null }));

    const campoDiar = campo("Modelo de diarização", "select", {
      opcoes: ["(usar o padrão do app)", ...diar.map((i) => i.pacote.nome)],
    });
    const selDiar = campoDiar.querySelector("select");
    selDiar.options[0].value = "";
    diar.forEach((i, n) => { selDiar.options[n + 1].value = i.pacote.id; });
    selDiar.value = prefs.diar_model ?? "";
    selDiar.addEventListener("change", (e) =>
      gravar({ diar_model: e.target.value || null }));

    const linha = document.createElement("div");
    linha.className = "linha";
    linha.append(campoModelo, campoIdioma, campoDiar);

    const separar = document.createElement("label");
    separar.className = "campo campo--linha";
    const caixaSep = document.createElement("input");
    caixaSep.type = "checkbox";
    caixaSep.checked = prefs.diarization !== false;
    caixaSep.addEventListener("change", (e) => gravar({ diarization: e.target.checked }));
    const rotSep = document.createElement("span");
    rotSep.textContent = "Separar os falantes";
    separar.append(caixaSep, rotSep);

    const vocab = campo("Vocabulário", "textarea", {
      linhas: 3, valor: prefs.initial_prompt ?? "",
    });
    vocab.querySelector("textarea").addEventListener("change", (e) =>
      gravar({ initial_prompt: e.target.value.trim() || null }));

    const dicaVocab = document.createElement("p");
    dicaVocab.className = "campo__dica";
    // O mesmo texto do preparo, pelo mesmo motivo: o teto de 224 tokens do
    // initial_prompt morreu quando a correção fonética entrou.
    dicaVocab.textContent = "Nomes de pessoas, jargão, nomes de sistemas. Sem "
      + "limite de tamanho — o que o modelo escrever parecido é corrigido depois. "
      + "É este vocabulário que alimenta a correção fonética.";

    corpo.append(linha, separar, vocab, dicaVocab, estadoDoProjeto);
  }

  let montado = false;
  topo.addEventListener("click", async () => {
    const abrindo = corpo.hidden;
    corpo.hidden = !abrindo;
    topo.setAttribute("aria-expanded", String(abrindo));
    seta.textContent = abrindo ? "▾" : "▸";
    if (abrindo && !montado) {
      montado = true;
      corpo.textContent = "carregando…";
      await montar();
    }
  });

  // O resumo aparece antes de abrir, e o que ele carrega fica guardado: é o que
  // responde "qual modelo este projeto usa?" sem exigir um clique por projeto,
  // e sem pedir as mesmas preferências duas vezes quando o projeto for aberto.
  pedir("prefs", { cliente, projeto })
    .then((r) => { prefs = r.prefs ?? {}; resumir(); })
    .catch(() => {});

  const linhaTopo = document.createElement("div");
  linhaTopo.className = "projeto__linha";
  linhaTopo.append(topo, acoes);

  caixa.append(linhaTopo, corpo);
  return caixa;
}

// ─────────────────────────────────────────────────────────── aba Vozes

/**
 * Toca o recorte de 4 segundos que gerou uma amostra.
 *
 * Usa o mesmo <audio> da revisão, e não um por linha: com dezenas de amostras,
 * um elemento por linha significaria dezenas de conexões abertas — e dois
 * trechos tocando juntos, que é pior ainda para quem está tentando decidir se
 * a voz é da mesma pessoa.
 */
function ouvirTrecho(relativo, botao) {
  const audio = document.getElementById("audio");
  const url = `https://vozes.local/${relativo.split("/").map(encodeURIComponent).join("/")}`;

  // Clicar de novo no que está tocando para. É o gesto que se espera, e sem
  // ele não há como interromper um trecho a não ser esperando os 4 segundos.
  if (audio.src === url && !audio.paused) {
    audio.pause();
    botao.removeAttribute("data-tocando");
    return;
  }

  for (const b of document.querySelectorAll(".tocar[data-tocando]"))
    b.removeAttribute("data-tocando");

  audio.src = url;
  audio.currentTime = 0;
  botao.dataset.tocando = "true";
  audio.onended = () => botao.removeAttribute("data-tocando");
  audio.play().catch((e) => {
    botao.removeAttribute("data-tocando");
    botao.title = `não tocou: ${e.message}`;
  });
}

function linhaDeAmostra(pessoa, a, aoMudar) {
  const linha = document.createElement("div");
  linha.className = "amostra";
  linha.dataset.quarentena = String(a.quarentena);

  const tocar = document.createElement("button");
  tocar.className = "tocar";
  tocar.type = "button";
  tocar.disabled = !a.trecho;
  tocar.title = a.trecho
    ? "Ouvir este trecho"
    : "Esta amostra foi guardada sem o trecho de áudio";
  tocar.setAttribute("aria-label", "Ouvir o trecho");
  if (a.trecho) tocar.addEventListener("click", () => ouvirTrecho(a.trecho, tocar));
  const svg = document.createElementNS("http://www.w3.org/2000/svg", "svg");
  svg.setAttribute("viewBox", "0 0 24 24");
  const uso = document.createElementNS("http://www.w3.org/2000/svg", "use");
  uso.setAttribute("href", "#i-tocar");
  svg.appendChild(uso);
  tocar.appendChild(svg);

  const proc = document.createElement("span");
  proc.className = "amostra__proc";
  // A procedência inteira numa linha: é ela que responde "de onde veio isto?",
  // que é a primeira pergunta de quem julga uma amostra suspeita.
  proc.textContent = [
    dia(a.criada_em), a.faixa, segundos(a.duracao_s),
    a.dispositivo, a.gravacao,
  ].filter(Boolean).join(" · ");

  const acoes = document.createElement("span");
  acoes.className = "amostra__acoes";

  if (a.quarentena) {
    const aprovar = document.createElement("button");
    aprovar.className = "aa-btn aa-btn-secundario";
    aprovar.type = "button";
    aprovar.textContent = "Aprovar";
    aprovar.title = "Esta voz soou diferente do resto do perfil. Aprovar a "
      + "aceita como uma condição nova da mesma pessoa.";
    aprovar.addEventListener("click", () =>
      aoMudar("aprovar-voz", pessoa, a.indice));
    acoes.appendChild(aprovar);
  }

  const esquecer = document.createElement("button");
  esquecer.className = "aa-btn aa-btn-texto";
  esquecer.type = "button";
  esquecer.textContent = "Esquecer";
  esquecer.addEventListener("click", () => {
    // Apagar amostra é irreversível e some com trabalho de reuniões passadas.
    // Um clique distraído não pode bastar.
    if (confirm(`Esquecer esta amostra de ${pessoa}?`))
      aoMudar("esquecer-voz", pessoa, a.indice);
  });
  acoes.appendChild(esquecer);

  linha.append(tocar, proc, acoes);
  return linha;
}

function abaVozes(vozes, aoMudar) {
  const painel = document.createElement("div");
  painel.className = "painel";

  const emQuarentena = vozes.reduce(
    (s, p) => s + p.amostras.filter((a) => a.quarentena).length, 0);

  const b = bloco("Vozes conhecidas",
    "Aprendidas quando você nomeia um falante. Na reunião seguinte, quem já "
    + "está aqui chega nomeado.");

  if (vozes.length === 0) {
    const vazio = document.createElement("p");
    vazio.className = "campo__dica";
    vazio.textContent = "Ninguém ainda. Nomeie um falante numa transcrição e a "
      + "voz dele aparece aqui.";
    b.appendChild(vazio);
  }

  if (emQuarentena > 0) {
    b.appendChild(alerta(
      `${emQuarentena} ${emQuarentena === 1 ? "amostra soou" : "amostras soaram"}`
      + " diferente do resto do perfil e aguardam sua revisão.", "atencao"));
  }

  for (const p of vozes) {
    const pessoa = document.createElement("div");
    pessoa.className = "pessoa";

    const topo = document.createElement("div");
    topo.className = "pessoa__topo";
    const h = document.createElement("p");
    h.className = "pessoa__nome";
    h.textContent = p.nome;
    const quantas = document.createElement("span");
    quantas.className = "campo__dica";
    const n = p.amostras.length;
    quantas.textContent = `${n} ${n === 1 ? "amostra" : "amostras"}`;
    topo.append(h, quantas);
    pessoa.appendChild(topo);

    for (const a of p.amostras)
      pessoa.appendChild(linhaDeAmostra(p.nome, a, aoMudar));

    b.appendChild(pessoa);
  }

  painel.appendChild(b);
  painel.appendChild(obra(
    "O ciclo completo nunca foi visto com áudio real.",
    "Nomear alguém numa reunião e ela chegar nomeada na seguinte está "
    + "implementado e não comprovado. Esta tela é o instrumento para comprovar: "
    + "depois de nomear um falante, a pessoa tem que aparecer aqui."));

  return painel;
}

// ─────────────────────────────────────────────────────────── a tela

const ABAS = [
  ["geral", "Geral"],
  ["modelos", "Modelos"],
  ["transcricao", "Transcrição"],
  ["clientes", "Clientes"],
  ["vozes", "Vozes"],
];

let recarregar = () => {};

/**
 * Desenha a tela de ajustes dentro de <main>.
 *
 * @param aba qual abrir. Existe para o --tela poder cair direto numa delas.
 */
export async function telaDeAjustes(ctx, aba = "geral") {
  const { cabecalho, tela } = ctx;
  cabecalho("Ajustes", "", false);
  tela.setAttribute("aria-busy", "true");
  tela.replaceChildren();

  let config, clientes, catalogo, vozes;
  try {
    // Tudo de uma vez: são quatro leituras baratas e locais, e pedir sob demanda
    // a cada troca de aba faria a aba piscar por nada.
    [{ config }, { clientes }, { catalogo }, { vozes }] = await Promise.all([
      pedir("config"), pedir("clientes"), pedir("catalogo"), pedir("vozes"),
    ]);
  } catch (e) {
    tela.setAttribute("aria-busy", "false");
    tela.replaceChildren(alerta(e.message, "erro"));
    return;
  }

  tela.setAttribute("aria-busy", "false");
  recarregar = () => telaDeAjustes(ctx, atual);

  const estado = document.createElement("p");
  estado.className = "campo__dica";

  /** Grava a cada mudança: um botão "salvar" só criaria como esquecer. */
  async function gravar(mudanca) {
    Object.assign(config, mudanca);
    estado.textContent = "salvando…";
    try {
      await pedir("salvar-config", { config });
      estado.textContent = "salvo";
    } catch (e) {
      estado.textContent = `não salvou: ${e.message}`;
    }
  }

  async function mexerNaVoz(op, pessoa, indice) {
    const r = await pedir(op, { pessoa, indice });
    vozes = r.vozes;
    desenharPainel();
  }

  const raiz = document.createElement("div");
  raiz.className = "ajustes";

  const colunaAbas = document.createElement("div");
  colunaAbas.className = "abas";
  colunaAbas.setAttribute("role", "tablist");
  colunaAbas.setAttribute("aria-orientation", "vertical");

  const painel = document.createElement("div");

  let atual = ABAS.some(([id]) => id === aba) ? aba : "geral";

  function desenharPainel() {
    const conteudo = {
      geral: () => abaGeral(config, gravar),
      modelos: () => abaModelos(catalogo, config, gravar),
      transcricao: () => abaTranscricao(config, gravar),
      clientes: () => abaClientes(clientes, catalogo),
      vozes: () => abaVozes(vozes, mexerNaVoz),
    }[atual]();
    conteudo.appendChild(estado);
    painel.replaceChildren(conteudo);
  }

  for (const [id, rotulo] of ABAS) {
    const botao = document.createElement("button");
    botao.className = "aba";
    botao.type = "button";
    botao.setAttribute("role", "tab");
    botao.textContent = rotulo;
    botao.setAttribute("aria-selected", String(id === atual));
    botao.addEventListener("click", () => {
      atual = id;
      for (const outro of colunaAbas.children)
        outro.setAttribute("aria-selected", String(outro === botao));
      estado.textContent = "";
      desenharPainel();
    });
    colunaAbas.appendChild(botao);
  }

  desenharPainel();
  raiz.append(colunaAbas, painel);
  tela.replaceChildren(raiz);
}
