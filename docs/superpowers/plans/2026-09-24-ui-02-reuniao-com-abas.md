# A reunião aberta: Transcrição · Ata · Notas — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Abrir uma reunião mostra três abas — Transcrição, Ata e Notas — e a ata deixa de ser um destino do trilho: gerar, ler, copiar e exportar a ata acontece na aba Ata da própria reunião, e o trilho fica Gravador · Reuniões · Ajustes.

**Architecture:** Um módulo novo, `web/reuniao.js`, desenha a reunião aberta: a barra de abas e três painéis montados na primeira vez que aparecem e depois só escondidos — a revisão guarda na memória o que ainda não foi para o disco, e não pode ser remontada a cada troca. A aba Transcrição é a `telaDeRevisao` de sempre; a aba Ata é uma função nova de `atas.js`, `montarAta`, feita do que era o cartão da tela de Atas sem a dobra e sem o título; a aba Notas é o `blocoDeNotas`, que sai da gaveta. Na tarefa 2, a tela de Atas, o botão do trilho e o CSS do cartão saem, e a bolinha da ata passa a ser a de Reuniões. Nada muda no núcleo.

**Tech Stack:** JavaScript ES modules sem biblioteca, WebView2, AA Design System (CSS), Python + Playwright (prova de tela).

**Spec:** [docs/superpowers/specs/2026-09-23-ui-ux.md](../specs/2026-09-23-ui-ux.md) — §2 linha "Três destinos", §3.3 "A reunião aberta", §5 linha do plano 2, §6 **D-A** (lida como sim em 24/09/2026). Mockup: https://claude.ai/artifact/BZLCSDP7cBLEhnwroZ5crS, prancha "Reunião · transcrição, ata e notas".

**Fora deste plano** (é o plano 2b, depois da semana): o preparo curto com o andamento no topo, o tocador fixo e o tempo da nota que leva ao áudio (`UI-4`). A semana com o calendário (D-D, "agora") vem entre os dois.

**Um desvio do §3.3, de propósito:** o spec põe Falantes e Exportar no cabeçalho da reunião, acima das abas. Aqui eles ficam onde estão, na barra da aba Transcrição. A gaveta de Falantes lê o estado da revisão montada — no cabeçalho, ela teria de funcionar com a reunião aberta direto na aba Ata, antes de a revisão existir —, e a barra da Transcrição é a mesma que o tocador fixo do 2b refaz. Mudam de lugar juntos, no 2b.

## Global Constraints

- CSP da página é `style-src 'self'` sem `'unsafe-inline'`: atributo `style` no HTML é ignorado em silêncio; estilo é classe no `app.css`, valor dinâmico é `elemento.style.x = …` (CSSOM).
- Nunca `innerHTML` com texto que veio do disco ou do modelo; nó com `textContent`.
- Só tokens do design system no `app.css` (`var(--cor-*)`, `--espaco-*`, `--raio-*`, `--texto-*`, `--fonte-*`); nenhum hexadecimal novo.
- O `data-tema="claro"` do `index.html` é procurado por texto exato pelo núcleo: não mexer.
- `.reuniao` já é a linha de reunião da agenda no Gravador: as classes novas são `reuniao-aberta*`.
- Arquivo novo em `app-net/App/web/` entra no executável sozinho (`web/*.*` no `.csproj`); teste nunca mora lá.
- A página tem de aguentar um núcleo mais velho (`MeetingApp.exe --web <pasta>`): campo ausente na gravação (`tem_ata`, `pendencias`) degrada para "sem selo", nunca lança.
- Texto em português, na voz do app; "convidados", nunca "participantes".
- Publicar só pelo `tools/publicar.sh`, com o app fechado pela bandeja pelo dono — nunca `dotnet publish` na mão, nunca matar um `MeetingApp.exe` aberto.
- Comentários no estilo do código em volta: português, dizendo **por quê**, apontando o doc quando a razão mora num.
- `export PATH="$HOME/.dotnet:$PATH"` antes de qualquer `dotnet`.
- Tudo roda no worktree `/home/andre/projects/mt-reuniao-abas` (Task 1, Step 1), nunca no checkout principal; `git add` sempre com os caminhos da tarefa, nunca `-A` nem `.`.

## Review Focus

1. **Trocar de aba no meio de uma revisão não perde nada** — nome de falante e edição de trecho gravam com atraso, e remontar a revisão a cada troca jogaria fora o que ainda não foi para o disco. Provado em `prova_reuniao_abas` ("voltar à Transcrição traz a MESMA revisão", busca preservada).
2. **Gerar a ata e sair da aba Ata antes de ela ficar pronta** — a ata tem de estar lá quando se volta, e o botão tem de dizer "Refazer ata". `prova_reuniao_gerar_ata`.
3. **O selo "N pendências" da aba Ata depois de gerar ou refazer ali dentro** — ele nasce do resumo da lista, lido antes da ata nova; tem de seguir a ata, e sumir quando ela vem sem pendência. `prova_reuniao_gerar_ata`.
4. **Endereços que já existem** — `#atas` (das fotos e do `--tela` antigo) cai na lista, e não numa tela em branco; `#revisao=N&notas` abre a reunião na aba Notas. `prova_endereco_de_atas`, `prova_reuniao_pelo_endereco`.
5. **Janela baixa (1280×520)** — a aba que recebe o foco quando o painel de Reuniões pede "Abrir a ata" não fica debaixo da barra fixa do topo. `prova_ata_na_reuniao_pedida` com `FOCO_A_VISTA`.

---

## Mapa de arquivos

| arquivo | o quê |
|---|---|
| `app-net/App/web/reuniao.js` | **novo** — a reunião aberta: abas, painéis, teclado, selo de contagem |
| `app-net/App/web/atas.js` | T1: + `montarAta`, `carregarAta`, `botoesDaAta`; `acompanhar` recebe um callback. T2: a tela de Atas e o cartão saem |
| `app-net/App/web/app.js` | T1: `abrirGravacao(g, { aba })` e `abrirResultado` usam `telaDaReuniao`. T2: `abrirAtas` e o botão saem; `#atas` cai na lista |
| `app-net/App/web/revisao.js` | T1: o botão e a gaveta de Notas saem |
| `app-net/App/web/reunioes.js` | T1: o próximo passo da ata abre a reunião na aba Ata |
| `app-net/App/web/index.html` | T1: a gaveta de notas sai. T2: o trilho vira Gravador · Reuniões · Ajustes |
| `app-net/App/web/trilho.js` | T2: a ata acende a bolinha de Reuniões |
| `app-net/App/web/app.css` | T1: as abas; a ata na aba. T2: o CSS do cartão, da dobra e do `.gravacao` sai |
| `tools/provar_reunioes.py` | T1: sete provas da reunião com abas. T2: duas do trilho e do endereço |
| `tools/checar_transcricao.py` | T2: a seção de Atas passa a percorrer a aba Ata |
| `docs/superpowers/specs/2026-09-23-ui-ux.md` | T3: o estado do plano 2 e da D-A |

Os diffs abaixo foram tirados de um ensaio completo das duas tarefas, com as provas verdes em cada uma — o de cada tarefa se aplica sobre o estado da anterior.

---

### Task 1: A reunião com abas

**Files:**
- Create: `app-net/App/web/reuniao.js`
- Modify: `app-net/App/web/atas.js`, `app-net/App/web/app.js`, `app-net/App/web/revisao.js`, `app-net/App/web/reunioes.js`, `app-net/App/web/index.html`, `app-net/App/web/app.css`
- Test: `tools/provar_reunioes.py`

**Interfaces:**
- Consumes: `telaDeRevisao(g, dados, { cabecalho, tela, aoRefazer, aoApagar })` e `abrirPainel(qual)` de `revisao.js`; `blocoDeNotas(caminho, { linhas })` de `notas.js` (devolve `{ raiz, campo }`); `duracao`, `quando`, `tituloDe` de `app.js`; os campos `tem_ata`, `pendencias`, `convidados`, `cliente`, `projeto` da op `gravacoes` (plano 1).
- Produces:
  - `telaDaReuniao(g, dados, { cabecalho, tela, aba = "transcricao", aoRefazer, aoApagar })` em `reuniao.js` — `aba` ∈ `"transcricao" | "ata" | "notas"`; qualquer outra cai em Transcrição.
  - `abrirPainel(qual)` em `reuniao.js` — `qual` de aba troca a aba; o resto (`falantes`, `exportar`, `editar:N`) vai para a revisão.
  - `montarAta(painel, g, { aoContar })` em `atas.js` — `aoContar(n: number)` a cada leitura da ata.
  - `abrirGravacao(g, { aba = "transcricao" } = {})` em `app.js`.
  - DOM: `.reuniao-aberta` > `.reuniao-aberta__abas[role=tablist]` > `button.reuniao-aberta__aba#aba-{id}[data-aba][role=tab]` (com `.reuniao-aberta__conta` opcional); painéis `section.reuniao-aberta__painel#painel-{id}[role=tabpanel]`. Na ata: `.ata[data-gravacao]` > `.ata__topo` (`.ata__tipo select`, `button[data-acao="ata"]`), `.ata__painel`, `.ata__corpo` > `.ata__cabeca` (`.ata__estado`, Copiar, Exportar), `.ata__texto`.

- [ ] **Step 1: O worktree e o registro**

```bash
git -C /home/andre/projects/meeting-transcription worktree add /home/andre/projects/mt-reuniao-abas -b ui-02-reuniao-com-abas main
cd /home/andre/projects/mt-reuniao-abas
git log --oneline -1   # ecbb02d Merge: a lista de Reuniões que se procura
```

- [ ] **Step 2: As provas da reunião com abas**

A ponte falsa passa a guardar atas em `window.__atas`, para uma prova poder "escrever" a ata que o núcleo terminaria. As duas provas do painel que levavam à tela de Atas passam a levar à aba; cinco são novas. A janela baixa das duas primeiras continua: agora é a aba com o foco que não pode ficar debaixo da barra.

