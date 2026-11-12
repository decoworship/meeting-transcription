# O briefing da reunião: plano de implementação

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Para cada projeto, o app monta um texto curto e pronto para mandar (o que está pendente de cada lado, o que se decidiu na última reunião e o que foi entregue desde então), e lembra dele na véspera da próxima reunião da série.

**Architecture:** O briefing é uma leitura do registro do projeto, **sem modelo**: uma função pura escolhe e ordena o que já foi conferido. O núcleo devolve seções e itens; a tela tira o que a pessoa não quer mandar e monta o texto, que sai pelo mesmo `htmlParaEmail` da ata. O lembrete é uma decisão pura sobre os eventos da agenda de hoje e de amanhã, disparada por um relógio na ponte, e nunca envia nada.

**Tech Stack:** C#/.NET 8, xUnit, System.Text.Json com contexto gerado em compilação, API do Google Calendar (só leitura), JavaScript sem build no WebView2, `node --test`.

**Spec:** [docs/superpowers/specs/2026-11-11-briefing-da-reuniao-design.md](../specs/2026-11-11-briefing-da-reuniao-design.md). As seções citadas como §N são dele.

**É o passo 3 de quatro, e só começa com o passo 2 construído e usado** ([plano](2026-11-11-pendencias-do-projeto.md)). Os nomes consumidos (`RegistroDoProjeto`, `ItemDoProjeto`, `AtaDoProjeto`, `LeitorDoProjeto`, `ProjetoDeExemplo`, a página `projeto.js`) são os que aquele plano define. **Antes da Tarefa 1, confira que existem com essas assinaturas.**

## Respostas assumidas

O desenho deixou cinco perguntas (§9). Este plano foi escrito antes das respostas, com a proposta do próprio desenho em cada uma.

| pergunta | o que este plano assume | se a resposta for outra |
|---|---|---|
| 1. Para quem vai o briefing? | Uma versão só, para todos os convidados, cliente incluído. Sem nota interna | uma versão "de casa" seria um parâmetro a mais na Tarefa 1 e um seletor na Tarefa 3 |
| 2. Cliente primeiro ou nós primeiro? | Cliente primeiro | troca a ordem de duas seções na Tarefa 1 |
| 3. A que horas é "a véspera"? | A partir das 17h do dia anterior, com o app aberto; se não estava, na manhã do dia, ao abrir | muda a constante `HoraDaVespera` na Tarefa 6 |
| 4. Vale para reunião que não é de série? | Sim, pelo título igual ao de reuniões já ligadas ao projeto (o próprio §7) | nada a mudar |
| 5. Quando o passo 4 chegar, e-mail novo ou descrição do convite? | E-mail novo; é assunto do passo 4 | fora deste plano |

## Global Constraints

- Comandos de teste sempre com `export PATH="$HOME/.dotnet:$PATH"` antes. Suítes: `dotnet test app-net/Tests/MeetingApp.Tests.csproj` e `node --test tools/web/*.test.mjs`. Toda tarefa termina com as duas verdes.
- **Sem modelo:** nenhuma chamada ao motor de ata nem ao Claude neste passo.
- **Nada sai sozinho:** este passo não envia. A pessoa copia.
- **O que não entra nunca no texto:** nota interna, item retirado, item de ata em rascunho, fala citada.
- **Os avisos de atraso ficam na tela e nunca no que se copia.**
- **O escopo da agenda continua `calendar.readonly`.**
- **A leitura da agenda não atrapalha o gravador:** o mesmo `ClienteDaAgenda`, com o timeout de 10 s que ele já tem, e toda falha engolida.
- **O lembrete nasce desligado**, por projeto, e não existe para quem não conectou a agenda.
- Sem rede, sem agenda e sem série, o briefing existe do mesmo jeito na página do projeto.
- Nós em vez de `innerHTML`, nenhum dado de cliente real, acervo real só com o dono, sem publicar e sem push: valem as regras do plano do passo 1.

## Review Focus

1. **Projeto sem nada aberto e sem decisão recente:** o briefing tem duas linhas e está certo; nenhuma seção vazia, nenhum erro. Teste na Tarefa 1.
2. **A pessoa tira todos os itens de uma seção:** a seção some do texto copiado, título inclusive. Teste na Tarefa 2.
3. **Dois eventos da mesma série no mesmo dia, ou o mesmo evento visto em duas rodadas do relógio:** um aviso por evento, uma vez. Teste na Tarefa 6.
4. **A agenda cai, o token expira ou a rede some no meio da rodada:** nenhum aviso, nenhuma exceção, e a rodada seguinte tenta de novo. Teste na Tarefa 6 (a decisão é pura; a rodada é exercitada com a lista vazia).
5. **Título de reunião com caixa, acento ou espaço diferente entre semanas** ("Semanal - Portal" e "semanal – portal "): casa pelo título sem acento, sem caixa e só com letras e dígitos. Teste na Tarefa 5.

---

## Mapa dos arquivos

