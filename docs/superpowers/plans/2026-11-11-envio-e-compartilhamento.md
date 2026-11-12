# Envio e compartilhamento: plano de implementação

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A ata conferida e o briefing saem de dentro do app com os destinatários já preenchidos, o app guarda o que saiu de fato, e as pendências de um projeto ficam visíveis para quem não tem o PulseMeet.

**Architecture:** Três partes independentes, na ordem do desenho. **A** abre o programa de e-mail com destinatários e assunto, sem permissão nova. **B** escreve um arquivo HTML e um `.xlsx` por projeto numa pasta que já sincroniza. **C**, depois de um portão que confere a conta do Google, manda pelo Gmail com o escopo `gmail.send` e registra o envio só com a confirmação do Gmail. Toda a lógica mora no `Nucleo` e na `Agenda`, onde a suíte alcança; a ponte junta as peças.

**Tech Stack:** C#/.NET 8, xUnit, System.Text.Json com contexto gerado em compilação, `System.IO.Compression` para o `.xlsx` (como o `.docx` de hoje), OAuth com PKCE e API do Gmail por `HttpClient`, JavaScript sem build no WebView2, `node --test`.

**Spec:** [docs/superpowers/specs/2026-11-11-envio-e-compartilhamento-design.md](../specs/2026-11-11-envio-e-compartilhamento-design.md). As seções citadas como §N são dele.

**É o passo 4 de quatro.** A parte A depende só do passo 1 ([plano](2026-11-11-ata-conferida.md)); a parte B, do passo 2 ([plano](2026-11-11-pendencias-do-projeto.md)); a parte C, do passo 1 para a ata e do passo 3 ([plano](2026-11-11-briefing-da-reuniao.md)) para o briefing. **Antes da primeira tarefa de cada parte, confira que os nomes consumidos existem com as assinaturas daqueles planos.**

## Respostas assumidas

O desenho deixou seis perguntas (§9), e disse que elas **mudam o desenho**. Este plano foi escrito antes das respostas.

| pergunta | o que este plano assume | se a resposta for outra |
|---|---|---|
| 1. De que conta as atas saem? | Da mesma conta do Google em que a agenda está conectada | **a parte C inteira muda de natureza**; a Tarefa 6 existe para confirmar isso antes de construir. A e B não mudam |
| 2. Quem precisa ver as pendências? | O time e o cliente, só para ler, de qualquer navegador | nada a mudar na parte B |
| 3. Ver basta, ou eles precisam mexer? | Ver basta | duas vias é outro passo (§5.3), fora deste plano |
| 4. O arquivo numa pasta sincronizada serve? | Sim | a planilha do Google (§5.2) está fora deste plano; entraria no lugar das Tarefas 3 a 5 |
| 5. Vale escrever na descrição do convite? | Não; o e-mail basta | fora deste plano (§4.4) |
| 6. O app vai ser usado por mais gente com o envio? | Por uma pessoa, por ora | com mais gente, a verificação do Google entra antes da Tarefa 7, com o prazo dela |

## Global Constraints

- Comandos de teste sempre com `export PATH="$HOME/.dotnet:$PATH"` antes. Suítes: `dotnet test app-net/Tests/MeetingApp.Tests.csproj` e `node --test tools/web/*.test.mjs`. Toda tarefa termina com as duas verdes.
- **Só ata conferida é enviada**, por qualquer caminho.
- **Nada sai sem um gesto da pessoa.** Nenhum envio agendado, automático ou repetido.
- **Uma tentativa por gesto.** O envio que fica sem resposta não é repetido: o app diz que não sabe se saiu e pede para conferir os Enviados.
- **"Enviada" pelo Gmail só com a confirmação do Gmail** (o id da mensagem).
- **Os destinatários saem só de `attendee_emails`.** Nunca se casa nome com e-mail por posição (`GRA-2`).
- **O único escopo novo é `https://www.googleapis.com/auth/gmail.send`**, pedido só quando a pessoa usa o envio. Quem usa só a agenda nunca vê o pedido e continua conectado.
- **O e-mail é montado pelo app:** HTML de `htmlParaEmail`, sem imagem, script, estilo externo nem link.
- **O painel compartilhado não leva nota interna, fala citada, nem item de ata em rascunho.**
- **Uma via só:** o app escreve o painel, os outros leem.
- **O segredo do Google** é obrigatório no binário publicado; `tools/publicar.sh` confere, e nada aqui o muda.
- Trimming, nós em vez de `innerHTML`, nenhum dado de cliente real, acervo real só com o dono, sem publicar e sem push: valem as regras do plano do passo 1. **Nenhum teste manda e-mail de verdade nem fala com o Google.**

## Review Focus

1. **Reunião sem `attendee_emails`** (gravada antes de 14/08/2026, ou sem agenda), ou em que o único e-mail é o do dono: a lista de destinatários vem vazia, a tela pede que a pessoa os escreva, e nada é deduzido dos nomes. Teste na Tarefa 1.
2. **Quebra de linha no título da reunião ou num e-mail** (o título vem da agenda, que é de fora): não vira cabeçalho novo na mensagem nem parâmetro novo no `mailto:`. Testes nas Tarefas 1 e 8.
3. **A rede cai depois de o pedido sair:** o resultado é "sem resposta", nada é marcado como enviado, e o cliente HTTP foi chamado uma vez só. Teste na Tarefa 9.
4. **A pasta dos painéis some ou o arquivo está aberto no Excel:** a ação da pessoa (concluir, fechar uma pendência) termina bem do mesmo jeito; o painel fica para a próxima atualização. Teste na Tarefa 5.
5. **Texto de pendência com `<`, `&` e aspas:** sai escapado no HTML e no XML da planilha, e os dois continuam bem formados. Testes nas Tarefas 3 e 4.

---

## Mapa dos arquivos

| arquivo | o que muda |
|---|---|
| `app-net/Nucleo/Envio/Destinatarios.cs`, `LinkDeEmail.cs` (novos) | quem recebe, o assunto e o `mailto:` |
| `app-net/Nucleo/Atas/RevisaoDaAta.cs` | `enviada` com modo e destinatários; `RegistrarEnvio` |
| `app-net/Nucleo/Envio/PainelCompartilhado.cs` (novo) | o HTML, o `.xlsx` e a escrita na pasta |
| `app-net/Nucleo/ConfiguracoesDoApp.cs`, `Projetos.cs` | `pasta_dos_paineis`; `compartilhar_painel` |
| `app-net/Nucleo/Envio/MensagemDeEmail.cs` (novo) | a mensagem MIME e a conferência do HTML |
| `app-net/Nucleo/Projeto/ArquivoDoProjeto.cs` | os envios de briefing |
| `app-net/Agenda/Autorizacao.cs`, `Credenciais.cs`, `ClienteDoGmail.cs` (novo) | o escopo a mais; o envio |
| `app-net/App/Ponte.cs` | `abrir-no-email`, `preparar-envio`, `permitir-envio`, `enviar-email`; o painel a cada mudança |
| `app-net/App/web/envio-regras.js`, `envio.js` (novos), `conferencia.js`, `projeto.js`, `configuracoes.js`, `clientes.js`, `app.css` | os botões, a tela de envio, Ajustes |
| `tools/web/envio-regras.test.mjs` (novo) | os testes da tela |

---

## Parte A — abrir no programa de e-mail (§4.1)

### Task 1: Quem recebe, o assunto e o link

**Files:**
- Create: `app-net/Nucleo/Envio/Destinatarios.cs`, `app-net/Nucleo/Envio/LinkDeEmail.cs`
- Test: `app-net/Tests/EnvioTests.cs`

