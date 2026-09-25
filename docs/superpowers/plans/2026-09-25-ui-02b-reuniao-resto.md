# Plano 2b — a reunião aberta, o resto

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Fechar a reunião aberta do §3.3 do spec: Falantes e Exportar no cabeçalho, um tocador fixo no pé, o tempo marcado na nota tocando dali (`UI-4`), e o preparo curto — vocabulário como etiquetas, motor dobrado numa linha, andamento no topo e o formulário virando resumo enquanto transcreve.

**Architecture:** Tudo é página (`app-net/App/web/`); o núcleo C# não muda. As regras puras (termos, momentos das notas, etapas, relógio do tocador, resumo do motor) moram num módulo novo `reuniao-regras.js`, provado por `node --test`. O tocador é um módulo novo (`tocador.js`) que dono do `<audio>` único da página só para a reunião; revisão e notas tocam por ele. O preparo sai do `app.js` para `preparo.js` antes de ser redesenhado, e o campo de etiquetas é um módulo próprio (`etiquetas.js`).

**Tech Stack:** JavaScript ES modules no WebView2, CSS com os tokens do AA Design System, Playwright (Python) para as provas de tela, `node --test` para as regras.

**Spec:** `docs/superpowers/specs/2026-09-23-ui-ux.md` — §3.3 ("A reunião aberta", "Antes de transcrever"), §4 (regras de todos os planos), §5 linha 2b. Backlog: `docs/BACKLOG.md` `UI-4`.

## Global Constraints

Do §4 do spec, valendo para toda tarefa:

- **CSP da página: `style-src 'self'` sem `'unsafe-inline'`.** Atributo `style` no HTML é ignorado em silêncio. Estilo é classe no `app.css`; valor dinâmico é `elemento.style.x = …` pelo CSSOM.
- **Nunca `innerHTML` com texto que veio do disco ou do modelo.** Monta-se nó com `textContent`.
- **Só tokens do design system** (`var(--cor-*)`, `--espaco-*`, `--raio-*`, `--texto-*`) no `app.css`; nenhum hexadecimal fora dos blocos de token que já existem.
- **O `data-tema="claro"` do `index.html` é procurado por texto exato** pelo núcleo; não se mexe nele.
- **Anúncio é de estado, nunca de conteúdo** (`anunciar()` de `pecas.js`).
- **Redesenhar não pode tirar o foco de quem digita** (F-2). Redesenhar a lista de momentos ou as etiquetas nunca recria o campo onde se digita.
- **Tela se prova no navegador** (`tools/provar_reunioes.py`, ponte falsa); **regra pura com `node --test` em `tools/web/`**.
- **Arquivo novo em `app-net/App/web/` entra no executável sozinho**; teste nunca mora lá.
- **Texto em português, na voz do app**: "gravação", "reunião", "ata", "transcrição", "convidados".
- **Publicar só pelo `tools/publicar.sh`**, com o app fechado pela bandeja.
- **Nada aqui toca em gravação.**

Comandos de prova (rodar da raiz do worktree):

```bash
node --test tools/web/*.test.mjs                                     # regras
uv run --with playwright python tools/provar_reunioes.py            # telas
.venv/bin/python tools/checar_transcricao.py                        # transcrição entre telas
export PATH="$HOME/.dotnet:$PATH"; dotnet test app-net/Tests/MeetingApp.Tests.csproj   # o núcleo (embute web/)
```

## Review Focus

1. **Tocar enquanto se está na aba Notas e trocar de reunião** — o áudio da reunião anterior tem de parar e o tocador da nova começa zerado, sem ouvir eventos do `<audio>` pela reunião que saiu (ouvinte vazado repinta um tocador desconectado ou, pior, o da reunião nova com o tempo da velha). Provado em `prova_tocador_troca_de_reuniao` (Task 4).
2. **Vocabulário antigo em prosa ou com quebra de linha** — `initial_prompt` gravado antes das etiquetas pode ter `\n`, `;` ou termos repetidos com caixa diferente; tem de virar etiquetas sem perder termo e sem duplicar, e voltar ao disco como `", "`. Provado em `reuniao-regras.test.mjs` (Task 1) e `prova_preparo_vocabulario` (Task 7).
3. **Colar "a, b, c" no campo de etiquetas** — vira três etiquetas, e o cursor fica no campo. Provado em `prova_preparo_vocabulario` (Task 7).
4. **Transcrição cancelada ou com erro** — o formulário volta a ser editável e o resumo some; um formulário travado depois de um erro deixa a pessoa sem como tentar de novo. Provado em `prova_preparo_andamento` (Task 8).
5. **Abrir a reunião direto na aba Ata e clicar em Falantes** — a gaveta tem de mostrar os falantes desta reunião, e não os da reunião aberta antes (o `estado` da revisão é de módulo). Provado em `prova_reuniao_cabecalho` (Task 2).

---

## Mapa de arquivos

| arquivo | o que muda |
|---|---|
| `app-net/App/web/reuniao-regras.js` | **novo** — regras puras |
| `tools/web/reuniao-regras.test.mjs` | **novo** — as provas delas |
| `app-net/App/web/reuniao.js` | Falantes/Exportar na barra; transcrição montada junto; tocador no pé; notas com `aoTocar` |
| `app-net/App/web/revisao.js` | sai Falantes/Exportar/⏸ das ferramentas; `salvar` preso à reunião; toca pelo tocador |
| `app-net/App/web/tocador.js` | **novo** — `ouvir()` e `tocadorDaReuniao()` |
| `app-net/App/web/notas.js` | opção `aoTocar`: os momentos marcados viram botões |
| `app-net/App/web/preparo.js` | **novo** — o preparo sai do `app.js`, e é redesenhado aqui |
| `app-net/App/web/etiquetas.js` | **novo** — o campo de etiquetas |
| `app-net/App/web/app.js` | perde o preparo; a barra esvazia nos pontos de navegação |
| `app-net/App/web/app.css` | tocador, momentos, etiquetas, motor dobrado, andamento, resumo |
| `tools/provar_reunioes.py` | ponte falsa ganha ganchos; provas novas |
| `tools/checar_transcricao.py` | lê o vocabulário das etiquetas |
| `docs/superpowers/specs/2026-09-23-ui-ux.md`, `docs/BACKLOG.md` | 2b feito, `UI-4` feito |

---

### Task 1: As regras puras

**Files:**
- Create: `app-net/App/web/reuniao-regras.js`
- Test: `tools/web/reuniao-regras.test.mjs`

**Interfaces:**
- Produces (todas exportadas de `/reuniao-regras.js`):
  - `separarTermos(texto: string|null) → string[]` — separa por `,`, `;` ou quebra de linha, tira espaço das pontas, descarta vazio, e tira repetido sem olhar caixa (fica a primeira grafia).
  - `juntarTermos(lista: string[]) → string` — `lista.join(", ")`.
  - `momentosDasNotas(texto: string|null) → {segundos:number, marca:string, texto:string}[]` — a primeira marca `[hh:mm:ss]` de cada linha; `marca` sem colchetes; `texto` é a linha sem a marca, aparada.
  - `ETAPAS: [id, rotulo][]` — as quatro, na ordem: `mix`, `asr`, `diarizacao`, `montagem`.
  - `estadoDasEtapas(atual: string|null) → {id, rotulo, estado: "feita"|"atual"|"pendente"}[]`.
  - `relogioDoTocador(segundos: number) → string` — `"mm:ss"` abaixo de uma hora, `"h:mm:ss"` a partir dela; não-número ou negativo é `"00:00"`.
  - `resumoDoMotor({modelo, idioma, diarizar}) → string` — `"large-v3 · pt · separa os falantes"`; idioma vazio vira `"idioma automático"`; `diarizar` falso vira `"sem separar falantes"`.

- [ ] **Step 1: Write the failing test**

`tools/web/reuniao-regras.test.mjs`:

```js
// As regras da reunião aberta e do preparo, fora do navegador.
//
//     node --test tools/web/reuniao-regras.test.mjs
//
// Ver docs/superpowers/specs/2026-09-23-ui-ux.md §3.3 e o plano 2b.

import { test } from "node:test";
import assert from "node:assert/strict";

const R = await import(new URL("../../app-net/App/web/reuniao-regras.js", import.meta.url));

test("separarTermos aceita vírgula, ponto e vírgula e quebra de linha", () => {
  assert.deepEqual(R.separarTermos("Beegol, NOC;Sherlock\nCCIL"), ["Beegol", "NOC", "Sherlock", "CCIL"]);
});

test("separarTermos descarta vazio e repetido, e fica a primeira grafia", () => {
  assert.deepEqual(R.separarTermos(" NOC ,, noc, Noc ,\n\n Algar "), ["NOC", "Algar"]);
  assert.deepEqual(R.separarTermos(""), []);
  assert.deepEqual(R.separarTermos(null), []);
});

test("juntarTermos é o formato que o motor e o disco sempre leram", () => {
  assert.equal(R.juntarTermos(["Beegol", "NOC"]), "Beegol, NOC");
  assert.equal(R.juntarTermos([]), "");
  // Ida e volta: o vocabulário gravado antes das etiquetas não perde termo.
  assert.equal(R.juntarTermos(R.separarTermos("Beegol,NOC\nCCIL")), "Beegol, NOC, CCIL");
});

test("momentosDasNotas acha a marca de cada linha, e a linha sem ela é o texto", () => {
  const m = R.momentosDasNotas("abertura\n[00:12:34] adiar o piloto\n  [01:02:03]   decidir o NOC ");
  assert.deepEqual(m, [
    { segundos: 754, marca: "00:12:34", texto: "adiar o piloto" },
    { segundos: 3723, marca: "01:02:03", texto: "decidir o NOC" },
  ]);
});

test("momentosDasNotas aceita a marca no meio da linha, e só a primeira conta", () => {
  assert.deepEqual(R.momentosDasNotas("Carol [00:00:04] e [00:00:09]"),
                   [{ segundos: 4, marca: "00:00:04", texto: "Carol  e [00:00:09]" }]);
});

test("momentosDasNotas ignora o que não é relógio", () => {
  assert.deepEqual(R.momentosDasNotas("[00:61:00] não\n[12:34] também não\n[aa:bb:cc]"), []);
  assert.deepEqual(R.momentosDasNotas(null), []);
});

test("estadoDasEtapas marca feitas, a atual e as pendentes", () => {
  assert.deepEqual(R.estadoDasEtapas("diarizacao").map((e) => e.estado),
                   ["feita", "feita", "atual", "pendente"]);
  assert.deepEqual(R.estadoDasEtapas("mix").map((e) => e.estado),
                   ["atual", "pendente", "pendente", "pendente"]);
  assert.deepEqual(R.estadoDasEtapas(null).map((e) => e.estado),
                   ["pendente", "pendente", "pendente", "pendente"]);
  assert.deepEqual(R.estadoDasEtapas("desconhecida").map((e) => e.estado),
                   ["pendente", "pendente", "pendente", "pendente"]);
  assert.deepEqual(R.ETAPAS.map(([id]) => id), ["mix", "asr", "diarizacao", "montagem"]);
  assert.equal(R.estadoDasEtapas("asr")[1].rotulo, "Transcrevendo");
});

test("relogioDoTocador", () => {
  assert.equal(R.relogioDoTocador(0), "00:00");
  assert.equal(R.relogioDoTocador(4.9), "00:04");
  assert.equal(R.relogioDoTocador(900), "15:00");
  assert.equal(R.relogioDoTocador(3723), "1:02:03");
  assert.equal(R.relogioDoTocador(NaN), "00:00");
  assert.equal(R.relogioDoTocador(-3), "00:00");
});

test("resumoDoMotor diz numa linha o que as opções dobradas escondem", () => {
  assert.equal(R.resumoDoMotor({ modelo: "large-v3", idioma: "pt", diarizar: true }),
               "large-v3 · pt · separa os falantes");
  assert.equal(R.resumoDoMotor({ modelo: "small", idioma: " ", diarizar: false }),
               "small · idioma automático · sem separar falantes");
});
```