| arquivo | o que muda |
|---|---|
| `app-net/Nucleo/Projeto/Briefing.cs` (novo) | o conteúdo e os avisos |
| `app-net/Nucleo/Projeto/RegistroDoProjeto.cs` | guarda a próxima reunião que a última ata registrou |
| `app-net/Nucleo/Projeto/SerieDoProjeto.cs` (novo) | de um evento da agenda ao projeto |
| `app-net/Nucleo/Projeto/LembreteDeBriefing.cs` (novo) | quem avisar, e uma vez só |
| `app-net/Agenda/Dtos.cs`, `Evento.cs`, `ClienteDaAgenda.cs` | `recurringEventId`; os eventos de um dia |
| `app-net/Gravacao/Meta.cs`, `app-net/Nucleo/Claude/Acervo.cs` | a série no `meta.json` e no acervo |
| `app-net/Nucleo/Projetos.cs` | a preferência `lembrar_briefing` |
| `app-net/App/Ponte.cs`, `app-net/App/Bandeja/Bandeja.cs`, `app-net/App/Nativo/Win32.cs` | a operação `briefing`; o relógio; o clique no balão |
| `app-net/App/web/briefing-regras.js` (novo), `projeto.js`, `clientes.js`, `reunioes.js`, `app.css` | o painel, a preferência, a faixa do aviso |
| `tools/web/briefing-regras.test.mjs` (novo) | os testes do texto |

---

### Task 1: O conteúdo do briefing

§4 e §5.

**Files:**
- Create: `app-net/Nucleo/Projeto/Briefing.cs`
- Modify: `app-net/Nucleo/Projeto/RegistroDoProjeto.cs`
- Test: `app-net/Tests/BriefingTests.cs`

**Interfaces:**
- Consumes: `RegistroDoProjeto` (`Abertas`, `Itens`, `VaiAte`, `NaoConferidas`), `ItemDoProjeto`.
- Produces:
  - `RegistroDoProjeto.ProximaReuniaoDita` (`string`): o `ProximaReuniao` da última ata conferida, ou `""`. Preenchido por `Montar`.
  - em `Briefing.cs`:

```csharp
public sealed record ItemDoBriefing(string Ref, string Texto);          // Texto é a linha como vai sair
public sealed record SecaoDoBriefing(string Id, string Titulo, IReadOnlyList<ItemDoBriefing> Itens);
// Id: "cliente" | "nosso" | "decidido" | "entregue" | "proxima"
public sealed class Briefing
{
    public string Projeto, PosicaoEm;                    // PosicaoEm: "dd/MM"
    public IReadOnlyList<SecaoDoBriefing> Secoes;        // só as que têm item, na ordem do §4
    public IReadOnlyList<string> Avisos;                 // só para a tela
    public bool TemOQueDizer => Secoes.Count > 0;
    public static Briefing Montar(RegistroDoProjeto r, string projeto, NomesDosLados nomes, DateOnly hoje);
}
```

**As regras de `Montar`:**
- **cliente** e **nosso**: `r.Abertas(lado)`, mais antigas primeiro. Título `"Pendentes — " + nome do lado`, e sem nome `"Pendentes do cliente"` e `"Pendentes do nosso lado"`. A linha é `texto`, mais `": " + responsável e prazo separados por vírgula` quando houver, mais `" (vencida)"` quando `PrazoEm < hoje`.
- **decidido**: as decisões vigentes que nasceram na última ata conferida (`NasceuEm == r.VaiAte`). Título `"Decidido na última reunião (dd/MM)"`. A linha é `tema: texto`, ou só o texto.
- **entregue**: as pendências concluídas com `FechouEm` **depois** de `r.VaiAte`. Título `"Entregue desde a última reunião"`. A linha é o texto.
- **proxima**: um item só, com `r.ProximaReuniaoDita`, quando não é vazia. Título `"Próxima reunião"`.
- **avisos**, nesta ordem: para a primeira reunião não conferida depois de `VaiAte`, `"A reunião de dd/MM deste projeto ainda não foi conferida. O briefing vai até dd/MM."`; com N pendências vencidas, `"Há N pendências com prazo vencido — confira se alguma já foi entregue antes de mandar."` (com uma: `"Há 1 pendência com prazo vencido — confira se já foi entregue antes de mandar."`).
- Sem ata conferida nenhuma: nenhuma seção, e o aviso `"Este projeto ainda não tem ata conferida."`.

- [ ] **Step 1: Escrever os testes**