```diff
diff --git a/tools/provar_reunioes.py b/tools/provar_reunioes.py
--- a/tools/provar_reunioes.py
+++ b/tools/provar_reunioes.py
@@ -90,7 +90,8 @@ PONTE_FALSA = r"""
       case "prefs": return { prefs: null };
       case "notas": return { notas: "" };
       case "modelos-de-ata": return { tipos: [{ id: "geral", nome: "Reunião geral" }] };
-      case "ata": return q.gravacao.includes("13-59") ? { ata, ata_velha: false } : { ata: null };
+      case "ata": return window.__atas[q.gravacao] ? { ata: window.__atas[q.gravacao], ata_velha: false }
+        : q.gravacao.includes("13-59") ? { ata, ata_velha: false } : { ata: null };
       default: return {};
     }
   };
@@ -105,6 +106,7 @@ PONTE_FALSA = r"""
     },
   } };
   window.__gravacoes = gravacoes;
+  window.__atas = {};
   window.__emitir = (ev) => {
     for (const f of window.chrome.webview._ouvintes)
       f({ data: JSON.stringify(Object.assign({ id: 0 }, ev)) });
@@ -332,34 +334,158 @@ prova_janela_estreita.janela = (860, 700)
 
 
 
+def abrir_reuniao(pagina, inicio: str) -> None:
+    """Abre a reunião cuja linha começa por `inicio`, pelo duplo clique da lista."""
+    linha_por_titulo(pagina, inicio)
+    pagina.evaluate("() => window.__linha.dispatchEvent(new MouseEvent('dblclick', { bubbles: true }))")
+    pagina.wait_for_selector(".reuniao-aberta [role='tab']", timeout=5000)
+
+
+ABA_ESCOLHIDA = """() => {
+  const a = document.querySelector(".reuniao-aberta [role='tab'][aria-selected='true']");
+  return a ? a.dataset.aba : null;
+}"""
+
+# Um evento de fim de tarefa, como o núcleo empurra.
+FIM_DE_TAREFA = ("([c, t]) => window.__emitir({ tipo: 'transcricoes', transcricoes: { atual: null, ultimo: "
+                 "{ gravacao: c, nome: 'x', tarefa: t, etapa: 'montagem', fracao: 1, texto: '', "
+                 "comecou_em: '', terminou: true, erro: null, cancelada: false } } })")
+TAREFA_EM_CURSO = ("([c, t, e]) => window.__emitir({ tipo: 'transcricoes', transcricoes: { atual: "
+                   "{ gravacao: c, nome: 'x', tarefa: t, etapa: e, fracao: 0.5, texto: 'lendo', "
+                   "comecou_em: '', terminou: false, erro: null, cancelada: false }, ultimo: null } })")
+
+
 def prova_ata_na_reuniao_pedida(pagina) -> None:
     linha_por_titulo(pagina, "Comunicação")
     pagina.evaluate("() => window.__linha.click()")
     pagina.click(".reunioes__painel [data-acao='abrir-ata']")
-    pagina.wait_for_selector(".ata", timeout=5000)
-    pagina.wait_for_timeout(100)
-    aberta = pagina.evaluate("""() => {
-        const c = [...document.querySelectorAll('.ata')]
-          .find((x) => x.dataset.gravacao.includes('13-59'));
-        const d = c && c.querySelector('details.ata__dobra');
-        return Boolean(d && d.open);
-    }""")
-    conferir(aberta, "'Abrir a ata' leva a Atas com a ata daquela reunião aberta")
-    foco = pagina.evaluate("() => document.activeElement && document.activeElement.tagName")
-    conferir(foco == "SUMMARY",
-             f"e o foco vai para a ata aberta, não para 'Refazer ata', que um Enter dispararia ({foco!r})")
-    conferir(pagina.evaluate(FOCO_A_VISTA), "e o que tem o foco está à vista, e não debaixo da barra do topo")
+    pagina.wait_for_selector(".reuniao-aberta [role='tab']", timeout=5000)
+    conferir(pagina.evaluate(ABA_ESCOLHIDA) == "ata", "'Abrir a ata' abre a reunião na aba Ata")
+    foco = pagina.evaluate("() => document.activeElement && document.activeElement.dataset.aba")
+    conferir(foco == "ata", f"com o foco na aba, e não num botão que um Enter dispararia ({foco!r})")
+    conferir(pagina.evaluate(FOCO_A_VISTA), "e a aba com o foco está à vista, e não debaixo da barra do topo")
+    pagina.wait_for_selector("#painel-ata .ata__texto", timeout=5000)
+    conferir("tom passa a ser separado" in pagina.text_content("#painel-ata .ata__texto"),
+             "e a ata daquela reunião está ali, aberta")
 
 
 def prova_gerar_ata_na_reuniao_pedida(pagina) -> None:
     linha_por_titulo(pagina, "Sherlock")
     pagina.evaluate("() => window.__linha.click()")
     pagina.click(".reunioes__painel [data-acao='gerar-ata']")
-    pagina.wait_for_selector(".ata", timeout=5000)
-    pagina.wait_for_timeout(100)
-    foco = pagina.evaluate("() => document.activeElement && document.activeElement.dataset.acao")
-    conferir(foco == "ata", f"'Gerar a ata' leva ao botão de gerar daquela reunião ({foco!r})")
-    conferir(pagina.evaluate(FOCO_A_VISTA), "e o botão está à vista, e não debaixo da barra do topo")
+    pagina.wait_for_selector("#painel-ata [data-acao='ata']", timeout=5000)
+    conferir(pagina.evaluate(ABA_ESCOLHIDA) == "ata", "'Gerar a ata' abre a reunião na aba Ata")
+    conferir(pagina.text_content("#painel-ata [data-acao='ata']") == "Gerar ata",
+             "que oferece gerar, porque ainda não há ata")
+
+
+def prova_reuniao_abas(pagina) -> None:
+    abrir_reuniao(pagina, "Comunicação")
+    abas = pagina.eval_on_selector_all(".reuniao-aberta [role='tab']", "els => els.map((e) => e.dataset.aba)")
+    conferir(abas == ["transcricao", "ata", "notas"], f"a reunião tem as três abas ({abas})")
+    conferir(pagina.evaluate(ABA_ESCOLHIDA) == "transcricao", "e abre na Transcrição")
+    conferir(pagina.is_visible(".revisao"), "que é a revisão de sempre")
+    em_cima = pagina.evaluate("""() => {
+        const abas = document.querySelector("[role='tablist']").getBoundingClientRect();
+        const painel = document.querySelector("#painel-transcricao").getBoundingClientRect();
+        return abas.bottom <= painel.top + 1 && painel.width >= abas.width - 1;
+    }""")
+    conferir(em_cima, "as abas ficam em cima do conteúdo, e o conteúdo tem a largura delas")
+    conferir(pagina.text_content("#titulo") == "Comunicação Beegol + App", "a barra do topo diz a reunião")
+    # A revisão não se remonta ao trocar de aba: ela guarda na memória nomes de
+    # falante e edições que gravam com atraso.
+    pagina.fill(".revisao input[type='search']", "começar")
+    pagina.evaluate("() => { window.__revisao = document.querySelector('.revisao'); }")
+    pagina.click(".reuniao-aberta [data-aba='notas']")
+    conferir(pagina.evaluate(ABA_ESCOLHIDA) == "notas", "clicar em Notas troca de aba")
+    conferir(pagina.is_visible("#painel-notas .notas"), "e mostra o bloco de notas")
+    conferir(not pagina.is_visible(".revisao"), "a revisão fica escondida")
+    pagina.click(".reuniao-aberta [data-aba='transcricao']")
+    conferir(pagina.evaluate("() => window.__revisao === document.querySelector('.revisao') "
+                             "&& window.__revisao.isConnected"),
+             "voltar à Transcrição traz a MESMA revisão, sem remontar")
+    conferir(pagina.input_value(".revisao input[type='search']") == "começar", "com a busca como estava")
+    notas_na_revisao = pagina.eval_on_selector_all(
+        ".revisao .ferramentas button", "els => els.filter((e) => e.textContent.trim() === 'Notas').length")
+    conferir(notas_na_revisao == 0, "a revisão não tem mais o botão Notas: as notas são uma aba")
+
+
+def prova_reuniao_teclado(pagina) -> None:
+    abrir_reuniao(pagina, "Comunicação")
+    pagina.focus(".reuniao-aberta [data-aba='transcricao']")
+    pagina.keyboard.press("ArrowRight")
+    conferir(pagina.evaluate(ABA_ESCOLHIDA) == "ata", "a seta para a direita vai à próxima aba")
+    conferir(pagina.evaluate("() => document.activeElement.dataset.aba") == "ata", "e leva o foco junto")
+    pagina.keyboard.press("End")
+    conferir(pagina.evaluate(ABA_ESCOLHIDA) == "notas", "End vai à última")
+    pagina.keyboard.press("ArrowRight")
+    conferir(pagina.evaluate(ABA_ESCOLHIDA) == "transcricao", "e a seta dá a volta")
+    paradas = pagina.eval_on_selector_all(".reuniao-aberta [role='tab']",
+                                          "els => els.filter((e) => e.tabIndex === 0).length")
+    conferir(paradas == 1, f"as abas são uma parada de Tab só ({paradas})")
+
+
+def prova_reuniao_ata(pagina) -> None:
+    abrir_reuniao(pagina, "Comunicação")
+    largura = "() => Math.round(document.querySelector('.reuniao-aberta').getBoundingClientRect().width)"
+    na_transcricao = pagina.evaluate(largura)
+    pagina.click(".reuniao-aberta [data-aba='ata']")
+    pagina.wait_for_selector("#painel-ata .ata__texto", timeout=5000)
+    na_ata = pagina.evaluate(largura)
+    conferir(na_ata == na_transcricao,
+             f"a página não muda de largura ao trocar de aba ({na_transcricao} → {na_ata} px)")
+    conferir("tom passa a ser separado" in pagina.text_content("#painel-ata .ata__texto"),
+             "a aba Ata mostra a ata da reunião")
+    conferir(pagina.locator("#painel-ata details").count() == 0, "aberta, sem dobra: a aba é a ata")
+    conferir(pagina.locator("#painel-ata .ata__tipo select").count() == 1, "com o tipo, para refazer")
+    botao = pagina.text_content("#painel-ata [data-acao='ata']")
+    conferir(botao == "Refazer ata", f"e o botão diz refazer, porque a ata existe ({botao!r})")
+    conferir(pagina.locator("#painel-ata >> text=Copiar").count() == 1, "dá para copiar")
+    conferir(pagina.locator("#painel-ata >> text=Exportar").count() == 1, "e para exportar")
+
+
+def prova_reuniao_gerar_ata(pagina) -> None:
+    # Gerar na aba, sair dela no meio, e a ata chegar mesmo assim.
+    abrir_reuniao(pagina, "Sherlock")
+    pagina.click(".reuniao-aberta [data-aba='ata']")
+    pagina.wait_for_selector("#painel-ata [data-acao='ata']", timeout=5000)
+    caminho = pagina.evaluate("() => document.querySelector('#painel-ata .ata').dataset.gravacao")
+    pagina.click("#painel-ata [data-acao='ata']")
+    pagina.evaluate(TAREFA_EM_CURSO, [caminho, "ata", "lendo"])
+    pagina.wait_for_timeout(50)
+    conferir(pagina.is_visible("#painel-ata .aa-progresso"), "gerar mostra o andamento na aba")
+    pagina.click(".reuniao-aberta [data-aba='notas']")
+    pagina.evaluate("(c) => { window.__atas[c] = '# Ata\\n\\n## Resumo\\n\\nO NOC assume o sábado.\\n\\n"
+                    "## Pendências\\n\\n- [ ] Escalar o sábado — **Dimi** — sexta\\n'; }", caminho)
+    pagina.evaluate(FIM_DE_TAREFA, [caminho, "ata"])
+    pagina.wait_for_timeout(80)
+    pagina.click(".reuniao-aberta [data-aba='ata']")
+    pagina.wait_for_selector("#painel-ata .ata__texto", timeout=5000)
+    conferir("NOC assume" in pagina.text_content("#painel-ata .ata__texto"),
+             "a ata que terminou com a aba escondida está lá quando se volta")
+    conferir(pagina.text_content("#painel-ata [data-acao='ata']") == "Refazer ata", "e o botão vira refazer")
+    # O selo da aba vem do resumo da lista, lido antes de a ata existir: ele
+    # tem de acompanhar a ata escrita aqui, e não o disco de quando se abriu.
+    selo = lambda: pagina.locator("#aba-ata .reuniao-aberta__conta")  # noqa: E731
+    conferir(selo().count() == 1 and selo().text_content() == "1 pendência",
+             f"a aba Ata passa a contar a pendência da ata nova ({selo().all_text_contents()})")
+    pagina.click("#painel-ata [data-acao='ata']")
+    # O clique é assíncrono: o fim só é ouvido depois que o pedido volta e o
+    # andamento aparece.
+    pagina.wait_for_selector("#painel-ata .aa-progresso", timeout=5000)
+    pagina.evaluate("(c) => { window.__atas[c] = '# Ata\\n\\n## Resumo\\n\\nNada ficou pendente.\\n'; }",
+                    caminho)
+    pagina.evaluate(FIM_DE_TAREFA, [caminho, "ata"])
+    pagina.wait_for_function("() => document.querySelector('#painel-ata .ata__texto')"
+                             "?.textContent.includes('Nada ficou')", timeout=5000)
+    conferir(selo().count() == 0, "e refeita sem pendência, o selo some")
+
+
+def prova_reuniao_pelo_endereco(pagina) -> None:
+    # O --tela do app (e as fotos de documentação) abrem uma reunião numa aba.
+    pagina.evaluate("() => { location.hash = 'revisao=4&notas'; location.reload(); }")
+    pagina.wait_for_selector(".reuniao-aberta [role='tab']", timeout=5000)
+    conferir(pagina.evaluate(ABA_ESCOLHIDA) == "notas", "'#revisao=4&notas' abre a reunião na aba Notas")
 
 
 def prova_troca_de_etapa(pagina) -> None:
@@ -458,8 +584,8 @@ def prova_data_nao_rouba_o_foco(pagina) -> None:
 
 
 
-# A tela de Atas com a janela baixa: o cartão pedido precisa de rolagem, e é
-# aí que a barra fixa do topo o cobriria.
+# Com a janela baixa, que é onde a barra fixa do topo cobriria o que recebe o
+# foco. Era o cartão rolado da tela de Atas; é a aba Ata desde que ela existe.
 prova_ata_na_reuniao_pedida.janela = (1280, 520)
 prova_gerar_ata_na_reuniao_pedida.janela = (1280, 520)
 
@@ -474,7 +600,9 @@ FOCO_A_VISTA = """() => {
 PROVAS = [prova_grupos, prova_busca, prova_filtros, prova_sem_resultado, prova_criterios_sobrevivem,
           prova_painel, prova_transcricao_em_curso, prova_janela_estreita, prova_ata_na_reuniao_pedida,
           prova_gerar_ata_na_reuniao_pedida, prova_troca_de_etapa, prova_fim_rele_o_nucleo,
-          prova_teclado, prova_janela_intermediaria, prova_data_nao_rouba_o_foco]
+          prova_teclado, prova_janela_intermediaria, prova_data_nao_rouba_o_foco,
+          prova_reuniao_abas, prova_reuniao_teclado, prova_reuniao_ata, prova_reuniao_gerar_ata,
+          prova_reuniao_pelo_endereco]
 
 
 
```