- [ ] **Step 2: Run test to verify it fails**

Run: `node --test tools/web/reuniao-regras.test.mjs`
Expected: FAIL — `Cannot find module …/reuniao-regras.js`.

- [ ] **Step 3: Write minimal implementation**

`app-net/App/web/reuniao-regras.js`:

```js
// As regras da reunião aberta e do preparo, sem DOM — para o `node --test`
// alcançá-las (tools/web/reuniao-regras.test.mjs). Ver o plano 2b,
// docs/superpowers/plans/2026-09-25-ui-02b-reuniao-resto.md.

/**
 * O vocabulário gravado, em termos.
 *
 * O `initial_prompt` do projeto sempre foi texto livre separado por vírgula,
 * mas quem digitava na caixa antiga também quebrava linha. Os três separadores
 * valem, e o repetido sai sem olhar caixa: "NOC" e "noc" são o mesmo termo para
 * quem lê a lista, e a primeira grafia é a que a pessoa escolheu.
 */
export function separarTermos(texto) {
  const vistos = new Set();
  const termos = [];
  for (const cru of String(texto ?? "").split(/[,;\n]/)) {
    const t = cru.trim();
    if (!t) continue;
    const chave = t.toLocaleLowerCase("pt-BR");
    if (vistos.has(chave)) continue;
    vistos.add(chave);
    termos.push(t);
  }
  return termos;
}

/** De volta ao formato que o motor e o disco sempre leram. */
export const juntarTermos = (lista) => lista.join(", ");

const MARCA = /\[(\d{1,2}):(\d{2}):(\d{2})\]/;

/**
 * Os momentos marcados nas notas (`UI-4`).
 *
 * "Marcar momento" do Gravador escreve `[hh:mm:ss]` com o tempo decorrido da
 * gravação, que é o mesmo relógio do mix. Uma marca por linha — a primeira —,
 * porque o botão é a linha: duas marcas na mesma linha são uma nota que cita
 * outro momento, e não dois momentos.
 */
export function momentosDasNotas(texto) {
  const momentos = [];
  for (const linha of String(texto ?? "").split("\n")) {
    const m = linha.match(MARCA);
    if (!m) continue;
    const [h, min, s] = [m[1], m[2], m[3]].map(Number);
    if (min > 59 || s > 59) continue;
    momentos.push({
      segundos: h * 3600 + min * 60 + s,
      marca: m[0].slice(1, -1),
      texto: (linha.slice(0, m.index) + linha.slice(m.index + m[0].length)).trim(),
    });
  }
  return momentos;
}

/** As etapas do pipeline, na ordem em que o núcleo as percorre. */
export const ETAPAS = [
  ["mix", "Somando as faixas"],
  ["asr", "Transcrevendo"],
  ["diarizacao", "Separando os falantes"],
  ["montagem", "Montando o resultado"],
];

/** Onde a transcrição está, etapa a etapa. Etapa desconhecida não acende nada. */
export function estadoDasEtapas(atual) {
  const i = ETAPAS.findIndex(([id]) => id === atual);
  return ETAPAS.map(([id, rotulo], j) => ({
    id, rotulo,
    estado: i < 0 ? "pendente" : j < i ? "feita" : j === i ? "atual" : "pendente",
  }));
}

/** "04:12", ou "1:02:03" a partir de uma hora — o relógio do tocador. */
export function relogioDoTocador(segundos) {
  const total = Number.isFinite(segundos) && segundos > 0 ? Math.floor(segundos) : 0;
  const h = Math.floor(total / 3600);
  const p = (n) => String(n).padStart(2, "0");
  const ms = `${p(Math.floor((total % 3600) / 60))}:${p(total % 60)}`;
  return h > 0 ? `${h}:${ms}` : ms;
}

/** A linha que diz o que as opções do motor, dobradas, estão escolhendo. */
export function resumoDoMotor({ modelo, idioma, diarizar }) {
  return [
    modelo,
    idioma?.trim() || "idioma automático",
    diarizar ? "separa os falantes" : "sem separar falantes",
  ].join(" · ");
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `node --test tools/web/*.test.mjs`
Expected: PASS — as provas novas e as 27 de `reunioes-regras.test.mjs`, nenhuma falha.

- [ ] **Step 5: Commit**

```bash
git add app-net/App/web/reuniao-regras.js tools/web/reuniao-regras.test.mjs
git commit -m "feat(reuniao): as regras puras do plano 2b, provadas fora do navegador"
```

---

### Task 2: Falantes e Exportar no cabeçalho; a barra esvazia em toda navegação

**Files:**
- Modify: `app-net/App/web/reuniao.js` (montagem das abas, barra)
- Modify: `app-net/App/web/revisao.js` (ferramentas, ~linhas 130-185)
- Modify: `app-net/App/web/app.js` (`acoesDaBarra`, `telaDeLista`, `abrirGravacao`, `telaDePreparo`, `abrirAjustes`, `abrirGravador`)
- Test: `tools/provar_reunioes.py`

**Interfaces:**
- Consumes: `abrirPainel(qual)` de `/revisao.js` (já existe: `"falantes"`, `"exportar"`); `acoesDaBarra(...nos)` de `/app.js`.
- Produces: na barra do topo da reunião aberta, dois botões `#acoes-da-barra [data-acao='falantes']` e `[data-acao='exportar']`. A aba Transcrição é **sempre montada** na abertura da reunião (escondida se outra aba foi pedida). Ganchos da ponte falsa, usados pelas Tasks 3-8: `window.__pedidos` (todo pedido recebido, na ordem), `window.__config`, `window.__notas`, `window.__prefs`, `window.__vinculo`, e `play`/`pause` de mídia registrados em `window.__tocou`.

**Por que a transcrição monta junto:** a gaveta de falantes lê o `estado` de módulo da revisão. Com a reunião aberta direto na Ata, o `estado` ainda é o da reunião aberta antes — e renomear ali gravaria nomes na reunião errada. A lista é virtual (`lista-de-trechos.js`, 80 por leva), então montar escondida custa pouco.

- [ ] **Step 1: Os ganchos da ponte falsa**

Em `tools/provar_reunioes.py`, `PONTE_FALSA`, trocar estas linhas do `switch`:

```js
      case "config": return { config: window.__config ?? {} };
      case "reuniao": return window.__vinculo ?? { cliente: "", projeto: "" };
      case "prefs": return { prefs: window.__prefs ?? null };
      case "notas": return { notas: window.__notas ?? "" };
```

No `postMessage`, registrar o pedido antes de responder:

```js
    postMessage(cru) {
      const q = JSON.parse(cru);
      window.__pedidos.push(q);
      const r = Object.assign({ id: q.id }, responder(q));
      setTimeout(() => { for (const f of this._ouvintes) f({ data: JSON.stringify(r) }); }, 0);
    },
```

E, antes de `window.__gravacoes = gravacoes;`:

```js
  window.__pedidos = [];
  // O Chromium sem janela não toca o mix (o endereço gravacoes.local não
  // existe aqui). A prova quer saber o que se pediu para tocar: de onde, e a
  // partir de quando. Os eventos são os que o <audio> de verdade dispara.
  HTMLMediaElement.prototype.play = function () {
    window.__tocou = { src: this.getAttribute("src"), t: this.currentTime };
    this.dispatchEvent(new Event("play"));
    return Promise.resolve();
  };
  HTMLMediaElement.prototype.pause = function () { this.dispatchEvent(new Event("pause")); };
```

- [ ] **Step 2: Write the failing tests**

Em `tools/provar_reunioes.py`, depois de `prova_reuniao_teclado`:

```python
def prova_reuniao_cabecalho(pagina) -> None:
    abrir_reuniao(pagina, "Comunicação")
    na_barra = pagina.eval_on_selector_all("#acoes-da-barra [data-acao]", "els => els.map((e) => e.dataset.acao)")
    conferir(na_barra == ["falantes", "exportar"], f"Falantes e Exportar moram na barra do topo ({na_barra})")
    nas_ferramentas = pagina.eval_on_selector_all(
        ".revisao .ferramentas button",
        "els => els.filter((e) => ['Falantes', 'Exportar', '⏸'].includes(e.textContent.trim())).length")
    conferir(nas_ferramentas == 0, "e saíram das ferramentas da revisão")
    pagina.click("#acoes-da-barra [data-acao='exportar']")
    conferir(pagina.is_visible("#gaveta-exportar"), "Exportar abre a gaveta de exportar")


def prova_reuniao_falantes_da_ata(pagina) -> None:
    # Abre outra reunião antes, para o estado da revisão ser o de outra reunião.
    pagina.evaluate("() => { window.__transcricao = JSON.stringify({ language: 'pt', segments: ["
                    "{ start: 0, end: 3, text: ' Outra.', speaker: 'Zé da Outra' }] }); }")
    abrir_reuniao(pagina, "Sherlock")
    pagina.click("#voltar")
    pagina.wait_for_selector(".reuniao-linha", timeout=5000)
    pagina.evaluate("() => { window.__transcricao = null; }")
    linha_por_titulo(pagina, "Comunicação")
    pagina.evaluate("() => window.__linha.click()")
    pagina.click(".reunioes__painel [data-acao='abrir-ata']")
    pagina.wait_for_selector("#painel-ata .ata__texto", timeout=5000)
    pagina.click("#acoes-da-barra [data-acao='falantes']")
    pagina.wait_for_selector("#gaveta-falantes .tabela-falantes", timeout=5000)
    quem = pagina.eval_on_selector_all("#gaveta-falantes .tabela-falantes tr td:first-child",
                                       "els => els.map((e) => e.textContent)")
    conferir(quem == ["André", "Carol"], f"aberta na Ata, Falantes mostra os falantes DESTA reunião ({quem})")


def prova_barra_esvazia_no_preparo(pagina) -> None:
    # Transcrever de novo leva ao preparo com o MESMO título: a troca de título
    # não esvazia a barra, e Falantes ficaria lá, abrindo a gaveta de nada.
    pagina.evaluate("() => { window.__config = { permitir_retranscrever: true }; }")
    abrir_reuniao(pagina, "Comunicação")
    pagina.click(".ferramentas__perigo >> text=Transcrever de novo")
    pagina.click(".aa-btn-primario:has-text('Transcrever de novo')")
    pagina.wait_for_selector("#vocabulario", timeout=5000)
    sobra = pagina.eval_on_selector_all("#acoes-da-barra > *", "els => els.length")
    conferir(sobra == 0, f"no preparo, a barra do topo não tem o que era da reunião ({sobra})")
```

E acrescentar `prova_reuniao_cabecalho, prova_reuniao_falantes_da_ata, prova_barra_esvazia_no_preparo` à lista `PROVAS`.

- [ ] **Step 3: Run tests to verify they fail**

Run: `uv run --with playwright python tools/provar_reunioes.py --so prova_reuniao_cabecalho,prova_reuniao_falantes_da_ata,prova_barra_esvazia_no_preparo`
Expected: FAIL — `prova_reuniao_cabecalho estourou` ou `FALHA  Falantes e Exportar moram na barra do topo ([])`; as outras duas estouram esperando `[data-acao='falantes']` / conferem uma barra não vazia.

- [ ] **Step 4: Tirar os três botões da revisão**

Em `app-net/App/web/revisao.js`, apagar a criação de `botaoFalantes`, `botaoExportar` e `parar` (o `⏸`; o tocador da Task 4 o substitui), e trocar a linha do `append`:

```js
  // Falantes e Exportar moram na barra do topo desde o plano 2b: são da reunião,
  // e não da aba (reuniao.js). O ⏸ solto saiu junto — o tocador no pé da
  // reunião é quem mostra que há áudio tocando (tocador.js).
  ferramentas.append(busca, estadoSalvo);
```

Tirar também o import de `pararAudio` **só se** ele deixar de ser usado no arquivo (ele continua em `telaDeRevisao`; conferir com `grep -n pararAudio app-net/App/web/revisao.js`).

- [ ] **Step 5: Os botões na barra, e a transcrição montada junto**

Em `app-net/App/web/reuniao.js`, trocar o import do `app.js` e acrescentar a barra logo depois do `cabecalho(...)`:

```js
import { duracao, quando, tituloDe, acoesDaBarra } from "/app.js";
```

```js
  // Falantes e Exportar são da reunião, e não de uma aba: nomear quem falou
  // serve à ata e às notas também, e exportar leva a transcrição inteira.
  // Os painéis continuam sendo da revisão (revisao.js), que é quem sabe os nomes.
  const botaoDaBarra = (acao, rotulo, classe) => {
    const b = document.createElement("button");
    b.type = "button";
    b.className = `aa-btn ${classe}`;
    b.dataset.acao = acao;
    b.textContent = rotulo;
    b.addEventListener("click", () => abrirPainelDaRevisao(acao));
    return b;
  };
  acoesDaBarra(botaoDaBarra("falantes", "Falantes", "aa-btn-secundario"),
               botaoDaBarra("exportar", "Exportar", "aa-btn-primario"));
```

E, logo antes de `mostrarAba = mostrar;`:

```js
  // **A transcrição monta sempre**, mesmo aberta noutra aba. A gaveta de
  // falantes lê o estado da revisão, que é de módulo: sem isto, a reunião aberta
  // direto na Ata mostrava — e renomeava — os falantes da reunião aberta antes.
  // A lista é virtual, e montar escondida custa uma leva de 80 linhas.
  const t = abas.get("transcricao");
  t.montada = true;
  montar.transcricao(t.painel);
```

- [ ] **Step 6: A barra esvazia em toda navegação**

Em `app-net/App/web/app.js`, a barra deixa de depender da troca de título. Trocar o comentário de `acoesDaBarra` e o trecho de `cabecalho`:

```js
/**
 * Põe na barra do topo, à direita, o que é da tela — e tira o que havia.
 *
 * Todo ponto de navegação a esvazia (`navegar`), e a tela que chega põe o seu.
 * Esvaziar pela troca de título falhava na única troca em que o título é o
 * mesmo: da reunião para o preparo dela, em "Transcrever de novo".
 */