```csharp
public sealed class BriefingTests
{
    private static readonly NomesDosLados Nomes = new("Acme Bebidas", "Casa");
    private static readonly DateOnly Hoje = new(2026, 10, 30);

    private static Briefing Do(ArquivoDoProjeto? a = null) => Briefing.Montar(
        RegistroDoProjeto.Montar(ProjetoDeExemplo.Atas(), a ?? new ArquivoDoProjeto()), "Portal de Pedidos", Nomes, Hoje);

    [Fact]
    public void OClienteVemPrimeiroEAVencidaVemMarcada()
    {
        var b = Do();

        Assert.Equal(["cliente", "nosso", "decidido"], b.Secoes.Select(s => s.Id));
        Assert.Equal("Pendentes — Acme Bebidas", b.Secoes[0].Titulo);
        Assert.Equal(["Enviar a base atualizada: Carla, sexta (vencida)", "Definir o rateio"],
                     b.Secoes[0].Itens.Select(i => i.Texto));
        Assert.Equal("Pendentes — Casa", b.Secoes[1].Titulo);
        Assert.Equal("Decidido na última reunião (21/10)", b.Secoes[2].Titulo);
        Assert.Equal(["Tema i2: Corte de 400 caixas por mês."], b.Secoes[2].Itens.Select(i => i.Texto));
        Assert.Equal("30/10", b.PosicaoEm);
    }

    [Fact]
    public void OsAvisosDizemOQuePodeEstarErradoEFicamForaDasSecoes()
    {
        var b = Do();

        Assert.Equal(["A reunião de 28/10 deste projeto ainda não foi conferida. O briefing vai até 21/10.",
                      "Há 1 pendência com prazo vencido — confira se já foi entregue antes de mandar."], b.Avisos);
        Assert.DoesNotContain(b.Secoes.SelectMany(s => s.Itens), i => i.Texto.Contains("conferida"));
    }

    [Fact]
    public void OEntregueEOQueFechouDepoisDaUltimaReuniaoEANotaNaoEntra()
    {
        var a = new ArquivoDoProjeto();
        a.Acontecimentos.Add(new() { Em = "2026-10-27T09:00:00-03:00", Reuniao = ProjetoDeExemplo.R2, Item = "i4", Tipo = "concluida" });
        a.Acontecimentos.Add(new() { Em = "2026-10-27T09:05:00-03:00", Reuniao = ProjetoDeExemplo.R1, Item = "i2", Tipo = "nota", Texto = "segredo interno" });

        var b = Do(a);

        Assert.Equal(["Definir o rateio"], b.Secoes.Single(s => s.Id == "entregue").Itens.Select(i => i.Texto));
        // O painel fechou NA última reunião: já estava na ata dela, e não é "desde".
        Assert.DoesNotContain(b.Secoes.SelectMany(s => s.Itens), i => i.Texto.Contains("painel"));
        Assert.DoesNotContain(b.Secoes.SelectMany(s => s.Itens), i => i.Texto.Contains("segredo"));
    }

    [Fact]
    public void ProjetoSemNadaAbertoTemBriefingVazioESemErro()
    {
        var atas = ProjetoDeExemplo.Atas().Take(1).ToList();
        foreach (var i in atas[0].Conferencia!.Itens) i.Revisao = "retirado";

        var b = Briefing.Montar(RegistroDoProjeto.Montar(atas, new ArquivoDoProjeto()), "Portal de Pedidos", Nomes, Hoje);

        Assert.Empty(b.Secoes);
        Assert.False(b.TemOQueDizer);
        Assert.Empty(b.Avisos);
    }

    [Fact]
    public void SemAtaConferidaOBriefingDizIsso()
    {
        var b = Briefing.Montar(RegistroDoProjeto.Montar([], new ArquivoDoProjeto()), "Portal de Pedidos", Nomes, Hoje);
        Assert.Equal(["Este projeto ainda não tem ata conferida."], b.Avisos);
    }

    [Fact]
    public void AProximaReuniaoSoEntraSeAUltimaAtaARegistrou()
    {
        var atas = ProjetoDeExemplo.Atas();
        atas[1].Conferencia!.ProximaReuniao = "quinta, 05/11";
        var b = Briefing.Montar(RegistroDoProjeto.Montar(atas, new ArquivoDoProjeto()), "Portal de Pedidos", Nomes, Hoje);

        Assert.Equal("quinta, 05/11", b.Secoes[^1].Itens.Single().Texto);
        Assert.DoesNotContain(Do().Secoes, s => s.Id == "proxima");
    }
}
```

- [ ] **Step 2: Rodar e ver falhar**

Run: `dotnet test app-net/Tests/MeetingApp.Tests.csproj --filter "FullyQualifiedName~BriefingTests"`
Expected: FAIL de compilação.

- [ ] **Step 3: Implementar** `Briefing.cs` e o campo novo do registro.

- [ ] **Step 4: Rodar a suíte**

Run: `dotnet test app-net/Tests/MeetingApp.Tests.csproj`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add app-net/Nucleo/Projeto app-net/Tests/BriefingTests.cs
git commit -m "feat(briefing): o conteúdo lido do registro, com os avisos de atraso à parte"
```

---

### Task 2: O texto que se copia

§3 (objetivo 3) e §6.

**Files:**
- Create: `app-net/App/web/briefing-regras.js`
- Test: `tools/web/briefing-regras.test.mjs`

**Interfaces:**
- Consumes: o `briefing` como a op `briefing` o devolve (Tarefa 3): `{ projeto, posicao_em, secoes: [{ id, titulo, itens: [{ ref, texto }] }], avisos }`.
- Produces:
  - `markdownDoBriefing(briefing, { tirados = new Set(), abertura = "" } = {})` → o Markdown, no formato da ata de formalização (títulos como linha em negrito, itens com `- `), para passar por `htmlParaEmail`.
  - `assuntoDoBriefing(titulo, dataIso)` → `"Briefing — <título> — dd/MM/aaaa"`.

**O formato:** a abertura, se houver, num parágrafo; depois `**<projeto> — posição em <dd/MM>**`; depois cada seção que ainda tem item, com `**<título>**`, uma linha em branco e os itens. A seção `proxima` sai como uma linha só: `Próxima reunião: <texto>`. Os avisos não entram.

- [ ] **Step 1: Escrever os testes**

```js
import { test } from "node:test";
import assert from "node:assert/strict";

