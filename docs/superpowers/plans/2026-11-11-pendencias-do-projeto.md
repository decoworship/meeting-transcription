# As pendências do projeto: plano de implementação

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Cada pendência e cada decisão de uma ata conferida ganha identidade no projeto, a ata seguinte diz o que fecha ou muda das anteriores, e o projeto passa a ter uma página com o que está aberto, com quem, desde quando, e a história de cada item.

**Architecture:** O registro do projeto é **derivado**: uma função pura lê as atas conferidas do projeto em ordem de data, aplica os acontecimentos e devolve o estado. Os itens e as ligações moram no `conferencia.json` de cada reunião; só o que acontece fora de reunião mora num arquivo por projeto, dentro da pasta das gravações. A ligação entre atas é sempre proposta (pelo Claude, ou por semelhança de palavras) e confirmada pela pessoa na tela de conferência.

**Tech Stack:** C#/.NET 8, xUnit, System.Text.Json com contexto gerado em compilação, `JsonNode` no MCP, JavaScript sem build no WebView2, `node --test`.

**Spec:** [docs/superpowers/specs/2026-11-11-pendencias-do-projeto-design.md](../specs/2026-11-11-pendencias-do-projeto-design.md). As seções citadas como §N são dele.

**É o passo 2 de quatro, e só começa com o passo 1 construído e validado** ([plano](2026-11-11-ata-conferida.md)). Os nomes consumidos daqui para baixo (`Conferencia`, `ItemDaConferencia`, `RevisaoDaAta`, `MudancaDaConferencia`, `ArquivosDaAta`, `FormalizacaoDeExemplo`, `ConferenciaTests.Exemplo()`) são os que aquele plano define. **Antes da Tarefa 1, confira que eles existem com essas assinaturas**; se o passo 1 mudou algum na execução, corrija este plano primeiro.

## Respostas assumidas

O desenho deixou cinco perguntas para o dono (§10). Este plano foi escrito a pedido dele antes das respostas, com a proposta mais conservadora em cada uma. **Leia antes de começar; uma resposta diferente muda só as tarefas indicadas.**

| pergunta | o que este plano assume | se a resposta for outra |
|---|---|---|
| 1. Onde mora a página do projeto? | Uma página própria, `#projeto=…`, **sem destino novo no trilho**: chega-se pelo "Cliente › Projeto" da reunião aberta e por Ajustes › Clientes | muda só a Tarefa 11 |
| 2. Semear os projetos em andamento? | Não há semeadura em lote. A página deixa acrescentar à mão um item "fora de reunião" (§8) | nada a mudar; é dado, não código |
| 3. Pendência interna existe? | Não neste corte. Existe só a **nota interna** num item | Tarefas 3, 4 e 7 ganham um campo `interna`, e os passos 3 e 4 um filtro |
| 4. "Entregue" é só a pendência concluída? | Sim | Tarefa 4 ganharia um terceiro tipo de item |
| 5. Quem pode concluir? | Só a pessoa, no app | é o passo 5 do desenho de envio, fora daqui |

## Global Constraints

- Comandos de teste sempre com `export PATH="$HOME/.dotnet:$PATH"` antes. Suítes: `dotnet test app-net/Tests/MeetingApp.Tests.csproj` e `node --test tools/web/*.test.mjs`. Toda tarefa termina com as duas verdes.
- **Só entra no registro o que saiu de uma ata conferida** (`Estado == "conferida"`), e dela só os itens que não estão fora. Rascunho, e ata de outro tipo, não entram nunca.
- **Ligação não confirmada não vira acontecimento.** O app nunca fecha, cobra ou revê um item sozinho.
- **Nunca se inventa prazo:** a data de um prazo é proposta pelo app e só vale depois de confirmada pela pessoa. Sem confirmação, a pendência aparece como "sem data".
- **O registro não é guardado:** nada de cache em disco neste plano. Se a leitura pesar, o cache entra depois, e nunca vira a fonte (§4.3).
- O arquivo do projeto mora em `<pasta das gravações>/_projetos/`, com escrita atômica (`.tmp` e troca). A pasta não tem `meta.json`, e por isso a lista de reuniões e o `Acervo` já a ignoram.
- **A nota interna nunca sai do app:** não vai para o MCP, e os passos 3 e 4 não a leem.
- **O MCP continua sem fechar nem criar pendência** (§7): o que o Claude propõe passa pela proposta de ata.
- Trimming, stdout do MCP, nós em vez de `innerHTML`, nenhum dado de cliente real, GPU e acervo real só com o dono, sem publicar e sem push: valem as regras do plano do passo 1.

## Review Focus

1. **A ata refeita depois de alimentar o registro:** os ids dos itens novos não podem coincidir com os antigos, senão um "fecha o i2" de uma reunião posterior passa a apontar outro item, em silêncio. Teste na Tarefa 1; e a ligação cujo alvo sumiu aparece como perdida, Tarefa 4.
2. **Duas reuniões do projeto no mesmo dia, ou fora de ordem de conferência** (a de 28/10 conferida antes da de 21/10): a ordem do registro é a da data da reunião, e uma ligação para uma reunião posterior é ignorada como perdida. Teste na Tarefa 4.
3. **O nome do cliente ou do projeto com caixa ou acento diferente entre reuniões** ("Acme Bebidas" e "acme bebidas"), e com caractere que não cabe em nome de arquivo ("A/B Testes"): é o mesmo projeto, e o arquivo dele tem um nome só. Testes nas Tarefas 3 e 5.
4. **Prazo dito de um jeito que a régua não resolve** ("depois do carnaval", "semana que vem"): nenhuma data proposta, e a pendência nunca aparece como vencida. Teste na Tarefa 2.
5. **A reunião que muda de projeto** depois de ter acontecimentos fora de reunião: eles vão junto, e não ficam órfãos no arquivo do projeto antigo. Teste na Tarefa 3.

---

## Mapa dos arquivos

| arquivo | o que muda |
|---|---|
| `app-net/Nucleo/Atas/Conferencia.cs` | `prazo_em` e `liga` no item; ids que continuam da conferência anterior |
| `app-net/Nucleo/Atas/RevisaoDaAta.cs` | ações `ligar` e `desligar`; `prazo_em` no corrigir |
| `app-net/Nucleo/Projeto/PrazoDito.cs` (novo) | a data proposta para um prazo dito |
| `app-net/Nucleo/Projeto/ArquivoDoProjeto.cs` (novo) | o que acontece fora de reunião, por projeto |
| `app-net/Nucleo/Projeto/RegistroDoProjeto.cs` (novo) | a derivação: itens, estados, história |
| `app-net/Nucleo/Projeto/LeitorDoProjeto.cs` (novo) | do disco ao registro; as abertas por reunião |
| `app-net/Nucleo/Projeto/LigacoesDoProjeto.cs` (novo) | a sugestão por semelhança de palavras |
| `app-net/Nucleo/Projeto/AcoesDoProjeto.cs` (novo) | concluir, cancelar, reabrir, prazo, dono, nota, acrescentar |
| `app-net/Nucleo/Atas/GeracaoDeAta.cs`, `app-net/App/Ponte.cs` | ids continuados; sugestões ao gerar; operações `projeto` e `projeto-acao` |
| `app-net/Nucleo/Claude/FerramentasDoMcp.cs` | `listar_pendencias`; abertos em `preparar_ata`; `liga` em `propor_ata`; os dois pedidos prontos |
| `app-net/App/web/projeto-regras.js`, `projeto.js` (novos) | as regras e a página |
| `app-net/App/web/conferencia.js`, `conferencia-regras.js`, `app.js`, `reuniao.js`, `clientes.js`, `app.css` | a ligação e a data na conferência; a navegação |
| `tools/web/projeto-regras.test.mjs` (novo) | os testes da página |

---

### Task 1: O item ganha a data do prazo, a ligação e um id que não volta

**Files:**
- Modify: `app-net/Nucleo/Atas/Conferencia.cs`, `app-net/Nucleo/Atas/RevisaoDaAta.cs`, `app-net/Nucleo/Atas/GeracaoDeAta.cs`
- Test: `app-net/Tests/ConferenciaTests.cs`, `app-net/Tests/RevisaoDaAtaTests.cs`

**Interfaces:**
- Produces:
  - `ItemDaConferencia.PrazoEm` (`"prazo_em"`, `string?`, `AAAA-MM-DD`).
  - `ItemDaConferencia.Liga` (`"liga"`, `LigacaoDoItem?`), com `class LigacaoDoItem`: `"reuniao"` (id da reunião do alvo), `"item"`, `"como"` (`"continua" | "fecha" | "revisa"`), `"confirmada"` (bool), `"texto_do_alvo"` (o texto do alvo quando a ligação foi proposta).
  - `void Conferencia.ContinuarOsIdsDe(Conferencia? anterior)`: soma a todos os ids de `Itens` e `Proposta` o maior número de id de `anterior` (e ajusta nada mais).
  - `MudancaDaConferencia` ganha `"prazo_em"`, `"reuniao_alvo"`, `"item_alvo"`, `"como"`; ações novas `ligar` e `desligar`.

**As regras novas de `RevisaoDaAta.Aplicar`:**
- **corrigir** com `prazo_em` não nulo: aceita `AAAA-MM-DD` ou vazio (vazio limpa); outro formato é recusado. Mudar `prazo` sem mandar `prazo_em` limpa o `PrazoEm` (a data confirmada era do prazo antigo).
- **ligar:** exige `item`, `reuniao_alvo`, `item_alvo` e `como` válido; grava a ligação com `Confirmada = true`, preservando o `TextoDoAlvo` se a ligação já existia para o mesmo alvo. Não muda a `Revisao` do item.
- **desligar:** apaga a ligação do item ("é outro item").
- `GerarFormalizacaoAsync` e `RevisaoDaAta.UsarProposta` chamam `ContinuarOsIdsDe(Conferencia.Ler(pasta))` **antes** de arquivar.