export function acoesDaBarra(...nos) {
  acoes.replaceChildren(...nos);
}

/** O que toda troca de destino faz antes de desenhar: gavetas fechadas, barra vazia. */
function navegar() {
  fecharGavetas();
  acoes.replaceChildren();
}
```

Em `cabecalho`, apagar as linhas:

```js
  // O que a tela anterior pôs na barra não é desta. Só na troca de título: o
  // cabeçalho é reescrito também para trocar o subtítulo, e aí a tela é a mesma.
  if (mudou) acoes.replaceChildren();
```

E trocar `fecharGavetas();` por `navegar();` no começo de `telaDeLista`, `abrirGravacao`, `abrirAjustes` e `abrirGravador`. Em `telaDePreparo`, logo antes de `cabecalho(tituloDe(g), …)`, acrescentar `navegar();` — ela é alcançada também por `aoRefazer`, sem passar por `abrirGravacao`.

- [ ] **Step 7: Run tests to verify they pass**

Run: `uv run --with playwright python tools/provar_reunioes.py`
Expected: `tudo certo.` — as três novas passam, e as de antes também (`prova_semana_barra_troca_de_tela` prova que a semana ainda repõe a navegação dela).

Run: `.venv/bin/python tools/checar_transcricao.py`
Expected: termina sem `FALHA`.

- [ ] **Step 8: Commit**

```bash
git add app-net/App/web/reuniao.js app-net/App/web/revisao.js app-net/App/web/app.js tools/provar_reunioes.py
git commit -m "feat(reuniao): Falantes e Exportar no cabeçalho, e a barra esvazia em toda navegação"
```

---

### Task 3: Renomear e trocar de reunião em menos de 800 ms não perde o nome

**Files:**
- Modify: `app-net/App/web/revisao.js` (`salvar`, ~linhas 45-75)
- Test: `tools/provar_reunioes.py`

**Interfaces:**
- Consumes: `window.__pedidos` (Task 2); `#acoes-da-barra [data-acao='falantes']` (Task 2).
- Produces: nada novo — `salvar()` passa a gravar a reunião em que a edição foi feita.

**O defeito:** o `setTimeout` de `salvar` lê o `estado` de módulo **quando dispara**. Trocar de reunião dentro dos 800 ms troca o `estado`, e a escrita vai com os dados e o caminho da reunião nova — o nome dado na velha nunca chega ao disco.

- [ ] **Step 1: Write the failing test**

```python
def prova_renomear_e_trocar_de_reuniao(pagina) -> None:
    abrir_reuniao(pagina, "Comunicação")
    caminho = pagina.evaluate("() => window.__gravacoes.find((g) => g.titulo?.startsWith('Comunicação')).caminho")
    pagina.click("#acoes-da-barra [data-acao='falantes']")
    pagina.wait_for_selector("#gaveta-falantes .tabela-falantes input", timeout=5000)
    entrada = pagina.locator("#gaveta-falantes .tabela-falantes tr:nth-child(3) input")
    entrada.fill("Carolina")
    entrada.dispatch_event("change")
    # Menos de 800 ms depois, outra reunião.
    pagina.click("#voltar")
    pagina.wait_for_selector(".reuniao-linha", timeout=5000)
    abrir_reuniao(pagina, "Sherlock")
    pagina.wait_for_timeout(1200)
    salvos = pagina.evaluate("() => window.__pedidos.filter((q) => q.op === 'salvar-transcricao')"
                             ".map((q) => ({ g: q.gravacao, carolina: q.conteudo.includes('Carolina') }))")
    conferir(salvos == [{"g": caminho, "carolina": True}],
             f"o nome vai para a reunião em que foi dado, e só para ela ({salvos})")
```

Acrescentar `prova_renomear_e_trocar_de_reuniao` a `PROVAS`.

- [ ] **Step 2: Run test to verify it fails**

Run: `uv run --with playwright python tools/provar_reunioes.py --so prova_renomear_e_trocar_de_reuniao`
Expected: FAIL — `o nome vai para a reunião em que foi dado…` com a lista mostrando o caminho da Sherlock e `carolina: False`.

- [ ] **Step 3: Prender a escrita à reunião**