**Interfaces:**
- Consumes: `ConvidadosDaAgenda.Ler(pasta)`.
- Produces (namespace `MeetingApp.Nucleo.Envio`):
  - `static List<string> Destinatarios.DaReuniao(string pasta, string emailDoDono)`: os `attendee_emails`, na ordem, sem o do dono (sem olhar caixa), sem repetição, e só o que `Valido` aceita.
  - `static bool Destinatarios.Valido(string email)`: um `@` só, com algo dos dois lados, e um ponto depois dele; sem espaço, vírgula, ponto e vírgula, `<`, `>`, aspas, `?`, `&` nem caractere de controle.
  - `static string LinkDeEmail.Assunto(string tipo, string? titulo, DateTime quando)`: `"Ata — <título> — dd/MM/aaaa"` para `"ata"` e `"Briefing — …"` para `"briefing"`; o título numa linha só (quebras e tabulações viram espaço, espaços seguidos viram um), e `"reunião"` quando vazio.
  - `const int LinkDeEmail.Maximo = 1900`
  - `static (string Link, IReadOnlyList<string> Fora) LinkDeEmail.Mailto(IReadOnlyList<string> para, string assunto)`: `mailto:<a>,<b>?subject=<assunto>`, com cada endereço e o assunto por `Uri.EscapeDataString` (o `@` do endereço fica literal). Se passar de `Maximo`, saem endereços do fim até caber, e eles voltam em `Fora`.

- [ ] **Step 1: Escrever os testes**

```csharp
public sealed class EnvioTests : IDisposable
{
    private readonly string _pasta = Directory.CreateTempSubdirectory("envio-testes").FullName;
    public void Dispose() => Directory.Delete(_pasta, recursive: true);

    private void Meta(string emails) => File.WriteAllText(Path.Combine(_pasta, "meta.json"),
        "{\"meeting\":{\"title\":\"Semanal\",\"attendees\":[\"Zeca\",\"Bruno Teles\",\"Carla Nunes\"]"
        + (emails.Length > 0 ? ",\"attendee_emails\":[" + emails + "]" : "") + "}}");

    [Fact]
    public void OsDestinatariosSaemSoDosEmailsSemODonoESemRepetir()
    {
        Meta("\"bruno@casa.com\",\"Carla@acme.com\",\"carla@acme.com\",\"DONO@casa.com\",\"sala 3\",\"a@b\"");

        Assert.Equal(["bruno@casa.com", "Carla@acme.com"], Destinatarios.DaReuniao(_pasta, "dono@casa.com"));
    }

    [Fact]
    public void SemEmailsNadaEDeduzidoDosNomes()
    {
        Meta("");
        Assert.Empty(Destinatarios.DaReuniao(_pasta, "dono@casa.com"));
        File.Delete(Path.Combine(_pasta, "meta.json"));
        Assert.Empty(Destinatarios.DaReuniao(_pasta, "dono@casa.com"));
    }

    [Theory]
    [InlineData("a@b.com", true)]
    [InlineData("a@b.com?bcc=x@y.com", false)]
    [InlineData("a@b.com\r\nBcc: x@y.com", false)]
    [InlineData("a b@c.com", false)]
    [InlineData("a@@b.com", false)]
    [InlineData("", false)]
    public void SoPassaOQueEUmEnderecoESoUm(string email, bool esperado) =>
        Assert.Equal(esperado, Destinatarios.Valido(email));

    [Fact]
    public void OAssuntoFicaNumaLinhaSo()
    {
        var dia = new DateTime(2026, 10, 21, 11, 30, 0);
        Assert.Equal("Ata — Semanal do Portal — 21/10/2026", LinkDeEmail.Assunto("ata", "Semanal do Portal", dia));
        Assert.Equal("Ata — Semanal Bcc: x@y.com — 21/10/2026", LinkDeEmail.Assunto("ata", "Semanal\r\nBcc: x@y.com", dia));
        Assert.Equal("Briefing — reunião — 21/10/2026", LinkDeEmail.Assunto("briefing", " ", dia));
    }

    [Fact]
    public void OLinkLevaDestinatariosEAssuntoEscapados()
    {
        var (link, fora) = LinkDeEmail.Mailto(["bruno@casa.com", "carla@acme.com"], "Ata — Semanal & Cia — 21/10/2026");

        Assert.Equal("mailto:bruno@casa.com,carla@acme.com?subject=Ata%20%E2%80%94%20Semanal%20%26%20Cia%20%E2%80%94%2021%2F10%2F2026", link);
        Assert.Empty(fora);
    }

    [Fact]
    public void OLinkQueNaoCabeDeixaDeForaOsUltimosEDizQuais()
    {
        var muitos = Enumerable.Range(0, 120).Select(i => $"pessoa{i:000}@empresa-cliente.com.br").ToList();
        var (link, fora) = LinkDeEmail.Mailto(muitos, "Ata");

        Assert.True(link.Length <= LinkDeEmail.Maximo);
        Assert.NotEmpty(fora);
        Assert.Equal("pessoa119@empresa-cliente.com.br", fora[^1]);
        Assert.Equal(120, fora.Count + link.Split(',').Length);
    }
}
```

- [ ] **Step 2: Rodar e ver falhar**

Run: `dotnet test app-net/Tests/MeetingApp.Tests.csproj --filter "FullyQualifiedName~EnvioTests"`
Expected: FAIL de compilação.

- [ ] **Step 3: Implementar** os dois arquivos.

- [ ] **Step 4: Rodar**

Run: `dotnet test app-net/Tests/MeetingApp.Tests.csproj --filter "FullyQualifiedName~EnvioTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add app-net/Nucleo/Envio app-net/Tests/EnvioTests.cs
git commit -m "feat(envio): os destinatários da reunião, o assunto e o link de e-mail"
```

---

### Task 2: O botão *Abrir no e-mail*

**Files:**
- Modify: `app-net/Nucleo/Atas/RevisaoDaAta.cs`, `app-net/App/Ponte.cs`, `app-net/App/web/conferencia.js`
- Test: `app-net/Tests/RevisaoDaAtaTests.cs`

**Interfaces:**
- Consumes: `Destinatarios`, `LinkDeEmail` (Tarefa 1); `ClienteDaAgenda.EmailDaConta()`; `Transcritor.DataDaReuniao(pasta)`.
- Produces:
  - `MudancaDaConferencia` ganha `"modo"` (`string?`) e `"para"` (`List<string>?`). Na ação `enviada`, o modo aceito de fora é `manual` (o padrão) ou `mailto`; `gmail` vindo da tela é recusado. Os destinatários vão para `EnvioDaAta.Para`.
  - op `abrir-no-email` com `{ gravacao }` → `{ para, assunto, link, fora }`, e abre o link. Recusa a ata que não está conferida, com `"só a ata conferida pode ser enviada"`.

- [ ] **Step 1: Escrever o teste**

```csharp
[Fact]
public void MarcarComoEnviadaGuardaComoEParaQuemESoOAppDizGmail()
{
    var c = ConferenciaTests.Exemplo();
    c.Estado = "conferida";

    RevisaoDaAta.Aplicar(c, new() { Acao = "enviada", Modo = "mailto", Para = ["carla@acme.com"] }, T0);

    Assert.Equal(("mailto", "carla@acme.com"), (c.Envios[0].Modo, c.Envios[0].Para![0]));
    Assert.Throws<InvalidOperationException>(() => RevisaoDaAta.Aplicar(c, new() { Acao = "enviada", Modo = "gmail" }, T0));
}
```

- [ ] **Step 2: Rodar e ver falhar**

Run: `dotnet test app-net/Tests/MeetingApp.Tests.csproj --filter "FullyQualifiedName~RevisaoDaAtaTests"`
Expected: FAIL de compilação.

- [ ] **Step 3: Implementar.** Na ponte, a op monta a resposta e abre o link com `Process.Start` e `UseShellExecute = true`, **só se o texto começa com `mailto:`**. Na tela, *Abrir no e-mail* aparece no topo da ata conferida: copia a ata como o *Copiar* (HTML e texto), chama a op, e mostra "O programa de e-mail abriu com os destinatários e o assunto. Cole o texto no corpo (Ctrl+V)."; se `fora` não é vazio, lista os endereços que não couberam, para copiar. Com `para` vazio, o link abre do mesmo jeito e a tela diz "Esta reunião não guardou os e-mails dos convidados: escreva os destinatários." O *Marcar como enviada* que se segue manda `modo: "mailto"` e o `para`.

- [ ] **Step 4: Rodar e compilar**