- [ ] **Step 1: Escrever os testes**

```csharp
// ConferenciaTests
[Fact]
public void OsIdsContinuamDeOndeAConferenciaAnteriorParou()
{
    var anterior = Exemplo();                        // i1..i5
    var nova = Exemplo();
    nova.Proposta = nova.CopiaDosItens();

    nova.ContinuarOsIdsDe(anterior);

    Assert.Equal(["i6", "i7", "i8", "i9", "i10"], nova.Itens.Select(i => i.Id));
    Assert.Equal(["i6", "i7", "i8", "i9", "i10"], nova.Proposta.Select(i => i.Id));
    nova.ContinuarOsIdsDe(null);                     // sem anterior, nada muda
    Assert.Equal("i6", nova.Itens[0].Id);
}

[Fact]
public async Task RefazerAFormalizacaoNaoReaproveitaId()
{
    Exemplo().Salvar(_pasta);                        // i1..i5 no disco
    var dados = new ResultadoDaTranscricao { Duration = 54, Segments = FormalizacaoDeExemplo.Segmentos() };
    await GeracaoDeAta.GerarAsync(_pasta, ModelosDeAta.Buscar(AtaDeFormalizacao.Id)!, dados,
        FormalizacaoDeExemplo.Contexto(), (_, _, _, _, _) => throw new InvalidOperationException(),
        null, CancellationToken.None, FormatoDaAta.Classico, FormalizacaoDeExemplo.Abrir());

    Assert.Equal(["i6", "i7", "i8", "i9"], Conferencia.Ler(_pasta)!.Itens.Select(i => i.Id));
}

// RevisaoDaAtaTests
[Fact]
public void LigarConfirmaEDesligarApaga()
{
    var c = ConferenciaTests.Exemplo();
    c.Itens[0].Liga = new() { Reuniao = "2026-10-14_11-30-00", Item = "i2", Como = "fecha", TextoDoAlvo = "Enviar a base" };

    RevisaoDaAta.Aplicar(c, new() { Acao = "ligar", Item = "i1", ReuniaoAlvo = "2026-10-14_11-30-00",
                                    ItemAlvo = "i2", Como = "fecha" }, T0);
    Assert.True(c.Itens[0].Liga!.Confirmada);
    Assert.Equal("Enviar a base", c.Itens[0].Liga!.TextoDoAlvo);
    Assert.Equal("a_conferir", c.Itens[0].Revisao);

    RevisaoDaAta.Aplicar(c, new() { Acao = "desligar", Item = "i1" }, T0);
    Assert.Null(c.Itens[0].Liga);
    Assert.Throws<InvalidOperationException>(() => RevisaoDaAta.Aplicar(c,
        new() { Acao = "ligar", Item = "i1", ReuniaoAlvo = "x", ItemAlvo = "i2", Como = "talvez" }, T0));
}

[Theory]
[InlineData("2026-10-23", "2026-10-23")]
[InlineData("", null)]
public void ADataDoPrazoSoEntraNoFormatoCerto(string enviada, string? esperada)
{
    var c = ConferenciaTests.Exemplo();
    RevisaoDaAta.Aplicar(c, new() { Acao = "corrigir", Item = "i2", PrazoEm = enviada }, T0);
    Assert.Equal(esperada, c.Itens[1].PrazoEm);
    Assert.Throws<InvalidOperationException>(() => RevisaoDaAta.Aplicar(c,
        new() { Acao = "corrigir", Item = "i2", PrazoEm = "23/10" }, T0));
}

[Fact]
public void MudarOPrazoDitoLimpaADataQueEraDoAntigo()
{
    var c = ConferenciaTests.Exemplo();
    c.Itens[1].PrazoEm = "2026-10-21";
    RevisaoDaAta.Aplicar(c, new() { Acao = "corrigir", Item = "i2", Prazo = "sexta" }, T0);
    Assert.Null(c.Itens[1].PrazoEm);
}
```

- [ ] **Step 2: Rodar e ver falhar**

Run: `dotnet test app-net/Tests/MeetingApp.Tests.csproj --filter "FullyQualifiedName~ConferenciaTests|FullyQualifiedName~RevisaoDaAtaTests"`
Expected: FAIL de compilação.

- [ ] **Step 3: Implementar** conforme as regras. `ParaAta` não muda: nem a data nem a ligação aparecem no `ata.md`.

- [ ] **Step 4: Rodar a suíte**

Run: `dotnet test app-net/Tests/MeetingApp.Tests.csproj`
Expected: PASS, inclusive a referência `AFormalizacaoLocalEscreveOMesmoAtaMd`.

- [ ] **Step 5: Commit**

```bash
git add app-net/Nucleo/Atas app-net/Tests
git commit -m "feat(ata): o item guarda a data do prazo e a ligação, e o id não volta ao refazer"
```

---

### Task 2: A data proposta para um prazo dito

§4.4.

**Files:**
- Create: `app-net/Nucleo/Projeto/PrazoDito.cs`
- Test: `app-net/Tests/PrazoDitoTests.cs`

**Interfaces:**
- Produces: `static DateOnly? PrazoDito.Propor(string prazo, DateOnly diaDaReuniao)` (namespace `MeetingApp.Nucleo.Projeto`). Nulo quando não dá para resolver com segurança.

**As regras**, sobre o texto sem acento e em minúscula, na primeira que casar:
- `dd/mm/aaaa` ou `dd/mm/aa`: a data. `dd/mm`: no ano da reunião; se cair mais de 180 dias antes da reunião, no ano seguinte.
- `dia N` (1 a 31): o próximo dia N a partir do dia da reunião, inclusive.
- `hoje`: o dia da reunião. `amanha`: o seguinte.
- dia da semana (`segunda` … `domingo`, com ou sem `-feira`): a próxima ocorrência **depois** do dia da reunião.
- `fim do mes`: o último dia do mês da reunião.
- `N dias`: a reunião mais N.
- Qualquer outra coisa, inclusive `semana que vem` e `proxima semana`: nulo. Data impossível (`31/02`): nulo.

- [ ] **Step 1: Escrever o teste**

```csharp
public sealed class PrazoDitoTests
{
    private static readonly DateOnly Terca = new(2026, 10, 20);   // uma terça-feira

    [Theory]
    [InlineData("até 23/10", "2026-10-23")]
    [InlineData("23/10/2026", "2026-10-23")]
    [InlineData("15/01", "2027-01-15")]            // já passou neste ano: é o do ano que vem
    [InlineData("dia 20", "2026-10-20")]
    [InlineData("dia 5", "2026-11-05")]
    [InlineData("hoje", "2026-10-20")]
    [InlineData("amanhã", "2026-10-21")]
    [InlineData("quinta", "2026-10-22")]
    [InlineData("terça-feira", "2026-10-27")]      // dita numa terça: é a próxima
    [InlineData("fim do mês", "2026-10-31")]
    [InlineData("em 3 dias", "2026-10-23")]
    public void PropoeADataQuandoOPrazoDizUma(string prazo, string esperada) =>
        Assert.Equal(DateOnly.Parse(esperada), PrazoDito.Propor(prazo, Terca));

    [Theory]
    [InlineData("")]
    [InlineData("semana que vem")]
    [InlineData("depois do carnaval")]
    [InlineData("31/02")]
    [InlineData("o quanto antes")]
    public void SemSegurancaNaoPropoeNada(string prazo) => Assert.Null(PrazoDito.Propor(prazo, Terca));
}
```

- [ ] **Step 2: Rodar e ver falhar**

Run: `dotnet test app-net/Tests/MeetingApp.Tests.csproj --filter "FullyQualifiedName~PrazoDitoTests"`
Expected: FAIL de compilação.

- [ ] **Step 3: Implementar** `PrazoDito.Propor`. Use `AtaDeFormalizacao.SemAcento` para normalizar.

- [ ] **Step 4: Rodar**

Run: `dotnet test app-net/Tests/MeetingApp.Tests.csproj --filter "FullyQualifiedName~PrazoDitoTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add app-net/Nucleo/Projeto/PrazoDito.cs app-net/Tests/PrazoDitoTests.cs
git commit -m "feat(projeto): a data proposta para um prazo dito, e nenhuma quando não é seguro"
```

---

### Task 3: O arquivo do projeto

§4.3: o que acontece fora de reunião.

**Files:**
- Create: `app-net/Nucleo/Projeto/ArquivoDoProjeto.cs`
- Test: `app-net/Tests/ArquivoDoProjetoTests.cs`

**Interfaces:**
- Produces:

```csharp
public sealed class AcontecimentoDoProjeto
{
    // "em" (ISO 8601), "reuniao" (id da reunião do item; "" para item manual), "item" (id),
    // "tipo": concluida | cancelada | reaberta | prazo | responsavel | nota
    // "prazo", "prazo_em", "responsavel", "lado", "texto" (a nota): todos string?
}
public sealed class ItemManual          // o item acrescentado na página, sem fala
{
    // "id" ("m1", "m2"…), "tipo" (pendente | resolvido), "tema", "texto", "lado", "responsavel",
    // "prazo", "prazo_em", "criado_em"
}
public sealed class ArquivoDoProjeto
{
    public const string Pasta = "_projetos";
    // "versao" (1), "cliente", "projeto", "itens" (List<ItemManual>), "acontecimentos"
    public static string Caminho(string raiz, string cliente, string projeto);
    public static ArquivoDoProjeto Ler(string raiz, string cliente, string projeto);   // vazio se não existe ou ilegível. Nunca lança.
    public void Salvar(string raiz);                                                    // .tmp e troca; cria a pasta
    public static void MoverAcontecimentos(string raiz, (string Cliente, string Projeto) de,
                                           (string Cliente, string Projeto) para, string idDaReuniao);
}
```

