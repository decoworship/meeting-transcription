// As notas da reunião: o mesmo editor, em dois lugares.
//
// No Gravador enquanto se grava — que é quando a nota de fato acontece — e na
// reunião depois, porque quem toma nota corre atrás do que perdeu assim que a
// reunião acaba (FASE3.md §3). Um editor só, para o comportamento de salvar não
// divergir entre as duas telas.
//
// Salva sozinho. Nota de reunião perdida por falta de um clique em "salvar" é a
// falha mais boba que este bloco pode ter.
//
// **Imagens nas notas.** O print do atalho (Ctrl+Alt+P) e do botão "Capturar
// tela", e a imagem que a pessoa cola, arrasta ou anexa, viram arquivo em
// `capturas/` e uma linha `![legenda](capturas/arquivo)` no texto — com o
// momento da reunião na frente quando há gravação correndo. O campo continua
// sendo texto; as imagens aparecem como miniaturas logo abaixo dele.

import { pedir, assinar } from "/ponte.js";
import { momentosDasNotas, imagensDasNotas, urlDaImagem } from "/reuniao-regras.js";
import { verImagem, escolherParticipantes } from "/pecas.js";

/** O mesmo teto do núcleo (Capturas.TamanhoMaximo), para recusar antes de mandar. */
const TAMANHO_MAXIMO = 30 * 1024 * 1024;