Run: `dotnet test app-net/Tests/MeetingApp.Tests.csproj && node --test tools/web/*.test.mjs && dotnet build app-net/App -c Release 2>&1 | tail -3`
Expected: PASS e `0 Erro(s)`.

- [ ] **Step 5: Commit**

```bash
git add app-net/Nucleo/Atas/RevisaoDaAta.cs app-net/App app-net/Tests/RevisaoDaAtaTests.cs
git commit -m "feat(envio): abrir no programa de e-mail com destinatários e assunto"
```

---

## Parte B — o painel numa pasta que já sincroniza (§5.1)

### Task 3: O painel em HTML

**Files:**
- Create: `app-net/Nucleo/Envio/PainelCompartilhado.cs`
- Test: `app-net/Tests/PainelCompartilhadoTests.cs`

**Interfaces:**
- Consumes: `RegistroDoProjeto`, `ItemDoProjeto`, `NomesDosLados`, `ProjetoDeExemplo`.
- Produces: `static string PainelCompartilhado.Html(RegistroDoProjeto r, string cliente, string projeto, NomesDosLados nomes, DateTimeOffset agora)`.

**O conteúdo**, num documento HTML completo, com o estilo num `<style>` dentro dele e nada de fora:
1. o título `Pendências — <cliente> › <projeto>`;
2. a linha `Atualizado em dd/MM/aaaa às HH:mm pelo PulseMeet. Este arquivo é reescrito a cada atualização: o que for mudado aqui se perde.`;
3. quando há reunião não conferida depois da última conferida: `A reunião de dd/MM ainda não foi conferida; este painel vai até dd/MM.`;
4. duas tabelas de pendências abertas, a do cliente primeiro, cada uma sob um `<h2>Pendentes — <nome do lado></h2>`; colunas *O que*, *Responsável*, *Prazo*, *Situação* (`vencida há N dias`, `em dia` ou `sem data`) e *Desde* (`dd/MM`);
5. *Concluídas nos últimos 30 dias*: o que e quando;
6. *Decisões vigentes*: tema e texto, com a data.

Tabela sem linha não aparece. **Nada de história, de fala citada nem de nota.** Todo texto escapado (`&`, `<`, `>`, `"`, `'`).

- [ ] **Step 1: Escrever os testes**

```csharp
public sealed class PainelCompartilhadoTests : IDisposable
{
    private readonly string _raiz = Directory.CreateTempSubdirectory("painel-testes").FullName;
    public void Dispose() => Directory.Delete(_raiz, recursive: true);

    private static readonly NomesDosLados Nomes = new("Acme Bebidas", "Casa");
    private static readonly DateTimeOffset Agora = DateTimeOffset.Parse("2026-10-30T09:15:00-03:00");

    private static RegistroDoProjeto Registro(Action<ArquivoDoProjeto>? mexer = null)
    {
        var a = new ArquivoDoProjeto { Cliente = "Acme Bebidas", Projeto = "Portal de Pedidos" };
        mexer?.Invoke(a);
        return RegistroDoProjeto.Montar(ProjetoDeExemplo.Atas(), a);
    }

    [Fact]
    public void OPainelTrazAsAbertasPorLadoEAvisaQueEReescrito()
    {
        string html = PainelCompartilhado.Html(Registro(), "Acme Bebidas", "Portal de Pedidos", Nomes, Agora);

        Assert.StartsWith("<!doctype html>", html);
        Assert.Contains("Pendências — Acme Bebidas › Portal de Pedidos", html);
        Assert.Contains("Atualizado em 30/10/2026 às 09:15 pelo PulseMeet.", html);
        Assert.Contains("o que for mudado aqui se perde", html);
        Assert.Contains("A reunião de 28/10 ainda não foi conferida; este painel vai até 21/10.", html);
        Assert.Contains("vencida há 7 dias", html);
        Assert.Contains("sem data", html);
        Assert.True(html.IndexOf("Acme Bebidas</h2>") < html.IndexOf("Casa</h2>"));
        Assert.Contains("Montar o painel de ruptura", html);          // concluída há menos de 30 dias
        Assert.DoesNotContain("Só no rascunho", html);
    }

    [Fact]
    public void NotaInternaEFalaCitadaNaoSaemEOTextoVaiEscapado()
    {
        var r = Registro(a =>
        {
            a.Acontecimentos.Add(new() { Em = "2026-10-27T09:00:00-03:00", Reuniao = ProjetoDeExemplo.R1, Item = "i2",
                                         Tipo = "nota", Texto = "segredo interno" });
            a.Itens.Add(new() { Id = "m1", Tipo = "pendente", Texto = "Rever <script>alert(1)</script> & \"aspas\"",
                                Lado = "nosso", CriadoEm = "2026-10-20T09:00:00-03:00" });
        });

        string html = PainelCompartilhado.Html(r, "Acme Bebidas", "Portal de Pedidos", Nomes, Agora);

        Assert.DoesNotContain("segredo interno", html);
        Assert.DoesNotContain("fala de", html);
        Assert.DoesNotContain("<script>alert", html);
        Assert.Contains("Rever &lt;script&gt;alert(1)&lt;/script&gt; &amp; &quot;aspas&quot;", html);
        Assert.DoesNotContain("http", html);                          // nada de fora
    }
}
```

- [ ] **Step 2: Rodar e ver falhar**

Run: `dotnet test app-net/Tests/MeetingApp.Tests.csproj --filter "FullyQualifiedName~PainelCompartilhadoTests"`
Expected: FAIL de compilação.

- [ ] **Step 3: Implementar** `Html`. As cores do documento são fixas e claras (é um arquivo para qualquer navegador, e não a tela do app), com `@media print` deixando as tabelas inteiras.

- [ ] **Step 4: Rodar**

Run: `dotnet test app-net/Tests/MeetingApp.Tests.csproj --filter "FullyQualifiedName~PainelCompartilhadoTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add app-net/Nucleo/Envio/PainelCompartilhado.cs app-net/Tests/PainelCompartilhadoTests.cs
git commit -m "feat(painel): as pendências do projeto num HTML que se abre em qualquer navegador"
```

---

### Task 4: O painel em planilha

**Files:**
- Modify: `app-net/Nucleo/Envio/PainelCompartilhado.cs`
- Test: `app-net/Tests/PainelCompartilhadoTests.cs`

**Interfaces:**
- Produces: `static void PainelCompartilhado.Xlsx(RegistroDoProjeto r, NomesDosLados nomes, DateTimeOffset agora, string destino)`.

**O arquivo:** um `.xlsx` mínimo, escrito à mão com `ZipArchive` como o `.docx` de `Exportacao.cs`: `[Content_Types].xml`, `_rels/.rels`, `xl/workbook.xml`, `xl/_rels/workbook.xml.rels` e `xl/worksheets/sheet1.xml`. Uma planilha, "Pendências", com todas as células como texto embutido (`t="inlineStr"`), sem estilos nem fórmulas. A primeira linha diz `Atualizado em … pelo PulseMeet. Este arquivo é reescrito a cada atualização.`; a segunda são os títulos `Lado`, `O que`, `Responsável`, `Prazo`, `Data do prazo`, `Situação`, `Desde`, `Cobrada em`; depois uma linha por pendência aberta, o cliente primeiro. *Lado* é o nome do lado; *Data do prazo* e *Desde* são `dd/MM/aaaa`; *Cobrada em* é o número de reuniões.

- [ ] **Step 1: Escrever o teste**

