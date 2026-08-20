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

/**
 * O diagnóstico, pedido uma vez por sessão.
 *
 * Duas telas o querem — o bloco "Sobre" e o aviso de placa em Modelos — e ele
 * custa um `nvidia-smi`, que é processo filho. Nem a versão nem a placa mudam
 * enquanto o app está aberto, então a promessa é guardada e reaproveitada. O que
 * muda (quais modelos estão em disco) não sai daqui: quem responde isso é o
 * catálogo, relido a cada recarga da tela.
 */
let _diagnostico = null;
function diagnostico() {
  _diagnostico ??= pedir("diagnostico").then((r) => r.diagnostico);
  return _diagnostico;
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

/**
 * A escolha do tema.
 *
 * O tema inicial NÃO é aplicado aqui: quem o escreve é o núcleo, trocando o
 * `data-tema` do index.html enquanto serve a página (ver App/Conteudo.cs). Se
 * dependesse deste arquivo, a configuração chegaria pela ponte depois da
 * primeira pintura, e quem escolheu escuro veria a interface piscar branca a
 * cada abertura.
 *
 * O que é daqui é a troca com o app aberto — mexer no mesmo atributo, e a
 * interface inteira vira de cor sem recarregar nada, porque tudo que pinta vem
 * de token semântico.
 */
function blocoDeTema(config, gravar) {
  const b = bloco("Aparência",
    "O tema escuro usa a mesma paleta de areia, em carvão. Trocar vale na hora.");

  // Rádio, e não uma chave de ligar: são três estados, e "auto" não é meio-termo
  // entre claro e escuro — é entregar a decisão ao Windows.
  const OPCOES = [
    ["claro",  "Claro"],
    ["escuro", "Escuro"],
    ["auto",   "Igual ao Windows"],
  ];

  const atual = OPCOES.some(([v]) => v === config.tema) ? config.tema : "claro";

  const grupo = document.createElement("div");
  grupo.className = "escolhas";
  grupo.setAttribute("role", "radiogroup");
  grupo.setAttribute("aria-label", "Tema");

  for (const [valor, rotulo] of OPCOES) {
    const label = document.createElement("label");
    label.className = "aa-escolha";

    const radio = document.createElement("input");
    radio.type = "radio";
    radio.name = "tema";
    radio.value = valor;
    radio.checked = valor === atual;
    radio.addEventListener("change", () => {
      // A tela vira primeiro e o disco depois: a resposta ao clique não deve
      // esperar uma escrita em arquivo, e falhar ao gravar já tem seu aviso.
      document.documentElement.dataset.tema = valor;
      gravar({ tema: valor });
    });

    label.append(radio, document.createTextNode(rotulo));
    grupo.appendChild(label);
  }

  b.appendChild(grupo);
  return b;
}

// ─────────────────────────────────────────────────────────── aba Geral

function abaGeral(config, gravador, gravar, estadoDoTexto) {
  const painel = document.createElement("div");
  painel.className = "painel";

  const pastas = bloco("Pastas",
    "Onde o app grava e procura as gravações, e para onde ele copia o que você exporta.");

  // Uma pasta só, desde a Fase 2.5.
  //
  // Havia duas chaves: o `output_dir` do gravador, que é onde o áudio de fato
  // caía, e este campo, que gravava um `pasta_das_gravacoes` que NADA lia —
  // mexer nele não tinha efeito nenhum. Fundidos os dois programas, gravar e
  // ler no mesmo lugar deixa de ser coincidência: este campo agora é o
  // output_dir, e a migração de quem já tinha escolhido algo aqui acontece na
  // primeira abertura (ver PastaDasGravacoes.cs).
  const campoPasta = campoDePasta("Pasta das gravações",
    gravador.pasta, "onde as reuniões são gravadas",
    async (v) => {
      estadoDoTexto("salvando…");
      try {
        const r = await pedir("pasta-das-gravacoes", { pasta: v });
        gravador.pasta = r.gravador.pasta;
        estadoDoTexto("salvo");
      } catch (e) {
        estadoDoTexto(`não salvou: ${e.message}`);
      }
    });

  const campoExport = campoDePasta("Pasta de exportação",
    config.pasta_de_exportacao ?? "", "vazio = só ao lado da gravação",
    (v) => gravar({ pasta_de_exportacao: v }));

  // Pasta própria para as atas: a transcrição é material de trabalho, a ata é o
  // que se manda para fora. Destinos diferentes, finalidades diferentes.
  const campoAtas = campoDePasta("Pasta das atas",
    config.pasta_de_atas ?? "", "vazio = só ao lado da gravação",
    (v) => gravar({ pasta_de_atas: v }));

  const dica = document.createElement("p");
  dica.className = "campo__dica";
  dica.textContent = "As duas exportações sempre gravam ao lado da gravação. "
    + "As pastas acima são a cópia que se leva para fora — e são separadas "
    + "porque a transcrição é material de trabalho e a ata é o que se manda "
    + "para o cliente ou para o time.";

  pastas.append(campoPasta, campoExport, campoAtas, dica);

  // A única conexão que o app abre por conta própria, e por isso ela é
  // desligável e fica à vista — junto do bloco que mostra a versão, que é onde
  // a pergunta "estou atualizado?" nasce.
  const aviso = bloco("Avisar de versão nova",
    "Confere, ao abrir os Ajustes, se saiu uma versão mais nova. Vai um pedido "
    + "de um arquivo público, sem identificação nenhuma — nada da sua reunião "
    + "sai daqui.");
  aviso.classList.add("bloco--chave");
  aviso.appendChild(chave(config.avisar_de_atualizacao !== false,
    async (v) => { await gravar({ avisar_de_atualizacao: v }); recarregar(); }));

  painel.append(pastas, blocoDeTema(config, gravar), blocoSobre(), aviso);
  return painel;
}

/**
 * Versão e diagnóstico.
 *
 * Nasceu na Fase 4, quando o app passou a ser instalado em máquina que não é a
 * de quem o compila. A partir daí "está dando erro" só vira relato utilizável
 * com um número de versão junto — e as três perguntas seguintes ("achou a
 * placa?", "o modelo chegou a baixar?", "para onde estão indo as gravações?")
 * são as mesmas toda vez. O botão responde as quatro de uma vez.
 *
 * O bloco vem do núcleo pronto (Nucleo/Diagnostico.cs) e não é remontado aqui:
 * o texto que a pessoa cola e o texto que ela vê na tela têm que ser o mesmo.
 */
function blocoSobre() {
  const b = bloco("Sobre",
    "A versão instalada, e o bloco que ajuda a resolver um problema à distância.");

  const linha = document.createElement("p");
  linha.className = "campo__dica";
  linha.textContent = "carregando…";

  const botao = document.createElement("button");
  botao.className = "aa-btn aa-btn-secundario";
  botao.type = "button";
  botao.textContent = "Copiar diagnóstico";
  botao.disabled = true;

  let texto = "";
  diagnostico().then((d) => {
    texto = d.texto;
    // A versão e a placa na linha visível: são as duas que a pessoa quer saber
    // sem clicar em nada. O resto está no bloco copiado.
    linha.textContent = `${d.marca} ${d.versao} — `
      + (d.placa ?? "sem placa NVIDIA; a transcrição vai rodar em CPU");
    botao.disabled = false;
  }).catch((e) => {
    linha.textContent = `não deu para ler o diagnóstico: ${e.message}`;
  });

  botao.addEventListener("click", async () => {
    try {
      await navigator.clipboard.writeText(texto);
      botao.textContent = "Copiado";
      setTimeout(() => { botao.textContent = "Copiar diagnóstico"; }, 2000);
    } catch {
      // Sem área de transferência, mostrar o texto ainda resolve: dá para
      // selecionar e copiar à mão. Falhar em silêncio, não.
      linha.textContent = texto;
    }
  });

  const acoes = document.createElement("div");
  acoes.className = "acoes";
  acoes.append(botao);

  b.append(linha, acoes, blocoDeAtualizacao());
  return b;
}

/**
 * Saiu versão nova?
 *
 * Mora dentro do bloco "Sobre" porque é a mesma pergunta: que versão eu tenho, e
 * ela é a atual? Nasceu depois da Fase 4, quando o app passou a existir em
 * máquina que não é a de quem o compila — antes disso, "atualizar" era
 * recompilar.
 *
 * Ele **só avisa**. Não baixa nem troca binário: fazer isso sem assinatura de
 * código seria ensinar o app a executar o que baixou da internet, e assinatura
 * ainda não existe (FASE4-HANDOFF §6.1).
 */
function blocoDeAtualizacao() {
  const caixa = document.createElement("div");

  const linha = document.createElement("p");
  linha.className = "campo__dica";
  linha.textContent = "conferindo se saiu versão nova…";
  caixa.appendChild(linha);

  function desenhar(a) {
    caixa.replaceChildren();

    if (a.desligado) {
      const p = document.createElement("p");
      p.className = "campo__dica";
      p.textContent = "O aviso de versão nova está desligado nesta máquina.";
      caixa.appendChild(p);
      return;
    }

    if (a.nao_deu) {
      // Sem rede não é problema de quem está usando o app: some, discreto.
      const p = document.createElement("p");
      p.className = "campo__dica";
      p.textContent = `Versão ${a.versao_instalada} — ${a.nao_deu}.`;
      caixa.appendChild(p);
      return;
    }

    if (!a.nova) {
      const p = document.createElement("p");
      p.className = "campo__dica";
      p.textContent = "Esta é a versão mais recente.";
      caixa.appendChild(p);
      return;
    }

    const texto = `Saiu a versão ${a.nova.versao}`
      + (a.nova.publicada ? ` (${dia(a.nova.publicada)})` : "")
      + (a.nova.notas ? `: ${a.nova.notas}` : ".");
    caixa.appendChild(alerta(texto, "atencao"));

    const como = document.createElement("p");
    como.className = "campo__dica";
    como.textContent = a.nova.onde
      ? `Baixe em ${a.nova.onde} e rode o instalador por cima — nada se perde.`
      : "Peça o instalador novo e rode por cima desta instalação: gravações, "
        + "transcrições, atas, notas, vozes e modelos baixados ficam onde estão.";
    caixa.appendChild(como);
  }

  pedir("atualizacao").then(desenhar).catch(() => {
    caixa.replaceChildren();
  });

  return caixa;
}

// ───────────────────────────────────────────────────────── aba Gravador

/**
 * O que é do gravador e não cabe na tela dele.
 *
 * A tela do Gravador é o que se olha durante uma reunião: estado, nível,
 * dispositivo. O que se configura uma vez e não se olha mais — notificações,
 * conta do Google — mora aqui, que é onde já se procura configuração.
 */
function abaGravador(gravador, aoMexer) {
  const painel = document.createElement("div");
  painel.className = "painel";

  const avisos = bloco("Notificações",
    "Os balões da bandeja: reunião reconhecida, lembrete de microfone mudo.");
  avisos.classList.add("bloco--chave");
  avisos.appendChild(chave(gravador.notificacoes,
    (v) => aoMexer("notificacoes", { ligado: v })));

  // Dito com todas as letras porque é a diferença entre um aviso que incomoda e
  // um aviso que salva a reunião — e desligar o primeiro não pode desligar o
  // segundo.
  const nota = document.createElement("p");
  nota.className = "bloco__texto";
  nota.textContent =
    "Desligar não silencia dispositivo caindo nem disco enchendo: isso você "
    + "precisa saber de qualquer jeito.";
  avisos.appendChild(nota);

  const agenda = bloco("Google Calendar",
    "Identifica a reunião que está sendo gravada e usa os participantes como "
    + "vocabulário na transcrição.");

  if (!gravador.agenda_configurada) {
    agenda.appendChild(obra(
      "Falta o google_client_secret.json",
      "Ponha o arquivo em %USERPROFILE%\\.meeting-recorder e reabra o app. "
      + "Só aparece em binário montado sem a credencial embutida."));
    painel.append(avisos, agenda);
    return painel;
  }

  const usar = document.createElement("div");
  usar.className = "bloco bloco--chave";
  const usarTitulo = document.createElement("h2");
  usarTitulo.className = "bloco__titulo";
  usarTitulo.textContent = "Usar esta agenda";
  usar.append(usarTitulo, chave(gravador.usar_agenda,
    (v) => aoMexer("usar-agenda", { ligado: v })));

  const conta = document.createElement("p");
  conta.className = "bloco__texto";
  conta.textContent = gravador.conta
    ? `Conectado: ${gravador.conta}` : "Nenhuma conta conectada.";

  const acoes = document.createElement("div");
  acoes.className = "acoes";

  const conectar = document.createElement("button");
  conectar.className = "aa-btn aa-btn-secundario";
  conectar.type = "button";
  conectar.textContent = gravador.conta ? "Trocar de conta…" : "Conectar conta…";
  conectar.addEventListener("click", () => {
    // O navegador abre e a autorização termina fora do app; o resultado chega
    // por balão da bandeja. Recarregar aqui mostraria o estado de antes.
    conta.textContent = "Abrindo o navegador… conclua a autorização por lá.";
    aoMexer("conectar-agenda");
  });
  acoes.appendChild(conectar);

  if (gravador.conta) {
    const desconectar = document.createElement("button");
    desconectar.className = "aa-btn aa-btn-texto";
    desconectar.type = "button";
    desconectar.textContent = "Desconectar";
    desconectar.addEventListener("click", () => aoMexer("desconectar-agenda"));
    acoes.appendChild(desconectar);
  }

  agenda.append(conta, acoes);
  painel.append(avisos, usar, agenda);
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

/**
 * O motor de ata: o programa, e não o modelo.
 *
 * Ele deixou de viajar no instalador na Fase 4 — são 1,1 GB descompactados para
 * uma funcionalidade que nem toda instalação usa, e tirá-lo tirou 400 MB do
 * arquivo que se manda por link. O bloco existe para que "baixar depois" seja
 * uma escolha visível e não uma surpresa na hora de gerar a primeira ata.
 *
 * São dois downloads da release oficial do llama.cpp no GitHub. Não hospedamos
 * nada, e a origem é conferível por quem quiser.
 */
function blocoDoMotorDeAta() {
  const b = bloco("Motor de ata",
    "O programa que roda o modelo de ata na sua placa. Vem separado do app.");

  const dizer = document.createElement("p");
  dizer.className = "campo__dica";
  dizer.textContent = "verificando…";

  const botao = document.createElement("button");
  botao.className = "aa-btn aa-btn-secundario";
  botao.type = "button";
  botao.textContent = "Baixar";
  botao.hidden = true;

  const barra = document.createElement("div");
  barra.className = "aa-progresso";
  barra.hidden = true;
  barra.appendChild(document.createElement("div"));

  const andamento = document.createElement("span");
  andamento.className = "campo__dica";

  function desenhar(m) {
    if (m.instalado) {
      dizer.textContent = `Instalado — ${tamanho(m.bytes_em_disco)} em disco.`;
      botao.hidden = true;
      return;
    }
    dizer.textContent = "Ainda não baixado. Sem ele, gerar ata falha na hora — "
      + `são ${tamanho(m.bytes_do_download)} de download, uma vez só.`;
    botao.hidden = false;
  }

  pedir("motor-de-ata")
    .then((r) => desenhar(r.motor_de_ata))
    .catch((e) => { dizer.textContent = `não deu para verificar: ${e.message}`; });

  botao.addEventListener("click", async () => {
    botao.disabled = true;
    barra.hidden = false;
    andamento.textContent = "começando…";
    try {
      const r = await pedir("baixar-motor-de-ata", {}, (p) => {
        barra.firstChild.style.width = `${Math.round((p.fracao ?? 0) * 100)}%`;
        andamento.textContent = p.texto ?? "";
      });
      barra.hidden = true;
      andamento.textContent = "";
      desenhar(r.motor_de_ata);
    } catch (e) {
      // O caminho continua utilizável depois de uma falha — rede cai, e tentar
      // de novo é só clicar. Daí o botão voltar, em vez de a tela travar.
      andamento.textContent = `não baixou: ${e.message}`;
      botao.disabled = false;
      barra.hidden = true;
    }
  });

  const acoes = document.createElement("div");
  acoes.className = "acoes";
  acoes.append(botao);

  b.append(dizer, acoes, barra, andamento);
  return b;
}

function abaModelos(catalogo, config, gravar) {
  const painel = document.createElement("div");
  painel.className = "painel";

  // O aviso de placa, quando não há placa.
  //
  // A primeira versão instalável só traz o caminho CUDA (docs/FASE4.md §2,
  // decisão 4). Sem NVIDIA o app funciona — o faster-whisper e o llama.cpp caem
  // para CPU sozinhos —, mas uma reunião de uma hora passa a levar horas. Dizer
  // isso aqui é a diferença entre um app lento e um app que parece travado.
  //
  // Entra por cima, e não some depois: quem instalou numa máquina sem placa
  // precisa dessa informação toda vez que escolher um modelo, não só na
  // primeira.
  const avisoDePlaca = document.createElement("div");
  painel.appendChild(avisoDePlaca);
  diagnostico().then((d) => {
    if (d.placa) return;
    avisoDePlaca.appendChild(alerta(
      "Não encontrei placa NVIDIA nesta máquina. O app funciona, mas transcreve "
      + "pela CPU — uma reunião de uma hora pode levar algumas horas. Modelos "
      + "menores (Medium, Small) ajudam bastante nesse caso.", "atencao"));
  }).catch(() => {
    // Sem diagnóstico não se afirma nada: um aviso errado sobre a placa é pior
    // que aviso nenhum.
  });

  for (const [familia, titulo, texto, chaveConfig, padrao] of [
    ["asr", "Transcrição", "Qual modelo transforma áudio em texto.",
     "modelo_padrao", "large-v3"],
    // A família da Fase 3. O valor guardado é o nome do arquivo, e não o id:
    // quem abre o .gguf é o llama.cpp, por caminho.
    ["ata", "Ata", "Qual modelo escreve as atas a partir da transcrição.",
     "modelo_de_ata", "qwen3-4b-instruct-q4km.gguf"],
  ]) {
    const b = bloco(titulo, texto);
    const itens = catalogo.filter((i) => i.pacote.familia === familia);

    const escolha = campo("Usar por padrão", "select",
      { opcoes: itens.map((i) => i.pacote.nome) });
    const sel = escolha.querySelector("select");
    // O valor visível é o nome bonito; o que vai para a configuração é o id que
    // o motor entende. Sem esta separação a tela dita o vocabulário do motor.
    itens.forEach((i, n) => {
      sel.options[n].value = i.pacote.nome_local ?? i.pacote.id;
    });
    sel.value = config[chaveConfig] ?? padrao;
    sel.addEventListener("change", async (e) => {
      await gravar({ [chaveConfig]: e.target.value });
      recarregar();
    });
    b.appendChild(escolha);

    for (const i of itens) b.appendChild(cartaoDeModelo(i));
    painel.appendChild(b);
  }

  painel.appendChild(blocoDoMotorDeAta());

  // Diarização: um bloco que informa, e não oferece.
  //
  // Ela tinha cartão e seletor até a Fase 4, e os dois mentiam de formas
  // diferentes. O cartão media o cache do HuggingFace, que o motor deixou de
  // ler quando os pesos passaram a viajar dentro do instalador — numa
  // instalação nova ele diria "ausente" sobre uma diarização que funciona. E o
  // seletor nunca chegou ao pipeline: era colhido, salvo e ignorado
  // (docs/FASE6.md §4.6).
  //
  // O que sobrou é o que a pessoa de fato quer saber olhando aqui: qual modelo
  // separa os falantes, e por que ele não aparece para baixar.
  const diar = bloco("Diarização", "Qual modelo separa quem falou.");
  const dizerDiar = document.createElement("p");
  dizerDiar.className = "campo__dica";
  dizerDiar.textContent = "Pyannote Community 1, e ele já vem dentro do app — "
    + "são 57 MB instalados junto, não há o que baixar nem o que escolher. "
    + "Separar falantes funciona sem internet desde a primeira reunião.";
  diar.appendChild(dizerDiar);
  painel.appendChild(diar);

  const nota = document.createElement("p");
  nota.className = "campo__dica";
  // O texto mudou na Fase 4, e o motivo é que o comportamento mudou.
  //
  // Antes o faster-whisper baixava 3 GB sozinho na primeira transcrição — sem
  // barra, sem anunciar o tamanho, no meio de um clique. Agora o app confere
  // antes e manda para cá, onde existe barra de progresso e o tamanho está
  // escrito. E a diarização não baixa mais nada: os pesos dela vêm dentro do
  // instalador.
  nota.textContent = "A transcrição e a ata precisam do modelo baixado antes: "
    + "o app não começa um download de gigabytes no meio de um clique. É aqui "
    + "que se baixa, com barra e tamanho à vista. A diarização não aparece para "
    + "baixar porque ela já vem dentro do app.";
  painel.appendChild(nota);

  return painel;
}

// ────────────────────────────────────────────────────── aba Transcrição

function abaTranscricao(config, gravar) {
  const painel = document.createElement("div");
  painel.className = "painel";

  // A placa, e o que fazer quando ela não aparece.
  //
  // Relatado em 18/08/2026: numa máquina com RTX 4050 a transcrição caiu para
  // CPU e o large-v3 consumiu RAM por horas até derrubar o Windows. Desde
  // então o pipeline recusa a CPU por padrão — e esta é a chave que torna a
  // exceção uma escolha, para quem de fato não tem placa.
  const semPlaca = bloco("Transcrever sem placa",
    "Por padrão o app recusa transcrever quando não encontra a placa de vídeo. "
    + "Ligue só se esta máquina não tem placa NVIDIA: a transcrição passa a "
    + "levar horas em vez de minutos e consome muita memória.");
  semPlaca.classList.add("bloco--chave");
  semPlaca.appendChild(chave(config.permitir_cpu === true,
    (v) => gravar({ permitir_cpu: v })));
  painel.appendChild(semPlaca);

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
  fonetica.classList.add("bloco--chave");
  fonetica.appendChild(chave(config.correcao_fonetica !== false,
    (v) => gravar({ correcao_fonetica: v })));

  const comoVer = document.createElement("p");
  comoVer.className = "campo__dica";
  comoVer.textContent = "Na transcrição, cada trecho corrigido ganha uma marca "
    + "✎, e o filtro no topo mostra só os corrigidos. Clicar na marca desfaz a "
    + "troca daquele trecho — a correção é um palpite, e palpite que ninguém "
    + "confere é um defeito esperando.";
  fonetica.appendChild(comoVer);
  painel.appendChild(fonetica);

  // ---- os domínios da casa
  //
  // Só os nossos, e não os dos clientes: quem não é da casa é cliente, e essa
  // regra não precisa de manutenção quando aparece um cliente novo.
  const casa = bloco("Domínios da nossa organização",
    "Os e-mails da agenda dizem de que lado da mesa cada pessoa está. Sem isto, "
    + "a ata deduz pelo assunto da conversa — e numa reunião que fala de um "
    + "cliente o tempo todo, alguém da equipe vira gente do cliente.");

  const entrada = document.createElement("input");
  entrada.className = "aa-entrada";
  entrada.id = "dominios-da-casa";
  entrada.placeholder = "beegol.com, minhaempresa.com.br";
  entrada.value = (config.dominios_da_casa ?? []).join(", ");
  entrada.addEventListener("change", () => gravar({
    dominios_da_casa: entrada.value.split(",")
      .map((d) => d.trim().replace(/^@/, "").toLowerCase())
      .filter(Boolean),
  }));

  const dica = document.createElement("p");
  dica.className = "campo__dica";
  dica.textContent = "Separados por vírgula. Quem tiver e-mail de outro domínio "
    + "entra como cliente; quem não tiver e-mail na agenda fica sem lado, e a "
    + "ata não inventa um.";
  casa.append(entrada, dica);
  painel.appendChild(casa);

  const silencio = bloco("Descartar fala inventada sobre silêncio");
  silencio.classList.add("bloco--chave");
  const porQue = document.createElement("p");
  porQue.className = "bloco__texto";
  // O número está na tela porque é ele que justifica a chave existir.
  porQue.textContent = "Cerca de 5% das palavras que o modelo transcreve caem "
    + "sobre trechos em que não há sinal nenhum — zeros exatos, não fala baixa. "
    + "Sobre ausência de som, qualquer palavra é invenção. Ligado, esses trechos "
    + "são descartados antes de a transcrição existir.";
  const cuidado = document.createElement("p");
  cuidado.className = "campo__dica";
  cuidado.textContent = "O critério é severo de propósito (99% de amostras "
    + "zeradas, e dois terços do trecho): fala removida por engano é conteúdo "
    + "que não volta, enquanto invenção some no meio da ata sem ninguém notar.";
  silencio.append(porQue, chave(config.filtrar_silencio,
    (v) => gravar({ filtrar_silencio: v })), cuidado);
  painel.appendChild(silencio);

  // ---- o hotwords
  //
  // Desligado desde 19/08/2026. A chave existe mais para poder medir do que
  // para escolher — a comparação com e sem, no mesmo áudio, é o que a régua de
  // fontes paralelas pede (FASE6 §5).
  const hot = bloco("Dar o vocabulário ao modelo enquanto ele transcreve");
  hot.classList.add("bloco--chave");
  const oQueCusta = document.createElement("p");
  oQueCusta.className = "bloco__texto";
  oQueCusta.textContent = "Desligado, o vocabulário continua sendo usado para "
    + "corrigir a grafia depois — que é o que recupera \"Dimi\" de \"Jimmy\". "
    + "Ligado, ele também é sussurrado ao modelo durante a transcrição, e isso "
    + "faz o modelo juntar a fala em blocos longos: na mesma reunião, 207 "
    + "trechos em vez de 787, e quatro vezes mais tempo para transcrever.";
  const porQueImporta = document.createElement("p");
  porQueImporta.className = "campo__dica";
  porQueImporta.textContent = "Quem falou é decidido por trecho. Num trecho de "
    + "40 segundos com três pessoas dentro, duas somem — e é por isso que a "
    + "chave está desligada: as duas medições dizem que os nomes se recuperam "
    + "igual pelos dois caminhos.";
  hot.append(oQueCusta, chave(config.usar_hotwords === true,
    (v) => gravar({ usar_hotwords: v })), porQueImporta);
  painel.appendChild(hot);

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
  const comoNascem = document.createElement("p");
  comoNascem.className = "campo__dica";
  comoNascem.textContent = "Cliente e projeto novos nascem na tela de preparo: "
    + "digite um nome que ainda não existe e ele passa a valer. Aqui se renomeia "
    + "e se apaga o que já existe — renomear leva junto o vocabulário e as "
    + "preferências do projeto.";
  painel.appendChild(comoNascem);

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
  ["gravador", "Gravador"],
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

  let config, clientes, catalogo, vozes, gravador;
  try {
    // Tudo de uma vez: são cinco leituras baratas e locais, e pedir sob demanda
    // a cada troca de aba faria a aba piscar por nada.
    [{ config }, { clientes }, { catalogo }, { vozes }, { gravador }] = await Promise.all([
      pedir("config"), pedir("clientes"), pedir("catalogo"), pedir("vozes"),
      pedir("gravador"),
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

  /** As operações do gravador: cada uma devolve o estado inteiro de volta. */
  async function mexerNoGravador(op, campos = {}) {
    estado.textContent = "salvando…";
    try {
      const r = await pedir(op, campos);
      gravador = r.gravador;
      estado.textContent = "salvo";
      desenharPainel();
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
      geral: () => abaGeral(config, gravador, gravar, (t) => { estado.textContent = t; }),
      gravador: () => abaGravador(gravador, mexerNoGravador),
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
