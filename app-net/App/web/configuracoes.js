// A tela de ajustes: seis abas numa coluna à esquerda.
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
import { abaClientes } from "/clientes.js";
import { abaVozes } from "/vozes.js";
import { secaoDeModelos } from "/modelos-de-ata.js";
import { alerta, campo, confirmar, dica, pararAudio } from "/pecas.js";

/** "3,1 GB", "148 MB" — tamanho para uma pessoa decidir, não para conferir. */
function tamanho(bytes) {
  if (!bytes) return "—";
  const gb = bytes / 1e9;
  if (gb >= 1) return `${gb.toFixed(1).replace(".", ",")} GB`;
  return `${Math.round(bytes / 1e6)} MB`;
}

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

/**
 * O interruptor do design system (`.aa-interruptor`), e não a caixinha crua.
 *
 * @param rotulo o nome que o leitor de tela diz — o título do ajuste.
 */
function chave(ligada, aoMudar, rotulo) {
  const raiz = document.createElement("label");
  raiz.className = "aa-interruptor";
  const caixa = document.createElement("input");
  caixa.type = "checkbox";
  caixa.checked = ligada === true;
  if (rotulo) caixa.setAttribute("aria-label", rotulo);
  caixa.addEventListener("change", (e) => aoMudar(e.target.checked));
  raiz.appendChild(caixa);
  return raiz;
}

/**
 * Um ajuste numa linha: título com o ⓘ, uma frase, o custo e o controle.
 *
 * É o desenho do plano 4b (spec §3.4, e o mockup de 05/10/2026): a decisão é o
 * que aparece; a explicação medida, que antes ocupava dois parágrafos por
 * cartão, fica atrás do ⓘ.
 *
 * @param o.titulo o nome do ajuste.
 * @param o.linha uma frase do que ele faz.
 * @param o.dica parágrafos do ⓘ; vazio, sem ⓘ.
 * @param o.custo etiqueta à direita ("≈ ⅓ da placa"), opcional.
 * @param o.etiqueta etiqueta neutra junto do título ("teste"), opcional.
 * @param o.controle o nó da direita — interruptor, seletor, campo.
 * @param o.sub recuado, porque só vale com o ajuste de cima ligado.
 * @param o.perigo a frase sai no tom de erro: ela avisa o que se perde.
 * @param o.largo o controle ocupa o resto da linha (campos de texto).
 */
function ajuste({ titulo, linha, dica: textos = [], custo, etiqueta, controle,
                  sub = false, perigo = false, largo = false }) {
  const raiz = document.createElement("div");
  raiz.className = "ajuste";
  if (sub) raiz.classList.add("ajuste--sub");
  if (largo) raiz.classList.add("ajuste--largo");

  const texto = document.createElement("div");
  texto.className = "ajuste__texto";
  const t = document.createElement("span");
  t.className = "ajuste__titulo";
  t.textContent = titulo;
  if (etiqueta) {
    const e = document.createElement("span");
    e.className = "aa-etiqueta";
    e.textContent = etiqueta;
    t.appendChild(e);
  }
  if (textos.length) t.appendChild(dica(titulo.toLowerCase(), ...textos));
  texto.appendChild(t);
  if (linha) {
    const l = document.createElement("span");
    l.className = "ajuste__linha";
    if (perigo) l.classList.add("ajuste__linha--perigo");
    l.textContent = linha;
    texto.appendChild(l);
  }
  raiz.appendChild(texto);

  if (custo) {
    const c = document.createElement("span");
    c.className = "aa-etiqueta aa-etiqueta--atencao ajuste__custo";
    c.textContent = custo;
    raiz.appendChild(c);
  }
  if (controle) raiz.appendChild(controle);
  return raiz;
}