```csharp
[Fact]
public void APlanilhaAbreTemUmaLinhaPorPendenciaEEstaBemFormada()
{
    string destino = Path.Combine(_raiz, "painel.xlsx");
    var r = Registro(a => a.Itens.Add(new() { Id = "m1", Tipo = "pendente", Texto = "Rever <b> & \"aspas\"",
                                              Lado = "nosso", CriadoEm = "2026-10-20T09:00:00-03:00" }));

    PainelCompartilhado.Xlsx(r, Nomes, Agora, destino);

    using var zip = System.IO.Compression.ZipFile.OpenRead(destino);
    Assert.Equal(["[Content_Types].xml", "_rels/.rels", "xl/_rels/workbook.xml.rels", "xl/workbook.xml", "xl/worksheets/sheet1.xml"],
                 zip.Entries.Select(e => e.FullName).Order(StringComparer.Ordinal));

    using var leitor = new StreamReader(zip.GetEntry("xl/worksheets/sheet1.xml")!.Open());
    var folha = System.Xml.Linq.XDocument.Parse(leitor.ReadToEnd());        // bem formada, ou lança
    var linhas = folha.Descendants().Where(e => e.Name.LocalName == "row").ToList();

    Assert.Equal(2 + 4, linhas.Count);                                      // aviso, títulos, 2 do cliente, 2 nossas
    string[] Celulas(int i) => [.. linhas[i].Descendants().Where(e => e.Name.LocalName == "t").Select(e => e.Value)];
    Assert.Equal(["Lado", "O que", "Responsável", "Prazo", "Data do prazo", "Situação", "Desde", "Cobrada em"], Celulas(1));
    Assert.Equal(["Acme Bebidas", "Enviar a base atualizada", "Carla", "sexta", "23/10/2026", "vencida há 7 dias", "14/10/2026", "2"], Celulas(2));
    Assert.Contains("Rever <b> & \"aspas\"", linhas.SelectMany(l => l.Descendants()).Select(e => e.Value));
}
```

- [ ] **Step 2: Rodar e ver falhar**

Run: `dotnet test app-net/Tests/MeetingApp.Tests.csproj --filter "FullyQualifiedName~PainelCompartilhadoTests"`
Expected: FAIL de compilação.

- [ ] **Step 3: Implementar** `Xlsx`. Célula vazia sai como texto vazio, para as colunas não escorregarem.

- [ ] **Step 4: Rodar**

Run: `dotnet test app-net/Tests/MeetingApp.Tests.csproj --filter "FullyQualifiedName~PainelCompartilhadoTests"`
Expected: PASS. Abrir o arquivo no Excel ou no LibreOffice fica para a Tarefa 11.

- [ ] **Step 5: Commit**

```bash
git add app-net/Nucleo/Envio/PainelCompartilhado.cs app-net/Tests/PainelCompartilhadoTests.cs
git commit -m "feat(painel): as pendências abertas numa planilha"
```

---

### Task 5: O painel se atualiza sozinho

**Files:**
- Modify: `app-net/Nucleo/Envio/PainelCompartilhado.cs`, `app-net/Nucleo/ConfiguracoesDoApp.cs`, `app-net/Nucleo/Projetos.cs`
- Modify: `app-net/App/Ponte.cs`, `app-net/App/web/configuracoes.js`, `app-net/App/web/clientes.js`
- Test: `app-net/Tests/PainelCompartilhadoTests.cs`

**Interfaces:**
- Consumes: `LeitorDoProjeto.Ler`.
- Produces:
  - `ConfiguracoesDoApp.PastaDosPaineis` (`"pasta_dos_paineis"`, `string?`).
  - `PreferenciasDoProjeto.CompartilharPainel` (`"compartilhar_painel"`, `bool?`).
  - `static string PainelCompartilhado.NomeBase(string cliente, string projeto)`: `"Pendências - <cliente> - <projeto>"`, com cada um de `\ / : * ? " < > |` e os caracteres de controle trocado por `-`. A lista é escrita no código, e não `Path.GetInvalidFileNameChars()`: a suíte roda em Linux, onde só a barra é inválida, e o arquivo vai para o Windows.
  - op `atualizar-painel` com `{ cliente, projeto }` → `{ arquivo }`: escreve o painel agora e devolve o caminho do HTML; sem pasta escolhida, erro com o caminho para Ajustes › Geral.
  - `static bool PainelCompartilhado.Atualizar(string raiz, string cliente, string projeto, string? destino, NomesDosLados nomes, DateTimeOffset agora)`: lê o registro, escreve `<NomeBase>.html` e `<NomeBase>.xlsx` em `destino`, cada um por `.tmp` e troca. Devolve se escreveu. **Nunca lança:** sem destino, sem cliente ou projeto, pasta que não existe (não a cria) ou arquivo travado devolvem falso.

- [ ] **Step 1: Escrever os testes**

```csharp
private void GravarOProjeto()
{
    foreach (var a in ProjetoDeExemplo.Atas())
    {
        string pasta = AcervoTests.Gravar(_raiz, a.Reuniao, a.Titulo, cliente: "Acme Bebidas",
                                          projeto: "Portal de Pedidos", ata: "x");
        a.Conferencia!.Salvar(pasta);
    }
}

[Fact]
public void AtualizarEscreveOsDoisArquivosComONomeDoProjeto()
{
    GravarOProjeto();
    string destino = Directory.CreateDirectory(Path.Combine(_raiz, "_saida")).FullName;

    Assert.True(PainelCompartilhado.Atualizar(_raiz, "Acme Bebidas", "Portal de Pedidos", destino, Nomes, Agora));

    Assert.Equal(["Pendências - Acme Bebidas - Portal de Pedidos.html", "Pendências - Acme Bebidas - Portal de Pedidos.xlsx"],
                 Directory.GetFiles(destino).Select(Path.GetFileName).Order());
    Assert.Equal("Pendências - A-B Testes - Operação- SP", PainelCompartilhado.NomeBase("A/B Testes", "Operação: SP"));
}

[Fact]
public void SemPastaOuComArquivoTravadoNaoLancaENaoEscreve()
{
    GravarOProjeto();
    Assert.False(PainelCompartilhado.Atualizar(_raiz, "Acme Bebidas", "Portal de Pedidos", null, Nomes, Agora));
    Assert.False(PainelCompartilhado.Atualizar(_raiz, "Acme Bebidas", "Portal de Pedidos",
                                               Path.Combine(_raiz, "não-existe"), Nomes, Agora));

    string destino = Directory.CreateDirectory(Path.Combine(_raiz, "_saida")).FullName;
    // Um diretório no lugar do arquivo: não dá para trocar, como um .xlsx aberto no Excel.
    Directory.CreateDirectory(Path.Combine(destino, "Pendências - Acme Bebidas - Portal de Pedidos.xlsx"));
    Assert.False(PainelCompartilhado.Atualizar(_raiz, "Acme Bebidas", "Portal de Pedidos", destino, Nomes, Agora));
}
```

- [ ] **Step 2: Rodar e ver falhar**

Run: `dotnet test app-net/Tests/MeetingApp.Tests.csproj --filter "FullyQualifiedName~PainelCompartilhadoTests"`
Expected: FAIL de compilação.

- [ ] **Step 3: Implementar** `Atualizar`, `NomeBase` e as duas chaves.

- [ ] **Step 4: Ligar na ponte.** Um método `AtualizarOPainel(string? cliente, string? projeto)`, que não faz nada sem `PastaDosPaineis` ou sem `CompartilharPainel == true` no projeto, e que roda fora do caminho da resposta (`Task.Run`, com tudo engolido). Chame-o depois de: a op `conferencia` (quando a ata está conferida), `usar-proposta`, o fim de `gerar-ata`, `projeto-acao`, `salvar-reuniao` (para o projeto antigo e o novo) e `apagar-gravacao`.

- [ ] **Step 5: Ajustes.**
- `configuracoes.js`, Ajustes › Geral, junto das outras pastas: "Pasta dos painéis compartilhados", com a dica "Um arquivo por projeto, com as pendências abertas. Escolha uma pasta do OneDrive ou do Google Drive e compartilhe o link uma vez." e o ⓘ: "O app escreve nessa pasta um HTML e uma planilha por projeto que você marcar. Eles levam as pendências abertas, as concluídas recentes e as decisões — sem nota interna, sem fala e sem áudio. Quem tem o link vê a versão mais nova quando a pasta sincroniza."
- `clientes.js`, no painel do projeto: a chave "Manter um painel de pendências na pasta compartilhada", gravando `compartilhar_painel`; sem pasta escolhida, desabilitada, com o caminho para Ajustes › Geral. Ao ligar, a tela chama a op `atualizar-painel` e mostra o caminho do HTML que ela devolve.