**O nome do arquivo:** `Chave(cliente) + "--" + Chave(projeto) + ".json"`, com `Chave` = sem acento, minúscula, e cada sequência de caracteres que não são letra ou dígito trocada por um `-`, sem `-` nas pontas. Cliente ou projeto vazio vira `sem-nome`.

- [ ] **Step 1: Escrever os testes**

```csharp
public sealed class ArquivoDoProjetoTests : IDisposable
{
    private readonly string _raiz = Directory.CreateTempSubdirectory("projeto-arq").FullName;
    public void Dispose() => Directory.Delete(_raiz, recursive: true);

    [Theory]
    [InlineData("Acme Bebidas", "Portal de Pedidos", "acme-bebidas--portal-de-pedidos.json")]
    [InlineData("acme bebidas ", "PORTAL  de Pedidos", "acme-bebidas--portal-de-pedidos.json")]
    [InlineData("A/B Testes", "Operação: São Paulo", "a-b-testes--operacao-sao-paulo.json")]
    [InlineData("", "", "sem-nome--sem-nome.json")]
    public void ONomeDoArquivoEUmSoParaOMesmoProjeto(string cliente, string projeto, string esperado) =>
        Assert.Equal(Path.Combine(_raiz, "_projetos", esperado), ArquivoDoProjeto.Caminho(_raiz, cliente, projeto));

    [Fact]
    public void SalvaLeEIlegivelViraVazio()
    {
        var a = ArquivoDoProjeto.Ler(_raiz, "Acme Bebidas", "Portal de Pedidos");
        Assert.Empty(a.Acontecimentos);
        a.Acontecimentos.Add(new() { Em = "2026-10-27T09:00:00-03:00", Reuniao = "2026-10-21_11-30-00",
                                     Item = "i2", Tipo = "concluida" });
        a.Salvar(_raiz);

        Assert.Single(ArquivoDoProjeto.Ler(_raiz, "acme bebidas", "portal de pedidos").Acontecimentos);
        File.WriteAllText(ArquivoDoProjeto.Caminho(_raiz, "Acme Bebidas", "Portal de Pedidos"), "{ quebrado");
        Assert.Empty(ArquivoDoProjeto.Ler(_raiz, "Acme Bebidas", "Portal de Pedidos").Acontecimentos);
    }

    [Fact]
    public void QuandoAReuniaoMudaDeProjetoOsAcontecimentosDelaVaoJunto()
    {
        var de = ArquivoDoProjeto.Ler(_raiz, "Acme Bebidas", "Portal de Pedidos");
        de.Acontecimentos.Add(new() { Em = "2026-10-27T09:00:00-03:00", Reuniao = "2026-10-21_11-30-00", Item = "i2", Tipo = "concluida" });
        de.Acontecimentos.Add(new() { Em = "2026-10-27T09:05:00-03:00", Reuniao = "2026-10-14_11-30-00", Item = "i1", Tipo = "nota", Texto = "fica" });
        de.Salvar(_raiz);

        ArquivoDoProjeto.MoverAcontecimentos(_raiz, ("Acme Bebidas", "Portal de Pedidos"),
                                             ("Acme Bebidas", "Aplicativo"), "2026-10-21_11-30-00");

        Assert.Equal("fica", Assert.Single(ArquivoDoProjeto.Ler(_raiz, "Acme Bebidas", "Portal de Pedidos").Acontecimentos).Texto);
        Assert.Equal("i2", Assert.Single(ArquivoDoProjeto.Ler(_raiz, "Acme Bebidas", "Aplicativo").Acontecimentos).Item);
    }
}
```

- [ ] **Step 2: Rodar e ver falhar**

Run: `dotnet test app-net/Tests/MeetingApp.Tests.csproj --filter "FullyQualifiedName~ArquivoDoProjetoTests"`
Expected: FAIL de compilação.

- [ ] **Step 3: Implementar**, com contexto de serialização próprio no molde do `ConferenciaJson`. `MoverAcontecimentos` não faz nada quando origem e destino são o mesmo arquivo, nem quando não há o que mover (e nesse caso não cria arquivo).

- [ ] **Step 4: Rodar**

Run: `dotnet test app-net/Tests/MeetingApp.Tests.csproj --filter "FullyQualifiedName~ArquivoDoProjetoTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add app-net/Nucleo/Projeto/ArquivoDoProjeto.cs app-net/Tests/ArquivoDoProjetoTests.cs
git commit -m "feat(projeto): o arquivo do que acontece fora de reunião"
```

---

### Task 4: A derivação do registro

§4.1, §4.2 e §5. O coração do passo: uma função pura.

**Files:**
- Create: `app-net/Nucleo/Projeto/RegistroDoProjeto.cs`
- Create: `app-net/Tests/ProjetoDeExemplo.cs`
- Test: `app-net/Tests/RegistroDoProjetoTests.cs`

**Interfaces:**
- Consumes: `Conferencia`, `ItemDaConferencia`, `LigacaoDoItem`, `FalaCitada`; `ArquivoDoProjeto`.
- Produces:

```csharp
public sealed record AtaDoProjeto(string Reuniao, DateTime Quando, string? Titulo, Conferencia? Conferencia, bool TemAta);

public sealed record Passo(DateTime Em, string Tipo, string? Reuniao, IReadOnlyList<FalaCitada> Falas, string Detalhe);
// Tipo: criado | cobrado | prazo | responsavel | concluido | cancelado | reaberto | revista | nota

public sealed class ItemDoProjeto
{
    public string Reuniao, Item;                 // a identidade: onde nasceu ("" e "m1" para o manual)
    public string Ref => Reuniao.Length > 0 ? $"{Reuniao}#{Item}" : Item;
    public string Tipo;                          // "pendencia" | "decisao"
    public string Tema, Texto, Lado, Responsavel, Prazo;
    public DateOnly? PrazoEm;
    public string Estado;                        // pendencia: aberta | concluida | cancelada; decisao: vigente | revista
    public DateTime NasceuEm; public DateTime? FechouEm;
    public int Mencoes;                          // 1 ao nascer, mais 1 a cada "cobrado"
    public List<Passo> Historia;
    public bool Aberto => Estado is "aberta" or "vigente";
}
public sealed record LigacaoPerdida(string Reuniao, string Item, string Texto, string Alvo);

public sealed class RegistroDoProjeto
{
    public IReadOnlyList<ItemDoProjeto> Itens;
    public IReadOnlyList<LigacaoPerdida> Perdidas;
    public IReadOnlyList<AtaDoProjeto> NaoConferidas;     // têm ata e não estão conferidas
    public DateTime? VaiAte;                               // a data da última ata conferida
    public static RegistroDoProjeto Montar(IEnumerable<AtaDoProjeto> atas, ArquivoDoProjeto arquivo);
    public IEnumerable<ItemDoProjeto> Abertas(string lado);   // pendências abertas de um lado, mais antigas primeiro
    public ItemDoProjeto? Achar(string referencia);           // pelo Ref
}
```

**As regras de `Montar`:**
1. As atas em ordem de `Quando`, e de `Reuniao` no empate. Só as com `Conferencia?.Estado == "conferida"` alimentam; as outras com `TemAta` vão para `NaoConferidas`. `VaiAte` é a data da última conferida.
2. De cada ata conferida, cada item que não está fora, na ordem:
   - **sem ligação confirmada:** nasce um `ItemDoProjeto` (resolvido → decisão `vigente`; pendente → pendência `aberta`), com o passo `criado` levando as falas do item. Item a confirmar não existe numa ata conferida; se aparecer, é ignorado.
   - **ligação confirmada cujo alvo não existe** no registro montado até ali (inclusive alvo em reunião posterior): vai para `Perdidas`, e o item **nasce como novo**.
   - **`continua`:** o alvo ganha o passo `cobrado` e `Mencoes + 1`; se prazo, `PrazoEm`, responsável ou lado do item novo não são vazios e diferem do alvo, o alvo é atualizado e ganha também o passo `prazo` ou `responsavel` com o valor novo em `Detalhe`. Um alvo concluído ou cancelado **reabre** (passo `reaberto`). O item novo não nasce.
   - **`fecha`:** o alvo vira `concluida`, com `FechouEm` e o passo `concluido`. O item novo não nasce.
   - **`revisa`:** o alvo (uma decisão) vira `revista`, com o passo `revista`; e o item novo **nasce** como decisão vigente.
3. Os itens manuais do arquivo nascem em `criado_em`, com `Reuniao = ""`.
4. Os acontecimentos do arquivo entram **na ordem do tempo junto com as atas**: cada um é aplicado depois de todas as atas com `Quando` menor ou igual ao `em` dele. O `em` é comparado pelo relógio como está escrito (`DateTimeOffset.Parse(em).DateTime`), sem converter fuso: `Quando` também é hora local de quem gravou. `concluida`, `cancelada` e `reaberta` mudam o estado; `prazo` e `responsavel` mudam os campos não nulos; `nota` só acrescenta o passo, com o texto em `Detalhe`. Acontecimento cujo item não existe vai para `Perdidas`.
5. A história de cada item fica em ordem de tempo.

- [ ] **Step 1: Escrever a fixture** `ProjetoDeExemplo.cs`