const R = await import(new URL("../../app-net/App/web/briefing-regras.js", import.meta.url));

const BRIEFING = {
  projeto: "Portal de Pedidos", posicao_em: "30/10",
  avisos: ["A reunião de 28/10 deste projeto ainda não foi conferida. O briefing vai até 21/10."],
  secoes: [
    { id: "cliente", titulo: "Pendentes — Acme Bebidas", itens: [
      { ref: "r1#i2", texto: "Enviar a base atualizada: Carla, sexta (vencida)" },
      { ref: "r2#i4", texto: "Definir o rateio" }] },
    { id: "nosso", titulo: "Pendentes — Casa", itens: [{ ref: "r2#i5", texto: "Item sem alvo" }] },
    { id: "proxima", titulo: "Próxima reunião", itens: [{ ref: "proxima", texto: "quinta, 05/11" }] },
  ],
};

test("o texto sai no formato da ata, sem os avisos", () => {
  assert.equal(R.markdownDoBriefing(BRIEFING), [
    "**Portal de Pedidos — posição em 30/10**", "",
    "**Pendentes — Acme Bebidas**", "",
    "- Enviar a base atualizada: Carla, sexta (vencida)", "- Definir o rateio", "",
    "**Pendentes — Casa**", "",
    "- Item sem alvo", "",
    "Próxima reunião: quinta, 05/11", "",
  ].join("\n"));
});

test("tirar um item o tira do texto, e a seção que esvazia some com o título", () => {
  const md = R.markdownDoBriefing(BRIEFING, { tirados: new Set(["r2#i5", "r2#i4"]) });
  assert.ok(!md.includes("Definir o rateio"));
  assert.ok(!md.includes("Pendentes — Casa"));
  assert.ok(md.includes("Enviar a base atualizada"));
});

test("a abertura vem antes de tudo, e vazia não deixa linha", () => {
  assert.ok(R.markdownDoBriefing(BRIEFING, { abertura: "Segue a posição para amanhã." })
    .startsWith("Segue a posição para amanhã.\n\n**Portal de Pedidos"));
  assert.ok(R.markdownDoBriefing(BRIEFING, { abertura: "   " }).startsWith("**Portal de Pedidos"));
});