- [ ] **Step 6: Rodar e compilar**

Run: `dotnet test app-net/Tests/MeetingApp.Tests.csproj && node --test tools/web/*.test.mjs && dotnet build app-net/App -c Release 2>&1 | tail -3`
Expected: PASS e `0 Erro(s)`.

- [ ] **Step 7: Commit**

```bash
git add app-net/Nucleo app-net/App app-net/Tests/PainelCompartilhadoTests.cs
git commit -m "feat(painel): o arquivo do projeto se atualiza a cada mudança das pendências"
```

---

## Parte C — enviar pelo Gmail (§4.2)

### Task 6: O portão — a conta e o cliente OAuth, com o dono

O §4.3 lista o que não se resolve lendo documentação. **As Tarefas 7 a 10 não começam sem o resultado desta, escrito no desenho.**

- [ ] **Step 1: De que conta as atas saem** (pergunta 1 do §9). Se não for a conta do Google em que a agenda está conectada, **pare aqui**: a parte C precisa ser redesenhada, e as partes A e B já resolvem o que dá sem ela.
- [ ] **Step 2: O estado do cliente OAuth**, no console do Google Cloud do projeto: *Testing* ou *In production*; quais contas estão cadastradas como teste; e se o token de hoje expira em 7 dias. Anotar no §4.3 do desenho.
- [ ] **Step 3: O escopo.** Acrescentar `gmail.send` à tela de consentimento do projeto no console. Em *Testing*, isso basta para as contas de teste. Em *In production*, o Google pede nova verificação: anotar o prazo que ele informar e decidir com o dono se o envio espera a verificação ou se o cliente volta para *Testing* (com o custo do token de 7 dias).
- [ ] **Step 4: A conta de trabalho.** Com a conta de onde as atas saem, autorizar o escopo uma vez pelo OAuth Playground do Google (ou pelo fluxo do app, depois da Tarefa 7, numa cópia) e mandar uma mensagem de teste **para o próprio dono**. Se a administração do Workspace bloquear, anotar a mensagem de erro e parar.
- [ ] **Step 5:** Escrever no desenho (§4.3 e §9) o que se achou, com a data, e commitar.

```bash
git add docs/superpowers/specs/2026-11-11-envio-e-compartilhamento-design.md
git commit -m "docs(envio): o que a conta e o cliente OAuth permitem"
```

---

### Task 7: O escopo a mais, sem desconectar a agenda

**Files:**
- Modify: `app-net/Agenda/Autorizacao.cs`, `app-net/Agenda/Credenciais.cs`
- Create: `app-net/Agenda/ClienteDoGmail.cs` (só a constante, nesta tarefa)
- Test: `app-net/Tests/AgendaTests.cs`

**Interfaces:**
- Produces:
  - `const string ClienteDoGmail.Escopo = "https://www.googleapis.com/auth/gmail.send"`
  - `static IReadOnlyList<string> Credenciais.EscoposDoToken(string? caminho = null)`: o campo `scopes` do arquivo do token; vazio se não existe ou ilegível.
  - `static bool ClienteDoGmail.PodeEnviar(string? caminhoDoToken = null)`: o token tem o escopo.
  - `static string Autorizacao.UrlDeAutorizacao(string authUri, string clientId, string redirect, IEnumerable<string> escopos, string estado, string desafio, string? conta, bool incremental)`: a URL que `AutorizarAsync` monta hoje, extraída. Com `incremental`: `include_granted_scopes=true`, `prompt=consent` (sem `select_account`) e `login_hint=<conta>` quando há conta.
  - `static Task<bool> Autorizacao.AcrescentarEscopoAsync(string escopo, CancellationToken ct = default)`: o mesmo fluxo de `AutorizarAsync`, **sem** o `Desconectar()` do começo, pedindo os escopos que o token já tem mais o novo, com `incremental`. Dando certo, o token é regravado com a união dos escopos; cancelado ou recusado, **o token de antes continua como estava**.

- [ ] **Step 1: Escrever os testes**

```csharp
[Fact]
public void AUrlIncrementalPedeOsDoisEscoposNaMesmaContaSemTrocarDeConta()
{
    string url = Autorizacao.UrlDeAutorizacao("https://accounts.google.com/o/oauth2/auth", "id-do-cliente",
        "http://127.0.0.1:5000/", [ClienteDaAgenda.Escopo, ClienteDoGmail.Escopo], "estado", "desafio",
        "dono@casa.com", incremental: true);

    Assert.Contains("include_granted_scopes=true", url);
    Assert.Contains("login_hint=dono%40casa.com", url);
    Assert.Contains("scope=" + Uri.EscapeDataString(ClienteDaAgenda.Escopo + " " + ClienteDoGmail.Escopo), url);
    Assert.Contains("prompt=consent", url);
    Assert.DoesNotContain("select_account", url);
    Assert.Contains("access_type=offline", url);
}

[Fact]
public void AUrlDeConectarAAgendaNaoMudou()
{
    string url = Autorizacao.UrlDeAutorizacao("https://accounts.google.com/o/oauth2/auth", "id-do-cliente",
        "http://127.0.0.1:5000/", [ClienteDaAgenda.Escopo], "estado", "desafio", null, incremental: false);

    Assert.Contains("prompt=" + Uri.EscapeDataString("select_account consent"), url);
    Assert.DoesNotContain("include_granted_scopes", url);
    Assert.DoesNotContain("gmail", url);
}

[Fact]
public void SoPodeEnviarQuemTemOEscopoNoToken()
{
    string token = Path.Combine(Path.GetTempPath(), "token-" + Guid.NewGuid().ToString("N") + ".json");
    try
    {
        Assert.False(ClienteDoGmail.PodeEnviar(token));                               // sem arquivo
        File.WriteAllText(token, $$"""{"scopes":["{{ClienteDaAgenda.Escopo}}"]}""");
        Assert.False(ClienteDoGmail.PodeEnviar(token));                               // só a agenda
        File.WriteAllText(token, $$"""{"scopes":["{{ClienteDaAgenda.Escopo}}","{{ClienteDoGmail.Escopo}}"]}""");
        Assert.True(ClienteDoGmail.PodeEnviar(token));
        File.WriteAllText(token, "{ quebrado");
        Assert.False(ClienteDoGmail.PodeEnviar(token));
    }
    finally { File.Delete(token); }
}
```

- [ ] **Step 2: Rodar e ver falhar**

Run: `dotnet test app-net/Tests/MeetingApp.Tests.csproj --filter "FullyQualifiedName~AgendaTests"`
Expected: FAIL de compilação.

- [ ] **Step 3: Implementar.** `AutorizarAsync` passa a montar a URL por `UrlDeAutorizacao(..., incremental: false)`, e o resto dele não muda. `AcrescentarEscopoAsync` guarda o token novo num arquivo temporário e só troca no fim; `SalvarTokenAsync` passa a receber a lista de escopos.

- [ ] **Step 4: Rodar a suíte**