Em `app-net/App/web/revisao.js`, trocar `salvar` inteira:

```js
/**
 * Grava a transcrição, juntando edições próximas numa escrita só.
 *
 * Sem espera, renomear três falantes seguidos daria três gravações do arquivo
 * inteiro. Com ela, o trabalho de revisão vira uma escrita a cada segundo
 * parado — e nada se perde, porque cada edição reinicia a contagem.
 *
 * **A escrita é da reunião em que a edição foi feita.** O `estado` é de módulo
 * e troca inteiro quando outra reunião abre; lê-lo na hora do disparo mandava,
 * a quem trocasse de reunião nos 800 ms, os dados da reunião nova — e o nome
 * dado na velha nunca chegava ao disco. Trocar de reunião também não cancela a
 * espera da anterior: cada reunião tem o seu relógio.
 */
function salvar() {
  const alvo = estado;
  marcarEstado("salvando…");
  clearTimeout(alvo.aguardando);
  alvo.aguardando = setTimeout(async () => {
    try {
      // Os nomes entram nos segmentos só na hora de gravar: durante a revisão
      // eles vivem à parte, para renomear em massa ser trocar uma entrada.
      const copia = {
        ...alvo.dados,
        segments: alvo.dados.segments.map((s) => ({
          ...s,
          speaker: alvo.nomes.get(s.speaker ?? "Unknown") ?? s.speaker ?? "Unknown",
        })),
      };
      await pedir("salvar-transcricao", {
        gravacao: alvo.gravacao.caminho,
        conteudo: JSON.stringify(copia, null, 2),
      });
      if (alvo === estado) marcarEstado("salvo");
    } catch (e) {
      if (alvo === estado) marcarEstado(`não salvou: ${e.message}`, true);
    }
  }, 800);
}
```

E apagar a linha de módulo `let aguardando = null;`.

- [ ] **Step 4: Run tests to verify they pass**

Run: `uv run --with playwright python tools/provar_reunioes.py`
Expected: `tudo certo.`

- [ ] **Step 5: Commit**

```bash
git add app-net/App/web/revisao.js tools/provar_reunioes.py
git commit -m "fix(revisao): renomear e trocar de reunião em menos de 800 ms não perde o nome"
```

---

### Task 4: O tocador fixo no pé da reunião

**Files:**
- Create: `app-net/App/web/tocador.js`
- Modify: `app-net/App/web/revisao.js` (`ouvirA` e seu uso em `linhaDoTrecho`)
- Modify: `app-net/App/web/reuniao.js`
- Modify: `app-net/App/web/app.css` (bloco novo depois de `.reuniao-aberta__conta`)
- Test: `tools/provar_reunioes.py`

**Interfaces:**
- Consumes: `relogioDoTocador` de `/reuniao-regras.js` (Task 1); `window.__tocou` (Task 2).
- Produces, de `/tocador.js`:
  - `ouvir(g, segundos: number) → void` — aponta o `<audio id="audio">` para o mix de `g` (se ainda não apontado) e toca de `segundos`.
  - `tocadorDaReuniao(g) → HTMLElement` — a faixa `.tocador` com `[data-acao='tocar']` (rótulo acessível "Tocar"/"Pausar"), `.tocador__tempo` (`"00:04 / 30:00"`) e `input.tocador__posicao` (`type=range`, `min=0`, `max=g.duracao_s`, `step=1`, `aria-label="Posição no áudio"`). Solta os ouvintes do `<audio>` sozinha quando sai do documento.

**Decisão de desenho:** o tocador é da **reunião**, e não só da aba Transcrição (o spec diz "no pé" da transcrição). Com a Task 5 a nota também toca, e tocar de uma nota sem ter onde pausar repete o defeito do `⏸` solto que o §1 item 7 aponta. Na aba Ata ele fica também: é o mesmo pé.

- [ ] **Step 1: Write the failing tests**

```python
TOCADOR = """() => ({
  rotulo: document.querySelector('.tocador [data-acao="tocar"]').getAttribute('aria-label'),
  tempo: document.querySelector('.tocador__tempo').textContent,
  posicao: document.querySelector('.tocador__posicao').value,
})"""


def prova_tocador(pagina) -> None:
    abrir_reuniao(pagina, "Comunicação")
    conferir(pagina.is_visible(".reuniao-aberta .tocador"), "a reunião tem o tocador")
    conferir(pagina.evaluate(TOCADOR) == {"rotulo": "Tocar", "tempo": "00:00 / 30:00", "posicao": "0"},
             f"parado, no zero, com a duração da reunião ({pagina.evaluate(TOCADOR)})")
    no_pe = pagina.evaluate("""() => {
        const t = document.querySelector('.tocador').getBoundingClientRect();
        return Math.abs(t.bottom - innerHeight) <= 1;
    }""")
    conferir(no_pe, "e ele fica no pé da janela")
    pagina.click("#corpo-transcricao .trecho >> nth=1")
    tocou = pagina.evaluate("() => window.__tocou")
    conferir(tocou and tocou["t"] == 4 and tocou["src"].endswith("/mix.wav"),
             f"clicar no trecho toca o mix de onde ele começa ({tocou})")
    conferir(pagina.evaluate(TOCADOR)["rotulo"] == "Pausar", "e o tocador passa a oferecer pausar")
    conferir(pagina.evaluate(TOCADOR)["tempo"] == "00:04 / 30:00", "mostrando onde está")
    pagina.click(".tocador [data-acao='tocar']")
    conferir(pagina.evaluate(TOCADOR)["rotulo"] == "Tocar", "pausar pelo tocador")
    pagina.evaluate("""() => { const r = document.querySelector('.tocador__posicao');
        r.value = '900'; r.dispatchEvent(new Event('input', { bubbles: true })); }""")
    conferir(pagina.evaluate("() => document.getElementById('audio').currentTime") == 900,
             "arrastar a posição leva o áudio para lá")
    conferir(pagina.evaluate(TOCADOR)["tempo"] == "15:00 / 30:00", "e o tempo acompanha")
    pagina.click(".reuniao-aberta [data-aba='notas']")
    conferir(pagina.is_visible(".reuniao-aberta .tocador"), "o tocador continua na aba Notas")


def prova_tocador_troca_de_reuniao(pagina) -> None:
    abrir_reuniao(pagina, "Comunicação")
    pagina.click("#corpo-transcricao .trecho >> nth=1")
    pagina.click("#voltar")
    pagina.wait_for_selector(".reuniao-linha", timeout=5000)
    conferir(pagina.evaluate("() => !document.getElementById('audio').hasAttribute('src')"),
             "sair da reunião solta o áudio")
    abrir_reuniao(pagina, "Sherlock")
    conferir(pagina.evaluate(TOCADOR)["tempo"] == "00:00 / 30:00", "a reunião seguinte começa com o tocador zerado")
    # Um evento do <audio> depois da troca não pode repintar o tocador que saiu.
    pagina.evaluate("() => document.getElementById('audio').dispatchEvent(new Event('play'))")
    conferir(pagina.locator(".tocador").count() == 1, "há um tocador só na página")
```

Acrescentar `prova_tocador, prova_tocador_troca_de_reuniao` a `PROVAS`. `.trecho` é a classe da linha em `linhaDoTrecho` (`revisao.js:333`).

- [ ] **Step 2: Run tests to verify they fail**

Run: `uv run --with playwright python tools/provar_reunioes.py --so prova_tocador,prova_tocador_troca_de_reuniao`
Expected: FAIL — `a reunião tem o tocador` e estouro em `.tocador`.

- [ ] **Step 3: O módulo do tocador**

`app-net/App/web/tocador.js`:

```js
// O tocador da reunião aberta: uma faixa fixa no pé, com tocar/pausar, o tempo
// e a posição.
//
// Antes dele, o único sinal de que havia áudio tocando era um ⏸ solto nas
// ferramentas da revisão (spec 2026-09-23-ui-ux.md §1, item 7). O <audio> é um
// só para o app inteiro (index.html) e quem o solta na troca de tela é o
// `pararAudio` de pecas.js; este módulo só o aponta e o mostra.

import { relogioDoTocador } from "/reuniao-regras.js";

const audio = document.getElementById("audio");

/**
 * Toca o mix de uma gravação a partir de um instante.
 *
 * O arquivo é a mesma soma das faixas que o ASR ouviu, então os tempos da
 * transcrição e das notas batem com o que se escuta. Mapeado em JanelaDoApp: o
 * WebView2 serve direto do disco, com Range, que é o que faz pular para o meio
 * de um WAV de 200 MB ser instantâneo.
 */
export function ouvir(g, segundos) {
  const src = `https://gravacoes.local/${encodeURIComponent(g.nome)}/mix.wav`;
  if (audio.getAttribute("src") !== src) audio.src = src;
  audio.currentTime = segundos;
  audio.play().catch(() => {});
}