test("o assunto leva o título e a data por extenso", () => {
  assert.equal(R.assuntoDoBriefing("Semanal do Portal", "2026-11-05"), "Briefing — Semanal do Portal — 05/11/2026");
});
```

- [ ] **Step 2: Rodar e ver falhar**

Run: `node --test tools/web/briefing-regras.test.mjs`
Expected: FAIL (`Cannot find module`).

- [ ] **Step 3: Implementar** `briefing-regras.js`.

- [ ] **Step 4: Rodar**

Run: `node --test tools/web/*.test.mjs`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add app-net/App/web/briefing-regras.js tools/web/briefing-regras.test.mjs
git commit -m "feat(web): o texto do briefing, com o que a pessoa tirou de fora"
```

---

### Task 3: O painel Briefing na página do projeto

§6.

**Files:**
- Modify: `app-net/App/Ponte.cs`, `app-net/App/web/projeto.js`, `app-net/App/web/app.css`

**Interfaces:**
- Consumes: `Briefing.Montar` (Tarefa 1); `LeitorDoProjeto.Ler`; `markdownDoBriefing`, `assuntoDoBriefing` (Tarefa 2); `htmlParaEmail` de `ata-regras.js`.
- Produces: op `briefing` com `{ cliente, projeto }` → `{ briefing: { projeto, posicao_em, secoes, avisos, tem_o_que_dizer } }`. Os nomes dos lados são o cliente do pedido e `ConfiguracoesDoApp.Carregar().NomeDaCasa()`; o dia é `DateOnly.FromDateTime(DateTime.Now)`.

- [ ] **Step 1: A operação** na ponte, com as classes de resposta (`[JsonPropertyName]` em cada campo) dentro de `Briefing.cs` ou ao lado do `ProjetoParaTela`.

- [ ] **Step 2: O painel** em `projeto.js`, abaixo das abertas:
- os avisos em cima, como alerta, **fora** da área que se copia;
- cada item com uma caixa marcada; desmarcar o tira **só deste texto** (um `Set` em memória, que morre ao sair da página — §6: não é guardado);
- um campo de uma linha, "Abertura (opcional)";
- a prévia do texto, renderizada com os mesmos nós da ata;
- *Copiar* leva `text/html` (`htmlParaEmail(markdown)`) e `text/plain` (o Markdown), como o Copiar da ata;
- o assunto sugerido numa linha, com *Copiar assunto*. O título é o da próxima reunião do projeto quando o lembrete a conhece (Tarefa 7), e senão o nome do projeto; a data é a dela, ou a de hoje;
- sem `tem_o_que_dizer`: "Nada aberto neste projeto." e nenhum botão.

- [ ] **Step 3: Rodar e compilar**

Run: `node --test tools/web/*.test.mjs && dotnet test app-net/Tests/MeetingApp.Tests.csproj && dotnet build app-net/App -c Release 2>&1 | tail -3`
Expected: PASS e `0 Erro(s)`.

- [ ] **Step 4: Commit**

```bash
git add app-net/App app-net/Nucleo/Projeto
git commit -m "feat(web): o painel Briefing na página do projeto"
```

---

### Task 4: A série da agenda chega ao meta.json

§7.

**Files:**
- Modify: `app-net/Agenda/Dtos.cs`, `app-net/Agenda/Evento.cs`, `app-net/Agenda/ClienteDaAgenda.cs`, `app-net/Gravacao/Meta.cs`, `app-net/Nucleo/Claude/Acervo.cs`
- Test: `app-net/Tests/AgendaTests.cs`, `app-net/Tests/IntegracaoComClaudeTests.cs`

**Interfaces:**
- Produces:
  - `ItemDeEvento.RecurringEventId` (`"recurringEventId"`).
  - `Evento` ganha o último parâmetro `string? Serie = null`.
  - `public static List<Evento> ClienteDaAgenda.LerEventos(string json)`: o corpo de uma resposta da API virando eventos, com o filtro de cancelados e sem título que hoje mora dentro de `BuscarAsync` (que passa a chamá-lo).
  - `MetaMeeting.RecurringEventId` (`"recurring_event_id"`, omitido quando nulo); `Evento.ParaMeta()` o preenche.
  - `ReuniaoNoAcervo.Serie` (`string?`, `init`), lido de `meeting.recurring_event_id`.

- [ ] **Step 1: Escrever os testes**

```csharp
// AgendaTests
[Fact]
public void OEventoDeSerieGuardaOIdDaSerieEOAvulsoNao()
{
    var eventos = ClienteDaAgenda.LerEventos("""
        {"items":[
          {"id":"abc_20261105T143000Z","recurringEventId":"abc","status":"confirmed","summary":"Semanal do Portal",
           "start":{"dateTime":"2026-11-05T11:30:00-03:00"},"end":{"dateTime":"2026-11-05T12:00:00-03:00"}},
          {"id":"xyz","status":"confirmed","summary":"Avulsa","start":{"dateTime":"2026-11-05T15:00:00-03:00"}},
          {"id":"c","status":"cancelled","summary":"Cancelada"}]}
        """);

    Assert.Equal(["abc", null], eventos.Select(e => e.Serie));
    Assert.Equal("abc", eventos[0].ParaMeta().RecurringEventId);
    Assert.Null(eventos[1].ParaMeta().RecurringEventId);
}

// AcervoTests (IntegracaoComClaudeTests.cs)
[Fact]
public void OAcervoLeASerieDoMetaEAAusenciaENula()
{
    string pasta = Gravar(_raiz, "2026-11-05_11-30-00", "Semanal do Portal");
    var meta = JsonNode.Parse(File.ReadAllText(Path.Combine(pasta, "meta.json")))!;
    meta["meeting"]!["recurring_event_id"] = "abc";
    File.WriteAllText(Path.Combine(pasta, "meta.json"), meta.ToJsonString());
    Gravar(_raiz, "2026-11-06_11-30-00", "Avulsa");

    var acervo = new Acervo(_raiz);
    Assert.Equal("abc", acervo.Achar("2026-11-05_11-30-00")!.Serie);
    Assert.Null(acervo.Achar("2026-11-06_11-30-00")!.Serie);
}
```

- [ ] **Step 2: Rodar e ver falhar**

Run: `dotnet test app-net/Tests/MeetingApp.Tests.csproj --filter "FullyQualifiedName~AgendaTests|FullyQualifiedName~AcervoTests"`
Expected: FAIL de compilação.

- [ ] **Step 3: Implementar.** `Meta.cs` está em `Gravacao/`, vizinho de arquivos que não se reabrem (CLAUDE.md); a mudança é **uma propriedade a mais em `MetaMeeting`**, e nada além.

- [ ] **Step 4: Rodar a suíte**

Run: `dotnet test app-net/Tests/MeetingApp.Tests.csproj`
Expected: PASS, inclusive `MetaTests`.

- [ ] **Step 5: Commit**

```bash
git add app-net/Agenda app-net/Gravacao/Meta.cs app-net/Nucleo/Claude/Acervo.cs app-net/Tests
git commit -m "feat(agenda): o id da série vai do evento ao meta.json e ao acervo"
```

---

### Task 5: De um evento da agenda ao projeto

§7.

**Files:**
- Create: `app-net/Nucleo/Projeto/SerieDoProjeto.cs`
- Test: `app-net/Tests/SerieDoProjetoTests.cs`

**Interfaces:**
- Consumes: `ReuniaoNoAcervo` (com `Serie`, Tarefa 4).
- Produces: `static (string Cliente, string Projeto)? SerieDoProjeto.Achar(IReadOnlyList<ReuniaoNoAcervo> reunioes, string? serie, string? titulo)`.

**As regras:** primeiro pela série: a reunião **mais recente** com a mesma `Serie` que tem cliente e projeto. Sem isso, pelo título: a mais recente com cliente e projeto cujo título é igual depois de tirar acento, caixa e tudo o que não é letra ou dígito. Sem isso, nulo. Título vazio não casa com nada.

- [ ] **Step 1: Escrever os testes**

```csharp
public sealed class SerieDoProjetoTests
{
    private static ReuniaoNoAcervo R(string id, string? titulo, string? cliente, string? projeto, string? serie = null) =>
        new(id, "/x/" + id, titulo, DateTime.ParseExact(id, "yyyy-MM-dd_HH-mm-ss", null), 600, cliente, projeto,
            [], true, true, false, false) { Serie = serie };

    private static readonly List<ReuniaoNoAcervo> Acervo =
    [
        R("2026-10-14_11-30-00", "Semanal - Portal", "Acme Bebidas", "Aplicativo", "abc"),
        R("2026-10-21_11-30-00", "Semanal - Portal", "Acme Bebidas", "Portal de Pedidos", "abc"),
        R("2026-10-28_11-30-00", "Semanal - Portal", null, null, "abc"),
        R("2026-10-22_09-00-00", "Comitê de Preços", "Acme Bebidas", "Preços"),
    ];

    [Fact]
    public void ASerieLevaAoProjetoDaReuniaoMaisRecenteQueTemProjeto() =>
        Assert.Equal(("Acme Bebidas", "Portal de Pedidos"), SerieDoProjeto.Achar(Acervo, "abc", "Outro título"));

    [Theory]
    [InlineData("comitê de preços")]
    [InlineData("Comite de Precos ")]
    [InlineData("Comitê – de Preços")]
    public void SemSerieValeOTituloSemAcentoCaixaNemPontuacao(string titulo) =>
        Assert.Equal(("Acme Bebidas", "Preços"), SerieDoProjeto.Achar(Acervo, null, titulo));

    [Theory]
    [InlineData("zzz", "Reunião nova")]
    [InlineData(null, "")]
    [InlineData(null, null)]
    public void SemSerieNemTituloConhecidoNaoHaProjeto(string? serie, string? titulo) =>
        Assert.Null(SerieDoProjeto.Achar(Acervo, serie, titulo));
}
```

- [ ] **Step 2: Rodar e ver falhar**

Run: `dotnet test app-net/Tests/MeetingApp.Tests.csproj --filter "FullyQualifiedName~SerieDoProjetoTests"`
Expected: FAIL de compilação.

- [ ] **Step 3: Implementar.**

- [ ] **Step 4: Rodar**

Run: `dotnet test app-net/Tests/MeetingApp.Tests.csproj --filter "FullyQualifiedName~SerieDoProjetoTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add app-net/Nucleo/Projeto/SerieDoProjeto.cs app-net/Tests/SerieDoProjetoTests.cs
git commit -m "feat(briefing): do evento da agenda ao projeto, pela série e depois pelo título"
```

---

### Task 6: Quem avisar, e uma vez só

§7.

**Files:**
- Create: `app-net/Nucleo/Projeto/LembreteDeBriefing.cs`
- Modify: `app-net/Agenda/ClienteDaAgenda.cs`, `app-net/Nucleo/Projetos.cs`
- Test: `app-net/Tests/LembreteDeBriefingTests.cs`

**Interfaces:**
- Consumes: `Evento` (com `Serie`).
- Produces:
  - `Task<Proximas> ClienteDaAgenda.DoDiaAsync(DateOnly dia, CancellationToken ct = default)`: os eventos com hora que começam naquele dia, no fuso local, com o mesmo tratamento de status de `ProximasAsync` e `maxResultados: 50`.
  - `PreferenciasDoProjeto.LembrarBriefing` (`"lembrar_briefing"`, `bool?`).
  - em `LembreteDeBriefing.cs`:

```csharp
public sealed record EventoDoProjeto(Evento Evento, string Cliente, string Projeto);
public sealed record AvisoDeBriefing(string Chave, string Cliente, string Projeto, string Titulo,
                                     DateTimeOffset Inicio, string Texto);
public static class LembreteDeBriefing
{
    public const int HoraDaVespera = 17;
    public static string CaminhoPadrao { get; }      // ~/.meeting-transcription/briefing-avisados.json
    public static IReadOnlyList<AvisoDeBriefing> Decidir(DateTimeOffset agora, IEnumerable<EventoDoProjeto> hoje,
        IEnumerable<EventoDoProjeto> amanha, IReadOnlySet<string> jaAvisados);
    public static HashSet<string> LerAvisados(string? caminho = null);                 // vazio se não existe ou ilegível
    public static void GuardarAvisados(IEnumerable<string> chaves, string? caminho = null);   // .tmp e troca; guarda as 200 últimas
}
```

**As regras de `Decidir`:**
- a chave de um evento é `"<id do evento>@<AAAA-MM-DD do início>"`;
- os de **amanhã** entram quando `agora.Hour >= HoraDaVespera`, com o texto `"Amanhã, HH:mm — <projeto>. O briefing está pronto."`;
- os de **hoje** que ainda não começaram entram a qualquer hora (o app não estava aberto na véspera), com `"Hoje, HH:mm — <projeto>. O briefing está pronto."`;
- quem já está em `jaAvisados` não entra; dois eventos do mesmo projeto no mesmo dia rendem um aviso só, o do primeiro;
- a saída vem em ordem de início.

Quem chama (Tarefa 7) já filtrou os eventos: só os que têm projeto (`SerieDoProjeto.Achar`), com `LembrarBriefing == true`, e cujo briefing `TemOQueDizer`.

- [ ] **Step 1: Escrever os testes**

```csharp
public sealed class LembreteDeBriefingTests : IDisposable
{
    private readonly string _arquivo = Path.Combine(Path.GetTempPath(), "avisados-" + Guid.NewGuid().ToString("N") + ".json");
    public void Dispose() { if (File.Exists(_arquivo)) File.Delete(_arquivo); }

    private static EventoDoProjeto E(string id, string inicio, string projeto = "Portal de Pedidos") =>
        new(new Evento(id, "Semanal", DateTimeOffset.Parse(inicio), null, [], null), "Acme Bebidas", projeto);

    private static readonly EventoDoProjeto Amanha = E("abc_1", "2026-11-05T11:30:00-03:00");

    [Fact]
    public void NaVesperaSoDepoisDasDezessete()
    {
        Assert.Empty(LembreteDeBriefing.Decidir(DateTimeOffset.Parse("2026-11-04T16:59:00-03:00"), [], [Amanha], new HashSet<string>()));

        var aviso = Assert.Single(LembreteDeBriefing.Decidir(
            DateTimeOffset.Parse("2026-11-04T17:00:00-03:00"), [], [Amanha], new HashSet<string>()));
        Assert.Equal("Amanhã, 11:30 — Portal de Pedidos. O briefing está pronto.", aviso.Texto);
        Assert.Equal("abc_1@2026-11-05", aviso.Chave);
    }

    [Fact]
    public void NaManhaDoDiaAvisaOQueAVesperaPerdeuEOQueJaComecouNao()
    {
        var agora = DateTimeOffset.Parse("2026-11-05T08:10:00-03:00");
        var passou = E("old", "2026-11-05T07:00:00-03:00", "Preços");

        var avisos = LembreteDeBriefing.Decidir(agora, [Amanha, passou], [], new HashSet<string>());

        Assert.Equal("Hoje, 11:30 — Portal de Pedidos. O briefing está pronto.", Assert.Single(avisos).Texto);
    }

    [Fact]
    public void UmAvisoPorEventoEUmPorProjetoNoDia()
    {
        var agora = DateTimeOffset.Parse("2026-11-04T18:00:00-03:00");
        var segundo = E("abc_2", "2026-11-05T15:00:00-03:00");

        Assert.Single(LembreteDeBriefing.Decidir(agora, [], [Amanha, segundo], new HashSet<string>()));
        Assert.Empty(LembreteDeBriefing.Decidir(agora, [], [Amanha], new HashSet<string> { "abc_1@2026-11-05" }));
    }

    [Fact]
    public void OsAvisadosSobrevivemAoReinicioEArquivoRuimViraVazio()
    {
        LembreteDeBriefing.GuardarAvisados(["a@2026-11-05", "b@2026-11-06"], _arquivo);
        Assert.Equal(2, LembreteDeBriefing.LerAvisados(_arquivo).Count);

        File.WriteAllText(_arquivo, "{ quebrado");
        Assert.Empty(LembreteDeBriefing.LerAvisados(_arquivo));
    }
}
```

- [ ] **Step 2: Rodar e ver falhar**

Run: `dotnet test app-net/Tests/MeetingApp.Tests.csproj --filter "FullyQualifiedName~LembreteDeBriefingTests"`
Expected: FAIL de compilação.

- [ ] **Step 3: Implementar** `LembreteDeBriefing`, `DoDiaAsync` e a preferência. A lista de chaves é serializada pelo `AgendaJson`/`ProjetosJson` que já registram `List<string>`, ou por um contexto próprio de uma linha; nunca por reflexão.

- [ ] **Step 4: Rodar a suíte**

Run: `dotnet test app-net/Tests/MeetingApp.Tests.csproj`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add app-net/Nucleo/Projeto/LembreteDeBriefing.cs app-net/Nucleo/Projetos.cs app-net/Agenda/ClienteDaAgenda.cs app-net/Tests/LembreteDeBriefingTests.cs
git commit -m "feat(briefing): a decisão de quem avisar na véspera, uma vez por evento"
```

---

### Task 7: O relógio, o aviso e a preferência

§7. A parte que só existe no executável: a suíte não a alcança, e por isso ela só junta peças já testadas.

**Files:**
- Modify: `app-net/App/Ponte.cs`, `app-net/App/Bandeja/Bandeja.cs`, `app-net/App/Nativo/Win32.cs`
- Modify: `app-net/App/web/clientes.js`, `app-net/App/web/reunioes.js`, `app-net/App/web/projeto.js`

**Interfaces:**
- Consumes: `DoDiaAsync`, `LembreteDeBriefing`, `SerieDoProjeto.Achar`, `Briefing.Montar`, `LeitorDoProjeto.Ler`, `PreferenciasDoProjeto.LembrarBriefing`.
- Produces:
  - evento empurrado `{ id: 0, tipo: "briefing", cliente, projeto, texto, titulo, inicio }`;
  - op `briefing-pendente` → o último aviso ainda não aberto, ou nada;
  - op `briefing-visto` com `{ cliente, projeto }` → esquece o pendente.

- [ ] **Step 1: A rodada** na `Ponte`, num método `RodadaDoBriefingAsync()`:
1. se nenhum projeto tem `LembrarBriefing == true`, ou a agenda não está autorizada, volta **sem tocar na rede**;
2. `DoDiaAsync` de hoje e de amanhã; se qualquer um vier com status diferente de `Ok` ou `SemEvento`, volta sem avisar;
3. para cada evento, `SerieDoProjeto.Achar` sobre `new Acervo(pastaDasGravacoes).Listar()`; fica só com os que acham projeto, com a preferência ligada e com `Briefing.Montar(...).TemOQueDizer`;
4. `LembreteDeBriefing.Decidir` com `LerAvisados()`; para cada aviso: `Avisar(texto)`, guarda como pendente, empurra o evento, e acrescenta a chave a `GuardarAvisados`.

Tudo dentro de um `try` que engole: uma falha aqui não pode chegar ao gravador.

- [ ] **Step 2: O relógio.** Um `System.Threading.Timer` criado no construtor da `Ponte`: a primeira rodada um minuto depois de o app subir, e depois a cada 30 minutos. Uma rodada não começa enquanto a anterior não terminou.

- [ ] **Step 3: O clique no balão.** Em `Win32.cs`, `internal const uint NIN_BALLOONUSERCLICK = 0x0405;`. Em `Bandeja.AoEvento`, esse caso abre a janela do app, com a mesma ação do item "Abrir" do menu. A página, ao aparecer, pede `briefing-pendente`.

- [ ] **Step 4: A tela.**
- `reunioes.js`: no topo da lista, uma faixa com o texto do aviso e *Abrir o briefing*, quando há pendente (pelo evento `briefing` ou por `briefing-pendente` ao montar). O botão vai para a página do projeto, rola até o painel Briefing e manda `briefing-visto`.
- `projeto.js`: o assunto sugerido passa a usar o `titulo` e o `inicio` do pendente deste projeto.
- `clientes.js`: no painel do projeto, a chave "Lembrar do briefing na véspera", gravando `lembrar_briefing`. Sem agenda conectada, a chave fica desligada e desabilitada, com a dica "Conecte a agenda em Ajustes › Geral para o app saber quando é a próxima reunião."

- [ ] **Step 5: Rodar e compilar**

Run: `node --test tools/web/*.test.mjs && dotnet test app-net/Tests/MeetingApp.Tests.csproj && dotnet build app-net/App -c Release 2>&1 | tail -3`
Expected: PASS e `0 Erro(s)`.

- [ ] **Step 6: Commit**

```bash
git add app-net/App
git commit -m "feat(briefing): o lembrete da véspera pela bandeja, desligado por padrão"
```

---

### Task 8: Documentos e validação com o dono

- [ ] **Step 1: Os documentos.** `docs/RASTREABILIDADE.md` (o estado do passo 3); o desenho (§9, com as respostas do dono); `docs/BACKLOG.md`.
- [ ] **Step 2: O binário publica.** Run: `tools/publicar.sh --so-build 2>&1 | tail -20`. Expected: sem `IL2026` nem `IL3050`.
- [ ] **Step 3: O que o desenho deixou para conferir** (§7): depois de gravar uma reunião de série **organizada pelo cliente**, abrir o `meta.json` dela e ver se `recurring_event_id` veio. Se não veio, escrever isso no desenho: aquele projeto fica no caminho do título, que a Tarefa 5 já cobre.
- [ ] **Step 4: Com o dono** (critérios 1 a 4 do §10): o painel mostra o briefing sem esperar por modelo, e *Copiar* cola certo no Gmail e no Outlook; um projeto com a última ata em rascunho mostra o aviso e o texto não traz nada dela; nota interna e item retirado não aparecem; na véspera de uma reunião de série, o aviso aparece uma vez e abre o painel certo.
- [ ] **Step 5: Três reuniões reais** (critério 5): o que o dono mudou à mão antes de mandar vai para o `RASTREABILIDADE.md`.
- [ ] **Step 6: Commit**

```bash
git add docs
git commit -m "docs: o briefing da reunião, e o que os três primeiros mostraram"
```