Run: `dotnet test app-net/Tests/MeetingApp.Tests.csproj`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add app-net/Agenda app-net/Tests/AgendaTests.cs
git commit -m "feat(envio): o escopo de enviar entra por consentimento a mais, sem desconectar a agenda"
```

---

### Task 8: A mensagem

**Files:**
- Create: `app-net/Nucleo/Envio/MensagemDeEmail.cs`
- Test: `app-net/Tests/MensagemDeEmailTests.cs`

**Interfaces:**
- Consumes: `Destinatarios.Valido`.
- Produces:
  - `static string MensagemDeEmail.Texto(string? abertura, string ataMd)`: a abertura aparada, uma linha em branco e a ata; sem abertura, só a ata. É o texto que vai como `text/plain` **e** o que vira o `ata-email.md`.
  - `static void MensagemDeEmail.ConferirHtml(string html)`: lança `InvalidOperationException("o corpo do e-mail tem conteúdo que o app não escreve")` se o HTML, sem olhar caixa, contiver `<script`, `<img`, `<iframe`, `<object`, `<link` ou `<style`, ou se **dentro de uma tag** (`<…>`) houver `href=`, `src=`, `url(` ou um atributo `on…=`. O texto entre as tags já chega escapado e não é olhado.
  - `static string MensagemDeEmail.Montar(string de, IReadOnlyList<string> para, IReadOnlyList<string> cc, string assunto, string html, string texto, string fronteira)`: a mensagem RFC 5322, com `\r\n`.
  - `static string MensagemDeEmail.ParaOGmail(string mensagem)`: base64 da mensagem em UTF-8, no alfabeto de URL (`-` e `_`), sem `=`.

**A mensagem:** os cabeçalhos `From`, `To` (endereços separados por vírgula e espaço), `Cc` (só quando há), `Subject` (como uma palavra codificada só, `=?UTF-8?B?…?=`), `MIME-Version: 1.0`, `Content-Type: multipart/alternative; boundary="<fronteira>"`; depois as duas partes, `text/plain; charset=UTF-8` e `text/html; charset=UTF-8`, cada uma com `Content-Transfer-Encoding: base64` e o corpo em linhas de 76 caracteres. `Montar` recusa (`ArgumentException`) qualquer endereço que `Destinatarios.Valido` reprove, lista `para` vazia, e assunto com `\r` ou `\n`; e chama `ConferirHtml`.

- [ ] **Step 1: Escrever os testes**

```csharp
public sealed class MensagemDeEmailTests
{
    private const string Html = "<div style=\"color:#1F1F1F\"><p><strong>Resolvidos em reunião</strong></p></div>";

    private static string Montar(string assunto = "Ata — Semanal — 21/10/2026", string html = Html,
                                 string[]? para = null, string[]? cc = null) =>
        MensagemDeEmail.Montar("dono@casa.com", para ?? ["carla@acme.com", "bruno@casa.com"], cc ?? [],
                               assunto, html, "Resolvidos em reunião", "fronteira-1");

    private static string Parte(string mensagem, int n) => System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(
        string.Concat(mensagem.Split("--fronteira-1")[n].Split("\r\n\r\n", 2)[1].Split("\r\n"))));

    [Fact]
    public void AMensagemTemOsCabecalhosEAsDuasPartesEmBase64()
    {
        string m = Montar();

        Assert.StartsWith("From: dono@casa.com\r\nTo: carla@acme.com, bruno@casa.com\r\nSubject: =?UTF-8?B?", m);
        Assert.DoesNotContain("\r\nCc:", m);
        Assert.Contains("Content-Type: multipart/alternative; boundary=\"fronteira-1\"", m);
        Assert.Equal("Resolvidos em reunião", Parte(m, 1));
        Assert.Equal(Html, Parte(m, 2));
        Assert.EndsWith("--fronteira-1--\r\n", m);
        Assert.All(m.Split("\r\n"), linha => Assert.True(linha.Length <= 998));
    }

    [Fact]
    public void OAssuntoComAcentoVoltaIgualDepoisDeDecodificado()
    {
        string codificado = Montar().Split("\r\n").Single(l => l.StartsWith("Subject: "))["Subject: =?UTF-8?B?".Length..^2];
        Assert.Equal("Ata — Semanal — 21/10/2026", System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(codificado)));
    }

    [Theory]
    [InlineData("Ata\r\nBcc: x@y.com")]
    [InlineData("Ata\nBcc: x@y.com")]
    public void QuebraDeLinhaNoAssuntoERecusada(string assunto) =>
        Assert.Throws<ArgumentException>(() => Montar(assunto));

    [Fact]
    public void EnderecoRuimESemDestinatarioSaoRecusados()
    {
        Assert.Throws<ArgumentException>(() => Montar(para: ["a@b.com\r\nBcc: x@y.com"]));
        Assert.Throws<ArgumentException>(() => Montar(para: []));
        Assert.Throws<ArgumentException>(() => Montar(cc: ["sem arroba"]));
    }

    [Theory]
    [InlineData("<p>ok</p><script>alert(1)</script>")]
    [InlineData("<p><img src=\"http://x/y.png\"></p>")]
    [InlineData("<a HREF=\"http://x\">x</a>")]
    [InlineData("<p onclick=\"x()\">x</p>")]
    [InlineData("<p style=\"background:url(http://x)\">x</p>")]
    public void OCorpoSoLevaOQueOAppEscreve(string html) =>
        Assert.Throws<InvalidOperationException>(() => Montar(html: html));

    [Fact]
    public void OTextoEAAberturaEAAta()
    {
        Assert.Equal("Segue a ata.\n\n**Pendentes**\n", MensagemDeEmail.Texto("  Segue a ata. ", "**Pendentes**\n"));
        Assert.Equal("**Pendentes**\n", MensagemDeEmail.Texto(" ", "**Pendentes**\n"));
    }

    [Fact]
    public void ParaOGmailUsaOAlfabetoDeUrlSemEnchimento()
    {
        string cru = MensagemDeEmail.ParaOGmail("ÿþ?>");
        Assert.DoesNotContain('+', cru);
        Assert.DoesNotContain('/', cru);
        Assert.DoesNotContain('=', cru);
        Assert.Equal("ÿþ?>", System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(
            cru.Replace('-', '+').Replace('_', '/').PadRight(cru.Length + (4 - cru.Length % 4) % 4, '='))));
    }
}
```

- [ ] **Step 2: Rodar e ver falhar**

Run: `dotnet test app-net/Tests/MeetingApp.Tests.csproj --filter "FullyQualifiedName~MensagemDeEmailTests"`
Expected: FAIL de compilação.

- [ ] **Step 3: Implementar** `MensagemDeEmail.cs`.

- [ ] **Step 4: Rodar**

Run: `dotnet test app-net/Tests/MeetingApp.Tests.csproj --filter "FullyQualifiedName~MensagemDeEmailTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add app-net/Nucleo/Envio/MensagemDeEmail.cs app-net/Tests/MensagemDeEmailTests.cs
git commit -m "feat(envio): a mensagem de e-mail montada pelo app, e nada além do que ele escreve"
```

---

### Task 9: Enviar uma vez, e registrar só com a confirmação

**Files:**
- Modify: `app-net/Agenda/ClienteDoGmail.cs`, `app-net/Nucleo/Atas/RevisaoDaAta.cs`, `app-net/Nucleo/Projeto/ArquivoDoProjeto.cs`
- Test: `app-net/Tests/ClienteDoGmailTests.cs`, `app-net/Tests/RevisaoDaAtaTests.cs`

**Interfaces:**
- Produces:

```csharp
// Agenda
public enum DesfechoDoEnvio { Enviada, Recusada, SemPermissao, SemResposta }
public sealed record ResultadoDoEnvio(DesfechoDoEnvio Desfecho, string? IdDaMensagem, string Detalhe);
public sealed class ClienteDoGmail(HttpClient http, Func<CancellationToken, Task<string?>> token)
{
    public const string Endereco = "https://gmail.googleapis.com/gmail/v1/users/me/messages/send";
    public Task<ResultadoDoEnvio> EnviarAsync(string mensagemParaOGmail, CancellationToken ct = default);
}
// Nucleo
public static Conferencia RevisaoDaAta.RegistrarEnvio(string pasta, IReadOnlyList<string> para, string idDaMensagem,
                                                      string textoEnviado, DateTimeOffset agora);