- [ ] **Step 3: Rodar e ver falhar**

Run: `timeout 500 uv run --with playwright python tools/provar_reunioes.py 2>&1 | grep -E "FALHA|tudo certo"`

Expected — as sete, todas porque a reunião aberta não tem abas:

```
  FALHA  prova_ata_na_reuniao_pedida estourou: Page.wait_for_selector: Timeout 5000ms exceeded.
  FALHA  prova_gerar_ata_na_reuniao_pedida estourou: Page.wait_for_selector: Timeout 5000ms exceeded.
  FALHA  prova_reuniao_abas estourou: Page.wait_for_selector: Timeout 5000ms exceeded.
  FALHA  prova_reuniao_teclado estourou: Page.wait_for_selector: Timeout 5000ms exceeded.
  FALHA  prova_reuniao_ata estourou: Page.wait_for_selector: Timeout 5000ms exceeded.
  FALHA  prova_reuniao_gerar_ata estourou: Page.wait_for_selector: Timeout 5000ms exceeded.
  FALHA  prova_reuniao_pelo_endereco estourou: Page.wait_for_selector: Timeout 5000ms exceeded.
```

- [ ] **Step 4: `reuniao.js`**

Create `app-net/App/web/reuniao.js`:

```js
// A reunião aberta: Transcrição · Ata · Notas.
//
// A ata foi um destino à parte (FASE3.md §4), e esse destino desenhava a mesma
// lista de Reuniões com um botão a mais. Ela é uma propriedade da reunião, e
// mora aqui desde a decisão D-A de docs/superpowers/specs/2026-09-23-ui-ux.md §6.
//
// **As abas não se remontam ao trocar.** Cada uma é montada na primeira vez que
// aparece e depois só se esconde: a revisão guarda na memória os nomes de
// falante e as edições que gravam com atraso (revisao.js, `salvar`), e
// remontá-la a cada troca perderia o que ainda não foi para o disco. A aba da
// ata, escondida no meio de uma geração, continua acompanhando o andamento.
//
// As classes são `reuniao-aberta*`, e não `reuniao*`: `.reuniao` já é a linha
// de reunião da agenda no Gravador, com grade e borda próprias.

import { telaDeRevisao, abrirPainel as abrirPainelDaRevisao } from "/revisao.js";
import { montarAta } from "/atas.js";
import { blocoDeNotas } from "/notas.js";
import { duracao, quando, tituloDe } from "/app.js";

const ABAS = ["transcricao", "ata", "notas"];

/** A troca de aba da reunião aberta, para o endereço (`#revisao=N&notas`). */
let mostrarAba = null;

/**
 * Desenha a reunião aberta dentro de <main>.
 *
 * @param aba qual abrir: "transcricao" (o padrão), "ata" ou "notas". Pedida de
 *   fora — o painel de Reuniões pede "ata" —, a aba recebe o foco: quem clicou
 *   em "Abrir a ata" chega na ata, e não no título.
 */
export function telaDaReuniao(g, dados, { cabecalho, tela, aba = "transcricao", aoRefazer, aoApagar }) {
  const raiz = document.createElement("div");
  raiz.className = "reuniao-aberta";

  const lista = document.createElement("div");
  lista.className = "reuniao-aberta__abas";
  lista.setAttribute("role", "tablist");
  lista.setAttribute("aria-label", "Partes da reunião");
  raiz.appendChild(lista);

  const rotulos = {
    transcricao: ["Transcrição", String(dados.segments.length)],
    ata: ["Ata", g.tem_ata ? rotuloDePendencias(g.pendencias) : null],
    notas: ["Notas", null],
  };

  /** id → { botao, painel, montada } */
  const abas = new Map();
  for (const id of ABAS) {
    const [rotulo, conta] = rotulos[id];
    const botao = document.createElement("button");
    botao.type = "button";
    botao.className = "reuniao-aberta__aba";
    botao.id = `aba-${id}`;
    botao.dataset.aba = id;
    botao.setAttribute("role", "tab");
    botao.setAttribute("aria-controls", `painel-${id}`);
    botao.textContent = rotulo;
    contar(botao, conta);
    botao.addEventListener("click", () => mostrar(id));
    botao.addEventListener("keydown", (e) => aoTeclar(e, id));
    lista.appendChild(botao);

    const painel = document.createElement("section");
    painel.className = "reuniao-aberta__painel";
    painel.id = `painel-${id}`;
    painel.setAttribute("role", "tabpanel");
    painel.setAttribute("aria-labelledby", botao.id);
    painel.hidden = true;
    raiz.appendChild(painel);

    abas.set(id, { botao, painel, montada: false });
  }

  tela.replaceChildren(raiz);
  cabecalho(tituloDe(g), [
    quando(g.nome), duracao(g.duracao_s),
    g.cliente || g.projeto ? [g.cliente, g.projeto].filter(Boolean).join(" › ") : null,
    g.convidados > 0 ? `${g.convidados} convidados` : null,
  ].filter(Boolean).join(" · "), true);

  const montar = {
    // A revisão recebe um cabeçalho que não faz nada: quem diz o título da
    // barra é a reunião, e não a aba.
    transcricao: (p) => telaDeRevisao(g, dados, { cabecalho: () => {}, tela: p, aoRefazer, aoApagar }),
    ata: (p) => montarAta(p, g, { aoContar: (n) => contar(abas.get("ata").botao, rotuloDePendencias(n)) }),
    notas: (p) => p.appendChild(blocoDeNotas(g.caminho, { linhas: 18 }).raiz),
  };

  function mostrar(id, { focar = false } = {}) {
    const alvo = abas.has(id) ? id : "transcricao";
    for (const [outro, a] of abas) {
      const esta = outro === alvo;
      a.botao.setAttribute("aria-selected", String(esta));
      // Uma parada de Tab só para as abas; as setas andam entre elas.
      a.botao.tabIndex = esta ? 0 : -1;
      a.painel.hidden = !esta;
    }
    const a = abas.get(alvo);
    if (!a.montada) {
      a.montada = true;
      montar[alvo](a.painel);
    }
    if (focar) a.botao.focus();
  }

  /** O padrão de abas: setas, Home e End andam, e a aba escolhida já se mostra. */
  function aoTeclar(e, id) {
    const i = ABAS.indexOf(id);
    const destino = {
      ArrowRight: (i + 1) % ABAS.length, ArrowLeft: (i - 1 + ABAS.length) % ABAS.length,
      Home: 0, End: ABAS.length - 1,
    }[e.key];
    if (destino === undefined) return;
    e.preventDefault();
    mostrar(ABAS[destino], { focar: true });
  }

  mostrarAba = mostrar;
  mostrar(aba, { focar: aba !== "transcricao" });
}

function rotuloDePendencias(n) {
  if (!(n > 0)) return null;
  return n === 1 ? "1 pendência" : `${n} pendências`;
}

/** Põe, troca ou tira o selo de contagem de uma aba. */
function contar(botao, texto) {
  let selo = botao.querySelector(".reuniao-aberta__conta");
  if (!texto) { selo?.remove(); return; }
  if (!selo) {
    selo = document.createElement("span");
    selo.className = "reuniao-aberta__conta";
    botao.appendChild(selo);
  }
  selo.textContent = texto;
}

/**
 * Abre por cima o que o endereço pedir: uma aba da reunião, ou um painel da
 * revisão (falantes, exportar, editar:N), que continua sendo dela.
 */