/** Uma seção de ajustes: o cartão com título e as linhas dentro. */
function secaoDeAjustes(titulo, ...itens) {
  const b = document.createElement("section");
  b.className = "bloco ajustes-secao";
  if (titulo) {
    const h = document.createElement("h2");
    h.className = "bloco__titulo";
    h.textContent = titulo;
    b.appendChild(h);
  }
  b.append(...itens.filter(Boolean));
  return b;
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

/**
 * O nome do dono, no lugar do "You" (05/10/2026, a pedido do dono do produto).
 *
 * Não muda nada ao vivo nem no reconhecimento de vozes: o núcleo troca o
 * rótulo só no fim da transcrição, como quando a pessoa renomeia o falante na
 * revisão (VozDoDono.Nomear). A prévia abaixo do campo mostra o efeito, porque
 * "seu nome nas transcrições" sozinho não diz onde ele aparece.
 */
function secaoVoce(config, gravar) {
  const entrada = document.createElement("input");
  entrada.className = "aa-entrada";
  entrada.id = "nome-do-dono";
  entrada.placeholder = "Você";
  entrada.value = config.nome_do_dono ?? "";
  entrada.setAttribute("aria-label", "Seu nome nas transcrições");

  const previa = document.createElement("div");
  previa.className = "voce__previa";
  const legenda = document.createElement("span");
  legenda.className = "voce__legenda";
  legenda.textContent = "Assim fica a transcrição";
  const linha = (tempo, quem, fala, dono) => {
    const l = document.createElement("div");
    l.className = "voce__linha";
    const t = document.createElement("span");
    t.className = "voce__tempo";
    t.textContent = tempo;
    const q = document.createElement("span");
    q.className = dono ? "voce__quem voce__quem--dono" : "voce__quem";
    q.textContent = quem;
    const f = document.createElement("span");
    f.textContent = fala;
    l.append(t, q, f);
    return { l, q };
  };
  const sua = linha("00:12", "", "Bom dia, pessoal, vamos começar pelo cronograma.", true);
  const outra = linha("00:19", "Speaker 1", "Fechei a parte da integração ontem.", false);
  previa.append(legenda, sua.l, outra.l);

  const mostrar = () => { sua.q.textContent = entrada.value.trim() || "You"; };
  mostrar();
  entrada.addEventListener("input", mostrar);
  entrada.addEventListener("change", () => gravar({ nome_do_dono: entrada.value.trim() }));

  return secaoDeAjustes("Você",
    ajuste({
      titulo: "Seu nome nas transcrições",
      linha: "Quem fala pelo seu microfone sai com este nome.",
      dica: [
        "Hoje a sua fala sai como \"You\" e você renomeia na revisão. Com um nome "
        + "aqui, ela já sai nomeada no fim da transcrição — como se você tivesse "
        + "renomeado à mão, e dá para renomear de novo numa reunião.",
        "Vale para as próximas transcrições. Vazio, continua \"You\". A legenda ao "
        + "vivo continua mostrando \"Você\".",
      ],
      controle: entrada, largo: true,
    }),
    previa);
}

function abaGeral(config, gravador, gravar, estadoDoTexto) {
  const painel = document.createElement("div");
  painel.className = "painel";

  const pastas = bloco("Pastas");

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

  const campoExport = campoDePasta("Cópia das transcrições",
    config.pasta_de_exportacao ?? "", "vazio = só ao lado da gravação",
    (v) => gravar({ pasta_de_exportacao: v }));

  // Pasta própria para as atas: a transcrição é material de trabalho, a ata é o
  // que se manda para fora. Destinos diferentes, finalidades diferentes.
  const campoAtas = campoDePasta("Cópia das atas",
    config.pasta_de_atas ?? "", "vazio = só ao lado da gravação",
    (v) => gravar({ pasta_de_atas: v }));

  const titulo = pastas.querySelector(".bloco__titulo");
  titulo.appendChild(dica("as pastas",
    "As exportações sempre gravam ao lado da gravação. As duas cópias são o que "
    + "se leva para fora — e são separadas porque a transcrição é material de "
    + "trabalho, e a ata é o que se manda para o cliente ou para o time."));

  pastas.append(campoPasta, campoExport, campoAtas);

  painel.append(secaoVoce(config, gravar), pastas, blocoDeTema(config, gravar),
                secaoDoClaude(), blocoSobre(config, gravar));
  return painel;
}

/**
 * A conexão com o Claude Desktop: uma entrada no arquivo de configuração dele,
 * que passa a abrir este executável com --mcp e a ler as reuniões por ali.
 *
 * O botão é o caminho inteiro, de propósito: o arquivo mora num lugar diferente
 * em cada instalação do Claude, guarda as preferências dele junto, e editá-lo à
 * mão é o tipo de instrução que ninguém segue. Ver docs/INTEGRACAO-CLAUDE.md.
 */
function secaoDoClaude() {
  const linha = document.createElement("span");
  const botao = document.createElement("button");
  botao.type = "button";
  botao.className = "aa-btn aa-btn-secundario";
  botao.hidden = true;
  const recado = document.createElement("p");
  recado.className = "campo__dica";
  recado.hidden = true;

  let conectado = false;

  function desenhar(c, depoisDeMexer = false) {
    conectado = c.conectado && c.neste_executavel;
    botao.hidden = !c.encontrado;
    botao.disabled = false;
    recado.hidden = true;

    if (c.erro) {
      linha.textContent = `Não deu para ler a configuração do Claude: ${c.erro}`;
      botao.hidden = true;
    } else if (!c.encontrado) {
      linha.textContent = "O Claude Desktop não está instalado nesta conta do Windows.";
    } else if (conectado) {
      linha.textContent = "Conectado: as reuniões aparecem no + do Claude Desktop, "
        + "e o Claude pode buscá-las sozinho.";
      botao.textContent = "Desconectar";
      botao.className = "aa-btn aa-btn-texto";
    } else if (c.conectado) {
      linha.textContent = "A conexão aponta para outro PulseMeet.exe, que pode ter mudado de lugar.";
      botao.textContent = "Reconectar";
      botao.className = "aa-btn aa-btn-secundario";
    } else {
      linha.textContent = "Deixa o Claude ler as atas, as transcrições e as notas, sem copiar.";
      botao.textContent = "Conectar ao Claude";
      botao.className = "aa-btn aa-btn-secundario";
    }

    // O Claude só lê a configuração ao abrir, e fechar a janela dele não o
    // encerra — ele fica na bandeja, como este app.
    if (depoisDeMexer && c.encontrado && !c.erro) {
      recado.hidden = false;
      recado.textContent = "Saia do Claude Desktop pela bandeja e abra de novo: "
        + "ele só lê a conexão quando abre.";
    }
  }

  botao.addEventListener("click", async () => {
    botao.disabled = true;
    try {
      const r = await pedir(conectado ? "desconectar-claude" : "conectar-claude");
      desenhar(r.claude, true);
    } catch (e) {
      botao.disabled = false;
      recado.hidden = false;
      recado.textContent = `não deu: ${e.message}`;
    }
  });

  linha.textContent = "conferindo…";
  pedir("claude").then((r) => desenhar(r.claude)).catch((e) => {
    linha.textContent = `não deu para conferir: ${e.message}`;
  });

  const item = ajuste({
    titulo: "Claude Desktop",
    dica: [
      "O Claude Desktop abre o PulseMeet num processo à parte, que só lê as reuniões "
      + "deste computador. Funciona com o app fechado, e nada muda nas gravações.",
      "Com a reunião anexada pelo +, ou pedindo direto (\"faça a linha do tempo do cliente "
      + "X\"), o Claude lê a ata, a transcrição e as notas. O que ele lê vai para a "
      + "conversa — e, como toda conversa com o Claude, para a Anthropic. O resto do "
      + "acervo não sai daqui.",
      "\"Esta reunião\" é a última que você abriu aqui.",
    ],
    controle: botao,
  });
  // A frase muda com o estado, então ela entra no lugar da linha fixa do ajuste.
  linha.className = "ajuste__linha";
  item.querySelector(".ajuste__texto").appendChild(linha);

  return secaoDeAjustes("Claude", item, recado);
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
function blocoSobre(config, gravar) {
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

  // O segundo botão leva o fim do registro.log junto (SUP-1): é o que se pede
  // quando a foto não basta, e pedir o arquivo à parte já deu errado duas vezes.
  const comRegistro = document.createElement("button");
  comRegistro.className = "aa-btn aa-btn-texto";
  comRegistro.type = "button";
  comRegistro.textContent = "Copiar com o registro";
  comRegistro.disabled = true;

  let texto = "";
  let textoComRegistro = "";
  diagnostico().then((d) => {
    texto = d.texto;
    textoComRegistro = d.registro_final
      ? `${d.texto}\n\núltimas linhas do registro:\n${d.registro_final}`
      : d.texto;
    comRegistro.disabled = false;
    // A versão e a placa na linha visível: são as duas que a pessoa quer saber
    // sem clicar em nada. O resto está no bloco copiado.
    linha.textContent = `${d.marca} ${d.versao} — `
      + (d.placa ?? "sem placa NVIDIA; a transcrição vai rodar em CPU");
    botao.disabled = false;
  }).catch((e) => {
    linha.textContent = `não deu para ler o diagnóstico: ${e.message}`;
  });

  const copiar = (b, rotulo, conteudo) => b.addEventListener("click", async () => {
    try {
      await navigator.clipboard.writeText(conteudo());
      b.textContent = "Copiado";
      setTimeout(() => { b.textContent = rotulo; }, 2000);
    } catch {
      // Sem área de transferência, mostrar o texto ainda resolve: dá para
      // selecionar e copiar à mão. Falhar em silêncio, não.
      linha.textContent = conteudo();
    }
  });
  copiar(botao, "Copiar diagnóstico", () => texto);
  copiar(comRegistro, "Copiar com o registro", () => textoComRegistro);

  const acoes = document.createElement("div");
  acoes.className = "acoes";
  acoes.append(botao, comRegistro);

  // A única conexão que o app abre por conta própria, e por isso ela é
  // desligável e fica à vista — no bloco que mostra a versão, que é onde a
  // pergunta "estou atualizado?" nasce.
  const aviso = ajuste({
    titulo: "Avisar de versão nova",
    linha: "Confere um arquivo público ao abrir os Ajustes; nada da reunião sai daqui.",
    controle: chave(config.avisar_de_atualizacao !== false,
      async (v) => { await gravar({ avisar_de_atualizacao: v }); recarregar(); },
      "Avisar de versão nova"),
  });

  b.append(linha, acoes, blocoDeAtualizacao(), aviso);
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
 * Um seletor de dispositivo, com "Padrão do Windows" primeiro.
 *
 * Primeiro e padrão porque seguir o dispositivo do sistema é o que a maioria
 * quer: fixar um headset específico quebra no dia em que ele é desconectado.
 */
function seletorDeDispositivo(faixa, lista, escolhido, rotulo) {
  const campo = document.createElement("select");
  campo.className = "aa-entrada";
  campo.id = `dispositivo-${faixa}`;
  campo.setAttribute("aria-label", rotulo);
  const padrao = document.createElement("option");
  padrao.value = "";
  padrao.textContent = "Padrão do Windows";
  campo.appendChild(padrao);
  for (const d of lista ?? []) {
    const o = document.createElement("option");
    o.value = d.id;
    o.textContent = d.padrao ? `${d.nome}  (padrão)` : d.nome;
    campo.appendChild(o);
  }
  campo.value = escolhido ?? "";
  return campo;
}

/**
 * O que é do gravador e se configura uma vez: dispositivos, o que roda durante
 * a reunião, notificações e agenda.
 *
 * **Dispositivos moram aqui desde o plano 4b** (spec §3.2): a tela do Gravador
 * é o que se olha durante uma reunião, e eles "se mexem uma vez por mês". O
 * "Vai gravar" de lá mostra o que está escolhido e traz para cá.
 *
 * **O que roda durante a reunião também** (05/10/2026): a legenda, os nomes
 * ao vivo e a pergunta aparecem no Gravador, e era em Transcrição — que é a
 * passada do fim — que se procurava por eles.
 */
function abaGravador({ gravador, dispositivos, config }, aoMexer, gravar) {
  const painel = document.createElement("div");
  painel.className = "painel";

  // ---- dispositivos
  //
  // A trava não é limitação de implementação e sim o que preserva o valor das
  // duas faixas separadas: reabrir o stream no meio exigiria realinhá-las.
  if (dispositivos) {
    const mic = seletorDeDispositivo("mic", dispositivos.entradas, dispositivos.mic_id,
                                     "Microfone");
    const sis = seletorDeDispositivo("loopback", dispositivos.saidas,
                                     dispositivos.loopback_id, "Áudio da reunião");
    for (const [faixa, campo] of [["mic", mic], ["loopback", sis]]) {
      campo.disabled = gravador.gravando === true;
      campo.addEventListener("change", () => {
        // A tela se redesenha com o estado do gravador que volta; o dispositivo
        // escolhido tem que estar na lista local para não voltar ao anterior.
        dispositivos[faixa === "mic" ? "mic_id" : "loopback_id"] = campo.value;
        aoMexer("escolher-dispositivo", { faixa, dispositivo: campo.value });
      });
    }
    const secao = secaoDeAjustes("Dispositivos",
      ajuste({ titulo: "Microfone", linha: "A sua voz.", controle: mic }),
      ajuste({
        titulo: "Áudio da reunião", linha: "O que sai pelo seu fone ou alto-falante.",
        dica: ["É o áudio que o Windows manda para este dispositivo de saída — as vozes "
               + "da chamada. Ele é gravado numa faixa separada da sua, e é isso que "
               + "permite saber com certeza quando quem falou foi você."],
        controle: sis,
      }));
    if (gravador.gravando) {
      const trava = document.createElement("p");
      trava.className = "ajustes__trava";
      trava.textContent = "Gravando agora: os dois ficam travados até a gravação parar — "
        + "as faixas começam alinhadas por começarem juntas.";
      secao.appendChild(trava);
    }
    painel.appendChild(secao);
  }

  // ---- durante a reunião
  //
  // Tudo nasce desligado enquanto o SUP-2 estiver aberto: ao vivo, um
  // desligamento da máquina custa a reunião, e não uma transcrição que a
  // retomada devolve.
  //
  // **A prévia em blocos saiu da tela** (05/10/2026, decisão do dono do
  // produto): ela era o "ao vivo" do tempo do MOSS, e a legenda faz o mesmo
  // melhor. Ligar a legenda continua desligando a chave antiga no app.json,
  // porque as duas não cabem juntas na placa (LegendaAoVivo.OQueImpede).
  const legenda = config.legenda_ao_vivo === true;
  const perguntar = config.perguntar_ao_vivo === true;
  painel.appendChild(secaoDeAjustes("Durante a reunião",
    ajuste({
      titulo: "Legenda ao vivo",
      linha: "O texto da reunião aparece no Gravador enquanto ela acontece.",
      dica: [
        "A fala aparece quase no instante — a sua de um lado, a dos outros do "
        + "outro. É rascunho, sem pontuação: a transcrição do fim continua sendo a "
        + "que vale.",
        "Ocupa cerca de um terço da placa a reunião inteira. Precisa do modelo da "
        + "legenda baixado em Ajustes › Modelos; sem ele, a chave não tem efeito.",
      ],
      custo: "≈ ⅓ da placa",
      controle: chave(legenda, async (v) => {
        await gravar(v ? { legenda_ao_vivo: true, transcricao_ao_vivo: false }
                       : { legenda_ao_vivo: false });
        recarregar();
      }, "Legenda ao vivo"),
    }),
    // Só com a legenda: é um segundo sidecar (o Nemotron-3) ao lado dela. O
    // custo medido sem o Meet está em docs/NEMOTRON-DIARIZACAO.md §4b.
    legenda && ajuste({
      titulo: "Nomes na legenda", etiqueta: "teste", sub: true,
      linha: "O nome de quem fala em vez de \"Outros\", para vozes que o app já conhece.",
      dica: [
        "Quem o app não conhece aparece como \"Pessoa 1\", \"Pessoa 2\". O nome "
        + "costuma aparecer depois de alguns segundos de fala da pessoa.",
        "Usa o nemotron-3 e vale a partir da próxima gravação. Se a legenda começar "
        + "a atrasar, desligue aqui.",
      ],
      custo: "+1,5 GB de placa",
      controle: chave(config.falantes_ao_vivo === true, async (v) => {
        await gravar({ falantes_ao_vivo: v });
        recarregar();
      }, "Nomes na legenda"),
    }),
    // Esta convive com a legenda: o motor de ata sobe por pergunta e morre
    // depois dela. A chave existe para poder desligar quando a máquina estiver
    // sendo usada para outra coisa (dono do produto, 16/09/2026).
    ajuste({
      titulo: "Perguntar durante a reunião",
      linha: "Uma caixa no Gravador que responde sobre o que já foi dito.",
      dica: [
        "O modelo lê o que já foi transcrito e responde ali mesmo — o que ficou "
        + "pendente, sobre o que estão falando agora.",
        "Cada pergunta leva cerca de vinte segundos, e enquanto ele responde a "
        + "legenda engasga. Lê no máximo a última hora de reunião. Usa o modelo "
        + "escolhido em Ajustes › Modelos.",
      ],
      custo: "~20 s por pergunta",
      controle: chave(perguntar, async (v) => {
        await gravar({ perguntar_ao_vivo: v });
        recarregar();
      }, "Perguntar durante a reunião"),
    }),
    // A única chave que deixa uma carga residente na placa: o terceiro contexto
    // CUDA, a carga que derrubou a legenda de 2,46x para 0,45x em 11/09.
    perguntar && ajuste({
      titulo: "Deixar o modelo pronto entre perguntas", sub: true,
      linha: "As perguntas seguintes respondem quase na hora.",
      dica: [
        "A primeira continua levando os mesmos vinte segundos; depois dela o modelo "
        + "fica de pé esperando a próxima.",
        "Em troca, ele segura cerca de 3 GB da placa — é isso que pode fazer a "
        + "legenda atrasar. Desliga sozinho depois de dez minutos sem pergunta e "
        + "quando a gravação para.",
      ],
      custo: "segura ~3 GB",
      controle: chave(config.modelo_quente === true, async (v) => {
        await gravar({ modelo_quente: v });
        recarregar();
      }, "Deixar o modelo pronto entre perguntas"),
    })));

  // ---- notificações
  //
  // Dito no ⓘ porque é a diferença entre um aviso que incomoda e um aviso que
  // salva a reunião — e desligar o primeiro não pode desligar o segundo.
  painel.appendChild(secaoDeAjustes("Notificações",
    ajuste({
      titulo: "Balões da bandeja",
      linha: "Reunião reconhecida, lembrete de microfone mudo.",
      dica: ["Desligar não silencia dispositivo caindo nem disco enchendo: isso "
             + "você precisa saber de qualquer jeito."],
      controle: chave(gravador.notificacoes,
        (v) => aoMexer("notificacoes", { ligado: v }), "Balões da bandeja"),
    })));

  painel.appendChild(secaoDaAgenda(gravador, aoMexer, config, gravar));
  return painel;
}

/** A agenda: a conta, a chave de usar, e os domínios que dizem quem é da casa. */
function secaoDaAgenda(gravador, aoMexer, config, gravar) {
  // ---- os domínios da casa
  //
  // Só os nossos, e não os dos clientes: quem não é da casa é cliente, e essa
  // regra não precisa de manutenção quando aparece um cliente novo. Moram aqui
  // porque é dos e-mails da agenda que eles leem o lado de cada pessoa.
  const dominios = document.createElement("input");
  dominios.className = "aa-entrada";
  dominios.id = "dominios-da-casa";
  dominios.placeholder = "beegol.com, minhaempresa.com.br";
  dominios.value = (config.dominios_da_casa ?? []).join(", ");
  dominios.setAttribute("aria-label", "Domínios da sua organização");
  dominios.addEventListener("change", () => gravar({
    dominios_da_casa: dominios.value.split(",")
      .map((d) => d.trim().replace(/^@/, "").toLowerCase())
      .filter(Boolean),
  }));
  const linhaDominios = ajuste({
    titulo: "Domínios da sua organização",
    linha: "Separados por vírgula. Quem tem outro domínio é cliente.",
    dica: [
      "Os e-mails da agenda dizem de que lado da mesa cada pessoa está. Sem isto, "
      + "a ata deduz pelo assunto — e numa reunião que fala de um cliente o tempo "
      + "todo, alguém da equipe vira gente do cliente.",
      "Quem não tiver e-mail na agenda fica sem lado, e a ata não inventa um.",
    ],
    controle: dominios, largo: true,
  });

  if (!gravador.agenda_configurada) {
    return secaoDeAjustes("Agenda",
      obra("Falta o google_client_secret.json",
        "Ponha o arquivo em %USERPROFILE%\\.meeting-recorder e reabra o app. "
        + "Só aparece em binário montado sem a credencial embutida."),
      linhaDominios);
  }

  const acoes = document.createElement("div");
  acoes.className = "acoes";
  const conectar = document.createElement("button");
  conectar.className = "aa-btn aa-btn-secundario";
  conectar.type = "button";
  conectar.textContent = gravador.conta ? "Trocar de conta…" : "Conectar conta…";
  const conta = ajuste({
    titulo: "Google Calendar",
    linha: gravador.conta ? `Conectado: ${gravador.conta}` : "Nenhuma conta conectada.",
    dica: ["Identifica a reunião que está sendo gravada e usa os convidados como "
           + "vocabulário na transcrição."],
    controle: acoes,
  });
  conectar.addEventListener("click", () => {
    // O navegador abre e a autorização termina fora do app; o resultado chega
    // por balão da bandeja. Recarregar aqui mostraria o estado de antes.
    conta.querySelector(".ajuste__linha").textContent =
      "Abrindo o navegador… conclua a autorização por lá.";
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

  return secaoDeAjustes("Agenda",
    conta,
    ajuste({
      titulo: "Usar esta agenda",
      linha: "Dá nome às gravações pela reunião que está acontecendo.",
      controle: chave(gravador.usar_agenda,
        (v) => aoMexer("usar-agenda", { ligado: v }), "Usar esta agenda"),
    }),
    linhaDominios);
}


// ──────────────────────────────────────────────── cartões de modelo

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
      if (!await confirmar(
            `São ${tamanho(bytes_em_disco)} que voltam a ser baixados se precisar.`,
            { titulo: `Remover ${pacote.nome} do disco?`, ok: "Remover" }))
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

// ───────────────────────────────────────────────────────── aba Modelos

/** O título de um bloco de modelo, com o ⓘ ao lado quando há o que dizer. */
function blocoDeModelo(titulo, texto, ...explicacao) {
  const b = bloco(titulo, texto);
  if (explicacao.length)
    b.querySelector(".bloco__titulo").appendChild(dica(titulo.toLowerCase(), ...explicacao));
  return b;
}

/**
 * Qual modelo separa os falantes — o padrão do app.
 *
 * Mora em Modelos desde o plano 4b (05/10/2026). Antes ficava em Transcrição, e
 * Modelos tinha um bloco que dizia "não há o que escolher" — verdade até o
 * nemotron-3 entrar ao lado do community-1 (MOD-2, 29/09/2026), e mentira
 * desde então.
 *
 * A lista vem do disco (Motores.ModelosDeDiarizacao), não de constante: é o
 * que faz um modelo novo aparecer sozinho no dia em que for empacotado, e o
 * que impede a tela de oferecer o que o motor não sabe carregar.
 */
function blocoDeFalantes(config, gravar, diarizadores) {
  const b = blocoDeModelo("Separar falantes",
    "Vem dentro do app — nada a baixar. Um projeto pode escolher outro em Clientes.",
    "O community-1 separou bem por meses. O nemotron-3 separou melhor nas quatro "
    + "reuniões medidas (metade dos erros), é bem mais rápido e usa menos memória "
    + "da placa; os nomes das vozes conhecidas saem iguais. Ele separa até 8 "
    + "pessoas além de você.");

  if (diarizadores.length === 0) {
    // Dizer a verdade em vez de um seletor vazio: nesta instalação a
    // diarização vai tentar o HuggingFace e falhar.
    b.appendChild(alerta("Nenhum modelo de diarização encontrado nesta instalação. "
      + "Separar falantes não vai funcionar até reinstalar o app.", "atencao"));
    return b;
  }

  const campoSep = campo("Usar por padrão", "select", { opcoes: diarizadores });
  const sel = campoSep.querySelector("select");
  sel.id = "diarizacao-padrao";
  diarizadores.forEach((nome, n) => { sel.options[n].value = nome; });
  sel.value = config.diarizacao_padrao ?? diarizadores[0];
  // Um padrão apontando para pasta que não existe mais não pode sumir em
  // silêncio: ele decide como toda reunião é transcrita.
  if (sel.selectedIndex === -1) {
    const solto = document.createElement("option");
    solto.value = config.diarizacao_padrao;
    solto.textContent = `${config.diarizacao_padrao} (não instalado)`;
    sel.appendChild(solto);
    sel.value = config.diarizacao_padrao;
  }
  sel.addEventListener("change", (e) => gravar({ diarizacao_padrao: e.target.value }));
  b.appendChild(campoSep);
  return b;
}

function abaModelos(catalogo, config, gravar, diarizadores = []) {
  const painel = document.createElement("div");
  painel.className = "painel";

  // O aviso de placa, quando não há placa.
  //
  // A primeira versão instalável só traz o caminho CUDA (docs/FASE4.md §2,
  // decisão 4). Sem NVIDIA o app funciona — o faster-whisper e o llama.cpp caem
  // para CPU sozinhos —, mas uma reunião de uma hora passa a levar horas. Dizer
  // isso aqui é a diferença entre um app lento e um app que parece travado.
  // Escondido até ter o que dizer: vazio, ele ainda levava um vão do painel, e
  // os cartões de Modelos começavam 32 px abaixo dos das outras seções.
  const avisoDePlaca = document.createElement("div");
  avisoDePlaca.hidden = true;
  painel.appendChild(avisoDePlaca);
  diagnostico().then((d) => {
    if (d.placa) return;
    avisoDePlaca.hidden = false;
    avisoDePlaca.appendChild(alerta(
      "Não encontrei placa NVIDIA nesta máquina. O app funciona, mas transcreve "
      + "pela CPU — uma reunião de uma hora pode levar algumas horas. Modelos "
      + "menores (Medium, Small) ajudam bastante nesse caso.", "atencao"));
  }).catch(() => {
    // Sem diagnóstico não se afirma nada: um aviso errado sobre a placa é pior
    // que aviso nenhum.
  });

  // **Dois seletores para a mesma família de modelos, e é de propósito.** A ata
  // roda com a reunião encerrada e a placa livre, e pode pagar um modelo
  // grande; a pergunta divide a placa com a legenda e tem vinte segundos para
  // responder. O estudo de 16/09 escolheu modelos diferentes para cada uma.
  //
  // O segundo não repete os cartões de download: são os mesmos arquivos, e uma
  // segunda lista de botões "baixar" faria parecer que são outros.
  const seletorDaFamilia = (b, familia, chaveConfig, padrao) => {
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
    return itens;
  };

  // O texto mudou na Fase 4, e o motivo é que o comportamento mudou: antes o
  // faster-whisper baixava 3 GB sozinho na primeira transcrição, sem barra.
  const asr = blocoDeModelo("Transcrição", "Transforma áudio em texto.",
    "A transcrição e a ata precisam do modelo baixado antes: o app não começa um "
    + "download de gigabytes no meio de um clique. É aqui que se baixa, com barra "
    + "e tamanho à vista.");
  for (const i of seletorDaFamilia(asr, "asr", "modelo_padrao", "large-v3"))
    asr.appendChild(cartaoDeModelo(i));
  painel.appendChild(asr);

  painel.appendChild(blocoDeFalantes(config, gravar, diarizadores));

  // A família da Fase 3. O valor guardado é o nome do arquivo, e não o id:
  // quem abre o .gguf é o llama.cpp, por caminho. O motor de ata entra dentro
  // do bloco, porque sem ele nenhum destes modelos roda.
  const ata = blocoDeModelo("Ata", "Escreve as atas a partir da transcrição.");
  const itensDeAta = seletorDaFamilia(ata, "ata", "modelo_de_ata", "qwen3-4b-instruct-q4km.gguf");
  ata.appendChild(blocoDoMotorDeAta());
  for (const i of itensDeAta) ata.appendChild(cartaoDeModelo(i));
  painel.appendChild(ata);

  const pergunta = blocoDeModelo("Pergunta durante a reunião",
    "Pode ser menor que o da ata: divide a placa com a legenda.",
    "Aqui o que conta é responder rápido e caber na placa junto com a legenda. "
    + "Usa os mesmos arquivos da ata — baixe acima.");
  // Vazio significa "o mesmo da ata", e é o que o núcleo entende — então o
  // seletor mostra o da ata até alguém escolher outro.
  seletorDaFamilia(pergunta, "ata", "modelo_da_pergunta",
                   config.modelo_de_ata ?? "qwen3-4b-instruct-q4km.gguf");
  painel.appendChild(pergunta);

  // O modelo da legenda ao vivo: sem cartão o modelo não teria como ser
  // baixado, e o motor subiria sem ele.
  const daLegenda = catalogo.filter((i) => i.pacote.familia === "legenda");
  if (daLegenda.length > 0) {
    const b = blocoDeModelo("Legenda ao vivo", "Liga em Ajustes › Gravador.",
      "O modelo que faz o texto aparecer quase no instante da fala. Enquanto não "
      + "for baixado, a chave da legenda não tem efeito.");
    for (const i of daLegenda) b.appendChild(cartaoDeModelo(i));
    painel.appendChild(b);
  }

  return painel;
}

// ─────────────────────────────────────────────────────────── aba Atas

/**
 * O formato da ata (ATA-5) e os modelos de cada tipo, para ver e ajustar
 * (modelos-de-ata.js).
 *
 * Rádio, como o tema: são dois formatos, e nenhum é "desligado".
 */
function abaAtas(config, gravar, tipos) {
  const painel = document.createElement("div");
  painel.className = "painel";

  const OPCOES = [
    ["classico", "De hoje"],
    ["por-tema", "Por tema"],
  ];
  // A mesma regra do núcleo (FormatosDaAta.Ler): ausente é por tema, e um
  // valor desconhecido é o clássico — a tela não pode mostrar um e o app fazer outro.
  const valor = config.formato_da_ata;
  const atual = valor == null || valor === "" || valor === "por-tema" ? "por-tema" : "classico";

  const grupo = document.createElement("div");
  grupo.className = "escolhas";
  grupo.setAttribute("role", "radiogroup");
  grupo.setAttribute("aria-label", "Formato da ata");
  for (const [valor, rotulo] of OPCOES) {
    const label = document.createElement("label");
    label.className = "aa-escolha";
    const radio = document.createElement("input");
    radio.type = "radio";
    radio.name = "formato-da-ata";
    radio.value = valor;
    radio.checked = valor === atual;
    radio.addEventListener("change", () => gravar({ formato_da_ata: valor }));
    label.append(radio, document.createTextNode(rotulo));
    grupo.appendChild(label);
  }

  painel.appendChild(secaoDeAjustes("Formato",
    ajuste({
      titulo: "Formato da ata",
      linha: "Por tema: cada decisão, ação e ponto em aberto dentro do assunto dele.",
      dica: [
        "De hoje: resumo, frentes e, no fim, as listas de decisões, pendências e "
        + "pontos em aberto. Cada tema aparece um pouco em cada lista.",
        "Por tema: um bloco por assunto, com o que foi decidido, quem faz o quê e o "
        + "que ficou em aberto. As observações sobre a transcrição ficam no app e não "
        + "vão no arquivo exportado.",
        "Vale para a próxima ata gerada. Ao gerar de novo, a ata anterior fica "
        + "guardada ao lado, como ata.anterior.md.",
      ],
      controle: grupo,
    }),
  ));
  painel.appendChild(secaoDeModelos(tipos));
  return painel;
}

// ────────────────────────────────────────────────────── aba Transcrição

/**
 * O que acontece na passada do fim, depois de a reunião acabar.
 *
 * Eram treze cartões com dois parágrafos cada (spec §2, item 6); o que roda ao
 * vivo foi para Gravador, o modelo de falantes para Modelos, os domínios para a
 * Agenda, e a explicação medida para trás do ⓘ.
 */
function abaTranscricao(config, gravar) {
  const painel = document.createElement("div");
  painel.className = "painel";

  const fonetica = config.correcao_fonetica !== false;
  painel.appendChild(secaoDeAjustes("Correção do texto",
    ajuste({
      titulo: "Correção fonética",
      linha: "O vocabulário do projeto corrige a grafia: \"Jimmy\" volta a ser \"Dimi\".",
      dica: ["Na transcrição, cada trecho corrigido ganha uma marca ✎, e o filtro no "
             + "topo mostra só os corrigidos. Clicar na marca desfaz a troca — a "
             + "correção é um palpite, e palpite que ninguém confere é um defeito "
             + "esperando."],
      controle: chave(fonetica, (v) => gravar({ correcao_fonetica: v }), "Correção fonética"),
    }),
    // A revisão pela regra não tem chave: ela roda junto com a correção
    // fonética. O que se liga aqui é o SEGUNDO propositor — e a etiqueta diz o
    // preço, porque 640 MB é decisão de quem paga.
    ajuste({
      titulo: "Revisar nomes e siglas com o modelo", sub: true,
      linha: "Acha o que a regra não alcança — \"Cláudio\" que era \"Claude\".",
      dica: [
        "O modelo lê a transcrição e sugere trocas; elas passam pela mesma "
        + "conferência da regra, e cada uma fica marcada com ✎ para você desfazer.",
        "Numa medição com dez erros reais, a regra pegou 3 e o modelo 8. Precisa do "
        + "modelo de ata baixado.",
      ],
      custo: "+640 MB · ~30 s por reunião",
      controle: chave(config.revisao_com_modelo === true,
        (v) => gravar({ revisao_com_modelo: v }), "Revisar nomes e siglas com o modelo"),
    }),
    ajuste({
      titulo: "Descartar fala inventada sobre silêncio",
      linha: "Palavras sobre trechos sem sinal nenhum saem antes da transcrição existir.",
      dica: [
        "Cerca de 5% das palavras que o modelo transcreve caem sobre trechos sem "
        + "sinal nenhum — zeros exatos, não fala baixa. Sobre ausência de som, "
        + "qualquer palavra é invenção.",
        "O critério é severo de propósito (99% de amostras zeradas, e dois terços "
        + "do trecho): fala removida por engano é conteúdo que não volta.",
      ],
      controle: chave(config.filtrar_silencio, (v) => gravar({ filtrar_silencio: v }),
                      "Descartar fala inventada sobre silêncio"),
    })));

  painel.appendChild(secaoDeAjustes("Refazer",
    ajuste({
      titulo: "Mostrar \"Transcrever de novo\"",
      linha: "Refazer descarta os nomes de falante e as correções daquela reunião.",
      perigo: true,
      dica: ["O botão aparece numa reunião já transcrita e reprocessa o áudio "
             + "inteiro. Fica escondido por padrão porque uma reunião de duas horas "
             + "custa meia hora de placa, e refazer apaga o que foi revisado."],
      controle: chave(config.permitir_retranscrever,
        (v) => gravar({ permitir_retranscrever: v }), "Mostrar Transcrever de novo"),
    })));

  // ---- o que quase ninguém muda
  //
  // Fechado por padrão. "Transcrever sem placa" é a exceção para quem não tem
  // NVIDIA (relato de 18/08/2026: a queda para CPU derrubou o Windows); o
  // hotwords está desligado desde 19/08/2026 e existe mais para medir do que
  // para escolher (FASE6 §5). Abre sozinho se algum dos dois estiver ligado —
  // um ajuste fora do padrão não pode ficar escondido.
  const avancado = document.createElement("details");
  avancado.className = "bloco ajustes-secao ajustes-avancado";
  avancado.open = config.permitir_cpu === true || config.usar_hotwords === true;
  const resumo = document.createElement("summary");
  resumo.className = "bloco__titulo";
  resumo.textContent = "Avançado";
  avancado.append(resumo,
    ajuste({
      titulo: "Transcrever sem placa",
      linha: "Só para máquina sem NVIDIA: leva horas e consome muita memória.",
      dica: ["Por padrão o app recusa transcrever quando não encontra a placa de "
             + "vídeo, porque cair para a CPU sem aviso já fez uma transcrição comer "
             + "a memória por horas até derrubar o Windows."],
      controle: chave(config.permitir_cpu === true, (v) => gravar({ permitir_cpu: v }),
                      "Transcrever sem placa"),
    }),
    ajuste({
      titulo: "Vocabulário durante a transcrição", etiqueta: "para medir",
      linha: "Junta a fala em blocos longos; os nomes se recuperam igual sem ele.",
      dica: [
        "Desligado, o vocabulário continua corrigindo a grafia depois. Ligado, ele "
        + "também é sussurrado ao modelo enquanto transcreve — e o modelo junta a "
        + "fala: na mesma reunião, 207 trechos em vez de 787.",
        "Quem falou é decidido por trecho: num trecho de 40 segundos com três "
        + "pessoas, duas somem. As duas medições dizem que os nomes se recuperam "
        + "igual pelos dois caminhos.",
      ],
      custo: "4× mais lento",
      controle: chave(config.usar_hotwords === true, (v) => gravar({ usar_hotwords: v }),
                      "Vocabulário durante a transcrição"),
    }));
  painel.appendChild(avancado);

  return painel;
}


// ──────────────────────────────────────────────────────── aba Clientes
//
// Mora em clientes.js desde o plano 4a do redesenho (mestre-detalhe).

// ─────────────────────────────────────────────────────────── a tela

const ABAS = [
  ["geral", "Geral"],
  ["gravador", "Gravador"],
  ["modelos", "Modelos"],
  ["transcricao", "Transcrição"],
  ["atas", "Atas"],
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

  let config, clientes, catalogo, diarizadores, vozes, parecidos, gravador, gravacoes, tipos,
      dispositivos;
  try {
    // Tudo de uma vez: são cinco leituras baratas e locais, e pedir sob demanda
    // a cada troca de aba faria a aba piscar por nada.
    // As gravações e os tipos de ata só servem a Clientes (as contagens e o
    // tipo padrão do projeto); sem eles, a seção perde isso e o resto vale.
    [{ config }, { clientes }, { catalogo, diarizadores }, { vozes, parecidos },
     { gravador }, { gravacoes }, { tipos }, { dispositivos }] = await Promise.all([
      pedir("config"), pedir("clientes"), pedir("catalogo"), pedir("vozes"),
      pedir("gravador"),
      pedir("gravacoes").catch(() => ({ gravacoes: [] })),
      pedir("modelos-de-ata").catch(() => ({ tipos: [] })),
      // Sem a lista de dispositivos a aba Gravador perde só os seletores.
      pedir("dispositivos").catch(() => ({ dispositivos: null })),
    ]);
  } catch (e) {
    tela.setAttribute("aria-busy", "false");
    tela.replaceChildren(alerta(e.message, "erro"));
    return;
  }

  tela.setAttribute("aria-busy", "false");
  recarregar = () => telaDeAjustes(ctx, atual);

  // No topo e grudado nele: no pé de uma aba longa, quem mexia no primeiro
  // ajuste não via o "salvo" (plano 4b). `role="status"` anuncia sem roubar o
  // foco de quem digita.
  const estado = document.createElement("p");
  estado.className = "campo__dica ajustes__estado";
  estado.setAttribute("role", "status");

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
      gravador: () => abaGravador({ gravador, dispositivos, config }, mexerNoGravador, gravar),
      modelos: () => abaModelos(catalogo, config, gravar, diarizadores ?? []),
      transcricao: () => abaTranscricao(config, gravar),
      atas: () => abaAtas(config, gravar, tipos ?? []),
      clientes: () => abaClientes({
        clientes, catalogo, diarizadores: diarizadores ?? [],
        gravacoes: gravacoes ?? [], tipos: tipos ?? [],
      }, () => recarregar(), estado),
      vozes: () => abaVozes({ vozes, parecidos: parecidos ?? [] }, estado),
    }[atual]();
    conteudo.prepend(estado);
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
      // O trecho de voz que tocava não segue para a outra seção.
      pararAudio();
      desenharPainel();
    });
    colunaAbas.appendChild(botao);
  }

  // A marca e a versão no pé do menu, como na prancha de Ajustes (4a). Do
  // diagnóstico, guardado por sessão, que é de onde o bloco Sobre já as lê.
  const versao = document.createElement("p");
  versao.className = "abas__versao";
  colunaAbas.appendChild(versao);
  diagnostico().then((d) => {
    if (d?.marca && d?.versao) versao.textContent = `${d.marca} ${d.versao}`;
  }).catch(() => {});

  desenharPainel();
  raiz.append(colunaAbas, painel);
  tela.replaceChildren(raiz);
}