/** A faixa do pé da reunião. */
export function tocadorDaReuniao(g) {
  const raiz = document.createElement("div");
  raiz.className = "tocador";

  const botao = document.createElement("button");
  botao.type = "button";
  botao.className = "aa-btn aa-btn-secundario tocador__botao";
  botao.dataset.acao = "tocar";

  const tempo = document.createElement("span");
  tempo.className = "tocador__tempo";

  const posicao = document.createElement("input");
  posicao.type = "range";
  posicao.className = "tocador__posicao";
  posicao.min = "0";
  posicao.max = String(Math.max(1, Math.round(g.duracao_s || 0)));
  posicao.step = "1";
  posicao.value = "0";
  posicao.setAttribute("aria-label", "Posição no áudio");

  raiz.append(botao, tempo, posicao);

  // O estado vem dos eventos, e não de `audio.paused`: é o que o <audio> diz
  // que aconteceu, inclusive quando quem pausou foi o `pararAudio` da troca de tela.
  let tocando = false;
  const total = relogioDoTocador(g.duracao_s);

  function pintar() {
    botao.textContent = tocando ? "❚❚" : "▶";
    botao.setAttribute("aria-label", tocando ? "Pausar" : "Tocar");
    const agora = audio.hasAttribute("src") ? audio.currentTime : 0;
    tempo.textContent = `${relogioDoTocador(agora)} / ${total}`;
    // Enquanto a pessoa arrasta, a posição é dela.
    if (document.activeElement !== posicao) posicao.value = String(Math.floor(agora));
  }

  // Os ouvintes são do <audio>, que vive fora da tela: sem soltá-los, cada
  // reunião aberta deixaria um tocador desconectado repintando a cada evento.
  const vida = new AbortController();
  const ouvinte = (fn) => () => {
    if (!raiz.isConnected) { vida.abort(); return; }
    fn();
    pintar();
  };
  audio.addEventListener("play", ouvinte(() => { tocando = true; }), { signal: vida.signal });
  audio.addEventListener("pause", ouvinte(() => { tocando = false; }), { signal: vida.signal });
  audio.addEventListener("ended", ouvinte(() => { tocando = false; }), { signal: vida.signal });
  audio.addEventListener("timeupdate", ouvinte(() => {}), { signal: vida.signal });

  botao.addEventListener("click", () => {
    if (tocando) audio.pause();
    else ouvir(g, audio.hasAttribute("src") ? audio.currentTime : Number(posicao.value));
  });

  posicao.addEventListener("input", () => {
    const s = Number(posicao.value);
    if (audio.hasAttribute("src")) audio.currentTime = s;
    tempo.textContent = `${relogioDoTocador(s)} / ${total}`;
  });

  pintar();
  return raiz;
}
```

Ao arrastar sem áudio carregado, a posição fica guardada no próprio `input` e o próximo ▶ toca dali. A prova de "arrastar leva o áudio para lá" roda com o áudio já apontado (o trecho tocou antes).

- [ ] **Step 4: A revisão toca pelo tocador**

Em `app-net/App/web/revisao.js`: apagar `const audio = document.getElementById("audio");` e a função `ouvirA` inteira; acrescentar `import { ouvir } from "/tocador.js";`; em `linhaDoTrecho` trocar `ouvirA(seg.start);` por:

```js
    ouvir(estado.gravacao, seg.start);
```

(O `marcarEstado("sem áudio: …")` que o `ouvirA` fazia sai junto: o tocador que não toca já diz isso ficando em ▶.)

- [ ] **Step 5: O tocador no pé da reunião**

Em `app-net/App/web/reuniao.js`, `import { tocadorDaReuniao } from "/tocador.js";` e, logo depois do laço que cria as abas (antes de `tela.replaceChildren(raiz);`):

```js
  // O tocador é da reunião, e não de uma aba: a nota também toca (UI-4), e
  // tocar sem ter onde pausar era o defeito do ⏸ solto.
  raiz.appendChild(tocadorDaReuniao(g));
```

- [ ] **Step 6: O CSS**

Em `app-net/App/web/app.css`, depois do bloco de `.reuniao-aberta__conta`:

```css
/* O tocador gruda no pé da janela, como as abas grudam no topo. É o último
 * filho da reunião, e por isso o sticky o leva até o fim da rolagem. */
.tocador {
  position: sticky;
  bottom: 0;
  z-index: var(--z-suspenso);
  display: flex;
  align-items: center;
  gap: var(--espaco-3);
  padding: var(--espaco-2) var(--espaco-3);
  background: var(--cor-superficie);
  border-top: 1px solid var(--cor-borda);
}
.tocador__botao { min-width: 2.5rem; justify-content: center; }
.tocador__tempo {
  font-family: var(--fonte-mono);
  font-size: var(--texto-pequeno);
  color: var(--cor-texto-suave);
  white-space: nowrap;
}
.tocador__posicao { flex: 1 1 auto; min-width: 0; accent-color: var(--cor-acao); }
```

- [ ] **Step 7: Run tests to verify they pass**

Run: `uv run --with playwright python tools/provar_reunioes.py`
Expected: `tudo certo.` Se `e ele fica no pé da janela` falhar por o conteúdo ser mais curto que a janela, é o caso da reunião de dois trechos: pôr `min-height` na `.reuniao-aberta` não é a correção — trocar a conferência por `t.bottom <= innerHeight + 1 && getComputedStyle(t).position === 'sticky'` e registrar a decisão no ledger.

- [ ] **Step 8: Commit**

```bash
git add app-net/App/web/tocador.js app-net/App/web/revisao.js app-net/App/web/reuniao.js app-net/App/web/app.css tools/provar_reunioes.py
git commit -m "feat(reuniao): o tocador fixo no pé, no lugar do ⏸ solto"
```

---

### Task 5: O tempo marcado na nota toca dali (UI-4)

**Files:**
- Modify: `app-net/App/web/notas.js`
- Modify: `app-net/App/web/reuniao.js` (a montagem da aba Notas)
- Modify: `app-net/App/web/app.css`
- Test: `tools/provar_reunioes.py`

**Interfaces:**
- Consumes: `momentosDasNotas` de `/reuniao-regras.js` (Task 1); `ouvir(g, s)` de `/tocador.js` (Task 4); `window.__notas` (Task 2).
- Produces: `blocoDeNotas(gravacao, { aoTocar })` — com `aoTocar(segundos)`, o bloco mostra `.notas__momentos`, uma lista de `button.notas__momento` (texto `"00:12:34 adiar o piloto"`), refeita a cada tecla **sem tocar no `<textarea>`**.

Uma `<textarea>` não tem botão dentro: os momentos são uma lista logo abaixo dela, e cada um é a linha inteira que o carrega.

- [ ] **Step 1: Write the failing test**

```python
def prova_notas_tocam(pagina) -> None:
    pagina.evaluate("() => { window.__notas = 'abertura\\n[00:00:04] Carol começa\\nsem tempo'; }")
    abrir_reuniao(pagina, "Comunicação")
    pagina.click(".reuniao-aberta [data-aba='notas']")
    pagina.wait_for_selector("#painel-notas .notas__momento", timeout=5000)
    momentos = pagina.eval_on_selector_all("#painel-notas .notas__momento", "els => els.map((e) => e.textContent)")
    conferir(momentos == ["00:00:04 Carol começa"], f"cada linha com tempo vira um botão ({momentos})")
    pagina.click("#painel-notas .notas__momento")
    tocou = pagina.evaluate("() => window.__tocou")
    conferir(tocou and tocou["t"] == 4, f"e o botão toca dali ({tocou})")
    pagina.focus("#painel-notas .notas__texto")
    pagina.keyboard.press("Control+End")
    pagina.keyboard.type("\n[00:01:00] novo")
    conferir(pagina.eval_on_selector_all("#painel-notas .notas__momento", "els => els.length") == 2,
             "escrever uma marca nova acrescenta o botão")
    conferir(pagina.evaluate("() => document.activeElement.classList.contains('notas__texto')"),
             "sem tirar o cursor de quem escreve")
```

Acrescentar `prova_notas_tocam` a `PROVAS`.

- [ ] **Step 2: Run test to verify it fails**

Run: `uv run --with playwright python tools/provar_reunioes.py --so prova_notas_tocam`
Expected: FAIL — `prova_notas_tocam estourou: Timeout … .notas__momento`.

- [ ] **Step 3: Os momentos no bloco de notas**

Em `app-net/App/web/notas.js`: `import { momentosDasNotas } from "/reuniao-regras.js";`, documentar a opção nova no JSDoc de `blocoDeNotas`:

```js
 * @param opcoes.aoTocar `(segundos) => void`. Com ela, as linhas com tempo
 *        marcado (`[00:12:34]`) viram botões que tocam dali — UI-4 do BACKLOG.
 *        Só a reunião a passa: no Gravador a gravação ainda está correndo.
```

Depois de `raiz.append(topo, campo, acoes);`:

```js
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
      b.append(marca, document.createTextNode(m.texto ? ` ${m.texto}` : ""));
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
```

E em `carregar`, depois de `ultimoSalvo = campo.value;`, chamar `desenharMomentos();`.

- [ ] **Step 4: A reunião passa o `aoTocar`**

Em `app-net/App/web/reuniao.js`, trocar a montagem das notas e importar `ouvir`:

```js
import { tocadorDaReuniao, ouvir } from "/tocador.js";
```

```js
    notas: (p) => p.appendChild(blocoDeNotas(g.caminho, { linhas: 18, aoTocar: (s) => ouvir(g, s) }).raiz),