export function abrirPainel(qual) {
  if (ABAS.includes(qual)) return mostrarAba?.(qual, { focar: true });
  return abrirPainelDaRevisao(qual);
}
```

- [ ] **Step 5: A ata que cabe numa aba (`atas.js`)**

`montarAta` é o cartão de Atas sem a dobra e sem o título. `acompanhar` deixa de saber onde a ata aparece: recebe o que fazer ao terminar, e o cartão, que convive com a aba até a Task 2, passa o seu.

```diff
diff --git a/app-net/App/web/atas.js b/app-net/App/web/atas.js
--- a/app-net/App/web/atas.js
+++ b/app-net/App/web/atas.js
@@ -127,7 +127,7 @@ function cartaoDeAta(g, tipos, ctx, emFoco = false) {
     botao.disabled = true;
     try {
       await pedir("gerar-ata", { gravacao: g.caminho, modelo: idDoTipo() });
-      acompanhar(g, botao, painel, corpo);
+      acompanhar(g, botao, painel, () => mostrarAtaExistente(g, corpo, botao, true));
     } catch (e) {
       botao.disabled = false;
       painel.replaceChildren(alerta(e.message, "erro"));
@@ -140,7 +140,7 @@ function cartaoDeAta(g, tipos, ctx, emFoco = false) {
   // "Refazer ata", que um Enter logo depois refaria sem perguntar.
   mostrarAtaExistente(g, corpo, botao, emFoco, emFoco);
 
-  if (emCurso(g.caminho)) acompanhar(g, botao, painel, corpo);
+  if (emCurso(g.caminho)) acompanhar(g, botao, painel, () => mostrarAtaExistente(g, corpo, botao, true));
 
   return raiz;
 }
@@ -257,6 +257,160 @@ function desenharAta(corpo, markdown, velha, abrir, gravacao) {
   corpo.appendChild(dobra);
 }
 
+/**
+ * Copiar e exportar a ata — o que se faz com ela depois de lida.
+ *
+ * @param depois onde pôr o caminho do arquivo exportado, ou o erro: logo
+ *   depois deste elemento.
+ */
+function botoesDaAta(markdown, gravacao, depois) {
+  const copiar = document.createElement("button");
+  copiar.className = "aa-btn aa-btn-texto";
+  copiar.type = "button";
+  copiar.textContent = "Copiar";
+  copiar.addEventListener("click", async () => {
+    // A ata existe para ser colada num e-mail. Markdown puro, e não o texto
+    // renderizado: é o que o Teams, o Slack e o e-mail entendem.
+    await navigator.clipboard.writeText(markdown);
+    copiar.textContent = "Copiado";
+    setTimeout(() => { copiar.textContent = "Copiar"; }, 1500);
+  });
+
+  // Exportar leva a ata para a pasta das atas, que é configurada à parte da de
+  // transcrições — a ata é o que sai para o cliente.
+  const exportar = document.createElement("button");
+  exportar.className = "aa-btn aa-btn-texto";
+  exportar.type = "button";
+  exportar.textContent = "Exportar";
+  exportar.addEventListener("click", async () => {
+    exportar.disabled = true;
+    try {
+      const r = await pedir("exportar-ata", { gravacao: gravacao.caminho,
+                                              nome: tituloDe(gravacao) });
+      exportar.textContent = "Exportada";
+      // O caminho fica na tela: exportar sem dizer onde obriga a procurar.
+      const onde = document.createElement("p");
+      onde.className = "campo__dica";
+      onde.textContent = r.arquivo;
+      depois.after(onde);
+    } catch (e) {
+      depois.after(alerta(e.message, "erro"));
+    } finally {
+      exportar.disabled = false;
+      setTimeout(() => { exportar.textContent = "Exportar"; }, 2000);
+    }
+  });
+
+  return [copiar, exportar];
+}
+
+// ───────────────────────────────────────── a ata na aba da reunião
+
+/**
+ * A ata de uma reunião, na aba Ata da reunião aberta (reuniao.js).
+ *
+ * É o que era o cartão da tela de Atas, sem a dobra e sem o título: numa aba
+ * que é só a ata, dobrá-la esconderia a única coisa que a aba mostra, e o
+ * título da reunião já está na barra do topo.
+ *
+ * @param aoContar recebe o número de pendências a cada vez que a ata é lida —
+ *   ao abrir e depois de cada geração. É o que mantém certo o selo da aba, que
+ *   nasceu do resumo da lista, lido antes de a ata nova existir.
+ */
+export async function montarAta(painel, g, { aoContar } = {}) {
+  let tipos;
+  try {
+    ({ tipos } = await pedir("modelos-de-ata"));
+  } catch (e) {
+    painel.replaceChildren(alerta(e.message, "erro"));
+    return;
+  }
+
+  const raiz = document.createElement("div");
+  raiz.className = "ata ata--aba";
+  raiz.dataset.gravacao = g.caminho;
+
+  const topo = document.createElement("div");
+  topo.className = "ata__topo";
+  const escolha = campo("Tipo de ata", "select", {
+    id: `tipo-${g.nome}`,
+    opcoes: tipos.map((t) => t.nome),
+  });
+  escolha.classList.add("ata__tipo");
+  const botao = document.createElement("button");
+  botao.className = "aa-btn aa-btn-primario";
+  botao.type = "button";
+  botao.textContent = "Gerar ata";
+  botao.dataset.acao = "ata";
+  topo.append(escolha, botao);
+
+  const andamento = document.createElement("div");
+  andamento.className = "ata__painel";
+  const corpo = document.createElement("div");
+  corpo.className = "ata__corpo";
+  raiz.append(topo, andamento, corpo);
+  painel.replaceChildren(raiz);
+
+  const idDoTipo = () => {
+    const nome = escolha.querySelector("select").value;
+    return (tipos.find((t) => t.nome === nome) ?? tipos[0]).id;
+  };
+  const carregar = () => carregarAta(g, corpo, botao, aoContar);
+
+  botao.addEventListener("click", async () => {
+    botao.disabled = true;
+    try {
+      await pedir("gerar-ata", { gravacao: g.caminho, modelo: idDoTipo() });
+      acompanhar(g, botao, andamento, carregar);
+    } catch (e) {
+      botao.disabled = false;
+      andamento.replaceChildren(alerta(e.message, "erro"));
+    }
+  });
+
+  await carregar();
+  if (emCurso(g.caminho)) acompanhar(g, botao, andamento, carregar);
+}
+
+/** Lê a ata do disco e a desenha aberta. Sem ata, o corpo fica vazio e o botão diz gerar. */
+async function carregarAta(g, corpo, botao, aoContar) {
+  let r;
+  try {
+    r = await pedir("ata", { gravacao: g.caminho });
+  } catch {
+    return;   // sem ata é o estado normal de quem nunca gerou
+  }
+  if (!r.ata) {
+    corpo.replaceChildren();
+    return;
+  }
+  botao.textContent = "Refazer ata";
+  botao.className = "aa-btn aa-btn-secundario";
+
+  const cabeca = document.createElement("div");
+  cabeca.className = "ata__cabeca";
+  const estado = document.createElement("p");
+  estado.className = "ata__estado";
+  const linhas = r.ata.split("\n").filter((l) => l.trim().length > 0).length;
+  const pendencias = (r.ata.match(/^- \[ \]/gm) ?? []).length;
+  aoContar?.(pendencias);
+  estado.textContent = pendencias > 0
+    ? `${pendencias} pendência${pendencias > 1 ? "s" : ""} · ${linhas} linhas`
+    : `${linhas} linhas`;
+  cabeca.append(estado, ...botoesDaAta(r.ata, g, cabeca));
+
+  const texto = document.createElement("div");
+  texto.className = "ata__texto";
+  texto.append(...renderizar(r.ata));
+
+  corpo.replaceChildren(cabeca);
+  if (r.ata_velha) {
+    corpo.appendChild(alerta(
+      "A transcrição foi corrigida depois que esta ata foi escrita. Vale refazer.", "aviso"));
+  }
+  corpo.appendChild(texto);
+}
+
 /**
  * Markdown suficiente para uma ata, sem biblioteca.
  *
@@ -333,8 +487,13 @@ function comNegrito(texto) {
   return nos;
 }
 
-/** A geração em curso, desenhada do registro do núcleo — como a transcrição. */
-function acompanhar(g, botao, painel, corpo) {
+/**
+ * A geração em curso, desenhada do registro do núcleo — como a transcrição.
+ *
+ * @param aoTerminar o que fazer quando a ata fica pronta: reler e desenhar.
+ *   Quem sabe onde a ata aparece é quem chamou — a aba ou o cartão.
+ */
+function acompanhar(g, botao, painel, aoTerminar) {
   botao.disabled = true;
   botao.textContent = "Escrevendo…";
 
@@ -387,6 +546,6 @@ function acompanhar(g, botao, painel, corpo) {
     }
     if (fim.erro) { painel.replaceChildren(alerta(fim.erro, "erro")); return; }
 
-    mostrarAtaExistente(g, corpo, botao, true);
+    aoTerminar();
   });
 }
```

- [ ] **Step 6: Abrir a gravação na aba pedida (`app.js`, `reunioes.js`)**

```diff
diff --git a/app-net/App/web/app.js b/app-net/App/web/app.js
--- a/app-net/App/web/app.js
+++ b/app-net/App/web/app.js
@@ -1,5 +1,5 @@
 import { pedir } from "/ponte.js";
-import { telaDeRevisao, abrirPainel } from "/revisao.js";
+import { telaDaReuniao, abrirPainel } from "/reuniao.js";
 import { telaDeAjustes } from "/configuracoes.js";
 import { telaDoGravador } from "/gravador.js";
 import { abrirGaveta, fecharGavetas, pararAudio, alerta, campo, secao,
@@ -625,7 +625,7 @@ async function abrirResultado(g) {
     const r = await pedir("transcricao", { gravacao: g.caminho });
     if (!r.transcricao) throw new Error("a transcrição não foi encontrada");
     g.transcrita = true;
-    telaDeRevisao(g, JSON.parse(r.transcricao), { cabecalho, tela });
+    telaDaReuniao(g, JSON.parse(r.transcricao), { cabecalho, tela });
   } catch (e) {
     tela.replaceChildren(alerta(e.message, "erro"));
   }
@@ -694,7 +694,14 @@ async function transcrever(g, botao, painel, modeloDeDiarizacao = null) {
   }
 }
 
-export async function abrirGravacao(g) {
+/**
+ * Abre uma gravação: a reunião com abas, se ela já foi transcrita, ou a tela de
+ * transcrever, se não foi.
+ *
+ * @param aba qual aba da reunião abrir — "transcricao" (o padrão), "ata" ou
+ *   "notas". O painel de Reuniões pede "ata" para o próximo passo da ata.
+ */
+export async function abrirGravacao(g, { aba = "transcricao" } = {}) {
   fecharGavetas();
   if (!g.transcrita) return telaDePreparo(g);
 
@@ -706,8 +713,8 @@ export async function abrirGravacao(g) {
     if (r.transcricao) {
       cabecalho(tituloDe(g), "carregando…", true);
       tela.replaceChildren();
-      telaDeRevisao(g, JSON.parse(r.transcricao), {
-        cabecalho, tela,
+      telaDaReuniao(g, JSON.parse(r.transcricao), {
+        cabecalho, tela, aba,
         aoRefazer: () => { g.transcrita = false; telaDePreparo(g); },
         aoApagar: botaoApagarGravacao(g),
       });
@@ -722,8 +729,8 @@ export async function abrirGravacao(g) {
     tela.replaceChildren(alerta("A transcrição não foi encontrada.", "erro"));
     return;
   }
-  telaDeRevisao(g, JSON.parse(r.transcricao), {
-    cabecalho, tela, aoApagar: botaoApagarGravacao(g),
+  telaDaReuniao(g, JSON.parse(r.transcricao), {
+    cabecalho, tela, aba, aoApagar: botaoApagarGravacao(g),
   });
 }
 
```

```diff
diff --git a/app-net/App/web/reunioes.js b/app-net/App/web/reunioes.js
--- a/app-net/App/web/reunioes.js
+++ b/app-net/App/web/reunioes.js
@@ -11,7 +11,7 @@
 import { pedir } from "/ponte.js";
 import { alerta, anunciar } from "/pecas.js";
 import { assinarTranscricoes, emCurso, ultimoResultado } from "/transcricoes.js";
-import { duracao, quando, tituloDe, abrirGravacao, abrirGravador, abrirAtas } from "/app.js";
+import { duracao, quando, tituloDe, abrirGravacao, abrirGravador } from "/app.js";
 import { agruparPorDia, clientesDe, estadoDe, filtrar, horaDe, hojeLocal, proximoPasso,
          ESTADOS, PERIODOS, SEM_CLIENTE } from "/reunioes-regras.js";
 
@@ -576,13 +576,11 @@ function textoDaAta(g, rodando) {
 }
 
 /**
- * O botão do próximo passo leva aonde o passo se dá.
- *
- * A ata ainda mora em Atas; o plano 2 a traz para dentro da reunião, e aí
- * esta função é o único lugar a mudar.
+ * O botão do próximo passo leva aonde o passo se dá: transcrever na tela de
+ * transcrever, e tudo o que é da ata na aba Ata da reunião.
  */
 function seguir(g, acao) {
   if (acao === "transcrever" || acao === "acompanhar-transcricao") return abrirGravacao(g);
-  return abrirAtas({ foco: g.caminho });
+  return abrirGravacao(g, { aba: "ata" });
 }
 
```

- [ ] **Step 7: As notas saem da gaveta (`revisao.js`, `index.html`)**

```diff
diff --git a/app-net/App/web/revisao.js b/app-net/App/web/revisao.js
--- a/app-net/App/web/revisao.js
+++ b/app-net/App/web/revisao.js
@@ -13,7 +13,6 @@
 import { pedir } from "/ponte.js";
 import { corDoFalante, abrirGaveta, pararAudio, secao, campo, alerta,
          campoComSugestoes, preencherSugestoes, confirmar } from "/pecas.js";
-import { blocoDeNotas } from "/notas.js";
 import { listaDeTrechos } from "/lista-de-trechos.js";
 
 let estado = null;
@@ -80,7 +79,6 @@ function marcarEstado(texto, erro = false) {
 
 export function abrirPainel(qual) {
   if (qual === "falantes") abrirFalantes();
-  else if (qual === "notas") abrirNotas();
   else if (qual === "exportar") abrirExportacao();
   else if (qual.startsWith("editar")) editar(Number(qual.split(":")[1] ?? 0));
 }
@@ -145,18 +143,6 @@ export function telaDeRevisao(gravacao, dados, { cabecalho, tela, aoRefazer, aoA
   botaoFalantes.textContent = "Falantes";
   botaoFalantes.addEventListener("click", abrirFalantes);
 
-  // As notas escritas na reunião, ao lado da transcrição dela.
-  //
-  // Em gaveta pelo mesmo motivo dos falantes: mexer nelas sem perder o lugar no
-  // texto. Quem lê a transcrição dias depois quer conferir o que anotou na hora
-  // — e, quando a ata por LLM chegar, é este texto que vale mais que o que o
-  // modelo ouviu (FASE3.md §3).
-  const botaoNotas = document.createElement("button");
-  botaoNotas.className = "aa-btn aa-btn-secundario";
-  botaoNotas.type = "button";
-  botaoNotas.textContent = "Notas";
-  botaoNotas.addEventListener("click", abrirNotas);
-
   const botaoExportar = document.createElement("button");
   botaoExportar.className = "aa-btn aa-btn-primario";
   botaoExportar.type = "button";
@@ -174,7 +160,9 @@ export function telaDeRevisao(gravacao, dados, { cabecalho, tela, aoRefazer, aoA
   estadoSalvo.className = "campo__dica";
   estadoSalvo.id = "estado-salvo";
 
-  ferramentas.append(busca, parar, estadoSalvo, botaoNotas, botaoFalantes, botaoExportar);
+  // As notas não têm botão aqui desde que viraram a aba Notas da reunião
+  // (reuniao.js): a aba não tira o lugar no texto, que era o motivo da gaveta.
+  ferramentas.append(busca, parar, estadoSalvo, botaoFalantes, botaoExportar);
 
   // As duas que destroem trabalho vão para um invólucro próprio, e o CSS o
   // empurra para a direita atrás de um fio. Antes elas eram apenas o sétimo e o
@@ -479,22 +467,6 @@ modal.addEventListener("close", () => {
 
 // ───────────────────────────────────────────────────── falantes
 
-/**
- * A gaveta de notas.
- *
- * Monta um editor novo a cada abertura e o joga fora ao fechar: o bloco carrega
- * do disco ao montar e grava ao perder o foco, então guardá-lo entre aberturas
- * só criaria a chance de mostrar um texto velho depois de alguém editar o
- * arquivo por fora.
- */
-function abrirNotas() {
-  const corpo = document.getElementById("corpo-notas");
-  const bloco = blocoDeNotas(estado.gravacao.caminho, { linhas: 18 });
-  corpo.replaceChildren(bloco.raiz);
-  abrirGaveta("gaveta-notas");
-  bloco.campo.focus();
-}
-
 function abrirFalantes() {
   const corpo = document.getElementById("corpo-falantes");
   corpo.replaceChildren();
```

```diff
diff --git a/app-net/App/web/index.html b/app-net/App/web/index.html
--- a/app-net/App/web/index.html
+++ b/app-net/App/web/index.html
@@ -123,14 +123,6 @@
   <div class="gaveta__corpo" id="corpo-falantes"></div>
 </aside>
 
-<aside class="gaveta gaveta--larga" id="gaveta-notas" hidden aria-label="Notas da reunião">
-  <div class="gaveta__topo">
-    <h2>Notas da reunião</h2>
-    <button class="aa-btn aa-btn-texto" data-fechar>✕</button>
-  </div>
-  <div class="gaveta__corpo" id="corpo-notas"></div>
-</aside>
-
 <aside class="gaveta" id="gaveta-exportar" hidden aria-label="Exportar">
   <div class="gaveta__topo">
     <h2>Exportar</h2>
```

- [ ] **Step 8: As abas no CSS (`app.css`)**

```diff
diff --git a/app-net/App/web/app.css b/app-net/App/web/app.css
--- a/app-net/App/web/app.css
+++ b/app-net/App/web/app.css
@@ -524,10 +524,57 @@ textarea.aa-entrada { font-family: inherit; resize: vertical; }
 .revisao { display: grid; gap: var(--espaco-3); }
 
 /* O .aa-pagina do design system respira 64px em cima, que é o certo para uma
- * página de documento. A revisão não é uma: a barra de ferramentas gruda no
- * topo logo abaixo do cabeçalho, e esses 64px só empurram a transcrição para
+ * página de documento. A reunião aberta não é uma: na Transcrição, a barra de
+ * ferramentas gruda no topo logo abaixo das abas, e esses 64px só empurram a transcrição para
  * fora da janela — na tela em que mais se lê do app inteiro. */
-.aa-pagina:has(> .revisao) { padding-top: var(--espaco-5); }
+/* A reunião ocupa a largura da página inteira em qualquer aba. Sem o width, a
+ * .aa-pagina (margin auto num item de grade) encolhe até o conteúdo: a aba Ata
+ * ficava com 390 px no meio da tela, e a página pulava de largura a cada troca
+ * de aba. O border-box impede o padding de somar aos 100%. */
+.aa-pagina:has(> .reuniao-aberta) {
+  box-sizing: border-box;
+  width: 100%;
+  padding-top: var(--espaco-5);
+}
+
+/* ─────────────────────────────────────────────────── a reunião aberta
+ *
+ * Transcrição · Ata · Notas (reuniao.js). A aba escolhida tem o fio embaixo
+ * e o texto forte; as outras, o texto suave. Ver o mockup, prancha "Reunião ·
+ * transcrição, ata e notas". */
+.reuniao-aberta { display: grid; gap: var(--espaco-4); }
+
+.reuniao-aberta__abas {
+  display: flex;
+  gap: var(--espaco-5);
+  border-bottom: 1px solid var(--cor-borda);
+}
+
+.reuniao-aberta__aba {
+  display: inline-flex;
+  align-items: center;
+  gap: var(--espaco-2);
+  padding: var(--espaco-2) var(--espaco-1);
+  margin-bottom: -1px;
+  border: 0;
+  border-bottom: 2px solid transparent;
+  background: transparent;
+  color: var(--cor-texto-suave);
+  font: 600 var(--texto-corpo) / 1.2 var(--fonte-ui);
+  cursor: pointer;
+}
+.reuniao-aberta__aba:hover { color: var(--cor-texto-forte); }
+.reuniao-aberta__aba[aria-selected="true"] { color: var(--cor-texto-forte); border-bottom-color: var(--cor-acao); }
+.reuniao-aberta__aba:focus-visible { outline: none; box-shadow: var(--anel-foco); border-radius: var(--raio-pequeno); }
+
+.reuniao-aberta__conta {
+  font-size: var(--texto-rotulo);
+  font-weight: 700;
+  color: var(--cor-texto-suave);
+  background: var(--cor-superficie-2);
+  border-radius: var(--raio-pilula);
+  padding: 2px var(--espaco-2);
+}
 
 /* Busca, estado e botões ficam colados no topo junto com os filtros: são as
  * ferramentas de revisar, e revisar é justamente percorrer o texto. Ter de
@@ -1394,6 +1441,13 @@ textarea.aa-entrada { font-family: inherit; resize: vertical; }
 
 .ata__tipo { min-width: 14rem; margin: 0; }
 
+/* A ata na aba da reunião: sem título à esquerda, o tipo e o botão encostados. */
+.ata--aba .ata__topo { grid-template-columns: auto 9.5rem; justify-content: start; }
+/* Na coluna do texto, para Copiar e Exportar não irem parar na outra ponta da tela. */
+.ata--aba .ata__corpo { max-width: 68ch; }
+.ata__cabeca { display: flex; align-items: center; gap: var(--espaco-2); flex-wrap: wrap; }
+.ata__estado { margin: 0 auto 0 0; font-size: var(--texto-pequeno); color: var(--cor-texto-suave); }
+
 /* A ata lida: coluna estreita de propósito. Linha de 100 caracteres cansa, e
  * este é o texto que se lê inteiro, ao contrário da transcrição, que se
  * percorre. */
```

- [ ] **Step 9: Rodar e ver passar**

```bash
for f in atas app reuniao reunioes revisao; do node --check app-net/App/web/$f.js; done
timeout 500 uv run --with playwright python tools/provar_reunioes.py 2>&1 | grep -E "FALHA|tudo certo"
timeout 300 uv run --with playwright python tools/checar_transcricao.py 2>&1 | grep -E "FALHA|tudo certo"
```

Expected: `node --check` calado; `tudo certo.` nas duas (113 `ok` no `provar_reunioes`). O `checar_transcricao` ainda percorre a tela de Atas, que continua existindo até a Task 2.

- [ ] **Step 10: Commit**

```bash
git add app-net/App/web/reuniao.js app-net/App/web/atas.js app-net/App/web/app.js \
        app-net/App/web/revisao.js app-net/App/web/reunioes.js app-net/App/web/index.html \
        app-net/App/web/app.css tools/provar_reunioes.py
git commit -m "feat(reuniao): a reunião aberta com abas Transcrição, Ata e Notas"
```

---

### Task 2: Atas sai do trilho

**Files:**
- Modify: `app-net/App/web/index.html`, `app-net/App/web/trilho.js`, `app-net/App/web/app.js`, `app-net/App/web/atas.js`, `app-net/App/web/app.css`
- Test: `tools/provar_reunioes.py`, `tools/checar_transcricao.py`

**Interfaces:**
- Consumes: tudo o que a Task 1 produz; `DESTINO_DA_TAREFA` e `ROTULO_DA_TAREFA` de `trilho.js` (`ROTULO_DA_TAREFA.ata` é "escrevendo a ata de").
- Produces: o trilho com `#ir-gravador`, `#ir-reunioes`, `#ir-config`, nessa ordem, e sem `#ir-atas`; `atas.js` exportando só `montarAta`; `app.js` sem `abrirAtas`.

- [ ] **Step 1: As provas do trilho e do endereço**

```diff
diff --git a/tools/provar_reunioes.py b/tools/provar_reunioes.py
--- a/tools/provar_reunioes.py
+++ b/tools/provar_reunioes.py
@@ -481,6 +481,27 @@ def prova_reuniao_gerar_ata(pagina) -> None:
     conferir(selo().count() == 0, "e refeita sem pendência, o selo some")
 
 
+def prova_trilho_sem_atas(pagina) -> None:
+    conferir(pagina.locator("#ir-atas").count() == 0, "Atas saiu do trilho: a ata mora na reunião")
+    ordem = pagina.eval_on_selector_all(".trilho__item", "els => els.map((e) => e.id)")
+    conferir(ordem == ["ir-gravador", "ir-reunioes", "ir-config"],
+             f"o trilho é Gravador, Reuniões, Ajustes ({ordem})")
+    caminho = pagina.evaluate("() => window.__gravacoes[4].caminho")
+    pagina.evaluate(TAREFA_EM_CURSO, [caminho, "ata", "lendo"])
+    pagina.wait_for_timeout(30)
+    conferir(pagina.get_attribute("#ir-reunioes", "data-ocupado") == "true",
+             "escrever a ata acende a bolinha de Reuniões")
+    rotulo = pagina.get_attribute("#ir-reunioes", "aria-label") or ""
+    conferir("escrevendo a ata" in rotulo, f"e a bolinha diz que é a ata, e não a transcrição ({rotulo!r})")
+
+
+def prova_endereco_de_atas(pagina) -> None:
+    # O --tela atas das fotos antigas não pode cair numa tela em branco.
+    pagina.evaluate("() => { location.hash = 'atas'; location.reload(); }")
+    pagina.wait_for_selector(".reuniao-linha", timeout=5000)
+    conferir(True, "o endereço antigo de Atas cai na lista de Reuniões")
+
+
 def prova_reuniao_pelo_endereco(pagina) -> None:
     # O --tela do app (e as fotos de documentação) abrem uma reunião numa aba.
     pagina.evaluate("() => { location.hash = 'revisao=4&notas'; location.reload(); }")
@@ -602,7 +623,7 @@ PROVAS = [prova_grupos, prova_busca, prova_filtros, prova_sem_resultado, prova_c
           prova_gerar_ata_na_reuniao_pedida, prova_troca_de_etapa, prova_fim_rele_o_nucleo,
           prova_teclado, prova_janela_intermediaria, prova_data_nao_rouba_o_foco,
           prova_reuniao_abas, prova_reuniao_teclado, prova_reuniao_ata, prova_reuniao_gerar_ata,
-          prova_reuniao_pelo_endereco]
+          prova_reuniao_pelo_endereco, prova_trilho_sem_atas, prova_endereco_de_atas]
 
 
 
```

A seção de Atas do `checar_transcricao` passa a percorrer a aba Ata. O que ela conferia continua; mudam o caminho até a ata e a bolinha, que agora é a de Reuniões dizendo "escrevendo a ata". Saem as três conferências que eram da lista de cartões (só as transcritas, a dobra aberta, a dobra fechada ao voltar).

```diff
diff --git a/tools/checar_transcricao.py b/tools/checar_transcricao.py
--- a/tools/checar_transcricao.py
+++ b/tools/checar_transcricao.py
@@ -492,68 +492,58 @@ def main() -> int:
                      pagina.input_value("#vocabulario").strip() != "",
                      pagina.input_value("#vocabulario"))
 
-            # ---- a tela Atas (item 3 da Fase 3)
+            # ---- a ata na aba da reunião (item 3 da Fase 3; aba desde o plano 2 da UI)
             pagina.evaluate("window.chrome.webview._transcritas.add('C:/g/a')")
-            pagina.click("#ir-atas")
-            pagina.wait_for_selector(".ata")
-            conferir("Atas lista só as reuniões transcritas",
-                     pagina.locator(".ata").count() >= 1)
-            conferir("o cartão oferece o tipo de reunião",
-                     pagina.locator(".ata__tipo select").count() >= 1)
-
-            pagina.click("text=Gerar ata")
-            pagina.wait_for_selector(".ata .aa-progresso")
-            # A bolinha da ata é a de Atas, não a de Reuniões: as duas tarefas
-            # dividem o registro, e até 14/08 a tela acendia a errada.
-            conferir("gerar mostra o progresso e a bolinha de Atas acende",
-                     pagina.get_attribute("#ir-atas", "data-ocupado") == "true"
-                     and estado() == "false")
+            pagina.click("#ir-reunioes")
+            pagina.wait_for_selector(".reuniao-linha")
+            conferir("Atas não é mais destino do trilho", pagina.locator("#ir-atas").count() == 0)
+            abrir_da_lista(pagina, "C:/g/a")
+            pagina.wait_for_selector(".reuniao-aberta [data-aba='ata']", timeout=5000)
+            pagina.click(".reuniao-aberta [data-aba='ata']")
+            pagina.wait_for_selector("#painel-ata .ata__tipo select")
+            conferir("a aba Ata oferece o tipo de reunião",
+                     pagina.locator("#painel-ata .ata__tipo select").count() == 1)
+
+            pagina.click("#painel-ata >> text=Gerar ata")
+            pagina.wait_for_selector("#painel-ata .aa-progresso")
+            # A bolinha da ata é a de Reuniões desde que Atas saiu do trilho —
+            # e ela diz que é a ata: até 14/08 a lista dizia "Transcrevendo…"
+            # enquanto a ata era escrita, e era esse o defeito relatado.
+            conferir("gerar mostra o progresso e acende a bolinha de Reuniões",
+                     estado() == "true")
+            conferir("a bolinha diz que está escrevendo a ata",
+                     "escrevendo a ata" in (pagina.get_attribute("#ir-reunioes", "aria-label") or ""))
 
             pagina.evaluate("""window.__ataPronta('C:/g/a',
               '# Ata — Teste\\n\\n## Decisões\\n\\n- **decidido** aqui\\n\\n'
               + '## Pendências\\n\\n- [ ] Mandar a base — **Dimi** — amanhã\\n')""")
-            pagina.wait_for_selector(".ata__texto", timeout=5000)
-            # A ata recém-gerada abre sozinha: quem acabou de mandar escrever
-            # quer ver. As outras ficam dobradas — com onze reuniões na tela,
-            # abrir todas era uma rolagem sem fim.
-            conferir("a ata recém-gerada abre sozinha",
-                     pagina.locator(".ata__dobra[open]").count() == 1)
-            conferir("a ata pronta aparece no cartão",
-                     "decidido" in pagina.inner_text(".ata__texto"))
-            conferir("o resumo da dobra conta as pendências",
-                     "pendência" in pagina.inner_text(".ata__resumo"),
-                     pagina.inner_text(".ata__resumo"))
-
-            pagina.click("#ir-reunioes")
-            pagina.wait_for_selector(".reuniao-linha")
-            pagina.click("#ir-atas")
-            pagina.wait_for_selector(".ata__dobra")
-            conferir("ao voltar, a ata vem fechada",
-                     pagina.locator(".ata__dobra[open]").count() == 0)
+            pagina.wait_for_selector("#painel-ata .ata__texto", timeout=5000)
+            conferir("a ata pronta aparece na aba",
+                     "decidido" in pagina.inner_text("#painel-ata .ata__texto"))
+            conferir("o estado da ata conta as pendências",
+                     "pendência" in pagina.inner_text("#painel-ata .ata__estado"),
+                     pagina.inner_text("#painel-ata .ata__estado"))
+            conferir("a bolinha de Reuniões apaga quando a ata fica pronta", estado() == "false")
 
             # Exportar leva a ata para a pasta das atas — separada da de
             # transcrições, porque os destinos são diferentes.
-            pagina.click(".ata__resumo")
-            pagina.wait_for_selector(".ata__texto")
-            pagina.click("text=Exportar")
+            pagina.click("#painel-ata >> text=Exportar")
             pagina.wait_for_timeout(150)
             conferir("exportar diz onde a ata foi parar",
-                     "atas" in pagina.inner_text(".ata"),
-                     [l for l in pagina.inner_text(".ata").splitlines() if "atas" in l][:1])
+                     "atas" in pagina.inner_text("#painel-ata"),
+                     [l for l in pagina.inner_text("#painel-ata").splitlines() if "atas" in l][:1])
             # O "#" da ata vira h2 e o "##" vira h3 — um nível abaixo do que o
             # Markdown diz, porque a página já tem o h1 na barra do topo. Título
             # de documento dentro de documento quebraria a hierarquia para quem
             # navega por cabeçalho.
             conferir("o markdown vira HTML de verdade",
-                     pagina.locator(".ata__texto h2").count() == 1
-                     and pagina.locator(".ata__texto h3").count() == 2
-                     and pagina.locator(".ata__texto strong").count() >= 2)
+                     pagina.locator("#painel-ata .ata__texto h2").count() == 1
+                     and pagina.locator("#painel-ata .ata__texto h3").count() == 2
+                     and pagina.locator("#painel-ata .ata__texto strong").count() >= 2)
             conferir("a pendência vira caixa marcável",
-                     pagina.locator(".ata__pendencia input").count() == 1)
-            conferir("a bolinha de Atas apaga quando a ata fica pronta",
-                     pagina.get_attribute("#ir-atas", "data-ocupado") == "false")
+                     pagina.locator("#painel-ata .ata__pendencia input").count() == 1)
 
-            # ---- as três bolinhas, cada uma no seu destino
+            # ---- as bolinhas, cada uma no seu destino
             ocupado = lambda id_: pagina.get_attribute(f"#{id_}", "data-ocupado")  # noqa: E731
 
             pagina.evaluate("window.__gravar(true)")
@@ -562,14 +552,10 @@ def main() -> int:
                      ocupado("ir-gravador") == "true")
             conferir("e não acende a de Reuniões", ocupado("ir-reunioes") == "false")
 
-            pagina.click("#ir-atas")
-            pagina.wait_for_selector(".ata")
-            pagina.click("text=Refazer ata")
-            pagina.wait_for_selector(".ata .aa-progresso")
-            conferir("escrever ata acende a bolinha de Atas",
-                     ocupado("ir-atas") == "true")
-            conferir("e a de Reuniões continua apagada — era o defeito relatado",
-                     ocupado("ir-reunioes") == "false")
+            pagina.click("#painel-ata >> text=Refazer ata")
+            pagina.wait_for_selector("#painel-ata .aa-progresso")
+            conferir("escrever ata acende a bolinha de Reuniões",
+                     ocupado("ir-reunioes") == "true")
             conferir("gravação e ata convivem", ocupado("ir-gravador") == "true")
 
             pagina.click("#ir-reunioes")
```

- [ ] **Step 2: Rodar e ver falhar**

Run:
```bash
timeout 500 uv run --with playwright python tools/provar_reunioes.py 2>&1 | grep -E "FALHA|tudo certo"
timeout 300 uv run --with playwright python tools/checar_transcricao.py 2>&1 | grep -E "FALHA|tudo certo"
```

Expected:

```
  FALHA  Atas saiu do trilho: a ata mora na reunião
  FALHA  o trilho é Gravador, Reuniões, Ajustes (['ir-reunioes', 'ir-gravador', 'ir-atas', 'ir-config'])
  FALHA  escrever a ata acende a bolinha de Reuniões
  FALHA  e a bolinha diz que é a ata, e não a transcrição ('')
  FALHA  prova_endereco_de_atas estourou: Page.wait_for_selector: Timeout 5000ms exceeded.
FALHA Atas não é mais destino do trilho
FALHA gerar mostra o progresso e acende a bolinha de Reuniões
FALHA a bolinha diz que está escrevendo a ata
FALHA escrever ata acende a bolinha de Reuniões
```

- [ ] **Step 3: O trilho (`index.html`, `trilho.js`)**

O Gravador sobe para o primeiro lugar — é o primeiro gesto do dia —, e o `aria-current` fica no botão de Reuniões, que só muda de lugar: o app continua abrindo em Reuniões.

```diff
diff --git a/app-net/App/web/index.html b/app-net/App/web/index.html
--- a/app-net/App/web/index.html
+++ b/app-net/App/web/index.html
@@ -60,17 +60,12 @@
        bandeja, e esta é a mesma gravação vista de dentro da janela. -->
   <nav class="trilho" aria-label="Seções">
     <!-- Uma bolinha por destino, acesa pelo trabalho daquele destino:
-         transcrever em Reuniões, gravar no Gravador, escrever ata em Atas.
-         Quem as acende é o web/trilho.js, do estado do núcleo — nunca por
-         tempo. -->
-    <button class="trilho__item" id="ir-reunioes" type="button" aria-current="page"
-            data-ocupado="false">
-      <span class="trilho__icone">
-        <svg viewBox="0 0 24 24" aria-hidden="true"><use href="#i-reunioes"/></svg>
-        <span class="trilho__ponto" aria-hidden="true"></span>
-      </span>
-      <span>Reuniões</span>
-    </button>
+         gravar no Gravador; transcrever, separar falantes e escrever a ata em
+         Reuniões. Quem as acende é o web/trilho.js, do estado do núcleo —
+         nunca por tempo.
+         O Gravador vem primeiro porque é o primeiro gesto do dia; Atas saiu
+         em 24/09/2026 e virou a aba Ata da reunião (docs/superpowers/specs/
+         2026-09-23-ui-ux.md, D-A). -->
     <button class="trilho__item" id="ir-gravador" type="button" data-ocupado="false">
       <span class="trilho__icone">
         <svg viewBox="0 0 24 24" aria-hidden="true"><use href="#i-gravador"/></svg>
@@ -78,14 +73,13 @@
       </span>
       <span>Gravador</span>
     </button>
-    <!-- Atas fica abaixo do Gravador: é o terceiro destino de todo dia, e o
-         que se visita depois da reunião, não durante. -->
-    <button class="trilho__item" id="ir-atas" type="button" data-ocupado="false">
+    <button class="trilho__item" id="ir-reunioes" type="button" aria-current="page"
+            data-ocupado="false">
       <span class="trilho__icone">
-        <svg viewBox="0 0 24 24" aria-hidden="true"><use href="#i-atas"/></svg>
+        <svg viewBox="0 0 24 24" aria-hidden="true"><use href="#i-reunioes"/></svg>
         <span class="trilho__ponto" aria-hidden="true"></span>
       </span>
-      <span>Atas</span>
+      <span>Reuniões</span>
     </button>
     <button class="trilho__item trilho__item--fim" id="ir-config" type="button">
       <svg viewBox="0 0 24 24" aria-hidden="true"><use href="#i-config"/></svg>
```

```diff
diff --git a/app-net/App/web/trilho.js b/app-net/App/web/trilho.js
--- a/app-net/App/web/trilho.js
+++ b/app-net/App/web/trilho.js
@@ -2,9 +2,12 @@
 //
 // Uma por destino, e cada uma acesa pelo trabalho que pertence àquele destino:
 //
-//   Reuniões  — transcrevendo
 //   Gravador  — gravando
-//   Atas      — escrevendo a ata
+//   Reuniões  — transcrevendo, separando falantes, escrevendo a ata
+//
+// A ata acendia a bolinha de Atas até 24/09/2026, quando Atas deixou de ser
+// destino e virou uma aba da reunião. O rótulo da bolinha diz qual das três
+// tarefas está rodando — foi a confusão entre elas o defeito de 14/08.
 //
 // Existem porque este app faz coisas que levam minutos e o usuário sai da tela
 // enquanto elas rodam. Antes da Fase 3 nada disso aparecia fora da tela em que
@@ -13,9 +16,9 @@
 // sabia distinguir. Foi o defeito que o dono do produto viu no primeiro uso.
 //
 // **Gravar não disputa nada com os motores** (capturar áudio não usa GPU), então
-// a bolinha do Gravador pode conviver com qualquer uma das outras duas. As
-// outras duas nunca convivem entre si — o núcleo recusa, porque os modelos não
-// cabem juntos na placa e porque a ata precisa da transcrição pronta.
+// a bolinha do Gravador pode conviver com a de Reuniões. As tarefas de
+// Reuniões nunca rodam juntas — o núcleo recusa, porque os modelos não cabem
+// juntos na placa e porque a ata precisa da transcrição pronta.
 
 import { pedir, assinar } from "/ponte.js";
 import { assinarTranscricoes, transcricoes } from "/transcricoes.js";
@@ -25,7 +28,7 @@ const DESTINO_DA_TAREFA = {
   // A separação de falantes acende a mesma bolinha da transcrição: ela roda
   // sobre a legenda de uma reunião, e é em Reuniões que se vai olhar.
   falantes: "ir-reunioes",
-  ata: "ir-atas",
+  ata: "ir-reunioes",
 };
 
 const ROTULO_DA_TAREFA = {
@@ -44,7 +47,8 @@ function acender(id, ligado, rotuloBase, oQue = "") {
 }
 
 /**
- * Reuniões e Atas: só uma das duas acende, porque só uma das duas roda.
+ * As tarefas de placa acendem todas a bolinha de Reuniões, e só uma roda por
+ * vez — o núcleo recusa a segunda.
  *
  * **Por destino, e não por tarefa.** Transcrição e falantes acendem a MESMA
  * bolinha, e pintar tarefa a tarefa apagava na segunda o que a primeira
@@ -56,7 +60,7 @@ function pintarTrabalhos() {
   const atual = transcricoes().atual;
   for (const id of new Set(Object.values(DESTINO_DA_TAREFA))) {
     const minha = DESTINO_DA_TAREFA[atual?.tarefa] === id;
-    acender(id, minha, id === "ir-atas" ? "Atas" : "Reuniões",
+    acender(id, minha, "Reuniões",
             minha ? `${ROTULO_DA_TAREFA[atual.tarefa]} ${atual.nome}` : "");
   }
 }
```

- [ ] **Step 4: Atas deixa de ser destino (`app.js`)**

```diff
diff --git a/app-net/App/web/app.js b/app-net/App/web/app.js
--- a/app-net/App/web/app.js
+++ b/app-net/App/web/app.js
@@ -8,7 +8,6 @@ import { abrirGaveta, fecharGavetas, pararAudio, alerta, campo, secao,
 import { transcrever as pedirTranscricao, assinarTranscricoes, emCurso,
          ultimoResultado, sincronizar, cancelar } from "/transcricoes.js";
 import { blocoDeNotas } from "/notas.js";
-import { telaDeAtas } from "/atas.js";
 import { telaDeReunioes } from "/reunioes.js";
 import { ligarBolinhas } from "/trilho.js";
 
@@ -761,19 +760,10 @@ export function abrirGravador() {
   return telaDoGravador({ cabecalho, tela });
 }
 
-/** O destino Atas mora em atas.js, pelo mesmo motivo dos outros dois. */
-export function abrirAtas(opcoes = {}) {
-  fecharGavetas();
-  destino("ir-atas");
-  return telaDeAtas({ cabecalho, tela }, opcoes);
-}
-
 // ─────────────────────────────────────────────────────────── ligação
 
 document.getElementById("ir-config").addEventListener("click", () => abrirAjustes());
 document.getElementById("ir-gravador").addEventListener("click", abrirGravador);
-// Embrulhado: o clique mandaria o evento no lugar das opções.
-document.getElementById("ir-atas").addEventListener("click", () => abrirAtas());
 
 document.getElementById("ir-reunioes").addEventListener("click", telaDeLista);
 voltar.addEventListener("click", telaDeLista);
@@ -835,7 +825,9 @@ async function inicio() {
   // a tela por nada. "#config=vozes" cai direto na aba.
   if (tela === "config") return abrirAjustes(arg || "geral");
   if (tela === "gravador") return abrirGravador();
-  if (tela === "atas") return abrirAtas();
+  // Atas deixou de ser destino (a ata é uma aba da reunião): o endereço antigo
+  // cai na lista, e não numa tela em branco.
+  if (tela === "atas") return telaDeLista();
 
   const { gravacoes } = await pedir("gravacoes");
   const g = gravacoes[Number(arg) || 0];
```

- [ ] **Step 5: Só a ata da aba fica (`atas.js`)**

Apague de `atas.js` o trecho que vai de

```js
/**
 * @param foco o caminho de uma gravação. Vem do painel de Reuniões: quem
```

(o comentário de `telaDeAtas`) até a linha antes de

```js
/**
 * Copiar e exportar a ata — o que se faz com ela depois de lida.
```

— são `telaDeAtas`, `cartaoDeAta`, `mostrarAtaExistente` e `desenharAta` inteiros, 241 linhas. O resto do arquivo muda assim:

```diff
diff --git a/app-net/App/web/atas.js b/app-net/App/web/atas.js
--- a/app-net/App/web/atas.js
+++ b/app-net/App/web/atas.js
@@ -1,14 +1,16 @@
-// O destino Atas: escolher a reunião, escolher o tipo, gerar e ler.
+// A ata de uma reunião: escolher o tipo, gerar, acompanhar, ler, copiar e
+// exportar. Mora na aba Ata da reunião aberta (reuniao.js).
 //
-// Gerar e ler acontecem aqui, sem passar por Reuniões — decisão do dono do
-// produto (FASE3.md §4). Reuniões continua sendo gravar, transcrever e revisar;
-// a ata tem vida própria: é o que se copia para o e-mail, o que se relê dias
-// depois, e o que se regenera quando a transcrição foi corrigida.
+// Foi um destino próprio de 14/08 a 24/09/2026 (FASE3.md §4): a ata "tinha vida
+// própria" — é o que se copia para o e-mail, o que se relê dias depois, o que se
+// regenera quando a transcrição foi corrigida. Tudo isso continua; o que saiu
+// foi a segunda lista de reuniões que o destino desenhava para chegar a ela.
+// Ver a decisão D-A de docs/superpowers/specs/2026-09-23-ui-ux.md §6.
 
 import { pedir } from "/ponte.js";
 import { alerta, campo } from "/pecas.js";
 import { assinarTranscricoes, emCurso, ultimoResultado, cancelar } from "/transcricoes.js";
-import { duracao, quando, tituloDe, abrirGravacao } from "/app.js";
+import { tituloDe } from "/app.js";
 
 const ETAPAS = {
   modelo: "Carregando o modelo",
@@ -327,7 +88,7 @@ export async function montarAta(painel, g, { aoContar } = {}) {
   }
 
   const raiz = document.createElement("div");
-  raiz.className = "ata ata--aba";
+  raiz.className = "ata";
   raiz.dataset.gravacao = g.caminho;
 
   const topo = document.createElement("div");
@@ -491,7 +252,7 @@ function comNegrito(texto) {
  * A geração em curso, desenhada do registro do núcleo — como a transcrição.
  *
  * @param aoTerminar o que fazer quando a ata fica pronta: reler e desenhar.
- *   Quem sabe onde a ata aparece é quem chamou — a aba ou o cartão.
+ *   Quem sabe onde a ata aparece é quem chamou.
  */
 function acompanhar(g, botao, painel, aoTerminar) {
   botao.disabled = true;
```

- [ ] **Step 6: O CSS que morreu (`app.css`)**

Saem o cartão `.gravacao*` (da lista antiga, depois só do cartão de Atas), a margem de rolagem do cartão, `.ata__acoes`, a dobra; e o modificador `.ata--aba` se funde nas regras da ata, que agora só existe na aba.

```diff
diff --git a/app-net/App/web/app.css b/app-net/App/web/app.css
--- a/app-net/App/web/app.css
+++ b/app-net/App/web/app.css
@@ -226,35 +226,6 @@ html {
 
 #tela { display: grid; gap: var(--espaco-3); align-content: start; }
 
-.gravacao {
-  display: grid;
-  grid-template-columns: 1fr auto;
-  gap: var(--espaco-4);
-  align-items: center;
-  cursor: pointer;
-  text-align: left;
-  width: 100%;
-  font: inherit;
-  color: inherit;
-  border: 1px solid var(--cor-borda);
-}
-
-.gravacao:hover { border-color: var(--cor-borda-forte); }
-.gravacao:focus-visible { outline: var(--anel-foco); outline-offset: 2px; }
-
-.gravacao__titulo { font-weight: 600; margin: 0 0 var(--espaco-1); }
-
-.gravacao__meta {
-  color: var(--cor-texto-suave);
-  font-size: var(--texto-pequeno);
-  margin: 0;
-  display: flex;
-  gap: var(--espaco-3);
-  flex-wrap: wrap;
-}
-
-.gravacao__avisos { margin-top: var(--espaco-3); display: grid; gap: var(--espaco-2); }
-
 .vazio { color: var(--cor-texto-suave); }
 
 /* ─────────────────────────────────────────────────────── reuniões
@@ -1418,21 +1389,16 @@ textarea.aa-entrada { font-family: inherit; resize: vertical; }
 /* ─────────────────────────────────────────────────────────────── atas */
 
 .ata { display: grid; gap: var(--espaco-3); }
-/* Rolado até ele pelo painel de Reuniões, o cartão para logo abaixo da barra do
- * topo, que é sticky: sem a margem, título e botão focado ficavam debaixo dela. */
-.ata { scroll-margin-top: calc(var(--altura-da-barra) + var(--espaco-4)); }
 
-/* Título à esquerda, tipo e botão à direita: a escolha do tipo fica encostada
- * no botão que a usa, e não perdida no meio do cartão. */
-/* A coluna do botão tem largura fixa, e não `auto`.
- *
- * Com `auto` cada cartão media o próprio botão, e "Refazer ata" é mais largo
- * que "Gerar ata": o seletor e o botão dançavam de linha para linha, cada
- * cartão com um alinhamento diferente do vizinho. Numa lista, o que denuncia
- * isso não é um cartão — são dois, um debaixo do outro. */
+/* O tipo encostado no botão que o usa. A coluna do botão tem largura fixa, e
+ * não `auto`: "Refazer ata" e "Escrevendo…" são mais largos que "Gerar ata", e
+ * com `auto` o botão mudava de tamanho a cada troca de rótulo. Era pior quando
+ * a ata era um cartão numa lista — dois cartões um debaixo do outro, cada um
+ * alinhado de um jeito. */
 .ata__topo {
   display: grid;
-  grid-template-columns: 1fr auto 9.5rem;
+  grid-template-columns: auto 9.5rem;
+  justify-content: start;
   align-items: end;
   gap: var(--espaco-3);
 }
@@ -1441,10 +1407,8 @@ textarea.aa-entrada { font-family: inherit; resize: vertical; }
 
 .ata__tipo { min-width: 14rem; margin: 0; }
 
-/* A ata na aba da reunião: sem título à esquerda, o tipo e o botão encostados. */
-.ata--aba .ata__topo { grid-template-columns: auto 9.5rem; justify-content: start; }
 /* Na coluna do texto, para Copiar e Exportar não irem parar na outra ponta da tela. */
-.ata--aba .ata__corpo { max-width: 68ch; }
+.ata__corpo { max-width: 68ch; }
 .ata__cabeca { display: flex; align-items: center; gap: var(--espaco-2); flex-wrap: wrap; }
 .ata__estado { margin: 0 auto 0 0; font-size: var(--texto-pequeno); color: var(--cor-texto-suave); }
 
@@ -1475,8 +1439,6 @@ textarea.aa-entrada { font-family: inherit; resize: vertical; }
 .ata__pendencia { list-style: none; margin-left: calc(var(--espaco-4) * -1); }
 .ata__pendencia input { margin-right: var(--espaco-2); }
 
-.ata__acoes { display: flex; justify-content: flex-end; flex-wrap: wrap; }
-
 @media (max-width: 900px) {
   .ata__topo { grid-template-columns: 1fr; align-items: stretch; }
 }
@@ -1486,22 +1448,3 @@ textarea.aa-entrada { font-family: inherit; resize: vertical; }
  * ação aqui faria o usuário traduzir duas linguagens para o mesmo fato. */
 .trilho__ponto--gravando { background: var(--cor-erro); }
 
-/* A ata dobrada. Fechada por padrão: com onze reuniões na tela, abrir todas
- * transformava a página numa rolagem sem fim. */
-.ata__dobra { display: grid; gap: var(--espaco-2); }
-
-.ata__resumo {
-  cursor: pointer;
-  color: var(--cor-acao);
-  font-size: var(--texto-pequeno);
-  font-weight: 600;
-  list-style: none;
-  padding: var(--espaco-2) 0;
-}
-
-/* O triângulo padrão do <details> é do navegador e destoa do resto; a seta
- * própria gira ao abrir. */
-.ata__resumo::-webkit-details-marker { display: none; }
-.ata__resumo::before { content: "▸ "; display: inline-block; transition: transform var(--transicao-rapida); }
-.ata__dobra[open] > .ata__resumo::before { transform: rotate(90deg); }
-.ata__resumo:focus-visible { outline: var(--anel-foco); }
```

- [ ] **Step 7: Rodar e ver passar**

```bash
for f in atas app trilho reuniao; do node --check app-net/App/web/$f.js; done
grep -rn "ir-atas\|abrirAtas\|telaDeAtas\|gaveta-notas\|ata--aba\|gravacao__\|ata__dobra" app-net/App/web/
timeout 500 uv run --with playwright python tools/provar_reunioes.py 2>&1 | grep -E "FALHA|tudo certo"
timeout 300 uv run --with playwright python tools/checar_transcricao.py 2>&1 | grep -E "FALHA|tudo certo"
```

Expected: `node --check` e `grep` calados; `tudo certo.` nas duas (117 `ok` no `provar_reunioes`, 43 no `checar_transcricao`).

- [ ] **Step 8: Commit**

```bash
git add app-net/App/web/index.html app-net/App/web/trilho.js app-net/App/web/app.js \
        app-net/App/web/atas.js app-net/App/web/app.css \
        tools/provar_reunioes.py tools/checar_transcricao.py
git commit -m "feat(trilho): Atas sai do trilho e vira a aba Ata da reunião"
```

---

### Task 3: Conferir no app de verdade e fechar o plano

**Files:**
- Modify: `docs/superpowers/specs/2026-09-23-ui-ux.md`

- [ ] **Step 1: As suítes inteiras**

```bash
export PATH="$HOME/.dotnet:$PATH"
dotnet test app-net/Tests/MeetingApp.Tests.csproj 2>&1 | tail -3
node --test tools/web/reunioes-regras.test.mjs 2>&1 | tail -3
```

Expected: 663 aprovados, 0 com falha (nada mudou no núcleo, mas os recursos web entram no binário e há teste do `index.html`); 18 no `node --test`.

- [ ] **Step 2: As fotos, nos dois temas**

Um roteiro descartável no scratchpad (não entra no repositório), que reaproveita a ponte falsa e o servidor da prova:

```python
"""Fotos da reunião aberta: python fotos.py <raiz do worktree> <pasta de saída> [hash ...]."""
import importlib.util, socketserver, sys, threading
from pathlib import Path

raiz, saida = Path(sys.argv[1]), Path(sys.argv[2])
spec = importlib.util.spec_from_file_location("p", raiz / "tools" / "provar_reunioes.py")
p = importlib.util.module_from_spec(spec); spec.loader.exec_module(p)
from playwright.sync_api import sync_playwright

saida.mkdir(parents=True, exist_ok=True)
alvos = sys.argv[3:] or ["", "revisao=4", "revisao=4&ata", "revisao=4&notas"]
socketserver.TCPServer.allow_reuse_address = True
with socketserver.TCPServer(("127.0.0.1", 0), p.Servidor) as srv:
    threading.Thread(target=srv.serve_forever, daemon=True).start()
    with sync_playwright() as pw:
        nav = pw.chromium.launch()
        for tema in ["escuro", "claro"]:
            for alvo in alvos:
                pg = nav.new_page(viewport={"width": 1280, "height": 800})
                pg.add_init_script(p.PONTE_FALSA)
                pg.add_init_script("document.addEventListener('DOMContentLoaded', () => "
                                   f"document.documentElement.dataset.tema = '{tema}')")
                pg.goto(f"http://127.0.0.1:{srv.server_address[1]}/index.html#{alvo}", wait_until="load")
                pg.wait_for_timeout(700)
                nome = (alvo or "lista").replace("=", "-").replace("&", "-")
                pg.screenshot(path=str(saida / f"{tema}-{nome}.png"))
                pg.close()
        nav.close()
    srv.shutdown()
```

Olhar as oito: o trilho com três destinos; a aba escolhida com o fio embaixo; o selo da Ata igual ao estado da ata; a ata na coluna de 68ch com Copiar e Exportar encostados nela; as notas ocupando a largura da página.

- [ ] **Step 3: O estado do plano no spec**

Em `docs/superpowers/specs/2026-09-23-ui-ux.md` §5, a linha do plano 2 se divide — o que este plano entregou, a semana que a D-D subiu, e o que sobra da reunião aberta:

```markdown
| **2 · A reunião aberta** | abas Transcrição · Ata · Notas; Atas sai do trilho | **D-A** | **feito em 24/09/2026** |
| **2a · A semana** | a semana com o calendário próprio (o ponto nos dias com gravação), também no filtro da lista | **D-D** (lida como agora) | a detalhar |
| **2b · A reunião aberta, o resto** | preparo curto com andamento no topo; tocador fixo; Falantes e Exportar no cabeçalho da reunião; tempo da nota que toca (`UI-4`) | plano 2 | a detalhar |
```

e a linha do plano 5 perde a semana:

```markdown
| **5 · O que tem gatilho** | busca no conteúdo; atalhos de janela (`UI-5`) | uso | escrito, sem gatilho |
```

Em §6, no fim da D-A: `**Feita em 24/09/2026** (plano 2).`

```bash
git add docs/superpowers/specs/2026-09-23-ui-ux.md
git commit -m "docs: o plano 2 da UI feito, e a semana antes do resto da reunião"
```

- [ ] **Step 4: O binário**

```bash
tools/publicar.sh --so-build
```

Expected: as réguas passam e o binário fica em `dist/publicar`. Não instala.

- [ ] **Step 5: Instalar — só com o app fechado pelo dono**

Pedir ao dono que feche o PulseMeet **pela bandeja** (fechar a janela só esconde). Nunca matar o processo. Com a confirmação:

```bash
tools/publicar.sh
```

Expected: instala em `AppData\Local\Programs\MeetingApp`. Se recusar por app aberto, voltar a pedir; não contornar.

- [ ] **Step 6: O percurso do dono**

O que pedir para conferir, no app instalado:

1. O trilho tem Gravador, Reuniões e Ajustes; o app abre em Reuniões.
2. Abrir uma reunião transcrita mostra Transcrição · Ata · Notas; as setas andam entre as abas.
3. Renomear um falante, ir para Notas e voltar: o nome continua.
4. No painel de Reuniões, "Abrir a ata" cai na aba Ata daquela reunião; "Gerar a ata" também.
5. Gerar uma ata, ir para Transcrição enquanto ela escreve (a bolinha de Reuniões acende dizendo "escrevendo a ata"), voltar: a ata está lá, e o selo da aba conta as pendências dela.
6. Copiar e Exportar funcionam da aba.
7. Os dois temas.