```csharp
/// <summary>Três reuniões inventadas do mesmo projeto, a terceira ainda em rascunho.</summary>
internal static class ProjetoDeExemplo
{
    public const string R1 = "2026-10-14_11-30-00", R2 = "2026-10-21_11-30-00", R3 = "2026-10-28_11-30-00";

    private static ItemDaConferencia Item(string id, string tipo, string texto, string lado = "",
        string responsavel = "", string prazo = "", string? prazoEm = null, LigacaoDoItem? liga = null) => new()
    {
        Id = id, Tipo = tipo, Texto = texto, Tema = tipo == "resolvido" ? "Tema " + id : "", Lado = lado,
        Responsavel = responsavel, Prazo = prazo, PrazoEm = prazoEm, Liga = liga, Revisao = "aprovado",
        Falas = [new() { Inicio = 60, Fim = 66, Falante = "Bruno Teles", Texto = "fala de " + id }],
    };

    private static LigacaoDoItem Liga(string reuniao, string item, string como, bool confirmada = true) =>
        new() { Reuniao = reuniao, Item = item, Como = como, Confirmada = confirmada };

    private static AtaDoProjeto Ata(string id, string estado, params ItemDaConferencia[] itens) => new(
        id, DateTime.ParseExact(id, "yyyy-MM-dd_HH-mm-ss", null), "Semanal",
        new Conferencia { Estado = estado, Itens = [.. itens] }, TemAta: true);

    public static List<AtaDoProjeto> Atas() =>
    [
        Ata(R1, "conferida",
            Item("i1", "resolvido", "Corte de 500 caixas por mês."),
            Item("i2", "pendente", "Enviar a base atualizada", "cliente", "Carla", "quinta", "2026-10-15"),
            Item("i3", "pendente", "Montar o painel de ruptura", "nosso", "Bruno")),
        Ata(R2, "conferida",
            Item("i1", "pendente", "Enviar a base atualizada", "cliente", "Carla", "sexta", "2026-10-23", Liga(R1, "i2", "continua")),
            Item("i2", "resolvido", "Corte de 400 caixas por mês.", liga: Liga(R1, "i1", "revisa")),
            Item("i3", "resolvido", "O painel de ruptura foi entregue.", liga: Liga(R1, "i3", "fecha")),
            Item("i4", "pendente", "Definir o rateio", "cliente", liga: Liga(R1, "i2", "continua", confirmada: false)),
            Item("i5", "pendente", "Item sem alvo", "nosso", liga: Liga(R1, "i99", "fecha"))),
        Ata(R3, "rascunho", Item("i1", "pendente", "Só no rascunho", "nosso")),
    ];
}
```

- [ ] **Step 2: Escrever os testes**

```csharp
public sealed class RegistroDoProjetoTests
{
    private static ArquivoDoProjeto Vazio() => new() { Cliente = "Acme Bebidas", Projeto = "Portal de Pedidos" };
    private static RegistroDoProjeto Registro(ArquivoDoProjeto? a = null) =>
        RegistroDoProjeto.Montar(ProjetoDeExemplo.Atas(), a ?? Vazio());

    [Fact]
    public void APendenciaCobradaDuasVezesApareceUmaVezComOPrazoNovo()
    {
        var base_ = Registro().Achar($"{ProjetoDeExemplo.R1}#i2")!;

        Assert.Equal(("aberta", 2, "sexta", new DateOnly(2026, 10, 23)), (base_.Estado, base_.Mencoes, base_.Prazo, base_.PrazoEm));
        Assert.Equal(["criado", "cobrado", "prazo"], base_.Historia.Select(p => p.Tipo));
        Assert.Equal("fala de i1", base_.Historia[1].Falas[0].Texto);     // a fala da reunião que cobrou
        Assert.Single(Registro().Itens, i => i.Texto == "Enviar a base atualizada");
    }

    [Fact]
    public void FecharConcluiERevisarTrocaADecisaoVigente()
    {
        var r = Registro();
        var painel = r.Achar($"{ProjetoDeExemplo.R1}#i3")!;

        Assert.Equal(("concluida", new DateTime(2026, 10, 21, 11, 30, 0)), (painel.Estado, painel.FechouEm));
        Assert.DoesNotContain(r.Itens, i => i.Texto == "O painel de ruptura foi entregue.");
        Assert.Equal("revista", r.Achar($"{ProjetoDeExemplo.R1}#i1")!.Estado);
        Assert.Equal("vigente", r.Achar($"{ProjetoDeExemplo.R2}#i2")!.Estado);
    }

    [Fact]
    public void LigacaoNaoConfirmadaEAlvoQueSumiuViramItemNovo()
    {
        var r = Registro();

        Assert.Equal("aberta", r.Achar($"{ProjetoDeExemplo.R2}#i4")!.Estado);    // não confirmada: novo
        Assert.Equal("aberta", r.Achar($"{ProjetoDeExemplo.R2}#i5")!.Estado);    // alvo sumiu: novo
        var perdida = Assert.Single(r.Perdidas);
        Assert.Equal((ProjetoDeExemplo.R2, "i5", $"{ProjetoDeExemplo.R1}#i99"), (perdida.Reuniao, perdida.Item, perdida.Alvo));
    }

    [Fact]
    public void ORascunhoNaoEntraEOPainelDizAteOndeVai()
    {
        var r = Registro();

        Assert.DoesNotContain(r.Itens, i => i.Texto == "Só no rascunho");
        Assert.Equal(ProjetoDeExemplo.R3, Assert.Single(r.NaoConferidas).Reuniao);
        Assert.Equal(new DateTime(2026, 10, 21, 11, 30, 0), r.VaiAte);
    }

    [Fact]
    public void AsAbertasSaemPorLadoDaMaisAntigaParaAMaisNova()
    {
        var r = Registro();
        Assert.Equal(["Enviar a base atualizada", "Definir o rateio"], r.Abertas("cliente").Select(i => i.Texto));
        Assert.Equal(["Item sem alvo"], r.Abertas("nosso").Select(i => i.Texto));
    }

    [Fact]
    public void OQueAconteceForaDeReuniaoEntraNaOrdemDoTempo()
    {
        var a = Vazio();
        a.Itens.Add(new() { Id = "m1", Tipo = "pendente", Texto = "Renovar o contrato", Lado = "nosso",
                            CriadoEm = "2026-10-10T09:00:00-03:00" });
        a.Acontecimentos.Add(new() { Em = "2026-10-16T09:00:00-03:00", Reuniao = ProjetoDeExemplo.R1, Item = "i2", Tipo = "concluida" });
        a.Acontecimentos.Add(new() { Em = "2026-10-27T09:00:00-03:00", Reuniao = "", Item = "m1", Tipo = "nota", Texto = "falei com o jurídico" });
        a.Acontecimentos.Add(new() { Em = "2026-10-27T09:10:00-03:00", Reuniao = "x", Item = "i1", Tipo = "cancelada" });

        var r = Registro(a);
        var base_ = r.Achar($"{ProjetoDeExemplo.R1}#i2")!;

        // Concluída na quinta, e cobrada de novo na reunião de 21/10: reabriu.
        Assert.Equal("aberta", base_.Estado);
        Assert.Equal(["criado", "concluido", "cobrado", "reaberto", "prazo"], base_.Historia.Select(p => p.Tipo));
        Assert.Equal("falei com o jurídico", r.Achar("m1")!.Historia[^1].Detalhe);
        Assert.Contains(r.Perdidas, p => p.Alvo == "x#i1");
    }

    [Fact]
    public void AOrdemEADaReuniaoENaoADaConferencia()
    {
        var atas = ProjetoDeExemplo.Atas();
        atas.Reverse();                                     // a mais nova chega primeiro
        var r = RegistroDoProjeto.Montar(atas, Vazio());
        Assert.Equal(2, r.Achar($"{ProjetoDeExemplo.R1}#i2")!.Mencoes);
    }
}
```

- [ ] **Step 3: Rodar e ver falhar**

Run: `dotnet test app-net/Tests/MeetingApp.Tests.csproj --filter "FullyQualifiedName~RegistroDoProjetoTests"`
Expected: FAIL de compilação.

- [ ] **Step 4: Implementar** `RegistroDoProjeto.cs` conforme as regras. A ordem dos passos quando um `continua` também reabre e muda o prazo é `cobrado`, `reaberto`, `prazo`, `responsavel`.

- [ ] **Step 5: Rodar**

Run: `dotnet test app-net/Tests/MeetingApp.Tests.csproj --filter "FullyQualifiedName~RegistroDoProjetoTests"`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add app-net/Nucleo/Projeto/RegistroDoProjeto.cs app-net/Tests/ProjetoDeExemplo.cs app-net/Tests/RegistroDoProjetoTests.cs
git commit -m "feat(projeto): o registro derivado das atas conferidas e do que acontece fora delas"
```

---

### Task 5: Do disco ao registro

**Files:**
- Create: `app-net/Nucleo/Projeto/LeitorDoProjeto.cs`
- Modify: `app-net/App/Ponte.cs` (a contagem da lista)
- Test: `app-net/Tests/LeitorDoProjetoTests.cs`

**Interfaces:**
- Consumes: `Acervo`, `ReuniaoNoAcervo`, `Conferencia.Ler`, `ArquivoDoProjeto.Ler`, `RegistroDoProjeto.Montar`.
- Produces:
  - `static RegistroDoProjeto LeitorDoProjeto.Ler(string raiz, string cliente, string projeto)`: as reuniões do `Acervo` cujo cliente e projeto são iguais aos pedidos **sem acento e sem caixa**, cada uma virando `AtaDoProjeto`.
  - `static Dictionary<string, int> LeitorDoProjeto.AbertasPorReuniao(string raiz)`: id da reunião → quantas pendências abertas nasceram nela; só reuniões com ata conferida aparecem. Nunca lança (um projeto ilegível não entra).
  - A lista de reuniões (§9): para a reunião que está nesse dicionário, `pendencias` passa a ser o número dele; para as outras, continua a contagem de caixinhas.

- [ ] **Step 1: Escrever os testes**

```csharp
public sealed class LeitorDoProjetoTests : IDisposable
{
    private readonly string _raiz = Directory.CreateTempSubdirectory("projeto-leitor").FullName;
    public void Dispose() => Directory.Delete(_raiz, recursive: true);