```

- [ ] **Step 5: O CSS**

Em `app-net/App/web/app.css`, junto dos estilos de `.notas`:

```css
/* Os momentos marcados (UI-4): uma coluna de botões com a marca em mono. */
.notas__momentos { display: grid; gap: var(--espaco-1); margin-top: var(--espaco-3); }
.notas__momento {
  display: block;
  text-align: left;
  padding: var(--espaco-1) var(--espaco-2);
  border: 1px solid transparent;
  border-radius: var(--raio-pequeno);
  background: none;
  color: var(--cor-texto);
  font: inherit;
  font-size: var(--texto-pequeno);
  cursor: pointer;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.notas__momento:hover { background: var(--cor-superficie-2); }
.notas__momento:focus-visible { outline: none; box-shadow: var(--anel-foco); }
.notas__marca { font-family: var(--fonte-mono); color: var(--cor-acao); }
```

- [ ] **Step 6: Run tests to verify they pass**

Run: `uv run --with playwright python tools/provar_reunioes.py`
Expected: `tudo certo.`
Run: `.venv/bin/python tools/checar_transcricao.py`
Expected: sem `FALHA` (as notas do Gravador continuam sem momentos).

- [ ] **Step 7: Commit**

```bash
git add app-net/App/web/notas.js app-net/App/web/reuniao.js app-net/App/web/app.css tools/provar_reunioes.py
git commit -m "feat(notas): o tempo marcado na nota toca dali (UI-4)"
```

---

### Task 6: O preparo sai do app.js

Mudança mecânica, sem comportamento novo: as Tasks 7 e 8 redesenham o preparo, e redesenhá-lo dentro de um `app.js` de 870 linhas mistura a moldura com a tela. É o mesmo molde de `reunioes.js`, `configuracoes.js` e `gravador.js`: a tela recebe `{ cabecalho, tela }` da moldura.

**Files:**
- Create: `app-net/App/web/preparo.js`
- Modify: `app-net/App/web/app.js`

**Interfaces:**
- Produces, de `/preparo.js`: `telaDePreparo(g, { cabecalho, tela, navegar, aoTerminar })` — `aoTerminar(g)` é chamado quando a transcrição termina bem (o `app.js` passa a função que abre a reunião). De `/app.js`, `botaoApagarGravacao` continua exportada e o `preparo.js` a importa.

- [ ] **Step 1: Mover**

Mover de `app.js` para `preparo.js`, **com os comentários como estão**: `telaDePreparo`, `dataDe`, `ETAPAS`, `acompanhar`, `transcrever`. Apagar do `app.js` o objeto `ETAPAS` (a Task 8 usa o de `reuniao-regras.js`; nesta task o `preparo.js` o mantém copiado como estava). `abrirResultado` fica no `app.js` e vira o `aoTerminar`.

Mudanças de ligação, e só elas:
- `telaDePreparo(g)` → `export async function telaDePreparo(g, { cabecalho, tela, navegar, aoTerminar })`; `acompanhar(g, botao, painel)` e `transcrever(g, botao, painel, modelo)` ganham um último parâmetro `aoTerminar` e o repassam; em `acompanhar`, `abrirResultado(g)` vira `aoTerminar(g)`.
- Imports de `preparo.js`:

```js
import { pedir } from "/ponte.js";
import { alerta, campo, secao, campoComSugestoes, preencherSugestoes, corDoFalante } from "/pecas.js";
import { transcrever as pedirTranscricao, assinarTranscricoes, emCurso,
         ultimoResultado, cancelar } from "/transcricoes.js";
import { blocoDeNotas } from "/notas.js";
import { duracao, quando, tituloDe, botaoApagarGravacao } from "/app.js";
```

- No `app.js`: `import { telaDePreparo as montarPreparo } from "/preparo.js";`, e uma função local no lugar da antiga:

```js
/** O preparo mora em preparo.js; daqui ele leva a moldura e o que fazer no fim. */
function telaDePreparo(g) {
  return montarPreparo(g, { cabecalho, tela, navegar, aoTerminar: abrirResultado });
}
```

- Em `preparo.js`, o `navegar();` que a Task 2 pôs antes do `cabecalho` passa a ser o `navegar` recebido.
- Tirar do `app.js` os imports que ficaram sem uso (`campo`, `secao`, `campoComSugestoes`, `preencherSugestoes`, `corDoFalante`, `pedirTranscricao`, `assinarTranscricoes`, `emCurso`, `ultimoResultado`, `cancelar`, `blocoDeNotas`, se nenhum outro trecho os usa — conferir cada um com `grep -n`).

- [ ] **Step 2: Run the screen checks**

Run: `uv run --with playwright python tools/provar_reunioes.py`
Expected: `tudo certo.`
Run: `.venv/bin/python tools/checar_transcricao.py`
Expected: sem `FALHA` — é a prova de que preparar, transcrever, sair no meio, voltar e parar continuam iguais.

- [ ] **Step 3: Commit**

```bash
git add app-net/App/web/preparo.js app-net/App/web/app.js
git commit -m "refactor(preparo): a tela de transcrever sai do app.js, sem mudar nada"
```

---

### Task 7: O vocabulário como etiquetas

**Files:**
- Create: `app-net/App/web/etiquetas.js`
- Modify: `app-net/App/web/preparo.js` (seção Vocabulário, `sugerirTermos`, `carregarPreferencias`, `guardarVocabulario`, `transcrever`)
- Modify: `app-net/App/web/app.css`
- Modify: `tools/checar_transcricao.py` (linhas ~490-494)
- Test: `tools/provar_reunioes.py`

**Interfaces:**
- Consumes: `separarTermos`, `juntarTermos` (Task 1); `window.__prefs`, `window.__vinculo`, `window.__pedidos` (Task 2).
- Produces, de `/etiquetas.js`: `campoDeEtiquetas({ id, rotulo, aoMudar }) → { raiz, valor(): string, definir(texto: string): void, acrescentar(termo: string): void }`. `raiz` tem `id` = o `id` pedido (`"vocabulario"`); o campo de digitar é `#<id>-novo`; cada etiqueta é `.etiquetas__termo` com o texto e um `button.etiquetas__tirar` (`aria-label="Tirar <termo>"`). `valor()` devolve `juntarTermos(termos)`. `aoMudar()` é chamado a cada termo posto ou tirado, **não** em `definir`.

Regras do campo: Enter ou vírgula põem o que foi digitado; colar texto com separador põe cada termo; Backspace com o campo vazio tira a última; sair do campo com texto digitado põe o termo (não se perde o que se escreveu). Termo repetido (sem olhar caixa) não entra de novo.

- [ ] **Step 1: Write the failing test**

```python
def abrir_preparo(pagina) -> None:
    linha_por_titulo(pagina, "Semanal")
    pagina.evaluate("() => window.__linha.dispatchEvent(new MouseEvent('dblclick', { bubbles: true }))")
    pagina.wait_for_selector("#vocabulario", timeout=5000)


TERMOS = "() => [...document.querySelectorAll('#vocabulario .etiquetas__termo')].map((e) => e.firstChild.textContent)"


def prova_preparo_vocabulario(pagina) -> None:
    pagina.evaluate("""() => {
        window.__vinculo = { cliente: 'Vivo', projeto: 'Sherlock' };
        window.__prefs = { language: 'pt', model_size: 'large-v3', initial_prompt: 'Beegol, NOC\\nnoc; CCIL' };
    }""")
    abrir_preparo(pagina)
    pagina.wait_for_timeout(100)
    conferir(pagina.evaluate(TERMOS) == ["Beegol", "NOC", "CCIL"],
             f"o vocabulário do projeto vira etiquetas, sem repetir ({pagina.evaluate(TERMOS)})")
    pagina.click("#vocabulario-novo")
    pagina.keyboard.type("Sherlock")
    pagina.keyboard.press("Enter")
    conferir(pagina.evaluate(TERMOS)[-1] == "Sherlock", "Enter põe o termo")
    conferir(pagina.evaluate("() => document.activeElement.id") == "vocabulario-novo", "e o cursor fica no campo")
    pagina.evaluate("""() => { const c = document.getElementById('vocabulario-novo');
        const d = new DataTransfer(); d.setData('text/plain', 'Algar, Mobi, NOC');
        c.dispatchEvent(new ClipboardEvent('paste', { clipboardData: d, bubbles: true, cancelable: true })); }""")
    conferir(pagina.evaluate(TERMOS) == ["Beegol", "NOC", "CCIL", "Sherlock", "Algar", "Mobi"],
             f"colar uma lista põe cada termo, e o repetido não entra ({pagina.evaluate(TERMOS)})")
    pagina.click("#vocabulario .etiquetas__tirar[aria-label='Tirar CCIL']")
    conferir("CCIL" not in pagina.evaluate(TERMOS), "o × tira a etiqueta")
    pagina.focus("#vocabulario-novo")
    pagina.keyboard.press("Backspace")
    conferir(pagina.evaluate(TERMOS)[-1] == "Algar", "Backspace no campo vazio tira a última")
    pagina.keyboard.type("Pendente")
    pagina.focus("#cliente")
    conferir(pagina.evaluate(TERMOS)[-1] == "Pendente", "sair do campo com texto digitado não perde o termo")
    pagina.wait_for_timeout(50)
    salvo = pagina.evaluate("() => window.__pedidos.filter((q) => q.op === 'salvar-projeto').pop()")
    conferir(salvo and salvo["prefs"]["initial_prompt"] == "Beegol, NOC, Sherlock, Algar, Pendente",
             f"e o projeto guarda a lista no formato de sempre ({salvo and salvo['prefs']['initial_prompt']!r})")
```

Acrescentar `prova_preparo_vocabulario` a `PROVAS`.

- [ ] **Step 2: Run test to verify it fails**

Run: `uv run --with playwright python tools/provar_reunioes.py --so prova_preparo_vocabulario`
Expected: FAIL — `o vocabulário do projeto vira etiquetas… ([])`.

- [ ] **Step 3: O campo de etiquetas**

`app-net/App/web/etiquetas.js`:

```js
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
```

- [ ] **Step 4: O preparo usa as etiquetas**

Em `app-net/App/web/preparo.js`, `import { campoDeEtiquetas } from "/etiquetas.js";`, e:

1. Trocar `const caixa = campo("Termos do projeto", "textarea", { id: "vocabulario", linhas: 4 });` por:

```js
  const termos = campoDeEtiquetas({
    id: "vocabulario", rotulo: "Termos do projeto",
    // Cada termo posto ou tirado grava no projeto — o equivalente do blur da
    // caixa de antes, que gravava ao sair dela.
    aoMudar: () => guardarVocabulario(),
  });
```

e `vocab.append(caixa, dica, sugestoes);` por `vocab.append(termos.raiz, dica, sugestoes);`. A dica passa a dizer: `"Nomes de pessoas, jargão, nomes de sistemas — Enter ou vírgula separam. Sem limite de tamanho: o que o modelo escrever parecido é corrigido depois."`

2. Em `sugerirTermos`: trocar a leitura da caixa por `new Set(separarTermos(termos.valor()).map((t) => t.toLocaleLowerCase("pt-BR")))` como `jaTem` (importar `separarTermos` de `/reuniao-regras.js`), filtrar `novos` com `!jaTem.has(t.toLocaleLowerCase("pt-BR"))`, apagar a guarda `if (!caixaVocab) return;` e, no clique da sugestão, trocar as duas linhas que editavam a caixa por `termos.acrescentar(termo);`.

3. Em `carregarPreferencias`: `document.getElementById("vocabulario").value = prefs.initial_prompt ?? "";` vira `termos.definir(prefs.initial_prompt ?? "");`.

4. Em `guardarVocabulario`: apagar `const caixaVocab = …` e `if (!caixaVocab) return;`, e `initial_prompt: caixaVocab.value.trim(),` vira `initial_prompt: termos.valor(),`. Guardar `if (!termos.raiz.isConnected) return;` no lugar da guarda apagada — a tela pode ter saído.

5. Apagar `caixa.querySelector("textarea").addEventListener("blur", guardarVocabulario);`.

6. `transcrever` é irmã de `telaDePreparo` e não enxerga `termos`: passar o valor por parâmetro, como o `modeloDeDiarizacao`. O clique vira `transcrever(g, botao, painel, modeloDeDiarizacao, termos.valor(), aoTerminar)`, a assinatura `async function transcrever(g, botao, painel, modeloDeDiarizacao, vocabulario, aoTerminar)`, e a linha `const vocabulario = document.getElementById("vocabulario").value.trim();` sai.

- [ ] **Step 5: O CSS**

```css
/* O vocabulário como etiquetas: a caixa tem cara de campo, e as etiquetas
 * quebram linha dentro dela antes do campo de digitar. */
.etiquetas__caixa {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: var(--espaco-1);
  min-height: 2.5rem;
  cursor: text;
}
.etiquetas__lista { display: contents; }
.etiquetas__termo { gap: var(--espaco-1); padding-right: var(--espaco-1); }
.etiquetas__tirar {
  border: 0;
  background: none;
  color: inherit;
  font: inherit;
  line-height: 1;
  padding: 0 var(--espaco-1);
  border-radius: var(--raio-pilula);
  cursor: pointer;
}
.etiquetas__tirar:hover { color: var(--cor-texto-forte); }
.etiquetas__tirar:focus-visible { outline: none; box-shadow: var(--anel-foco); }
.etiquetas__novo {
  flex: 1 1 8rem;
  min-width: 0;
  border: 0;
  background: none;
  color: var(--cor-texto);
  font: inherit;
  outline: none;
}
```

- [ ] **Step 6: checar_transcricao lê as etiquetas**

Em `tools/checar_transcricao.py`, a conferência de "clicar na sugestão a joga no vocabulário" vira:

```python
            n_termos = pagina.locator("#vocabulario .etiquetas__termo").count()
            conferir("clicar na sugestão a joga no vocabulário", n_termos > 0,
                     pagina.inner_text("#vocabulario"))
```

- [ ] **Step 7: Run tests to verify they pass**

Run: `uv run --with playwright python tools/provar_reunioes.py`
Expected: `tudo certo.`
Run: `.venv/bin/python tools/checar_transcricao.py`
Expected: sem `FALHA`.

- [ ] **Step 8: Commit**

```bash
git add app-net/App/web/etiquetas.js app-net/App/web/preparo.js app-net/App/web/app.css tools/checar_transcricao.py tools/provar_reunioes.py
git commit -m "feat(preparo): o vocabulário como etiquetas"
```

---

### Task 8: O motor dobrado, o andamento no topo e o formulário que vira resumo

**Files:**
- Modify: `app-net/App/web/preparo.js` (seção Motor, ordem das seções, `acompanhar`)
- Modify: `app-net/App/web/app.css`
- Test: `tools/provar_reunioes.py`

**Interfaces:**
- Consumes: `estadoDasEtapas`, `resumoDoMotor` (Task 1); o `campoDeEtiquetas` (Task 7, `termos.valor()`); `TAREFA_EM_CURSO` e `FIM_DE_TAREFA` de `provar_reunioes.py`.
- Produces: no preparo, `details.preparo__motor` (fechado) com `summary` = `"Motor · " + resumoDoMotor(...)`; `.preparo__andamento` (o primeiro filho da tela quando transcreve) com `ol.preparo__etapas > li[data-estado]` e `aria-current="step"` na atual, a `.aa-progresso`, o texto e "Parar transcrição"; `.preparo__resumo`, uma linha só. Transcrevendo, `.preparo__formulario` fica `hidden` e o resumo aparece; cancelada ou com erro, o contrário.

A ordem do formulário passa a ser: Reunião (cliente, projeto, data) · Vocabulário · Notas · Motor (dobrado) · ações. **As notas não entram no resumo nem se trancam**: elas não são mandadas ao motor, e anotar enquanto a transcrição roda é uso normal.

- [ ] **Step 1: Write the failing tests**

```python
def prova_preparo_curto(pagina) -> None:
    abrir_preparo(pagina)
    conferir(not pagina.evaluate("() => document.querySelector('.preparo__motor').open"),
             "as opções do motor vêm dobradas")
    resumo = pagina.text_content(".preparo__motor summary")
    conferir(resumo == "Motor · large-v3 · pt · separa os falantes", f"numa linha que diz o que escolhem ({resumo!r})")
    pagina.click(".preparo__motor summary")
    pagina.select_option("#diarizacao", "não")
    conferir(pagina.text_content(".preparo__motor summary").endswith("sem separar falantes"),
             "e a linha acompanha a escolha")
    conferir(pagina.evaluate("""() => {
        const y = (s) => document.querySelector(s).getBoundingClientRect().top;
        return y('#vocabulario') < y('.preparo__motor') && y('#cliente') < y('#vocabulario');
    }"""), "reunião, vocabulário, e o motor depois")


ETAPAS_DA_TELA = """() => [...document.querySelectorAll('.preparo__etapas li')]
  .map((l) => [l.dataset.estado, l.getAttribute('aria-current')])"""


def prova_preparo_andamento(pagina) -> None:
    pagina.evaluate("() => { window.__vinculo = { cliente: 'Vivo', projeto: 'Sherlock' }; "
                    "window.__prefs = { language: 'pt', model_size: 'large-v3', initial_prompt: 'NOC, CCIL' }; }")
    abrir_preparo(pagina)
    caminho = pagina.evaluate("() => window.__gravacoes.find((g) => g.titulo?.startsWith('Semanal')).caminho")
    pagina.click("text=Transcrever")
    pagina.wait_for_selector(".preparo__andamento", timeout=5000)
    pagina.evaluate(TAREFA_EM_CURSO, [caminho, "transcricao", "asr"])
    pagina.wait_for_timeout(30)
    conferir(pagina.evaluate(ETAPAS_DA_TELA) == [["feita", None], ["atual", "step"], ["pendente", None], ["pendente", None]],
             f"o andamento mostra as quatro etapas, e onde está ({pagina.evaluate(ETAPAS_DA_TELA)})")
    conferir(pagina.evaluate("() => document.querySelector('#tela').firstElementChild.classList.contains('preparo__andamento')"),
             "no topo da tela")
    conferir(pagina.evaluate("() => document.querySelector('.preparo__andamento').getBoundingClientRect().bottom <= innerHeight"),
             "e à vista sem rolar")
    conferir(not pagina.is_visible(".preparo__formulario"), "o formulário sai")
    resumo = pagina.text_content(".preparo__resumo")
    conferir(resumo == "Vivo › Sherlock · large-v3 · pt · separa os falantes · 2 termos",
             f"e vira uma linha de resumo ({resumo!r})")
    conferir(pagina.is_visible("#painel-preparo-notas .notas__texto") and
             not pagina.is_disabled("#painel-preparo-notas .notas__texto"),
             "as notas continuam à mão, e editáveis")
    # Cancelada: tudo volta a ser editável.
    pagina.evaluate("""(c) => window.__emitir({ tipo: 'transcricoes', transcricoes: { atual: null, ultimo:
        { gravacao: c, nome: 'x', tarefa: 'transcricao', etapa: 'asr', fracao: 0.5, texto: '',
          comecou_em: '', terminou: true, erro: null, cancelada: true } } })""", caminho)
    pagina.wait_for_timeout(30)
    conferir(pagina.is_visible(".preparo__formulario") and not pagina.is_visible(".preparo__resumo"),
             "parada, o formulário volta e o resumo some")
    conferir("interrompida" in (pagina.text_content(".preparo__andamento") or ""), "dizendo que parou")


def prova_preparo_erro_devolve_o_formulario(pagina) -> None:
    abrir_preparo(pagina)
    caminho = pagina.evaluate("() => window.__gravacoes.find((g) => g.titulo?.startsWith('Semanal')).caminho")
    pagina.click("text=Transcrever")
    pagina.wait_for_selector(".preparo__andamento", timeout=5000)
    pagina.evaluate("""(c) => window.__emitir({ tipo: 'transcricoes', transcricoes: { atual: null, ultimo:
        { gravacao: c, nome: 'x', tarefa: 'transcricao', etapa: 'asr', fracao: 0.5, texto: '',
          comecou_em: '', terminou: true, erro: 'a placa sumiu', cancelada: false } } })""", caminho)
    pagina.wait_for_timeout(30)
    conferir(pagina.is_visible(".preparo__formulario"), "com erro, o formulário volta")
    conferir("a placa sumiu" in (pagina.text_content(".preparo__andamento") or ""), "e o erro fica no topo, onde se olhava")
    conferir(pagina.text_content(".preparo__formulario .aa-btn-primario") == "Tentar de novo", "oferecendo tentar de novo")
```

Acrescentar `prova_preparo_curto, prova_preparo_andamento, prova_preparo_erro_devolve_o_formulario` a `PROVAS`.

- [ ] **Step 2: Run tests to verify they fail**

Run: `uv run --with playwright python tools/provar_reunioes.py --so prova_preparo_curto,prova_preparo_andamento,prova_preparo_erro_devolve_o_formulario`
Expected: FAIL — estouro esperando `.preparo__motor` / `.preparo__andamento`.

- [ ] **Step 3: O formulário, reordenado e com o motor dobrado**

Em `app-net/App/web/preparo.js`, importar `estadoDasEtapas, resumoDoMotor` de `/reuniao-regras.js` e apagar o `ETAPAS` copiado na Task 6.

1. `forma.className = "secao";` vira `forma.className = "secao preparo__formulario";`.
2. A seção Motor vira um `<details>`:

```js
  // ---- motor, dobrado: se mexe uma vez por projeto, e a linha fechada já diz
  // o que ele escolhe. Os ids ficam os mesmos, e com eles tudo o que os lê.
  const motor = document.createElement("details");
  motor.className = "bloco preparo__motor";
  const motorResumo = document.createElement("summary");
  motorResumo.className = "preparo__motor-resumo";
  const linha2 = document.createElement("div");
  linha2.className = "linha";
  linha2.append( /* os três campos, como estavam */ );
  motor.append(motorResumo, linha2);

  const escolhaDoMotor = () => ({
    modelo: document.getElementById("modelo").value,
    idioma: document.getElementById("idioma").value,
    diarizar: document.getElementById("diarizacao").value === "sim",
  });
  const pintarMotor = () => { motorResumo.textContent = `Motor · ${resumoDoMotor(escolhaDoMotor())}`; };
```

   Chamar `pintarMotor()` logo depois de `tela.appendChild(forma)`, no fim de `carregarPreferencias` (depois de pôr os valores) e no `change` de `modelo`, `idioma` e `diarizacao` (no mesmo laço que já liga `guardarVocabulario`).
3. As notas vão para uma seção com id próprio, para a prova e o CSS acharem: `blocoNotas.id = "painel-preparo-notas";`. A montagem vira:

```js
  const painel = document.createElement("div");
  const andamento = document.createElement("div");
  andamento.className = "preparo__andamento";
  andamento.hidden = true;
  const resumo = document.createElement("p");
  resumo.className = "preparo__resumo";
  resumo.hidden = true;

  forma.append(reuniao, vocab, motor, acoes, painel);
  // As notas ficam fora do formulário: não são mandadas ao motor, e anotar
  // enquanto a transcrição roda é uso normal — elas não se trancam com ele.
  tela.append(andamento, resumo, forma, blocoNotas);
```

   O bloco da legenda gravada (`legenda-gravada`) continua entrando antes, com `tela.appendChild(b)`; como o andamento tem de ser o primeiro filho, trocar esse `tela.appendChild(b)` por guardar `b` numa variável `legendaGravada` e montar `tela.append(andamento, resumo, ...(legendaGravada ? [legendaGravada] : []), forma, blocoNotas);`.

- [ ] **Step 4: O andamento no topo e o resumo**

Trocar `acompanhar` para desenhar no `andamento` e alternar formulário e resumo. A assinatura vira `acompanhar(g, botao, partes, aoTerminar)`, com `partes = { painel, andamento, resumo, forma, linhaDeResumo }`, onde `linhaDeResumo()` é definida em `telaDePreparo`:

```js
  // O que foi mandado, numa linha: é o que o formulário diz enquanto não se
  // pode mais editá-lo.
  const linhaDeResumo = () => {
    const n = separarTermos(termos.valor()).length;
    return [
      [campoCliente.value.trim(), campoProjeto.value.trim()].filter(Boolean).join(" › ") || null,
      resumoDoMotor(escolhaDoMotor()),
      n ? `${n} ${n === 1 ? "termo" : "termos"}` : null,
    ].filter(Boolean).join(" · ");
  };
  const partes = { painel, andamento, resumo, forma, linhaDeResumo };
```

(`campoCliente`/`campoProjeto` são lidos na hora da chamada, então a função pode ser declarada antes deles, como já é o caso de `atualizarProjetos`.) Os chamadores (`transcrever`, e o `emCurso(g.caminho)` do fim de `telaDePreparo`, e o erro de `ultimoResultado`) passam `partes` no lugar de `painel`; em `transcrever`, `painel.replaceChildren(alerta(...))` do `catch` vira `mostrarFim(partes, alerta(e.message, "erro"))` — a função abaixo.

```js
/** Transcrevendo: o andamento no topo, e o formulário vira uma linha. */
function trancar({ andamento, resumo, forma, linhaDeResumo }) {
  resumo.textContent = linhaDeResumo();
  resumo.hidden = false;
  forma.hidden = true;
  andamento.hidden = false;
}

/** Parada ou com erro: o recado fica no topo, onde se olhava, e o formulário volta. */
function mostrarFim({ andamento, resumo, forma }, ...nos) {
  andamento.replaceChildren(...nos);
  andamento.hidden = nos.length === 0;
  resumo.hidden = true;
  forma.hidden = false;
}

function acompanhar(g, botao, partes, aoTerminar) {
  const { andamento } = partes;
  botao.disabled = true;
  botao.textContent = "Transcrevendo…";

  const etapas = document.createElement("ol");
  etapas.className = "preparo__etapas";

  const barra = document.createElement("div");
  barra.className = "aa-progresso";
  const preenchimento = document.createElement("div");
  barra.appendChild(preenchimento);

  const estado = document.createElement("p");
  estado.className = "campo__dica";
  estado.textContent = "preparando…";

  // Parar mora no andamento, e não entre os botões do formulário: é a ação de
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
      andamento.appendChild(alerta(e.message, "erro"));
    }
  });

  const linha = document.createElement("div");
  linha.className = "progresso__linha";
  linha.append(estado, parar);
  andamento.replaceChildren(etapas, barra, linha);
  trancar(partes);

  function pintar(t) {
    etapas.replaceChildren(...estadoDasEtapas(t.etapa).map((e) => {
      const li = document.createElement("li");
      li.dataset.estado = e.estado;
      if (e.estado === "atual") li.setAttribute("aria-current", "step");
      li.textContent = e.rotulo;
      return li;
    }));
    estado.textContent = t.texto || "";
    preenchimento.style.width = `${t.fracao >= 0 ? Math.round(t.fracao * 100) : 0}%`;
  }
  pintar({ etapa: null, texto: "preparando…", fracao: 0 });

  const atual = emCurso(g.caminho);
  if (atual) pintar(atual);

  const cancelarAssinatura = assinarTranscricoes(() => {
    if (!andamento.isConnected) { cancelarAssinatura(); return; }

    const rodando = emCurso(g.caminho);
    if (rodando) { pintar(rodando); return; }

    const fim = ultimoResultado(g.caminho);
    if (!fim) return;

    cancelarAssinatura();
    if (fim.cancelada) {
      botao.disabled = false;
      botao.textContent = "Transcrever";
      const nota = document.createElement("p");
      nota.className = "campo__dica";
      nota.textContent = "Transcrição interrompida. A placa foi liberada.";
      mostrarFim(partes, nota);
      return;
    }
    if (fim.erro) {
      botao.disabled = false;
      botao.textContent = "Tentar de novo";
      mostrarFim(partes, alerta(fim.erro, "erro"));
      return;
    }
    aoTerminar(g);
  });
}
```

   Os comentários que já existiam em `acompanhar` (o do registro do núcleo, o de largar a assinatura, o de "parou a pedido") ficam, nos mesmos lugares. O `painel` deixa de receber a barra; ele continua no formulário para o que não é andamento (nada, nesta tarefa) — se ficar sem uso, apagá-lo.

   A linha `estado.textContent = \`${ETAPAS[t.etapa] ?? t.etapa}: ${t.texto}\`` some: o nome da etapa agora está na lista, e o texto do núcleo vem sozinho.

- [ ] **Step 5: O CSS**

```css
/* O preparo transcrevendo: o andamento em cima, as quatro etapas numa linha. */
.preparo__andamento { display: grid; gap: var(--espaco-2); margin-bottom: var(--espaco-4); }
.preparo__etapas {
  display: flex;
  flex-wrap: wrap;
  gap: var(--espaco-2) var(--espaco-4);
  margin: 0;
  padding: 0;
  list-style: none;
  font-size: var(--texto-pequeno);
}
.preparo__etapas li { color: var(--cor-texto-suave); }
.preparo__etapas li[data-estado="feita"]::before { content: "✓ "; color: var(--cor-acao); }
.preparo__etapas li[data-estado="atual"] { color: var(--cor-texto-forte); font-weight: 600; }
.preparo__resumo { margin: 0 0 var(--espaco-4); color: var(--cor-texto-suave); font-size: var(--texto-pequeno); }
.preparo__motor > summary { cursor: pointer; color: var(--cor-texto-suave); font-size: var(--texto-pequeno); }
.preparo__motor[open] > summary { margin-bottom: var(--espaco-3); }
```

- [ ] **Step 6: Run tests to verify they pass**

Run: `uv run --with playwright python tools/provar_reunioes.py`
Expected: `tudo certo.`
Run: `.venv/bin/python tools/checar_transcricao.py`
Expected: sem `FALHA` — ele espera `.aa-progresso` (continua existindo, agora no andamento) e clica `text=Parar transcrição` (idem).
Run: `node --test tools/web/*.test.mjs`
Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git add app-net/App/web/preparo.js app-net/App/web/app.css tools/provar_reunioes.py
git commit -m "feat(preparo): o motor dobrado, o andamento no topo e o formulário que vira resumo"
```

---

### Task 9: Os documentos e a prova inteira

**Files:**
- Modify: `docs/superpowers/specs/2026-09-23-ui-ux.md` (§5, linha 2b)
- Modify: `docs/BACKLOG.md` (`UI-4`)

- [ ] **Step 1: O spec e o backlog**

No §5 do spec, a coluna "estado" da linha 2b vira `**feito em 25/09/2026**`. No `docs/BACKLOG.md`, o título do `UI-4` troca `` `espera` `` por `` `feito` `` e ganha uma linha no fim do item:

```markdown
**Feito em 25/09/2026** (plano 2b): na aba Notas da reunião, cada linha com
`[hh:mm:ss]` vira um botão que toca dali, pelo tocador fixo do pé.
```

- [ ] **Step 2: A prova inteira**

Run: `node --test tools/web/*.test.mjs`
Expected: PASS, nenhuma falha.
Run: `uv run --with playwright python tools/provar_reunioes.py --fotos dist/fotos-2b`
Expected: `tudo certo.`
Run: `.venv/bin/python tools/checar_transcricao.py`
Expected: sem `FALHA`.
Run: `export PATH="$HOME/.dotnet:$PATH"; dotnet test app-net/Tests/MeetingApp.Tests.csproj`
Expected: todos passam (o número de antes do plano; nenhum teste C# muda).

- [ ] **Step 3: Commit**

```bash
git add docs/superpowers/specs/2026-09-23-ui-ux.md docs/BACKLOG.md
git commit -m "docs: o plano 2b feito, e o UI-4 com ele"
```

A instalação (`tools/publicar.sh` com o app fechado pela bandeja) e o percurso do dono ficam fora do plano: são passos que o dono dispara.