// ArquivoDoProjeto ganha "envios": List<EnvioDoBriefing> { "em", "para", "assunto", "id_da_mensagem" }
```

**As regras de `EnviarAsync`:** um `POST` só, com o corpo `{"raw":"…"}` e o `Bearer`. Token nulo: `SemPermissao`, **sem chamar a rede**. `200`: lê o `id` da resposta; com id, `Enviada`; sem id, `SemResposta`. `401` e `403`: `SemPermissao`, com o texto do erro. Outro código: `Recusada`, com `HTTP <código>` e a mensagem do Google quando houver. `HttpRequestException`, `TaskCanceledException` e `IOException`: `SemResposta`. **Nenhum desfecho repete o pedido.**

**`RegistrarEnvio`:** exige a ata conferida; acrescenta `EnvioDaAta { Em, Modo = "gmail", Para, IdDaMensagem }`; salva o registro; copia um `ata-email.md` existente para `ata-email.anterior.md` e escreve `textoEnviado` em `ata-email.md`. Não deriva nem toca no `ata.md`.

- [ ] **Step 1: Escrever os testes**

```csharp
public sealed class ClienteDoGmailTests
{
    private sealed class Falso(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        public int Chamadas;
        public string? Corpo, Autorizacao;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage pedido, CancellationToken ct)
        {
            Chamadas++;
            Corpo = await pedido.Content!.ReadAsStringAsync(ct);
            Autorizacao = pedido.Headers.Authorization?.ToString();
            return responder(pedido);
        }
    }

    private static (ClienteDoGmail, Falso) Cliente(Func<HttpRequestMessage, HttpResponseMessage> responder, string? token = "tok")
    {
        var falso = new Falso(responder);
        return (new ClienteDoGmail(new HttpClient(falso), _ => Task.FromResult(token)), falso);
    }

    private static HttpResponseMessage Resposta(int codigo, string corpo) =>
        new((System.Net.HttpStatusCode)codigo) { Content = new StringContent(corpo) };

    [Fact]
    public async Task ComOIdDoGmailFoiEnviada()
    {
        var (c, falso) = Cliente(_ => Resposta(200, """{"id":"18c3","threadId":"18c3"}"""));

        var r = await c.EnviarAsync("bWVuc2FnZW0");

        Assert.Equal((DesfechoDoEnvio.Enviada, "18c3"), (r.Desfecho, r.IdDaMensagem));
        Assert.Equal("""{"raw":"bWVuc2FnZW0"}""", falso.Corpo);
        Assert.Equal("Bearer tok", falso.Autorizacao);
    }

    [Theory]
    [InlineData(403, DesfechoDoEnvio.SemPermissao)]
    [InlineData(401, DesfechoDoEnvio.SemPermissao)]
    [InlineData(400, DesfechoDoEnvio.Recusada)]
    [InlineData(503, DesfechoDoEnvio.Recusada)]
    public async Task ErroDoGoogleNaoEEnviadaENaoETentadoDeNovo(int codigo, DesfechoDoEnvio esperado)
    {
        var (c, falso) = Cliente(_ => Resposta(codigo, """{"error":{"message":"não pode"}}"""));

        var r = await c.EnviarAsync("x");

        Assert.Equal(esperado, r.Desfecho);
        Assert.Null(r.IdDaMensagem);
        Assert.Contains("não pode", r.Detalhe);
        Assert.Equal(1, falso.Chamadas);
    }

    [Fact]
    public async Task RedeQueCaiNoMeioESemRespostaEUmaChamadaSo()
    {
        var (c, falso) = Cliente(_ => throw new HttpRequestException("conexão caiu"));

        var r = await c.EnviarAsync("x");

        Assert.Equal(DesfechoDoEnvio.SemResposta, r.Desfecho);
        Assert.Equal(1, falso.Chamadas);
    }

    [Fact]
    public async Task SemTokenNaoChamaARede()
    {
        var (c, falso) = Cliente(_ => Resposta(200, "{}"), token: null);

        Assert.Equal(DesfechoDoEnvio.SemPermissao, (await c.EnviarAsync("x")).Desfecho);
        Assert.Equal(0, falso.Chamadas);
    }

    [Fact]
    public async Task DuzentosSemIdNaoEConfirmacao()
    {
        var (c, _) = Cliente(_ => Resposta(200, "{}"));
        Assert.Equal(DesfechoDoEnvio.SemResposta, (await c.EnviarAsync("x")).Desfecho);
    }
}

// RevisaoDaAtaTests
[Fact]
public void RegistrarOEnvioGuardaQuandoParaQuemOIdEOTextoExato()
{
    var c = ConferenciaTests.Exemplo();
    c.Estado = "conferida";
    c.Salvar(_pasta);
    ArquivosDaAta.Derivar(_pasta, c, Nomes, manterData: false);
    var dataDaAta = File.GetLastWriteTimeUtc(Path.Combine(_pasta, "ata.md"));

    var depois = RevisaoDaAta.RegistrarEnvio(_pasta, ["carla@acme.com"], "18c3", "Segue a ata.\n\n**Pendentes**\n", T0);

    var envio = Assert.Single(depois.Envios);
    Assert.Equal(("gmail", "18c3", "carla@acme.com", T0.ToString("o")), (envio.Modo, envio.IdDaMensagem, envio.Para![0], envio.Em));
    Assert.Equal("Segue a ata.\n\n**Pendentes**\n", File.ReadAllText(Path.Combine(_pasta, "ata-email.md")));
    Assert.Equal(dataDaAta, File.GetLastWriteTimeUtc(Path.Combine(_pasta, "ata.md")));

    RevisaoDaAta.RegistrarEnvio(_pasta, ["bruno@casa.com"], "18c4", "de novo\n", T0.AddDays(1));
    Assert.Equal(2, Conferencia.Ler(_pasta)!.Envios.Count);                // reenviar não substitui
    Assert.Equal("Segue a ata.\n\n**Pendentes**\n", File.ReadAllText(Path.Combine(_pasta, "ata-email.anterior.md")));
}

[Fact]
public void NaoSeRegistraEnvioDeAtaEmRascunho()
{
    ConferenciaTests.Exemplo().Salvar(_pasta);
    Assert.Throws<InvalidOperationException>(() => RevisaoDaAta.RegistrarEnvio(_pasta, ["a@b.com"], "id", "x", T0));
    Assert.False(File.Exists(Path.Combine(_pasta, "ata-email.md")));
}
```

- [ ] **Step 2: Rodar e ver falhar**

Run: `dotnet test app-net/Tests/MeetingApp.Tests.csproj --filter "FullyQualifiedName~ClienteDoGmailTests|FullyQualifiedName~RevisaoDaAtaTests"`
Expected: FAIL de compilação.

- [ ] **Step 3: Implementar.** O corpo do pedido e a leitura do `id` usam `JsonNode` ou `Utf8JsonWriter` e `JsonDocument`, sem reflexão. O `ClienteDoGmail` não tem timeout próprio: quem o constrói (a ponte) passa um `HttpClient` com 30 s.

- [ ] **Step 4: Rodar a suíte**

Run: `dotnet test app-net/Tests/MeetingApp.Tests.csproj`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add app-net/Agenda/ClienteDoGmail.cs app-net/Nucleo app-net/Tests
git commit -m "feat(envio): uma tentativa pelo Gmail, e o registro só com o id que ele devolve"
```

---

### Task 10: A tela de envio

§4.2.

**Files:**
- Create: `app-net/App/web/envio-regras.js`, `app-net/App/web/envio.js`
- Modify: `app-net/App/Ponte.cs`, `app-net/App/web/conferencia.js`, `app-net/App/web/projeto.js`, `app-net/App/web/configuracoes.js`, `app-net/App/web/app.css`
- Test: `tools/web/envio-regras.test.mjs`

**Interfaces:**
- Consumes: `Destinatarios`, `LinkDeEmail.Assunto`, `MensagemDeEmail`, `ClienteDoGmail`, `Autorizacao.AcrescentarEscopoAsync`, `RevisaoDaAta.RegistrarEnvio`, `Credenciais.AccessTokenAsync`; `htmlParaEmail`; `markdownDoBriefing`.
- Produces:
  - op `preparar-envio` com `{ tipo: "ata", gravacao }` ou `{ tipo: "briefing", cliente, projeto }` → `{ de, para, assunto, texto, pode_enviar, conectada }`. `de` é `ClienteDaAgenda.EmailDaConta()`. Para o briefing, `para` são os destinatários da reunião mais recente do projeto, e `texto` vem vazio (a tela já tem o texto que a pessoa montou).
  - op `permitir-envio` → `{ pode_enviar }`, depois de `AcrescentarEscopoAsync(ClienteDoGmail.Escopo)`.
  - op `enviar-email` com `{ tipo, gravacao | cliente + projeto, para, cc, assunto, abertura, html, texto }` → `{ desfecho: "enviada" | "recusada" | "sem_permissao" | "sem_resposta", detalhe }`.
  - na resposta de `ata`: `email` (o texto do `ata-email.md`, ou ausente).
  - em `envio-regras.js`: `dominios(enderecos)` (únicos, em ordem alfabética); `fraseDeConfirmacao(para, cc)`; `enderecoValido(texto)` (a mesma régua de `Destinatarios.Valido`); `mudouDepoisDoEnvio(ata, email, abertura)`; `mensagemDoDesfecho(desfecho, detalhe)`.