    private void Gravar(AtaDoProjeto a, string cliente, string projeto)
    {
        string pasta = AcervoTests.Gravar(_raiz, a.Reuniao, a.Titulo, cliente: cliente, projeto: projeto, ata: "x");
        a.Conferencia!.Salvar(pasta);
    }

    [Fact]
    public void LeOProjetoSemOlharCaixaNemAcentoEIgnoraOsOutros()
    {
        var atas = ProjetoDeExemplo.Atas();
        Gravar(atas[0], "Acme Bebidas", "Portal de Pedidos");
        Gravar(atas[1], "acme bebidas", "PORTAL DE PEDIDOS");
        Gravar(atas[2], "Acme Bebidas", "Portal de Pedidos");
        AcervoTests.Gravar(_raiz, "2026-10-22_09-00-00", "Outra", cliente: "Acme Bebidas", projeto: "Portal", ata: "x");

        var r = LeitorDoProjeto.Ler(_raiz, "Acme Bebidas", "Portal de Pedidos");

        Assert.Equal(2, r.Achar($"{ProjetoDeExemplo.R1}#i2")!.Mencoes);
        Assert.Single(r.NaoConferidas);                       // a de 28/10; a do projeto "Portal" não é deste
    }

    [Fact]
    public void AsAbertasPorReuniaoContamSoOQueNasceuNela()
    {
        var atas = ProjetoDeExemplo.Atas();
        Gravar(atas[0], "Acme Bebidas", "Portal de Pedidos");
        Gravar(atas[1], "Acme Bebidas", "Portal de Pedidos");
        Gravar(atas[2], "Acme Bebidas", "Portal de Pedidos");

        var abertas = LeitorDoProjeto.AbertasPorReuniao(_raiz);

        Assert.Equal(1, abertas[ProjetoDeExemplo.R1]);        // a base; o painel fechou
        Assert.Equal(2, abertas[ProjetoDeExemplo.R2]);        // o rateio e o item sem alvo
        Assert.False(abertas.ContainsKey(ProjetoDeExemplo.R3));
    }
}
```

- [ ] **Step 2: Rodar e ver falhar**

Run: `dotnet test app-net/Tests/MeetingApp.Tests.csproj --filter "FullyQualifiedName~LeitorDoProjetoTests"`
Expected: FAIL de compilação.

- [ ] **Step 3: Implementar.** `AbertasPorReuniao` agrupa as reuniões do acervo por projeto e monta cada registro uma vez. Em `Ponte.Listar`, chame-a uma vez por listagem, dentro de `try`, e use o número ao montar cada `GravacaoResumo`.

- [ ] **Step 4: Rodar a suíte**

Run: `dotnet test app-net/Tests/MeetingApp.Tests.csproj`
Expected: PASS.

- [ ] **Step 5: Medir o custo.** Rode a ferramenta abaixo sobre uma **cópia** do acervo (ou peça ao dono o tempo no acervo real) e anote no relato da tarefa. Se `AbertasPorReuniao` passar de 300 ms, pare e leve o número ao dono antes de seguir: é o gatilho do cache que o §4.3 deixou para depois.

Run: `dotnet test app-net/Tests/MeetingApp.Tests.csproj --filter "FullyQualifiedName~LeitorDoProjetoTests" --logger "console;verbosity=detailed" | grep -i "ms"`

- [ ] **Step 6: Commit**

```bash
git add app-net/Nucleo/Projeto/LeitorDoProjeto.cs app-net/App/Ponte.cs app-net/Tests/LeitorDoProjetoTests.cs
git commit -m "feat(projeto): o registro lido do acervo, e as abertas de cada reunião na lista"
```

---

### Task 6: A sugestão de ligação, sem modelo

§5, o caminho local.

**Files:**
- Create: `app-net/Nucleo/Projeto/LigacoesDoProjeto.cs`
- Modify: `app-net/App/Ponte.cs` (`GerarAta`, `usar-proposta`)
- Test: `app-net/Tests/LigacoesDoProjetoTests.cs`

**Interfaces:**
- Produces:
  - `const double LigacoesDoProjeto.Corte = 0.5`
  - `static double Semelhanca(string a, string b)`: Jaccard sobre as palavras de quatro letras ou mais, sem acento e em minúscula.
  - `static int Sugerir(Conferencia c, RegistroDoProjeto registro)`: para cada item de `c` que não está fora e **não tem ligação**, acha o item aberto do registro mais parecido; com semelhança ≥ `Corte`, grava `Liga` com `Confirmada = false` e o `TextoDoAlvo`. Devolve quantas sugeriu. `Como`: pendente sobre pendência → `continua`; resolvido sobre pendência → `fecha`; resolvido sobre decisão → `revisa`; pendente sobre decisão não se liga.
  - `static void SugerirNaPasta(string raiz, string pasta)`: lê a conferência e o vínculo da reunião, monta o registro **sem a própria reunião**, sugere e salva se sugeriu alguma. Sem cliente ou projeto, não faz nada. Nunca lança.

- [ ] **Step 1: Escrever os testes**

```csharp
public sealed class LigacoesDoProjetoTests
{
    private static RegistroDoProjeto Registro() =>
        RegistroDoProjeto.Montar(ProjetoDeExemplo.Atas().Take(1), new ArquivoDoProjeto());

    private static ItemDaConferencia Novo(string id, string tipo, string texto) =>
        new() { Id = id, Tipo = tipo, Texto = texto };

    [Fact]
    public void SugereOMaisParecidoEDizComo()
    {
        var c = new Conferencia { Itens =
        [
            Novo("i1", "pendente", "Enviar a base atualizada do pedido"),
            Novo("i2", "resolvido", "O painel de ruptura foi montado"),
            Novo("i3", "resolvido", "Corte de 400 caixas por mês"),
            Novo("i4", "pendente", "Agendar o treinamento do time"),
        ] };

        Assert.Equal(3, LigacoesDoProjeto.Sugerir(c, Registro()));

        Assert.Equal((ProjetoDeExemplo.R1, "i2", "continua", false, "Enviar a base atualizada"),
            (c.Itens[0].Liga!.Reuniao, c.Itens[0].Liga!.Item, c.Itens[0].Liga!.Como, c.Itens[0].Liga!.Confirmada, c.Itens[0].Liga!.TextoDoAlvo));
        Assert.Equal("fecha", c.Itens[1].Liga!.Como);
        Assert.Equal("revisa", c.Itens[2].Liga!.Como);
        Assert.Null(c.Itens[3].Liga);
    }

    [Fact]
    public void NaoMexeEmQuemJaTemLigacaoNemEmQuemEstaFora()
    {
        var c = new Conferencia { Itens =
        [
            Novo("i1", "pendente", "Enviar a base atualizada"),
            Novo("i2", "pendente", "Enviar a base atualizada"),
        ] };
        c.Itens[0].Liga = new() { Reuniao = "x", Item = "i9", Como = "continua", Confirmada = true };
        c.Itens[1].Revisao = "retirado";

        Assert.Equal(0, LigacoesDoProjeto.Sugerir(c, Registro()));
        Assert.Equal("x", c.Itens[0].Liga!.Reuniao);
    }
}
```

- [ ] **Step 2: Rodar e ver falhar**

Run: `dotnet test app-net/Tests/MeetingApp.Tests.csproj --filter "FullyQualifiedName~LigacoesDoProjetoTests"`
Expected: FAIL de compilação.

- [ ] **Step 3: Implementar.** Na `Ponte`: depois de `GeracaoDeAta.GerarAsync` terminar bem com o tipo `formalizacao`, e depois de `usar-proposta`, chame `LigacoesDoProjeto.SugerirNaPasta(pastaDasGravacoes, pasta)`. Numa proposta do Claude os itens já chegam com a ligação que ele propôs (Tarefa 9), e a sugestão só alcança os que vieram sem.

- [ ] **Step 4: Rodar a suíte**

Run: `dotnet test app-net/Tests/MeetingApp.Tests.csproj`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add app-net/Nucleo/Projeto/LigacoesDoProjeto.cs app-net/App/Ponte.cs app-net/Tests/LigacoesDoProjetoTests.cs
git commit -m "feat(projeto): a ligação sugerida por semelhança de palavras, sempre por confirmar"
```

---

### Task 7: As ações fora de reunião

§6.

**Files:**
- Create: `app-net/Nucleo/Projeto/AcoesDoProjeto.cs`
- Test: `app-net/Tests/AcoesDoProjetoTests.cs`

**Interfaces:**
- Produces:

```csharp
public sealed class MudancaDoProjeto
{
    // "acao": concluir | cancelar | reabrir | prazo | responsavel | nota | adicionar
    // "ref" (o Ref do item), "tipo", "tema", "texto", "lado", "responsavel", "prazo", "prazo_em": string?
}
public static class AcoesDoProjeto
{
    public static void Aplicar(ArquivoDoProjeto arquivo, RegistroDoProjeto registro, MudancaDoProjeto m, DateTimeOffset agora);
    public static RegistroDoProjeto AplicarNoDisco(string raiz, string cliente, string projeto,
                                                   MudancaDoProjeto m, DateTimeOffset agora);   // lê, aplica, salva, relê
}
```