/** Duas pastas são a mesma gravação? O caminho pode vir com outra barra ou caixa. */
const mesmaPasta = (a, b) => Boolean(a && b)
  && String(a).replace(/[\\/]+$/, "").replace(/\//g, "\\").toLowerCase()
     === String(b).replace(/[\\/]+$/, "").replace(/\//g, "\\").toLowerCase();

/** O conteúdo de um arquivo em base64, que é como a ponte carrega bytes. */
function base64De(arquivo) {
  return new Promise((resolver, rejeitar) => {
    const leitor = new FileReader();
    leitor.onload = () => resolver(String(leitor.result).split(",")[1] ?? "");
    leitor.onerror = () => rejeitar(leitor.error ?? new Error("não foi possível ler a imagem"));
    leitor.readAsDataURL(arquivo);
  });
}

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
 * @param opcoes.aoTocar `(segundos) => void`. Com ela, as linhas com tempo
 *        marcado (`[00:12:34]`) viram botões que tocam dali — UI-4 do BACKLOG.
 *        Só a reunião a passa: no Gravador a gravação ainda está correndo.
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
    marcar.addEventListener("click", () => marcarMomento());
    acoes.appendChild(marcar);
  }

  // Anexar uma imagem que já está num arquivo. Colar (Ctrl+V) e arrastar para
  // o campo fazem o mesmo, e cobrem o print tirado com Win+Shift+S.
  const seletor = document.createElement("input");
  seletor.type = "file";
  seletor.accept = "image/png,image/jpeg,image/gif,image/webp";
  seletor.hidden = true;
  seletor.addEventListener("change", () => {
    const arquivo = seletor.files?.[0];
    seletor.value = "";
    if (arquivo) inserirImagem(arquivo);
  });
  const anexar = document.createElement("button");
  anexar.className = "aa-btn aa-btn-secundario";
  anexar.type = "button";
  anexar.textContent = "Inserir imagem";
  anexar.title = "Também dá para colar (Ctrl+V) ou arrastar uma imagem para o campo";
  anexar.addEventListener("click", () => escolherImagem());
  acoes.append(anexar, seletor);

  // As imagens que o texto cita, em miniatura. Fora do campo pelo mesmo motivo
  // dos momentos: uma <textarea> não mostra imagem.
  const imagens = document.createElement("div");
  imagens.className = "notas__imagens";
  imagens.setAttribute("aria-label", "Imagens das notas");
  imagens.hidden = true;

  raiz.append(topo, campo, acoes, imagens);

  /**
   * Põe o tempo de agora onde está o cursor.
   *
   * @param focar leva o cursor ao campo depois. O Gravador passa `false` quando
   *   a aba de Notas não está à vista: tirar o foco de quem digita uma pergunta
   *   para pôr num campo escondido é o F-2 da FASE7-FRONTEND.
   */
  function marcarMomento({ focar = true } = {}) {
    if (!opcoes.tempo || campo.disabled) return false;
    const marca = `\n[${relogio(opcoes.tempo())}] `;
    const pos = campo.selectionStart ?? campo.value.length;
    campo.value = campo.value.slice(0, pos) + marca + campo.value.slice(pos);
    if (focar) campo.focus();
    campo.selectionStart = campo.selectionEnd = pos + marca.length;
    agendar();
    return true;
  }

  /** Abre o seletor de arquivo. O Gravador chama do rodapé dele. */
  function escolherImagem() {
    if (!alvo || campo.disabled) return;
    seletor.click();
  }

  /**
   * Guarda a imagem na pasta da gravação e põe a linha dela onde está o
   * cursor, numa linha própria.
   */
  async function inserirImagem(arquivo) {
    if (!alvo || campo.disabled) return;
    if (arquivo.size > TAMANHO_MAXIMO) {
      estado.textContent = "imagem grande demais (o limite é 30 MB)";
      return;
    }
    // O cursor de agora, não o de depois do disco: quem colou continua
    // digitando enquanto a imagem é guardada.
    const pos = campo.selectionStart ?? campo.value.length;
    const textoAntes = campo.value;
    estado.textContent = "guardando a imagem…";
    try {
      const conteudo = await base64De(arquivo);
      const r = await pedir("salvar-imagem", {
        gravacao: alvo, conteudo,
        ...(opcoes.tempo ? { segundos: opcoes.tempo() } : {}),
      });
      // Se o texto mudou enquanto se esperava, a imagem vai para o fim: o
      // cursor guardado já não aponta para o mesmo lugar.
      inserirLinha(r.captura.linha, campo.value === textoAntes ? pos : campo.value.length);
    } catch (e) {
      estado.textContent = `a imagem não entrou: ${e.message}`;
    }
  }

  function inserirLinha(linha, pos) {
    const antes = campo.value.slice(0, pos);
    const depois = campo.value.slice(pos);
    const abre = antes && !antes.endsWith("\n") ? "\n" : "";
    const fecha = depois && !depois.startsWith("\n") ? "\n" : "";
    campo.value = antes + abre + linha + fecha + depois;
    const fim = (antes + abre + linha).length;
    campo.selectionStart = campo.selectionEnd = fim;
    aoEditar();
    agendar();
  }

  campo.addEventListener("paste", (e) => {
    const item = [...(e.clipboardData?.items ?? [])]
      .find((i) => i.kind === "file" && i.type.startsWith("image/"));
    const arquivo = item?.getAsFile();
    if (!arquivo) return;          // texto colado segue o caminho normal
    e.preventDefault();
    inserirImagem(arquivo);
  });
  // Sem o preventDefault no dragover, o WebView2 trata o arquivo solto como
  // navegação e troca a página do app pela imagem.
  campo.addEventListener("dragover", (e) => {
    if ([...(e.dataTransfer?.items ?? [])].some((i) => i.kind === "file")) e.preventDefault();
  });
  campo.addEventListener("drop", (e) => {
    const arquivo = [...(e.dataTransfer?.files ?? [])].find((f) => f.type.startsWith("image/"));
    if (!arquivo) return;
    e.preventDefault();
    inserirImagem(arquivo);
  });

  /**
   * Os nomes de um print viram os participantes da reunião (UI-12): o motor de
   * OCR lê, a pessoa confirma, e eles passam a ser as sugestões de nome dos
   * falantes na revisão.
   */
  async function lerNomes(imagem) {
    const gravacao = alvo;
    const nomes = await escolherParticipantes(
      () => pedir("ler-nomes", { gravacao, texto: imagem.caminho }));
    if (!nomes?.length) return;
    try {
      const r = await pedir("salvar-participantes", { gravacao, nomes });
      const n = r.participantes?.length ?? nomes.length;
      estado.textContent = `${n} ${n === 1 ? "participante guardado" : "participantes guardados"}`;
    } catch (e) {
      estado.textContent = `os participantes não foram guardados: ${e.message}`;
    }
  }

  /** A imagem grande, com "ouvir daqui" quando ela tem momento e a tela toca. */
  function ampliar(imagem) {
    const acoesDaImagem = [{ rotulo: "Ler nomes desta imagem", fazer: () => lerNomes(imagem) }];
    if (imagem.segundos != null && opcoes.aoTocar)
      acoesDaImagem.push({ rotulo: `Ouvir a partir de ${imagem.marca}`, fazer: () => opcoes.aoTocar(imagem.segundos) });
    const linha = campo.value.split("\n").find((l) => l.includes(`(${imagem.caminho})`)) ?? "";
    const legenda = linha.replace(/!\[[^\]]*\]\([^)]*\)/g, "").replace(/\[\d{1,2}:\d{2}:\d{2}\]/, "").trim();
    verImagem(urlDaImagem(alvo, imagem.caminho),
              imagem.marca ? `Imagem em ${imagem.marca}` : "Imagem das notas",
              { legenda, acoes: acoesDaImagem });
  }

  function miniatura(imagem, classe) {
    const img = document.createElement("img");
    img.className = classe;
    img.src = urlDaImagem(alvo, imagem.caminho);
    img.alt = imagem.legenda || "imagem";
    img.loading = "lazy";
    img.decoding = "async";
    return img;
  }

  // Refeitas só quando a lista muda, e não a cada tecla: refazer um <img> é
  // recarregá-lo, e a faixa piscaria enquanto se digita.
  let imagensDesenhadas = "";
  function desenharImagens() {
    const lista = alvo ? imagensDasNotas(campo.value) : [];
    const chave = `${alvo}|${lista.map((i) => i.caminho).join("|")}`;
    if (chave === imagensDesenhadas) return;
    imagensDesenhadas = chave;
    imagens.replaceChildren(...lista.map((imagem) => {
      const b = document.createElement("button");
      b.type = "button";
      b.className = "notas__imagem";
      b.title = imagem.marca ? `Ver a imagem de ${imagem.marca}` : "Ver a imagem";
      b.appendChild(miniatura(imagem, "notas__miniatura"));
      if (imagem.marca) {
        const marca = document.createElement("span");
        marca.className = "notas__marca";
        marca.textContent = imagem.marca;
        b.appendChild(marca);
      }
      b.addEventListener("click", () => ampliar(imagem));
      return b;
    }));
    imagens.hidden = lista.length === 0;
  }

  /** O que muda com o texto, fora o salvar. */
  function aoEditar() {
    desenharMomentos();
    desenharImagens();
  }
  campo.addEventListener("input", desenharImagens);

  // O print do atalho e do botão chega por evento: o núcleo já pôs a linha no
  // notas.md, e o campo aberto precisa tê-la também — senão o próximo
  // salvamento da tela, que grava o campo inteiro, a apagaria do disco.
  let jaConectou = false;
  const cancelarCaptura = assinar("captura", (r) => {
    if (raiz.isConnected) jaConectou = true;
    else if (jaConectou) { cancelarCaptura(); return; }

    if (r.erro) {
      if (opcoes.tempo) estado.textContent = r.erro;
      return;
    }
    const c = r.captura;
    if (!c || !mesmaPasta(c.gravacao, alvo) || !carregado) return;
    if (!campo.value.includes(c.linha)) {
      const texto = campo.value;
      campo.value = texto + (texto && !texto.endsWith("\n") ? "\n" : "") + c.linha;
      aoEditar();
      salvar();
    }
  });

  // Os momentos marcados, como botões que tocam dali (UI-4). Uma lista ao lado
  // do campo, e não dentro dele — uma <textarea> não tem botão —, refeita a
  // cada tecla sem tocar no campo: quem escreve não perde o cursor (F-2).
  let momentos = null;
  function desenharMomentos() {
    if (!momentos) return;
    const botoes = momentosDasNotas(campo.value).map((m) => {
      const b = document.createElement("button");
      b.type = "button";
      b.className = "notas__momento";
      const marca = document.createElement("span");
      marca.className = "notas__marca";
      marca.textContent = m.marca;
      b.append(marca);
      if (m.imagem) b.append(" ", miniatura({ caminho: m.imagem, legenda: m.texto }, "notas__momento-imagem"));
      b.append(document.createTextNode(m.texto ? ` ${m.texto}` : m.imagem ? " imagem" : ""));
      b.title = `Ouvir a partir de ${m.marca}`;
      b.addEventListener("click", () => opcoes.aoTocar(m.segundos));
      return b;
    });
    momentos.replaceChildren(...botoes);
    momentos.hidden = botoes.length === 0;
  }
  if (opcoes.aoTocar) {
    momentos = document.createElement("div");
    momentos.className = "notas__momentos";
    momentos.setAttribute("aria-label", "Momentos marcados");
    momentos.hidden = true;
    raiz.appendChild(momentos);
    campo.addEventListener("input", desenharMomentos);
  }

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
      aoEditar();
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
    marcarMomento,
    escolherImagem,
    /** A faixa de miniaturas, para o Gravador pô-la fora da grade do campo. */
    imagens,
    /** Grava agora, sem esperar o atraso. Para quem está saindo da tela. */
    salvarAgora: salvar,
    definirHabilitado(ligado, aviso = "") {
      campo.disabled = !ligado;
      if (marcar) marcar.disabled = !ligado;
      anexar.disabled = !ligado;
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
    aoEditar();
    if (caminho) await carregar(caminho);
  }
}