**As regras de `enviar-email`, na ponte:** uma por vez (um segundo pedido enquanto o primeiro não voltou é recusado com `"já há um envio em curso"`); para a ata, exige a conferida, e o texto é `MensagemDeEmail.Texto(abertura, <ata.md do disco>)` — **o texto que a tela mandou é ignorado**; para o briefing, vale o texto da tela; `MensagemDeEmail.Montar` com uma fronteira aleatória; `EnviarAsync`; **só em `Enviada`**, `RevisaoDaAta.RegistrarEnvio` (ata) ou um `EnvioDoBriefing` no arquivo do projeto (briefing). Nos outros desfechos nada é gravado.

- [ ] **Step 1: Escrever os testes** em `tools/web/envio-regras.test.mjs`

```js
import { test } from "node:test";
import assert from "node:assert/strict";

const R = await import(new URL("../../app-net/App/web/envio-regras.js", import.meta.url));

test("a confirmação diz para quantas pessoas vai, e de que domínios", () => {
  assert.equal(R.fraseDeConfirmacao(["carla@acme.com", "diego@Acme.com", "bruno@casa.com"], []),
    "Enviar para 3 pessoas, dos domínios acme.com e casa.com?");
  assert.equal(R.fraseDeConfirmacao(["carla@acme.com"], []), "Enviar para 1 pessoa, do domínio acme.com?");
  assert.equal(R.fraseDeConfirmacao(["carla@acme.com"], ["x@terceiro.com.br"]),
    "Enviar para 2 pessoas, dos domínios acme.com e terceiro.com.br?");
});

test("o endereço passa pela mesma régua do núcleo", () => {
  assert.equal(R.enderecoValido("a@b.com"), true);
  assert.equal(R.enderecoValido("a@b.com?bcc=x@y.com"), false);
  assert.equal(R.enderecoValido("a b@c.com"), false);
  assert.equal(R.enderecoValido("a@b"), false);
});

test("a ata que mudou depois do envio é notada, e a que não mudou não", () => {
  const ata = "**Pendentes**\n\n- Margem por cliente: Carla, hoje\n";
  assert.equal(R.mudouDepoisDoEnvio(ata, ata), false);
  assert.equal(R.mudouDepoisDoEnvio(ata, `Segue a ata.\n\n${ata}`), false);     // só a abertura a mais
  assert.equal(R.mudouDepoisDoEnvio(ata.replace("hoje", "amanhã"), ata), true);
  assert.equal(R.mudouDepoisDoEnvio(ata, null), false);                          // nunca enviada
});

test("cada desfecho tem a sua frase, e sem resposta não promete nada", () => {
  assert.equal(R.mensagemDoDesfecho("enviada"), "Enviada. A mensagem está nos seus Enviados.");
  assert.equal(R.mensagemDoDesfecho("sem_resposta"),
    "O Gmail não respondeu, e o app não sabe se a mensagem saiu. Confira os seus Enviados antes de tentar de novo.");
  assert.equal(R.mensagemDoDesfecho("recusada", "HTTP 400: endereço inválido"),
    "O Gmail recusou o envio: HTTP 400: endereço inválido");
  assert.equal(R.mensagemDoDesfecho("sem_permissao"),
    "O PulseMeet não tem permissão para enviar por esta conta. Permita o envio e tente de novo.");
});
```

- [ ] **Step 2: Rodar e ver falhar**

Run: `node --test tools/web/envio-regras.test.mjs`
Expected: FAIL (`Cannot find module`).

- [ ] **Step 3: Implementar** as regras. `mudouDepoisDoEnvio` compara a ata de agora com o **fim** do texto enviado (o que veio antes é a abertura).

- [ ] **Step 4: A tela** em `envio.js`, `telaDeEnvio({ tipo, gravacao, cliente, projeto, markdown, aoEnviar })`, aberta por *Enviar por e-mail* na ata conferida e no painel Briefing:
- sem a agenda conectada (`conectada` falso): explica que o envio usa a conta do Google da agenda, e leva a Ajustes › Geral;
- sem `pode_enviar`: "Para enviar, o PulseMeet precisa da permissão de mandar e-mail pela sua conta do Google. Ele não lê a sua caixa." com *Permitir* (manda `permitir-envio`). **É a única hora em que esse pedido aparece**;
- **Para** e **Cc** como listas editáveis de endereços (o inválido não entra, e a tela diz por quê); **Assunto**; uma linha de **Abertura**; e **o corpo como vai chegar**, renderizado do Markdown;
- *Enviar* pede `confirmar(fraseDeConfirmacao(...))`, desabilita o botão, manda `enviar-email` com `html: htmlParaEmail(texto montado)`, e mostra `mensagemDoDesfecho`. Em `enviada`, fecha e chama `aoEnviar`. Nos outros, a tela fica aberta com o que a pessoa escreveu, **e nada é repetido sozinho**;
- na conferência, o topo da ata enviada diz "Enviada em dd/MM para N pessoas", e, com `mudouDepoisDoEnvio`, "A ata mudou depois do envio de dd/MM." *Enviar de novo* abre a mesma tela, e a confirmação começa com "Esta ata já foi enviada em dd/MM.".

- [ ] **Step 5: Ajustes.** O ⓘ da conexão com o Google, em Ajustes › Geral, ganha: "Se você usar o envio por e-mail, o PulseMeet pede mais uma permissão: mandar e-mail pela sua conta. Ele manda só o que você confirmar, para quem você escolher, e não lê a sua caixa."

- [ ] **Step 6: Rodar e compilar**

Run: `node --test tools/web/*.test.mjs && dotnet test app-net/Tests/MeetingApp.Tests.csproj && dotnet build app-net/App -c Release 2>&1 | tail -3`
Expected: PASS e `0 Erro(s)`.

- [ ] **Step 7: Commit**

```bash
git add app-net/App tools/web/envio-regras.test.mjs
git commit -m "feat(envio): a tela de envio pelo Gmail, para a ata e para o briefing"
```

---

### Task 11: Documentos e validação com o dono

- [ ] **Step 1: Os documentos.** `docs/RASTREABILIDADE.md` (o estado do passo 4, parte por parte); o desenho (§9, com as respostas); `docs/BACKLOG.md` (`GRA-2`, se o envio mudou o que se sabe das listas paralelas); `docs/ATUALIZACAO.md` ou o documento da agenda, com o escopo novo e o que ele pede de quem instala.
- [ ] **Step 2: O binário publica.** Run: `tools/publicar.sh --so-build 2>&1 | tail -20`. Expected: sem `IL2026` nem `IL3050`, e a régua do segredo do Google passa.
- [ ] **Step 3: Parte A, com o dono** (critério 1 do §8): *Abrir no e-mail* abre o programa padrão com os convidados e o assunto, e a ata cola com a formatação.
- [ ] **Step 4: Parte B** (critérios 5 e 6): fechar uma pendência no app atualiza o arquivo do projeto; outra pessoa, com o link da pasta, vê a mudança sem instalar nada; o arquivo não traz nota interna, fala citada nem item de rascunho; a planilha abre no Excel e no Google Sheets.
- [ ] **Step 5: Parte C** (critérios 2, 3 e 4), **primeiro para o próprio dono, e só depois para um cliente**: a mensagem chega com a formatação da ata e aparece nos Enviados; o registro guarda quando, para quem e o texto; com a rede cortada no meio (desligar o Wi-Fi depois de clicar em Enviar), o app não marca como enviada e não reenvia; numa instalação que só usa a agenda, o pedido do escopo novo não aparece.
- [ ] **Step 6: Commit**

```bash
git add docs
git commit -m "docs: o envio e o compartilhamento, e o que a validação mostrou"
```