**As regras** (erro é `InvalidOperationException` com a frase da tela):
- `ref` que o registro não acha: `"este item não existe mais no projeto"`.
- **concluir** e **cancelar** só de pendência aberta; **reabrir** só de concluída ou cancelada. Decisão não se conclui nem se cancela.
- **prazo:** grava `prazo` e `prazo_em` (um dos dois pode vir vazio para limpar). **responsavel:** grava `responsavel` e `lado`.
- **nota:** texto obrigatório. É a nota interna (§6): fica só no arquivo do projeto.
- **adicionar:** `tipo` pendente ou resolvido, texto obrigatório, e lado obrigatório no pendente; cria um `ItemManual` com `Id` = `"m" + (maior + 1)`.
- Cada ação menos `adicionar` acrescenta um `AcontecimentoDoProjeto` com `Em = agora`, partindo o `ref` em `Reuniao` e `Item`.

- [ ] **Step 1: Escrever os testes**

```csharp
public sealed class AcoesDoProjetoTests
{
    private static readonly DateTimeOffset T = DateTimeOffset.Parse("2026-10-27T09:00:00-03:00");
    private static (ArquivoDoProjeto, RegistroDoProjeto) Cena()
    {
        var a = new ArquivoDoProjeto { Cliente = "Acme Bebidas", Projeto = "Portal de Pedidos" };
        return (a, RegistroDoProjeto.Montar(ProjetoDeExemplo.Atas(), a));
    }
    private static string Base => $"{ProjetoDeExemplo.R1}#i2";

    [Fact]
    public void ConcluirViraAcontecimentoEORegistroRefeitoMostra()
    {
        var (a, r) = Cena();
        AcoesDoProjeto.Aplicar(a, r, new() { Acao = "concluir", Ref = Base }, T);

        var e = Assert.Single(a.Acontecimentos);
        Assert.Equal((ProjetoDeExemplo.R1, "i2", "concluida", T.ToString("o")), (e.Reuniao, e.Item, e.Tipo, e.Em));
        Assert.Equal("concluida", RegistroDoProjeto.Montar(ProjetoDeExemplo.Atas(), a).Achar(Base)!.Estado);
    }

    [Fact]
    public void SoSeConcluiOQueEstaAbertoESoSeReabreOQueFechou()
    {
        var (a, r) = Cena();
        string painel = $"{ProjetoDeExemplo.R1}#i3", decisao = $"{ProjetoDeExemplo.R2}#i2";

        Assert.Throws<InvalidOperationException>(() => AcoesDoProjeto.Aplicar(a, r, new() { Acao = "concluir", Ref = painel }, T));
        Assert.Throws<InvalidOperationException>(() => AcoesDoProjeto.Aplicar(a, r, new() { Acao = "reabrir", Ref = Base }, T));
        Assert.Throws<InvalidOperationException>(() => AcoesDoProjeto.Aplicar(a, r, new() { Acao = "cancelar", Ref = decisao }, T));
        Assert.Throws<InvalidOperationException>(() => AcoesDoProjeto.Aplicar(a, r, new() { Acao = "concluir", Ref = "nada#i1" }, T));
        AcoesDoProjeto.Aplicar(a, r, new() { Acao = "reabrir", Ref = painel }, T);
        Assert.Single(a.Acontecimentos);
    }

    [Fact]
    public void AdicionarCriaOItemManualComIdProprio()
    {
        var (a, r) = Cena();
        AcoesDoProjeto.Aplicar(a, r, new() { Acao = "adicionar", Tipo = "pendente", Texto = "Renovar o contrato", Lado = "nosso" }, T);
        AcoesDoProjeto.Aplicar(a, r, new() { Acao = "adicionar", Tipo = "resolvido", Tema = "Escopo", Texto = "Fica fora o aplicativo." }, T);

        Assert.Equal(["m1", "m2"], a.Itens.Select(i => i.Id));
        Assert.Empty(a.Acontecimentos);
        Assert.Throws<InvalidOperationException>(() => AcoesDoProjeto.Aplicar(a, r,
            new() { Acao = "adicionar", Tipo = "pendente", Texto = "Sem lado" }, T));
    }

    [Fact]
    public void ANotaPedeTextoEFicaNoItem()
    {
        var (a, r) = Cena();
        Assert.Throws<InvalidOperationException>(() => AcoesDoProjeto.Aplicar(a, r, new() { Acao = "nota", Ref = Base, Texto = " " }, T));
        AcoesDoProjeto.Aplicar(a, r, new() { Acao = "nota", Ref = Base, Texto = "ela pediu mais uma semana" }, T);

        var passo = RegistroDoProjeto.Montar(ProjetoDeExemplo.Atas(), a).Achar(Base)!.Historia[^1];
        Assert.Equal(("nota", "ela pediu mais uma semana"), (passo.Tipo, passo.Detalhe));
    }
}
```

- [ ] **Step 2: Rodar e ver falhar**

Run: `dotnet test app-net/Tests/MeetingApp.Tests.csproj --filter "FullyQualifiedName~AcoesDoProjetoTests"`
Expected: FAIL de compilação.

- [ ] **Step 3: Implementar.**

- [ ] **Step 4: Rodar a suíte**

Run: `dotnet test app-net/Tests/MeetingApp.Tests.csproj`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add app-net/Nucleo/Projeto/AcoesDoProjeto.cs app-net/Tests/AcoesDoProjetoTests.cs
git commit -m "feat(projeto): concluir, cancelar, reabrir, anotar e acrescentar fora de reunião"
```

---

### Task 8: A ponte

**Files:**
- Modify: `app-net/App/Ponte.cs`
- Create: `app-net/Nucleo/Projeto/ProjetoParaTela.cs`
- Test: `app-net/Tests/ProjetoParaTelaTests.cs`

**Interfaces:**
- Produces:
  - `static ProjetoNaTela ProjetoParaTela.De(RegistroDoProjeto r, DateOnly hoje)`: o que a página recebe. `ProjetoNaTela` tem `"itens"` (cada um com `ref`, `reuniao`, `tipo`, `tema`, `texto`, `lado`, `responsavel`, `prazo`, `prazo_em`, `estado`, `nasceu_em`, `fechou_em`, `mencoes`, `dias_aberta`, `dias_vencida` (nulo sem data ou em dia), `historia` (cada passo com `em`, `tipo`, `reuniao`, `detalhe`, `falas`)), `"perdidas"`, `"nao_conferidas"` (cada uma com `reuniao`, `quando`, `titulo`) e `"vai_ate"`.
  - op `projeto` com `{ cliente, projeto }` → `{ projeto: ProjetoNaTela }`.
  - op `projeto-acao` com `{ cliente, projeto, mudanca_do_projeto }` → o mesmo, depois da ação.
  - op `sugestoes-da-conferencia` com `{ gravacao }` → `{ sugestoes: [{ item, prazo_em }], abertos: [itens do projeto que a ata não menciona] }`: a data proposta por `PrazoDito.Propor` para cada pendente com prazo e sem `prazo_em`, e os itens abertos do projeto que nenhuma ligação desta ata alcança (§5, o fim da conferência).
  - na op `salvar-reuniao`: se o vínculo mudou, `ArquivoDoProjeto.MoverAcontecimentos` do antigo para o novo.

- [ ] **Step 1: Escrever o teste** do que a página recebe

```csharp
public sealed class ProjetoParaTelaTests
{
    [Fact]
    public void ATelaRecebeOsDiasAbertosEVencidosCalculadosNoDia()
    {
        var r = RegistroDoProjeto.Montar(ProjetoDeExemplo.Atas(), new ArquivoDoProjeto());
        var t = ProjetoParaTela.De(r, new DateOnly(2026, 10, 30));

        var base_ = t.Itens.Single(i => i.Ref == $"{ProjetoDeExemplo.R1}#i2");
        Assert.Equal((16, 7, 2), (base_.DiasAberta, base_.DiasVencida, base_.Mencoes));   // nasceu 14/10; venceu 23/10
        Assert.Null(t.Itens.Single(i => i.Texto == "Definir o rateio").DiasVencida);       // sem data: nunca vencida
        Assert.Equal("2026-10-21", t.VaiAte);
        Assert.Equal(ProjetoDeExemplo.R3, t.NaoConferidas[0].Reuniao);
    }
}
```

- [ ] **Step 2: Rodar e ver falhar**

Run: `dotnet test app-net/Tests/MeetingApp.Tests.csproj --filter "FullyQualifiedName~ProjetoParaTelaTests"`
Expected: FAIL de compilação.

- [ ] **Step 3: Implementar** `ProjetoParaTela` (classes com `[JsonPropertyName]`, registradas no `PonteJson` por serem propriedade da `Resposta`) e as três operações. `Pedido` ganha `mudanca_do_projeto` (`MudancaDoProjeto`).

- [ ] **Step 4: Rodar a suíte e compilar o app**

Run: `dotnet test app-net/Tests/MeetingApp.Tests.csproj && dotnet build app-net/App -c Release 2>&1 | tail -3`
Expected: PASS e `0 Erro(s)`.

- [ ] **Step 5: Commit**

```bash
git add app-net/Nucleo/Projeto/ProjetoParaTela.cs app-net/App/Ponte.cs app-net/Tests/ProjetoParaTelaTests.cs
git commit -m "feat(ponte): as operações do projeto e as sugestões da conferência"
```

---

### Task 9: O MCP

§7, e a metade do Claude no §5.

**Files:**
- Modify: `app-net/Nucleo/Claude/FerramentasDoMcp.cs`
- Test: `app-net/Tests/IntegracaoComClaudeTests.cs`

**Interfaces:**
- Consumes: `LeitorDoProjeto.Ler`, `RegistroDoProjeto`, `ItemDoProjeto.Ref`.
- Produces:
  - ferramenta `listar_pendencias` com `cliente` (obrigatório), `projeto`, `estado` (`abertas`, o padrão, ou `todas`). Só leitura.
  - `preparar_ata` ganha a seção `## Itens abertos do projeto` (o registro montado **sem a própria reunião**), antes da transcrição, uma linha por item: `- <ref> — pendente (cliente, Carla, até sexta): Enviar a base atualizada` ou `- <ref> — decisão: Corte de 400 caixas por mês.`. Sem projeto ou sem aberto, a seção não aparece.
  - `propor_ata` aceita em cada resolvido e pendente o campo `liga`: `{ "ref": "<ref>", "como": "continua" | "fecha" | "revisa" }`. A ligação entra no item com `Confirmada = false`. `ref` que não está entre os abertos do projeto, ou `como` inválido, é descartado, e a resposta diz: `Ligação descartada (o item <ref> não está aberto no projeto): <texto>`.
  - os pedidos `timeline` e `preparar-reuniao` ganham, como passo 1: "Chame listar_pendencias. Se o projeto tiver atas conferidas, parta do registro e diga que partiu dele; senão, parta das atas, como abaixo, e diga isso."

**O texto de `listar_pendencias`:** uma linha de cabeçalho com o projeto e até quando o registro vai; o aviso das não conferidas, quando houver ("A reunião de 28/10/2026 ainda não foi conferida: o que saiu dela não está aqui."); depois `## Pendentes do lado do cliente`, `## Pendentes do nosso lado`, `## Decisões vigentes`, e com `estado = todas` também `## Concluídas e canceladas` e `## Decisões revistas`. Cada item numa linha com a ref, o texto, responsável, prazo (e "vencida há N dias"), a data em que nasceu e, com mais de uma menção, "cobrada em N reuniões". **A nota interna não aparece.** Cliente que casa com mais de um projeto e sem `projeto`: lista os projetos e pede para escolher, como `ErroDeUso`.

- [ ] **Step 1: Escrever os testes**

No construtor de `ServidorMcpTests`, o projeto "Aurora Bebidas" › "Portal de Pedidos" passa a ter as três atas do `ProjetoDeExemplo`: a de `R2` é a reunião que o plano do passo 1 já grava ali (mesmo id), e ganha só o `conferencia.json`; as de `R1` e `R3` são novas, a de `R3` com `FormalizacaoDeExemplo.Segmentos()` como transcrição. Os testes que contam reuniões do acervo de teste sobem duas unidades.

```csharp
[Fact]
public void ListarPendenciasMostraOQueEstaAbertoPorLadoESemANota()
{
    var a = ArquivoDoProjeto.Ler(Path.Combine(_raiz, "acervo"), "Aurora Bebidas", "Portal de Pedidos");
    a.Acontecimentos.Add(new() { Em = "2026-10-27T09:00:00-03:00", Reuniao = ProjetoDeExemplo.R1, Item = "i2",
                                 Tipo = "nota", Texto = "segredo interno" });
    a.Salvar(Path.Combine(_raiz, "acervo"));

    string texto = Ferramenta("listar_pendencias", new JsonObject { ["cliente"] = "aurora", ["projeto"] = "portal de pedidos" }, out bool erro);

    Assert.False(erro);
    Assert.Contains("ainda não foi conferida", texto);
    Assert.Contains($"{ProjetoDeExemplo.R1}#i2", texto);
    Assert.Contains("cobrada em 2 reuniões", texto);
    Assert.True(texto.IndexOf("lado do cliente") < texto.IndexOf("nosso lado"));
    Assert.DoesNotContain("segredo interno", texto);
    Assert.DoesNotContain("Montar o painel de ruptura", texto);   // concluída: só com estado = todas
}

[Fact]
public void PrepararAtaTrazOsAbertosEProporAtaGuardaALigacaoPorConfirmar()
{
    string preparo = Ferramenta("preparar_ata", new JsonObject { ["reuniao"] = ProjetoDeExemplo.R3 }, out _);
    Assert.Contains("## Itens abertos do projeto", preparo);
    Assert.Contains($"- {ProjetoDeExemplo.R1}#i2 — pendente (cliente, Carla, sexta): Enviar a base atualizada", preparo);

    string versao = System.Text.RegularExpressions.Regex.Match(preparo, @"versao: (\S+)").Groups[1].Value;
    string texto = Ferramenta("propor_ata", new JsonObject
    {
        ["reuniao"] = ProjetoDeExemplo.R3, ["versao"] = versao,
        ["resolvidos"] = new JsonArray(
            new JsonObject { ["tema"] = "Base", ["decisao"] = "A base foi enviada.", ["linhas"] = new JsonArray(3),
                             ["liga"] = new JsonObject { ["ref"] = $"{ProjetoDeExemplo.R1}#i2", ["como"] = "fecha" } },
            new JsonObject { ["tema"] = "Outro", ["decisao"] = "Fechado.", ["linhas"] = new JsonArray(3),
                             ["liga"] = new JsonObject { ["ref"] = "nada#i9", ["como"] = "fecha" } }),
    }, out bool erro);

    Assert.False(erro);
    Assert.Contains("Ligação descartada (o item nada#i9 não está aberto no projeto)", texto);
    var p = Conferencia.LerProposta(Path.Combine(_raiz, "acervo", ProjetoDeExemplo.R3))!;
    Assert.Equal((ProjetoDeExemplo.R1, "i2", "fecha", false), (p.Itens[0].Liga!.Reuniao, p.Itens[0].Liga!.Item, p.Itens[0].Liga!.Como, p.Itens[0].Liga!.Confirmada));
    Assert.Null(p.Itens[1].Liga);
}
```

- [ ] **Step 2: Rodar e ver falhar**

Run: `dotnet test app-net/Tests/MeetingApp.Tests.csproj --filter "FullyQualifiedName~ServidorMcpTests"`
Expected: FAIL.

- [ ] **Step 3: Implementar.** A ligação viaja pela conferência automática sem ser tocada: guarde-a ao lado do item por posição antes de `Conferir`, e reponha em `DoClaude` só nos itens que sobreviveram (o item que cai leva a ligação junto). Para isso `Resolvido` e `Pendente` ganham `LigacaoDoItem? Liga { get; init; }`, que `Conferir` preserva e `DoClaude` copia para o item.

- [ ] **Step 4: Rodar a suíte**

Run: `dotnet test app-net/Tests/MeetingApp.Tests.csproj`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add app-net/Nucleo app-net/Tests/IntegracaoComClaudeTests.cs
git commit -m "feat(mcp): listar_pendencias, os abertos em preparar_ata e a ligação em propor_ata"
```

---

### Task 10: O protótipo da página, com o dono

O §6 diz que o desenho fino espera o dono ver um protótipo, como no passo 1. **Esta tarefa é um portão: a Tarefa 11 não começa sem a aprovação dela.**

- [ ] **Step 1:** Montar um protótipo clicável da página do projeto com os tokens do AA Design System (`assets/ds`), sobre o `ProjetoDeExemplo` (dados inventados): as abertas em dois grupos, um item aberto com a história, a linha do tempo, o aviso de reunião não conferida, as ações fora de reunião. **Fora do repositório** se usar qualquer dado real.
- [ ] **Step 2:** Mostrar ao dono, junto com as cinco respostas assumidas no topo deste plano, e anotar no desenho (§6 e §10) o que ele decidir.
- [ ] **Step 3:** Se alguma resposta mudou, corrigir as tarefas indicadas na tabela **antes** de seguir.

---

### Task 11: A página do projeto

§6, no desenho que a Tarefa 10 aprovou.

**Files:**
- Create: `app-net/App/web/projeto-regras.js`, `app-net/App/web/projeto.js`
- Modify: `app-net/App/web/app.js`, `app-net/App/web/reuniao.js`, `app-net/App/web/clientes.js`, `app-net/App/web/app.css`
- Test: `tools/web/projeto-regras.test.mjs`

**Interfaces:**
- Consumes: as ops `projeto` e `projeto-acao` (Tarefa 8); `ouvir` de `tocador.js`; `relogioDoTocador`.
- Produces, em `projeto-regras.js`:
  - `abertasPorLado(projeto)` → `{ cliente: [...], nosso: [...] }`, mais antigas primeiro
  - `rotuloDoItem(item)` → `"Carla · até sexta (23/10) · vencida há 7 dias · há 16 dias · cobrada em 2 reuniões"`, só com as partes que existem
  - `linhaDoTempo(projeto)` → decisões e conclusões por data, da mais nova para a mais antiga, `[{ em, tipo, texto, reuniao }]`
  - `avisoDoPainel(projeto)` → `"A reunião de 28/10 ainda não foi conferida; o painel vai até 21/10."`, ou nulo
  - `enderecoDoProjeto(cliente, projeto)` → `"#projeto=" + encodeURIComponent(cliente) + "/" + encodeURIComponent(projeto)`, e `lerEndereco(arg)` de volta
- Produces, em `projeto.js`: `telaDoProjeto(cliente, projeto, { cabecalho, tela })`.

- [ ] **Step 1: Escrever os testes** em `tools/web/projeto-regras.test.mjs`

```js
import { test } from "node:test";
import assert from "node:assert/strict";

const R = await import(new URL("../../app-net/App/web/projeto-regras.js", import.meta.url));

const item = (ref, extra = {}) => ({
  ref, reuniao: ref.split("#")[0], tipo: "pendencia", tema: "", texto: `texto ${ref}`, lado: "cliente",
  responsavel: "", prazo: "", prazo_em: null, estado: "aberta", nasceu_em: "2026-10-14T11:30:00",
  fechou_em: null, mencoes: 1, dias_aberta: 16, dias_vencida: null, historia: [], ...extra,
});

const PROJETO = {
  vai_ate: "2026-10-21",
  nao_conferidas: [{ reuniao: "2026-10-28_11-30-00", quando: "2026-10-28T11:30:00", titulo: "Semanal" }],
  perdidas: [],
  itens: [
    item("r2#i4", { texto: "Definir o rateio", nasceu_em: "2026-10-21T11:30:00", dias_aberta: 9 }),
    item("r1#i2", { texto: "Enviar a base atualizada", responsavel: "Carla", prazo: "sexta",
                    prazo_em: "2026-10-23", dias_vencida: 7, mencoes: 2 }),
    item("r2#i5", { texto: "Item sem alvo", lado: "nosso" }),
    item("r1#i3", { texto: "Montar o painel de ruptura", lado: "nosso", estado: "concluida",
                    fechou_em: "2026-10-21T11:30:00" }),
    item("r2#i2", { tipo: "decisao", texto: "Corte de 400 caixas por mês.", estado: "vigente",
                    nasceu_em: "2026-10-21T11:30:00" }),
  ],
};

test("as abertas saem por lado, da mais antiga para a mais nova", () => {
  const a = R.abertasPorLado(PROJETO);
  assert.deepEqual(a.cliente.map((i) => i.ref), ["r1#i2", "r2#i4"]);
  assert.deepEqual(a.nosso.map((i) => i.ref), ["r2#i5"]);
});

test("o rótulo só traz o que existe", () => {
  assert.equal(R.rotuloDoItem(PROJETO.itens[1]),
    "Carla · até sexta (23/10) · vencida há 7 dias · há 16 dias · cobrada em 2 reuniões");
  assert.equal(R.rotuloDoItem(PROJETO.itens[0]), "sem data · há 9 dias");
});

test("a linha do tempo junta decisões e conclusões, da mais nova para a mais antiga", () => {
  assert.deepEqual(R.linhaDoTempo(PROJETO).map((l) => [l.tipo, l.texto]), [
    ["concluida", "Montar o painel de ruptura"],
    ["decisao", "Corte de 400 caixas por mês."],
  ]);
});

test("o aviso diz qual reunião falta e até onde o painel vai", () => {
  assert.equal(R.avisoDoPainel(PROJETO), "A reunião de 28/10 ainda não foi conferida; o painel vai até 21/10.");
  assert.equal(R.avisoDoPainel({ ...PROJETO, nao_conferidas: [] }), null);
});

test("o endereço aguenta barra e acento no nome", () => {
  const e = R.enderecoDoProjeto("A/B Testes", "Operação São Paulo");
  assert.deepEqual(R.lerEndereco(e.slice("#projeto=".length)), { cliente: "A/B Testes", projeto: "Operação São Paulo" });
});
```

- [ ] **Step 2: Rodar e ver falhar**

Run: `node --test tools/web/projeto-regras.test.mjs`
Expected: FAIL (`Cannot find module`).

- [ ] **Step 3: Implementar** as regras; depois a página, no desenho aprovado: o aviso do painel no topo; as abertas em dois grupos; abrir um item mostra a história, cada passo com a fala e *Ouvir* (o `g` do `ouvir` é a gravação da reunião do passo, achada pela lista de `gravacoes`) e o atalho para a reunião; as ações *Concluir*, *Cancelar*, *Reabrir*, *Mudar prazo*, *Mudar dono*, *Nota interna*, cada uma mandando `projeto-acao`; *Acrescentar item*; a linha do tempo; e as ligações perdidas, quando houver, com o atalho para a ata de onde saíram.

- [ ] **Step 4: A navegação.** Em `app.js`, `inicio()` trata `tela === "projeto"` chamando `telaDoProjeto`. Em `reuniao.js`, o "Cliente › Projeto" do subtítulo da reunião vira um link para `enderecoDoProjeto` quando a reunião tem os dois. Em `clientes.js`, o painel do projeto ganha "Pendências do projeto", ao lado de "Ver as reuniões deste projeto".

- [ ] **Step 5: Rodar e compilar**

Run: `node --test tools/web/*.test.mjs && dotnet build app-net/App -c Release 2>&1 | tail -3`
Expected: PASS e `0 Erro(s)`.

- [ ] **Step 6: Commit**

```bash
git add app-net/App/web tools/web/projeto-regras.test.mjs
git commit -m "feat(web): a página do projeto, com as abertas, a história e a linha do tempo"
```

---

### Task 12: A conferência ganha a ligação e a data do prazo

§5 e §4.4, na tela do passo 1.

**Files:**
- Modify: `app-net/App/web/conferencia-regras.js`, `app-net/App/web/conferencia.js`
- Test: `tools/web/conferencia-regras.test.mjs`

**Interfaces:**
- Consumes: `liga` e `prazo_em` no item (Tarefa 1); a op `sugestoes-da-conferencia` e a op `projeto-acao` (Tarefa 8).
- Produces, em `conferencia-regras.js`:
  - `fraseDaLigacao(liga, quando)` → `"parece fechar: «Enviar a base atualizada», de 14/10"`, com o verbo por `como` (`continua` → "parece continuar", `fecha` → "parece fechar", `revisa` → "parece rever"); confirmada, sem o "parece" ("fecha: …").
  - `dataPorExtenso("2026-10-23")` → `"23/10 (sexta)"`.

- [ ] **Step 1: Escrever os testes**

```js
test("a ligação diz o que parece fazer, e depois de confirmada diz o que faz", () => {
  const liga = { reuniao: "2026-10-14_11-30-00", item: "i2", como: "fecha", confirmada: false,
                 texto_do_alvo: "Enviar a base atualizada" };
  assert.equal(R.fraseDaLigacao(liga), "parece fechar: «Enviar a base atualizada», de 14/10");
  assert.equal(R.fraseDaLigacao({ ...liga, como: "continua", confirmada: true }),
    "continua: «Enviar a base atualizada», de 14/10");
});

test("a data do prazo sai com o dia da semana", () => {
  assert.equal(R.dataPorExtenso("2026-10-23"), "23/10 (sexta)");
});
```

- [ ] **Step 2: Rodar e ver falhar**

Run: `node --test tools/web/conferencia-regras.test.mjs`
Expected: FAIL.

- [ ] **Step 3: Implementar** as duas regras (a data da ligação sai do id da reunião, `AAAA-MM-DD_…`; o dia da semana por conta de calendário sobre a data, sem depender do fuso). Na tela:
- cada item com `liga` mostra `fraseDaLigacao` e, enquanto não confirmada, *Confirmar* (manda `ligar`), *É outro item* (manda `desligar`) e *Escolher outro* (uma lista dos abertos do projeto, vinda de `sugestoes-da-conferencia`);
- cada pendente com prazo e sem `prazo_em` mostra a data proposta, quando há: "vence em 23/10 (sexta)?" com *Confirmar* (manda `corrigir` com `prazo_em`) e *Outra data*; sem proposta, "sem data" e *Pôr data*;
- **ao concluir**, se `abertos` não é vazio, uma etapa opcional antes de fechar: "Estes itens do projeto não apareceram nesta ata. Algum fechou?", cada um com *Concluído* e *Cancelado* (mandam `projeto-acao`) e um *Seguir* que conclui sem mexer em nada.

- [ ] **Step 4: Rodar**

Run: `node --test tools/web/*.test.mjs`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add app-net/App/web tools/web/conferencia-regras.test.mjs
git commit -m "feat(web): a conferência confirma a ligação e a data do prazo, e pergunta o que não foi falado"
```

---

### Task 13: Os avisos do que não pode quebrar

§9.

**Files:**
- Modify: `app-net/App/web/atas.js`, `app-net/App/web/app.js` (`botaoApagarGravacao`)

**Interfaces:**
- Consumes: a op `projeto` (Tarefa 8), para saber quantos itens da reunião estão no registro.

- [ ] **Step 1: Refazer uma ata conferida.** A confirmação do passo 1 ("Refazer joga fora a conferência…") ganha, quando a reunião tem itens no registro do projeto: "N itens desta ata fazem parte das pendências do projeto. O que reuniões posteriores ligaram a eles vai aparecer como ligação perdida, para você religar."
- [ ] **Step 2: Apagar uma reunião.** A confirmação de apagar ganha, quando a reunião tem pendências abertas no projeto: "Esta reunião tem N pendências abertas no projeto X. Apagá-la as tira do painel."
- [ ] **Step 3: Rodar e compilar**

Run: `node --test tools/web/*.test.mjs && dotnet build app-net/App -c Release 2>&1 | tail -3`
Expected: PASS e `0 Erro(s)`.

- [ ] **Step 4: Commit**

```bash
git add app-net/App/web
git commit -m "feat(web): refazer e apagar avisam o que muda nas pendências do projeto"
```

---

### Task 14: Documentos e validação com o dono

- [ ] **Step 1: Os documentos.** `docs/RASTREABILIDADE.md` (o estado do passo 2 e o que foi decidido na Tarefa 10); `docs/BACKLOG.md` (o `ATA-2`, que este passo fecha); `docs/INTEGRACAO-CLAUDE.md` (a ferramenta nova e o que mudou nas duas da ata); o desenho (§10, com as respostas do dono).
- [ ] **Step 2: O binário publica.** Run: `tools/publicar.sh --so-build 2>&1 | tail -20`. Expected: sem `IL2026` nem `IL3050`.
- [ ] **Step 3: Com o dono, em dois projetos reais** (critérios 1 a 5 do §11): conferir a ata de uma segunda reunião propõe ligações e nenhuma vale sem confirmação; a página mostra as abertas por lado e a história toca o áudio; uma pendência cobrada em três reuniões aparece uma vez; refazer uma conferida avisa; `listar_pendencias` devolve o mesmo que a página.
- [ ] **Step 4: Duas semanas de uso** (critério 6): o que o dono teve que consertar à mão vai para o `RASTREABILIDADE.md`. O passo 3 só começa depois disso.
- [ ] **Step 5: Commit**

```bash
git add docs
git commit -m "docs: as pendências do projeto, e o que o primeiro uso mostrou"
```
