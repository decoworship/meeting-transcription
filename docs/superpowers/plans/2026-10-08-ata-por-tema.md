# Ata por tema: plano de implementação

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A ata passa a escrever cada decisão, ação, ponto em aberto e risco dentro do tema a que pertence, com uma chave em Ajustes › Atas para voltar ao formato de hoje.

**Architecture:** Cada item ganha um campo `secao` no JSON que o modelo preenche, e o `RedatorDeAta` aninha os itens nas seções quando o tipo tem a marca `<!-- ata: itens-por-secao -->` e o formato é "por tema". O formato clássico continua byte a byte o de hoje: mesmo prompt, mesmo esquema e mesmo `ata.md`, e testes de referência travam isso antes de qualquer mudança.

**Tech Stack:** C#/.NET 8, xUnit, System.Text.Json com contexto gerado em compilação, JavaScript sem build no WebView2, llama.cpp (`llama-server`) com JSON Schema estrito.

**Spec:** [docs/superpowers/specs/2026-10-08-ata-por-tema-design.md](../specs/2026-10-08-ata-por-tema-design.md)

## Global Constraints

- Comandos de teste sempre com `export PATH="$HOME/.dotnet:$PATH"` antes.
- Suíte: `dotnet test app-net/Tests/MeetingApp.Tests.csproj` (628 testes no início deste plano). Toda tarefa termina com a suíte inteira verde.
- `formato_da_ata` no `app.json`: `"classico"` ou `"por-tema"`. Padrão `"por-tema"`; ausente ou vazio → `"por-tema"`; qualquer outro valor → `"classico"`.
- A marca é exatamente a linha `<!-- ata: itens-por-secao -->`. Ela entra em `assets/atas/cliente-update.md`, `trabalho.md` e `kickoff.md`, e em nenhum outro.
- Arquivos novos na pasta da gravação: `ata.observacoes.md` e `ata.anterior.md`.
- **Nenhum dado de cliente real em fixture, teste ou commit.** As fixtures usam a reunião inventada "Acme Bebidas · Portal de Pedidos".
- **Nunca rodar o motor de ata (GPU) sem o dono autorizar na hora**: a placa roda a legenda ao vivo das reuniões dele. A Tarefa 10 começa perguntando.
- **Nunca escrever nas pastas reais do acervo.** O `Sidecar.exe --ata` grava `ata.md` na pasta que recebe; a validação roda sobre cópias.
- Não publicar (`tools/publicar.sh`), não subir versão, não dar push. O dono decide.
- Comentários e textos em português, no tom do código vizinho; `EmbeddedResource`/`None` com barra normal (armadilha medida no CLAUDE.md).

## Review Focus

1. **Ata antiga no disco** (`ata.json` com `decisoes` em `string[]`, gerada antes desta versão), lida por qualquer leitor do JSON: tem que carregar igual. Teste na Tarefa 2.
2. **O modelo devolve `secao` com grafia diferente** ("Pedido Mínimo", "pedido minimo ", "Pedido mínimo · Comercial") ou com um título que não existe: o item tem que cair na seção certa quando ela existe e na lista geral quando não existe, sem nunca sumir. Teste na Tarefa 6.
3. **Tipo do usuário sem a marca, ou com a marca e um esqueleto diferente** (pasta `.meeting-transcription/atas/`): sem marca, a ata não muda de estrutura; com marca, as seções do rodapé seguem o esqueleto dele. Teste na Tarefa 6 (`trabalho` como tipo marcado com rodapé).
4. **Gerar a ata de novo duas vezes seguidas:** o `ata.anterior.md` tem que guardar a penúltima, e um `ata.observacoes.md` de uma geração "por tema" não pode sobrar quando a seguinte é "clássica". Teste na Tarefa 7.
5. **Ação com dono cujo primeiro nome também começa o texto por acaso** ("Ana Paula" como dono e "Analisar a base" como ação): o nome não pode ser cortado do texto. Teste na Tarefa 5.

---

## Mapa dos arquivos

| arquivo | o que muda |
|---|---|
| `app-net/Nucleo/Atas/AtaGerada.cs` | `ItemDaAta` + conversor; `secao` em `AcaoDaAta`; `grupo`/`responsavel` em `SecaoDaAta`; `EsquemaPorTema` e `EsquemaPara` |
| `app-net/Nucleo/Atas/FormatoDaAta.cs` (novo) | o enum e a leitura do valor do `app.json` |
| `app-net/Nucleo/ConfiguracoesDoApp.cs` | chave `formato_da_ata` |
| `app-net/Nucleo/Atas/ModelosDeAta.cs` | `ItensPorSecao`; `TextoParaPrompt` sem a marca |
| `assets/atas/{cliente-update,trabalho,kickoff}.md` | a marca na primeira linha |
| `app-net/Nucleo/Atas/PromptDeAta.cs` | `Agrupa`, `BlocoPorTema`, `Montar(…, formato)` |
| `app-net/Nucleo/Atas/RedatorDeAta.cs` | limpeza de formato, aninhamento, "Quem ficou com o quê", `Observacoes` |
| `app-net/Nucleo/Atas/VerificadorDeAta.cs`, `SecaoDobrada.cs` | ler `.Texto`, preservar `secao` |
| `app-net/Nucleo/Atas/GeracaoDeAta.cs` | formato, esquema para o motor, `ata.anterior.md`, `ata.observacoes.md` |
| `app-net/Nucleo/Atas/MotorDeAta.cs` | `GerarAsync` recebe o esquema |
| `app-net/App/Ponte.cs` | lê o formato; a operação `ata` devolve `observacoes` |
| `app-net/Cli/GeradorDeAta.cs`, `Cli/Program.cs` | `--formato` |
| `app-net/App/web/configuracoes.js`, `atas.js`, `app.css` | aba Atas; bloco de observações |
| `app-net/Tests/ReuniaoDeExemplo.cs` (novo) | a fixture inventada |
| `app-net/Tests/Referencias/*` (novo) | as saídas de hoje, congeladas |
| `app-net/Tests/AtaPorTemaTests.cs` (novo) | os testes do formato novo |
| `docs/BACKLOG.md`, `docs/ATA.md` | `ATA-5` e o esquema |
| `tools/validar_ata_por_tema.py` (novo) | a validação com o modelo de verdade |

---

### Task 1: Congelar o formato de hoje

O primeiro commit não muda comportamento: grava em disco o que o redator e o prompt produzem **hoje** para quatro tipos, e cria o teste que exige igualdade. Tudo o que vem depois tem que manter esse teste verde no formato clássico.

**Files:**
- Create: `app-net/Tests/ReuniaoDeExemplo.cs`
- Create: `app-net/Tests/ReferenciasDaAtaTests.cs`
- Create: `app-net/Tests/Referencias/` (gerado pelo teste)

**Interfaces:**
- Produces: `ReuniaoDeExemplo.Contexto()`, `ReuniaoDeExemplo.Segmentos()`, `ReuniaoDeExemplo.AtaSemSecao()`, `ReuniaoDeExemplo.AtaComSecao()` (esta última preenche `secao`, usada a partir da Tarefa 2, e por isso entra nesta tarefa comentada; ver Step 1).

- [ ] **Step 1: Escrever a fixture**

`app-net/Tests/ReuniaoDeExemplo.cs`:

```csharp
using MeetingApp.Nucleo;
using MeetingApp.Nucleo.Atas;

namespace MeetingApp.Tests;

/// <summary>
/// Uma reunião inventada com a forma da de 22/09/2026 (ATA-5).
/// </summary>
/// <remarks>
/// Temas, itens de todos os tipos, ações dos dois lados, um dono repetido no
/// texto e prazos vazios — o que o formato por tema mexe. Nenhum nome ou dado
/// de cliente de verdade: o repositório publica releases.
/// </remarks>
internal static class ReuniaoDeExemplo
{
    public static ContextoDaReuniao Contexto() => new()
    {
        Titulo = "Semanal do Portal de Pedidos",
        Cliente = "Acme Bebidas",
        Projeto = "Portal de Pedidos",
        Data = "2026-09-22T10:30:00-03:00",
        DuracaoS = 1910,
        Convidados = ["Bruno Teles", "Diego Prado", "Carla Nunes"],
        Pessoas =
        [
            new Pessoa("Bruno Teles", "casa.com", true),
            new Pessoa("Diego Prado", "casa.com", true),
            new Pessoa("Carla Nunes", "acme.com", false),
        ],
        Falantes = ["Bruno Teles", "Carla Nunes"],
        Notas = "Ranking por canal - Comercial\nRegra de ruptura - Trade",
    };

    public static List<SegmentoFinal> Segmentos() =>
    [
        new SegmentoFinal { Start = 0, End = 6, Speaker = "Bruno Teles",
            Text = " Vamos começar o dashboard antes da integração com a Diana." },
        new SegmentoFinal { Start = 6, End = 12, Speaker = "Carla Nunes",
            Text = " O pedido mínimo agora soma o aplicativo e o SFA, e eu levanto o valor novo por canal." },
        new SegmentoFinal { Start = 12, End = 18, Speaker = "Carla Nunes",
            Text = " As regras de ruptura da Hiper eu busco até sexta." },
        new SegmentoFinal { Start = 18, End = 24, Speaker = "Bruno Teles",
            Text = " Perdemos duas semanas de sugestão em julho, 37% de conversão." },
    ];

    /// <summary>A ata como o modelo devolve hoje: itens sem seção.</summary>
    public static AtaGerada AtaSemSecao() => new()
    {
        Resumo = "Acompanhamento do Portal de Pedidos. O dashboard começa antes da integração.",
        Secoes =
        [
            new SecaoDaAta { Titulo = "Status por frente",
                Texto = "### Dashboard\n**Situação:** Em andamento\nO filtro já foi ajustado." },
            new SecaoDaAta { Titulo = "Próxima reunião", Texto = "A definir." },
        ],
        Decisoes = ["Começar o dashboard antes da integração com a Diana."],
        Acoes =
        [
            new AcaoDaAta { Acao = "Começar o dashboard", Responsavel = "Bruno Teles",
                            Prazo = "[prazo a definir]", Lado = "nosso" },
            new AcaoDaAta { Acao = "Carla Nunes buscar as regras de ruptura da Hiper",
                            Responsavel = "Carla Nunes", Prazo = "sexta", Lado = "cliente" },
        ],
        PontosEmAberto = ["Como implementar a regra que soma o aplicativo e o SFA."],
        Riscos = ["Duas semanas de sugestão perdidas em julho."],
        Observacoes = ["Números citados na reunião que não aparecem nesta ata: 37."],
    };
}
```

- [ ] **Step 2: Escrever o teste de referência**

`app-net/Tests/ReferenciasDaAtaTests.cs`:

```csharp
using System.Runtime.CompilerServices;
using MeetingApp.Nucleo.Atas;
using Xunit;

namespace MeetingApp.Tests;

/// <summary>
/// O formato de hoje, congelado antes da ata por tema (ATA-5).
/// </summary>
/// <remarks>
/// <para>
/// A chave "De hoje" em Ajustes promete o app de antes, byte a byte: mesmo
/// prompt e mesmo <c>ata.md</c>. Esta é a promessa escrita como teste. As
/// referências foram gravadas pelo código anterior à mudança.
/// </para>
/// <para>
/// Para regravar, de propósito: <c>ATA_REFERENCIAS=gravar dotnet test …</c>.
/// Regravar para fazer um teste passar é desfazer a promessa.
/// </para>
/// </remarks>
public sealed class ReferenciasDaAtaTests
{
    private static string Pasta([CallerFilePath] string aqui = "") =>
        Path.Combine(Path.GetDirectoryName(aqui)!, "Referencias");

    public static TheoryData<string> Tipos => new() { "cliente-update", "trabalho", "kickoff", "daily" };

    private static void Conferir(string arquivo, string atual)
    {
        string caminho = Path.Combine(Pasta(), arquivo);
        if (Environment.GetEnvironmentVariable("ATA_REFERENCIAS") == "gravar")
        {
            Directory.CreateDirectory(Pasta());
            File.WriteAllText(caminho, atual);
        }
        Assert.True(File.Exists(caminho), $"falta a referência {arquivo}");
        Assert.Equal(File.ReadAllText(caminho), atual);
    }

    [Theory, MemberData(nameof(Tipos))]
    public void OAtaMdClassicoNaoMuda(string tipo)
    {
        string md = RedatorDeAta.Escrever(ReuniaoDeExemplo.AtaSemSecao(),
            ModelosDeAta.Buscar(tipo)!, ReuniaoDeExemplo.Contexto());
        Conferir($"ata-classica-{tipo}.md", md);
    }

    [Theory, MemberData(nameof(Tipos))]
    public void OPromptClassicoNaoMuda(string tipo)
    {
        var segmentos = ReuniaoDeExemplo.Segmentos();
        string prompt = PromptDeAta.Montar(ModelosDeAta.Buscar(tipo)!,
            ReuniaoDeExemplo.Contexto(), segmentos, RoteiroDeFatos.De(segmentos));
        Conferir($"prompt-classico-{tipo}.txt", prompt);
    }

    [Fact]
    public void OEsquemaClassicoNaoMuda() => Conferir("esquema-classico.json", AtaGerada.Esquema);
}
```

- [ ] **Step 3: Rodar sem referências e ver falhar**

Run: `export PATH="$HOME/.dotnet:$PATH"; dotnet test app-net/Tests/MeetingApp.Tests.csproj --filter ReferenciasDaAtaTests`
Expected: 9 FAIL com "falta a referência".

- [ ] **Step 4: Gravar as referências com o código de hoje**

Run: `ATA_REFERENCIAS=gravar dotnet test app-net/Tests/MeetingApp.Tests.csproj --filter ReferenciasDaAtaTests`
Expected: PASS, e 9 arquivos em `app-net/Tests/Referencias/`. Abrir `ata-classica-cliente-update.md` e conferir que tem "## Observações sobre a transcrição", "[prazo a definir]" e "### Do nosso lado": é o formato de hoje.

- [ ] **Step 5: Rodar a suíte inteira**

Run: `dotnet test app-net/Tests/MeetingApp.Tests.csproj`
Expected: PASS (628 + 9).

- [ ] **Step 6: Commit**

```bash
git add app-net/Tests/ReuniaoDeExemplo.cs app-net/Tests/ReferenciasDaAtaTests.cs app-net/Tests/Referencias
git commit -m "test(ata): congela o formato de hoje antes da ata por tema"
```

---

### Task 2: O item da ata sabe a sua seção

**Files:**
- Modify: `app-net/Nucleo/Atas/AtaGerada.cs`
- Modify: `app-net/Nucleo/Atas/VerificadorDeAta.cs` (`ConferirDecisoes`, `ConferirRiscos`, `ConferirOmissoes`)
- Modify: `app-net/Nucleo/Atas/SecaoDobrada.cs` (`Preencher`)
- Modify: `app-net/Nucleo/Atas/RedatorDeAta.cs` (`Canonica`)
- Modify: `app-net/Tests/SecaoDobradaTests.cs:29,78,119`
- Create: `app-net/Tests/AtaPorTemaTests.cs`

**Interfaces:**
- Produces:
  - `public sealed record ItemDaAta(string Texto, string? Secao = null)` com `implicit operator ItemDaAta(string)`;
  - `AtaGerada.Decisoes`, `PontosEmAberto`, `Riscos`: `List<ItemDaAta>`;
  - `AcaoDaAta.Secao: string?`; `SecaoDaAta.Grupo: string?`; `SecaoDaAta.Responsavel: string?`.

- [ ] **Step 1: Escrever os testes que falham**

`app-net/Tests/AtaPorTemaTests.cs`:

```csharp
using MeetingApp.Nucleo.Atas;
using Xunit;

namespace MeetingApp.Tests;

/// <summary>A ata por tema (ATA-5): o esquema, o prompt e o redator.</summary>
public sealed class AtaPorTemaTests
{
    [Fact]
    public void AtaAntigaComListasDeTextoContinuaAbrindo()
    {
        // Toda ata.json gerada antes desta versão tem as três listas como
        // string[]. Ela tem que abrir igual, ou o acervo inteiro quebra.
        var ata = AtaGerada.DeJson("""
            {"resumo":"r","secoes":[],"decisoes":["d1"],"acoes":[],
             "pontos_em_aberto":["p1"],"riscos":["r1"],"observacoes":[]}
            """)!;

        Assert.Equal("d1", Assert.Single(ata.Decisoes).Texto);
        Assert.Null(ata.Decisoes[0].Secao);
        Assert.Equal("p1", Assert.Single(ata.PontosEmAberto).Texto);
        Assert.Equal("r1", Assert.Single(ata.Riscos).Texto);
    }

    [Fact]
    public void ItemComSecaoVaiEVoltaPeloJson()
    {
        var ata = new AtaGerada
        {
            Decisoes = [new ItemDaAta("d1", "Pedido mínimo")],
            Acoes = [new AcaoDaAta { Acao = "a1", Secao = "Pedido mínimo" }],
            Secoes = [new SecaoDaAta { Titulo = "Pedido mínimo", Texto = "t",
                                       Grupo = "Pedido Sugerido", Responsavel = "Comercial" }],
        };

        var volta = AtaGerada.DeJson(ata.ParaJson())!;

        Assert.Equal(new ItemDaAta("d1", "Pedido mínimo"), volta.Decisoes[0]);
        Assert.Equal("Pedido mínimo", volta.Acoes[0].Secao);
        Assert.Equal("Pedido Sugerido", volta.Secoes[0].Grupo);
        Assert.Equal("Comercial", volta.Secoes[0].Responsavel);
    }

    [Fact]
    public void SecaoEmBrancoViraNula()
    {
        // O esquema por tema exige o campo, então o modelo escreve "" quando o
        // item não pertence a seção nenhuma. "" e ausente têm que ser a mesma coisa.
        var ata = AtaGerada.DeJson("""
            {"resumo":"","secoes":[],"decisoes":[{"texto":"d","secao":"  "}],
             "acoes":[],"pontos_em_aberto":[],"riscos":[],"observacoes":[]}
            """)!;

        Assert.Null(ata.Decisoes[0].Secao);
    }

    [Fact]
    public void ItemSemSecaoNaoEscreveOCampo()
    {
        string json = new AtaGerada { Decisoes = ["d1"] }.ParaJson();
        Assert.DoesNotContain("\"secao\"", json);
    }

    [Fact]
    public void DecisaoRebaixadaPeloVerificadorLevaASecao()
    {
        // O verificador move para pontos em aberto a decisão que quase não
        // aparece na transcrição. Ela tem que continuar no tema dela.
        var ata = new AtaGerada
        {
            Decisoes = [new ItemDaAta("Migrar tudo para Kubernetes no trimestre", "Infra")],
        };

        VerificadorDeAta.Conferir(ata, ReuniaoDeExemplo.Segmentos(), ["Bruno Teles"], []);

        Assert.Empty(ata.Decisoes);
        Assert.Equal("Infra", Assert.Single(ata.PontosEmAberto).Secao);
    }
}
```

- [ ] **Step 2: Rodar e ver falhar**

Run: `dotnet test app-net/Tests/MeetingApp.Tests.csproj --filter AtaPorTemaTests`
Expected: erro de compilação (`ItemDaAta`, `Secao`, `Grupo`, `Responsavel` não existem).

- [ ] **Step 3: O tipo do item e o conversor**

Em `app-net/Nucleo/Atas/AtaGerada.cs`, antes de `public sealed class SecaoDaAta`:

```csharp
/// <summary>
/// Uma decisão, um ponto em aberto ou um risco, com a seção onde ele mora.
/// </summary>
/// <remarks>
/// <para>
/// Era <c>string</c> até a ata por tema (ATA-5). A seção é o que deixa o redator
/// escrever "Decidido" dentro do tema, em vez de numa lista no fim que repete o
/// tema de novo.
/// </para>
/// <para>
/// A conversão de <c>string</c> existe para o código que monta ata à mão — a
/// dobra de seções, os testes — continuar legível. O JSON antigo, com as listas
/// em <c>string[]</c>, abre pelo <see cref="ConversorDeItemDaAta"/>.
/// </para>
/// </remarks>
[JsonConverter(typeof(ConversorDeItemDaAta))]
public sealed record ItemDaAta(string Texto, string? Secao = null)
{
    public static implicit operator ItemDaAta(string texto) => new(texto);

    public override string ToString() => Texto;
}

/// <summary>Lê o item como texto (ata antiga) ou objeto; escreve sempre objeto.</summary>
public sealed class ConversorDeItemDaAta : JsonConverter<ItemDaAta>
{
    public override ItemDaAta Read(ref Utf8JsonReader leitor, Type tipo, JsonSerializerOptions opcoes)
    {
        if (leitor.TokenType == JsonTokenType.String)
            return new ItemDaAta(leitor.GetString() ?? "");

        if (leitor.TokenType != JsonTokenType.StartObject)
            throw new JsonException("item de ata: esperava texto ou objeto");

        string texto = "";
        string? secao = null;
        while (leitor.Read() && leitor.TokenType != JsonTokenType.EndObject)
        {
            string nome = leitor.GetString() ?? "";
            leitor.Read();
            string? valor = leitor.TokenType == JsonTokenType.String ? leitor.GetString() : null;
            if (leitor.TokenType is JsonTokenType.StartObject or JsonTokenType.StartArray)
                leitor.Skip();
            if (nome == "texto") texto = valor ?? "";
            else if (nome == "secao") secao = valor;
        }
        return new ItemDaAta(texto, string.IsNullOrWhiteSpace(secao) ? null : secao.Trim());
    }

    public override void Write(Utf8JsonWriter escritor, ItemDaAta item, JsonSerializerOptions opcoes)
    {
        escritor.WriteStartObject();
        escritor.WriteString("texto", item.Texto);
        if (item.Secao is { Length: > 0 } s) escritor.WriteString("secao", s);
        escritor.WriteEndObject();
    }
}
```

Em `SecaoDaAta`, depois de `Texto`:

```csharp
    /// <summary>O assunto maior de que esta seção é parte ("Pedido Sugerido").</summary>
    [JsonPropertyName("grupo")] public string? Grupo { get; set; }

    /// <summary>A área ou pessoa dona do tema, quando foi dita ou está nas notas.</summary>
    [JsonPropertyName("responsavel")] public string? Responsavel { get; set; }
```

Em `AcaoDaAta`, depois de `Lado`:

```csharp
    /// <summary>O título da seção onde a ação mora; nulo, vai para a lista geral.</summary>
    [JsonPropertyName("secao")] public string? Secao { get; set; }
```

Em `AtaGerada`, trocar as três listas:

```csharp
    [JsonPropertyName("decisoes")] public List<ItemDaAta> Decisoes { get; set; } = [];
    [JsonPropertyName("pontos_em_aberto")] public List<ItemDaAta> PontosEmAberto { get; set; } = [];
    [JsonPropertyName("riscos")] public List<ItemDaAta> Riscos { get; set; } = [];
```

O `AtaGerada.DeJson` de um `secao: ""` em `AcaoDaAta` também deve virar nulo. No fim de `DeJson`, normalizar:

```csharp
    public static AtaGerada? DeJson(string json)
    {
        var ata = JsonSerializer.Deserialize(json, AtaJson.Default.AtaGerada);
        if (ata is null) return null;
        foreach (var a in ata.Acoes)
            if (string.IsNullOrWhiteSpace(a.Secao)) a.Secao = null;
        return ata;
    }
```

- [ ] **Step 4: O verificador preserva a seção**

Em `VerificadorDeAta.ConferirDecisoes`, trocar o laço:

```csharp
        var sobreviventes = new List<ItemDaAta>();
        foreach (var decisao in ata.Decisoes)
        {
            var palavras = Palavras(decisao.Texto).ToList();
            if (palavras.Count == 0) continue;

            int eco = palavras.Count(p => Ecoa(p, faladas));
            double proporcao = (double)eco / palavras.Count;

            if (proporcao < 0.5)
            {
                notas.Add($"\"{Encurtar(decisao.Texto)}\" foi registrada como decisão, mas quase "
                          + "nada dela aparece na transcrição. Movida para pontos em aberto "
                          + "— confira antes de tratar como combinada.");
                // A seção vai junto: rebaixar não é tirar do tema.
                ata.PontosEmAberto.Add(decisao);
            }
            else
            {
                sobreviventes.Add(decisao);
            }
        }
        ata.Decisoes = sobreviventes;
```

Em `ConferirRiscos`, o mesmo: `var sobreviventes = new List<ItemDaAta>();`, `foreach (var risco in ata.Riscos)`, `Palavras(risco.Texto)`.

Em `ConferirOmissoes`:

```csharp
            .Concat(ata.Decisoes.Select(d => d.Texto))
            .Concat(ata.Acoes.Select(a => a.Acao))
            .Concat(ata.PontosEmAberto.Select(p => p.Texto))
            .Concat(ata.Riscos.Select(r => r.Texto)));
```

- [ ] **Step 5: A dobra e o redator leem o texto**

Em `SecaoDobrada.Preencher`:

```csharp
            case "Decisões": ata.Decisoes = [.. itens.Select(i => new ItemDaAta(i))]; break;
            case "Pontos em aberto": ata.PontosEmAberto = [.. itens.Select(i => new ItemDaAta(i))]; break;
            case "Riscos e alertas": ata.Riscos = [.. itens.Select(i => new ItemDaAta(i))]; break;
```

Em `RedatorDeAta.Canonica`:

```csharp
            case "Decisões": Lista(sb, "Decisões", Textos(ata.Decisoes)); break;
            case "Pendências": Acoes(sb, ata.Acoes); break;
            case "Pontos em aberto": Lista(sb, "Pontos em aberto", Textos(ata.PontosEmAberto)); break;
            case "Riscos e alertas": Lista(sb, "Riscos e alertas", Textos(ata.Riscos)); break;
```

e, no fim da classe:

```csharp
    private static List<string> Textos(IEnumerable<ItemDaAta> itens) => [.. itens.Select(i => i.Texto)];
```

- [ ] **Step 6: Os três asserts que comparavam texto**

Em `app-net/Tests/SecaoDobradaTests.cs`:
- linha 29: `Assert.Equal(["Não clusterizar por preço.", "Testar margem."], ata.Decisoes.Select(d => d.Texto));`
- linha 78: `Assert.Equal(["a decisão de verdade"], ata.Decisoes.Select(d => d.Texto));`
- linha 119-120: `Assert.Equal("Falta definir o prazo. E o escopo também.", Assert.Single(ata.PontosEmAberto).Texto);`

- [ ] **Step 7: Rodar a suíte inteira**

Run: `dotnet test app-net/Tests/MeetingApp.Tests.csproj`
Expected: PASS, incluindo os 5 novos e os 9 de referência. Se algum outro teste não compilar por comparar `string` com `ItemDaAta`, a correção é a mesma do Step 6 (`.Texto`), nunca regravar referência.

- [ ] **Step 8: Commit**

```bash
git add app-net/Nucleo/Atas app-net/Tests
git commit -m "feat(ata): cada item da ata pode dizer a sua seção (ATA-5)"
```

---

### Task 3: A chave, a marca e o tipo que agrupa

**Files:**
- Create: `app-net/Nucleo/Atas/FormatoDaAta.cs`
- Modify: `app-net/Nucleo/ConfiguracoesDoApp.cs` (depois de `TipoDeAtaPadrao`)
- Modify: `app-net/Nucleo/Atas/ModelosDeAta.cs` (`ModeloDeAta`)
- Modify: `assets/atas/cliente-update.md`, `assets/atas/trabalho.md`, `assets/atas/kickoff.md` (primeira linha)
- Test: `app-net/Tests/AtaPorTemaTests.cs`

**Interfaces:**
- Produces:
  - `public enum FormatoDaAta { Classico, PorTema }`;
  - `public static class FormatosDaAta { const string Padrao = "por-tema"; static FormatoDaAta Ler(string?); }`;
  - `ConfiguracoesDoApp.FormatoDaAta: string` (JSON `formato_da_ata`) e `ConfiguracoesDoApp.Formato: FormatoDaAta` (`[JsonIgnore]`);
  - `ModeloDeAta.ItensPorSecao: bool`; `ModeloDeAta.Marca` (const).

- [ ] **Step 1: Escrever os testes que falham**

Acrescentar a `AtaPorTemaTests`:

```csharp
    [Theory]
    [InlineData(null, FormatoDaAta.PorTema)]
    [InlineData("", FormatoDaAta.PorTema)]
    [InlineData("por-tema", FormatoDaAta.PorTema)]
    [InlineData("classico", FormatoDaAta.Classico)]
    [InlineData("outra-coisa", FormatoDaAta.Classico)]
    public void OFormatoLidoDoAppJson(string? escrito, FormatoDaAta esperado) =>
        Assert.Equal(esperado, FormatosDaAta.Ler(escrito));

    [Fact]
    public void AppJsonSemAChaveCaiNoPorTema()
    {
        string caminho = Path.Combine(Path.GetTempPath(), $"app-{Guid.NewGuid():N}.json");
        File.WriteAllText(caminho, """{"tema":"claro"}""");
        try
        {
            Assert.Equal(FormatoDaAta.PorTema, MeetingApp.Nucleo.ConfiguracoesDoApp.Carregar(caminho).Formato);
        }
        finally { File.Delete(caminho); }
    }

    [Theory]
    [InlineData("cliente-update", true)]
    [InlineData("trabalho", true)]
    [InlineData("kickoff", true)]
    [InlineData("sprint", false)]
    [InlineData("daily", false)]
    [InlineData("resultados", false)]
    public void SoOsTiposMarcadosAgrupam(string tipo, bool agrupa) =>
        Assert.Equal(agrupa, ModelosDeAta.Buscar(tipo)!.ItensPorSecao);

    [Fact]
    public void AMarcaNaoChegaAoModelo() =>
        Assert.DoesNotContain("itens-por-secao", ModelosDeAta.Buscar("cliente-update")!.TextoParaPrompt());
```

- [ ] **Step 2: Rodar e ver falhar**

Run: `dotnet test app-net/Tests/MeetingApp.Tests.csproj --filter AtaPorTemaTests`
Expected: erro de compilação (`FormatoDaAta`, `ItensPorSecao`).

- [ ] **Step 3: O formato**

`app-net/Nucleo/Atas/FormatoDaAta.cs`:

```csharp
namespace MeetingApp.Nucleo.Atas;

/// <summary>Como a ata é escrita: a de antes, ou com os itens dentro de cada tema.</summary>
/// <remarks>
/// O clássico é o app de antes da ATA-5, byte a byte — o
/// <c>ReferenciasDaAtaTests</c> cobra isso. Existe para comparar, e para voltar
/// se o modelo não acompanhar o formato novo numa reunião.
/// </remarks>
public enum FormatoDaAta { Classico, PorTema }

public static class FormatosDaAta
{
    public const string Padrao = "por-tema";

    /// <summary>
    /// Ausente é o padrão; desconhecido é o clássico, que é o caminho conhecido.
    /// </summary>
    public static FormatoDaAta Ler(string? valor) => valor switch
    {
        null or "" or Padrao => FormatoDaAta.PorTema,
        _ => FormatoDaAta.Classico,
    };
}
```

Em `ConfiguracoesDoApp.cs`, depois de `TipoDeAtaPadrao`:

```csharp
    /// <summary>"por-tema" (padrão) ou "classico" — ver Nucleo/Atas/FormatoDaAta.cs.</summary>
    [JsonPropertyName("formato_da_ata")] public string FormatoDaAta { get; set; }
        = Atas.FormatosDaAta.Padrao;

    [JsonIgnore] public Atas.FormatoDaAta Formato => Atas.FormatosDaAta.Ler(FormatoDaAta);
```

(Conferir que `System.Text.Json.Serialization` já está nos `using` do arquivo; está, pelo `JsonPropertyName`.)

- [ ] **Step 4: A marca no tipo**

Em `ModeloDeAta` (`ModelosDeAta.cs`), antes de `Secoes`:

```csharp
    /// <summary>A linha que liga a ata por tema num tipo (ATA-5).</summary>
    /// <remarks>
    /// É texto do arquivo, e não uma lista no código, porque o tipo é
    /// personalizável (ATA.md §1): um tipo do usuário liga o agrupamento
    /// escrevendo a linha. O modelo nunca a vê — <see cref="TextoParaPrompt"/>
    /// a tira.
    /// </remarks>
    public const string Marca = "<!-- ata: itens-por-secao -->";

    /// <summary>Os itens moram dentro das seções, quando o formato é por tema.</summary>
    public bool ItensPorSecao => Texto.Contains(Marca, StringComparison.Ordinal);

    /// <summary>O texto sem a linha da marca, para tudo o que vai ao modelo.</summary>
    private string SemMarca => Texto.Replace(Marca + "\r\n", "").Replace(Marca + "\n", "").Replace(Marca, "");
```

Em `TextoParaPrompt()`, trocar todas as leituras de `Texto` por `SemMarca` (são cinco: os dois `IndexOf`, o `Texto[(inicio + 11)..fim]`, o `return Texto` dos dois retornos antecipados e o `Texto[..(inicio + 11)]`/`Texto[fim..]` do fim). Começar o método com `string texto = SemMarca;` e usar `texto` no lugar de `Texto`.

- [ ] **Step 5: Pôr a marca nos três arquivos**

Inserir como **primeira linha** de `assets/atas/cliente-update.md`, `assets/atas/trabalho.md` e `assets/atas/kickoff.md`:

```
<!-- ata: itens-por-secao -->
```

- [ ] **Step 6: Rodar a suíte inteira**

Run: `dotnet test app-net/Tests/MeetingApp.Tests.csproj`
Expected: PASS. Os 4 testes `OPromptClassicoNaoMuda` continuam verdes: é a prova de que a marca não chega ao modelo.

- [ ] **Step 7: Commit**

```bash
git add app-net/Nucleo assets/atas app-net/Tests/AtaPorTemaTests.cs
git commit -m "feat(ata): chave formato_da_ata e a marca dos tipos que agrupam"
```

---

### Task 4: O prompt e o esquema do formato por tema

**Files:**
- Modify: `app-net/Nucleo/Atas/AtaGerada.cs` (depois de `Esquema`)
- Modify: `app-net/Nucleo/Atas/PromptDeAta.cs` (`ContratoDoJson`, `Montar`)
- Test: `app-net/Tests/AtaPorTemaTests.cs`

**Interfaces:**
- Consumes: `FormatoDaAta`, `ModeloDeAta.ItensPorSecao` (Tarefa 3).
- Produces:
  - `AtaGerada.EsquemaPorTema` (const), `AtaGerada.EsquemaPara(bool agrupa): string`;
  - `PromptDeAta.Agrupa(ModeloDeAta tipo, FormatoDaAta formato): bool`;
  - `PromptDeAta.BlocoPorTema` (const);
  - `PromptDeAta.Montar(ModeloDeAta tipo, ContextoDaReuniao ctx, IReadOnlyList<SegmentoFinal> segmentos, IReadOnlyList<Fato> roteiro, FormatoDaAta formato = FormatoDaAta.Classico): string`.

- [ ] **Step 1: Escrever os testes que falham**

```csharp
    private static string Prompt(string tipo, FormatoDaAta formato)
    {
        var s = ReuniaoDeExemplo.Segmentos();
        return PromptDeAta.Montar(ModelosDeAta.Buscar(tipo)!, ReuniaoDeExemplo.Contexto(),
                                  s, RoteiroDeFatos.De(s), formato);
    }

    [Fact]
    public void PorTemaComMarcaLevaOBlocoNovo()
    {
        string p = Prompt("cliente-update", FormatoDaAta.PorTema);
        Assert.Contains(PromptDeAta.BlocoPorTema, p);
        Assert.Contains("no máximo três frases", p);
    }

    [Fact]
    public void PorTemaSemMarcaEOPromptDeHoje() =>
        Assert.Equal(Prompt("daily", FormatoDaAta.Classico), Prompt("daily", FormatoDaAta.PorTema));

    [Fact]
    public void ClassicoComMarcaNaoLevaOBlocoNovo() =>
        Assert.DoesNotContain(PromptDeAta.BlocoPorTema, Prompt("cliente-update", FormatoDaAta.Classico));

    [Fact]
    public void OEsquemaPorTemaEJsonValidoEExigeASecao()
    {
        using var doc = System.Text.Json.JsonDocument.Parse(AtaGerada.EsquemaPorTema);
        var props = doc.RootElement.GetProperty("properties");

        foreach (string lista in new[] { "decisoes", "pontos_em_aberto", "riscos", "acoes" })
        {
            var item = props.GetProperty(lista).GetProperty("items");
            Assert.Equal("object", item.GetProperty("type").GetString());
            Assert.Contains("secao", item.GetProperty("required").EnumerateArray().Select(e => e.GetString()));
        }
        var secao = props.GetProperty("secoes").GetProperty("items").GetProperty("properties");
        Assert.True(secao.TryGetProperty("grupo", out _));
        Assert.True(secao.TryGetProperty("responsavel", out _));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OEsquemaCertoParaCadaCaso(bool agrupa) =>
        Assert.Equal(agrupa ? AtaGerada.EsquemaPorTema : AtaGerada.Esquema, AtaGerada.EsquemaPara(agrupa));
```

- [ ] **Step 2: Rodar e ver falhar**

Run: `dotnet test app-net/Tests/MeetingApp.Tests.csproj --filter AtaPorTemaTests`
Expected: erro de compilação (`BlocoPorTema`, `EsquemaPorTema`, `Montar` com cinco argumentos).

- [ ] **Step 3: O esquema por tema**

Em `AtaGerada.cs`, depois do `Esquema`:

```csharp
    /// <summary>
    /// O esquema da ata por tema: os itens dizem a seção, a seção diz grupo e dono.
    /// </summary>
    /// <remarks>
    /// <c>secao</c> é obrigatória nos itens, e pode vir vazia: obrigar o campo é
    /// o que faz o modelo pensar no tema de cada item, e o vazio é a saída
    /// honesta para o que não pertence a nenhum. <c>grupo</c> e
    /// <c>responsavel</c> da seção são opcionais — não inventar dono é regra.
    /// </remarks>
    public const string EsquemaPorTema = """
    {
      "type": "object",
      "properties": {
        "resumo": {
          "type": "string",
          "description": "No máximo três frases. Os temas abaixo detalham; não os repita aqui."
        },
        "secoes": {
          "type": "array",
          "description": "Um tema por seção, na ordem em que a reunião os tratou. Nunca uma seção com subtítulos dentro.",
          "items": {
            "type": "object",
            "properties": {
              "titulo": { "type": "string", "description": "O nome curto do tema." },
              "situacao": {
                "type": "string",
                "description": "Só quando a seção tiver estado. Vazio se não tiver."
              },
              "texto": {
                "type": "string",
                "description": "O contexto do tema em uma ou duas frases. Não repita as decisões, ações, pontos em aberto e riscos, que vão nos campos próprios."
              },
              "grupo": {
                "type": "string",
                "description": "O assunto maior, quando várias seções são partes dele. Mesmo texto em todas."
              },
              "responsavel": {
                "type": "string",
                "description": "A área ou pessoa dona do tema, só se foi dita na reunião ou está nas notas."
              }
            },
            "required": ["titulo", "texto"],
            "additionalProperties": false
          }
        },
        "decisoes": {
          "type": "array",
          "description": "Só o que teve conclusão explícita. Hipótese não é decisão.",
          "items": {
            "type": "object",
            "properties": {
              "texto": { "type": "string" },
              "secao": {
                "type": "string",
                "description": "O título exato da seção a que o item pertence, ou vazio."
              }
            },
            "required": ["texto", "secao"],
            "additionalProperties": false
          }
        },
        "acoes": {
          "type": "array",
          "items": {
            "type": "object",
            "properties": {
              "acao": { "type": "string", "description": "O que fazer, curto. O tema já diz para quê." },
              "responsavel": {
                "type": "string",
                "description": "Nome de quem faz. Se ninguém foi nomeado, escreva exatamente: [responsável a definir]"
              },
              "prazo": {
                "type": "string",
                "description": "Se não houve prazo, escreva exatamente: [prazo a definir]"
              },
              "lado": {
                "type": "string",
                "enum": ["nosso", "cliente"],
                "description": "De quem depende a ação."
              },
              "secao": {
                "type": "string",
                "description": "O título exato da seção a que a ação pertence, ou vazio."
              }
            },
            "required": ["acao", "responsavel", "prazo", "lado", "secao"],
            "additionalProperties": false
          }
        },
        "pontos_em_aberto": {
          "type": "array",
          "description": "O que foi levantado e não concluiu, com a pergunta que ficou.",
          "items": {
            "type": "object",
            "properties": {
              "texto": { "type": "string" },
              "secao": {
                "type": "string",
                "description": "O título exato da seção a que o item pertence, ou vazio."
              }
            },
            "required": ["texto", "secao"],
            "additionalProperties": false
          }
        },
        "riscos": {
          "type": "array",
          "description": "Só quando alguém sinalizou preocupação. Vazio se ninguém sinalizou.",
          "items": {
            "type": "object",
            "properties": {
              "texto": { "type": "string" },
              "secao": {
                "type": "string",
                "description": "O título exato da seção a que o item pertence, ou vazio."
              }
            },
            "required": ["texto", "secao"],
            "additionalProperties": false
          }
        },
        "observacoes": {
          "type": "array",
          "description": "Trechos inaudíveis, números divergentes, nomes com grafia incerta.",
          "items": { "type": "string" }
        }
      },
      "required": ["resumo", "secoes", "decisoes", "acoes", "pontos_em_aberto", "riscos", "observacoes"],
      "additionalProperties": false
    }
    """;

    public static string EsquemaPara(bool agrupa) => agrupa ? EsquemaPorTema : Esquema;
```

Os três `items` de `decisoes`, `pontos_em_aberto` e `riscos` são o mesmo objeto escrito por extenso, e não um `$ref`: o suporte do `json-schema-to-grammar` do llama.cpp a `$ref` nunca foi medido neste projeto, e o teste do Step 1 lê `items.type` direto.

- [ ] **Step 4: O bloco do prompt**

Em `PromptDeAta.cs`, depois de `ContratoDoJson`:

```csharp
    /// <summary>
    /// O que muda para o modelo na ata por tema (ATA-5).
    /// </summary>
    /// <remarks>
    /// Cada frase vem de um defeito da ata de 22/09/2026, a que foi para o
    /// cliente e voltou com "ficou extensa e repetitiva": o modelo pôs seis
    /// frentes dentro de uma seção só com subtítulos, e cada tema reapareceu em
    /// até sete lugares.
    /// </remarks>
    public const string BlocoPorTema = """
        # Cada item mora no seu tema

        Esta ata é organizada **por tema**: quem lê quer ver, dentro de cada
        assunto, o que foi decidido, quem faz o quê e o que ficou em aberto.

        - **Um tema por seção** em `secoes`. Nunca uma seção com subtítulos `###`
          dentro: cada frente, assunto ou módulo é uma entrada própria.
        - Todo item de `decisoes`, `acoes`, `pontos_em_aberto` e `riscos`
          preenche `secao` com o **título exato** da seção a que pertence. Se não
          pertence a nenhuma, deixe `secao` vazio.
        - O `texto` da seção dá o **contexto** em uma ou duas frases e **não
          repete** a decisão, a ação, o ponto em aberto nem o risco — eles já
          aparecem dentro do tema.
        - A ação é **curta**: o que fazer. Sem "para avaliar a aplicação de…"; o
          tema já diz para quê. Não comece a ação pelo nome do responsável.
        - `grupo` só quando várias seções são partes de um mesmo assunto maior,
          com o mesmo texto em todas.
        - `responsavel` da seção só quando a área ou a pessoa dona do tema **foi
          dita ou está nas notas**. Nunca deduza.
        - O `resumo` tem **no máximo três frases**.
        """;

    /// <summary>A ata agrupa por tema: formato por tema e um tipo com a marca.</summary>
    public static bool Agrupa(ModeloDeAta tipo, FormatoDaAta formato) =>
        formato == FormatoDaAta.PorTema && tipo.ItensPorSecao;
```

Em `Montar`, acrescentar o parâmetro `FormatoDaAta formato = FormatoDaAta.Classico` e, logo depois de `sb.AppendLine(ContratoDoJson);`:

```csharp
        if (Agrupa(tipo, formato))
        {
            sb.AppendLine();
            sb.AppendLine(BlocoPorTema);
        }
```

Nada mais no `Montar` muda: o resto do prompt é idêntico nos dois formatos, e o teste `PorTemaSemMarcaEOPromptDeHoje` cobra isso.

- [ ] **Step 5: Rodar a suíte inteira**

Run: `dotnet test app-net/Tests/MeetingApp.Tests.csproj`
Expected: PASS, com `OPromptClassicoNaoMuda` e `OEsquemaClassicoNaoMuda` verdes.

- [ ] **Step 6: Commit**

```bash
git add app-net/Nucleo/Atas app-net/Tests/AtaPorTemaTests.cs
git commit -m "feat(ata): prompt e esquema do formato por tema"
```

---

### Task 5: O redator limpa o formato

As melhorias que valem para qualquer tipo no formato por tema: sem prazo vazio, sem o dono repetido no texto, observações fora do `ata.md`.

**Files:**
- Modify: `app-net/Nucleo/Atas/RedatorDeAta.cs`
- Test: `app-net/Tests/AtaPorTemaTests.cs`

**Interfaces:**
- Consumes: `FormatoDaAta` (Tarefa 3).
- Produces:
  - `RedatorDeAta.Escrever(AtaGerada ata, ModeloDeAta tipo, ContextoDaReuniao ctx, FormatoDaAta formato = FormatoDaAta.Classico): string`;
  - `RedatorDeAta.Observacoes(AtaGerada ata): string?` — o conteúdo do `ata.observacoes.md`, ou nulo sem observações;
  - `RedatorDeAta.SemDonoNoComeco(string acao, string dono): string` (interno, `internal static`, visível aos testes pelo `InternalsVisibleTo` que o projeto já tem; conferir em `MeetingApp.Nucleo.csproj`, e se não houver, deixar `public`).

- [ ] **Step 1: Escrever os testes que falham**

```csharp
    private static string Md(string tipo, FormatoDaAta formato, AtaGerada? ata = null) =>
        RedatorDeAta.Escrever(ata ?? ReuniaoDeExemplo.AtaSemSecao(), ModelosDeAta.Buscar(tipo)!,
                              ReuniaoDeExemplo.Contexto(), formato);

    [Theory]
    [InlineData("daily")]
    [InlineData("cliente-update")]
    public void PorTemaNaoEscrevePrazoVazioNemObservacoes(string tipo)
    {
        string md = Md(tipo, FormatoDaAta.PorTema);
        Assert.DoesNotContain("[prazo a definir]", md);
        Assert.DoesNotContain("Observações sobre a transcrição", md);
        Assert.Contains("— sexta", md);   // prazo de verdade continua
    }

    [Fact]
    public void AsObservacoesVaoParaOArquivoProprio()
    {
        string? obs = RedatorDeAta.Observacoes(ReuniaoDeExemplo.AtaSemSecao());
        Assert.NotNull(obs);
        Assert.StartsWith("## Observações sobre a transcrição", obs);
        Assert.Contains("Números citados", obs);
        Assert.Null(RedatorDeAta.Observacoes(new AtaGerada()));
    }

    [Theory]
    [InlineData("Andre Yuri investigar a influência da Copa", "Andre Yuri", "Investigar a influência da Copa")]
    [InlineData("Carla Nunes buscar as regras", "Carla Nunes", "Buscar as regras")]
    [InlineData("Marina Lopes buscar as regras", "Marina Costa Lopes", "Buscar as regras")]
    [InlineData("Começar o dashboard", "Bruno Teles", "Começar o dashboard")]
    [InlineData("Analisar a base", "Ana Paula", "Analisar a base")]
    [InlineData("Carla Nunes", "Carla Nunes", "Carla Nunes")]
    [InlineData("Agendar alinhamento com o Rodrigo", "[responsável a definir]", "Agendar alinhamento com o Rodrigo")]
    public void ODonoSaiDoComecoDoTexto(string acao, string dono, string esperado) =>
        Assert.Equal(esperado, RedatorDeAta.SemDonoNoComeco(acao, dono));

    [Fact]
    public void PorTemaTiraODonoRepetidoNaAta() =>
        Assert.Contains("- [ ] Buscar as regras de ruptura da Hiper — **Carla Nunes** — sexta",
                        Md("daily", FormatoDaAta.PorTema));
```

- [ ] **Step 2: Rodar e ver falhar**

Run: `dotnet test app-net/Tests/MeetingApp.Tests.csproj --filter AtaPorTemaTests`
Expected: erro de compilação (`Escrever` com quatro argumentos, `Observacoes`, `SemDonoNoComeco`).

- [ ] **Step 3: Implementar**

Em `RedatorDeAta.Escrever`, acrescentar o parâmetro `FormatoDaAta formato = FormatoDaAta.Classico` e guardar `bool limpo = formato == FormatoDaAta.PorTema;`. Passar `limpo` para `Canonica` → `Acoes` → `Linha`. Na lista fixa do fim, a observação só sai no clássico:

```csharp
        if (!limpo)
            Canonica(sb, "Observações sobre a transcrição", ata, escritas, limpo);
```

e, no laço do esqueleto, pular a canônica de observações quando `limpo` (o esqueleto de alguns tipos a lista):

```csharp
            if (EhCanonica(doEsqueleto))
            {
                if (limpo && Canonizar(doEsqueleto) == "Observações sobre a transcrição") continue;
                Canonica(sb, doEsqueleto, ata, escritas, limpo);
                continue;
            }
```

`Linha` passa a:

```csharp
    /// <summary>O formato que a skill pede, escrito por nós e não pelo modelo.</summary>
    /// <param name="limpo">
    /// Formato por tema: sem "[prazo a definir]" — sete vezes na ata de 22/09 —
    /// e sem o dono repetido no começo do texto.
    /// </param>
    private static string Linha(AcaoDaAta a, bool limpo)
    {
        string dono = a.Responsavel.Trim() is { Length: > 0 } d ? d : "[responsável a definir]";
        string prazo = a.Prazo.Trim() is { Length: > 0 } p ? p : "[prazo a definir]";
        string acao = a.Acao.Trim();
        if (!limpo) return $"- [ ] {acao} — **{dono}** — {prazo}";

        acao = SemDonoNoComeco(acao, dono).TrimEnd('.');
        bool semPrazo = prazo.StartsWith('[');
        return $"- [ ] {acao} — **{dono}**" + (semPrazo ? "" : $" — {prazo}");
    }

    /// <summary>"Andre Yuri investigar…" → "Investigar…", quando o dono já está no campo.</summary>
    /// <remarks>
    /// Corta só o primeiro nome do dono seguido de palavras com inicial
    /// maiúscula — o resto do nome, que o modelo às vezes abrevia ("Marina
    /// Lopes" para "Marina Costa Lopes"). Exige a palavra inteira: "Ana"
    /// não corta "Analisar". E se não sobra nada depois do nome, o texto fica.
    /// </remarks>
    internal static string SemDonoNoComeco(string acao, string dono)
    {
        string primeiro = dono.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
        if (primeiro.Length == 0 || primeiro.StartsWith('[')) return acao;

        var palavras = acao.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (palavras.Length < 2 || !palavras[0].Equals(primeiro, StringComparison.OrdinalIgnoreCase))
            return acao;

        int i = 1;
        while (i < palavras.Length && char.IsUpper(palavras[i][0])) i++;
        if (i >= palavras.Length) return acao;

        string resto = string.Join(' ', palavras[i..]);
        return char.ToUpper(resto[0]) + resto[1..];
    }

    /// <summary>
    /// O <c>ata.observacoes.md</c>: o recado do verificador para quem gerou a ata.
    /// </summary>
    /// <remarks>
    /// No formato por tema ele sai do <c>ata.md</c>, porque o <c>ata.md</c> é o
    /// que se exporta e vai para o cliente — e a de 22/09 foi com "confira se
    /// algum deveria estar aqui" dentro. A tela da ata o mostra à parte.
    /// </remarks>
    public static string? Observacoes(AtaGerada ata)
    {
        var sb = new StringBuilder();
        Lista(sb, "Observações sobre a transcrição", ata.Observacoes);
        return sb.Length > 0 ? sb.ToString().TrimEnd() + "\n" : null;
    }
```

Atualizar `Acoes(sb, acoes)` para `Acoes(sb, acoes, bool limpo)` e as duas chamadas de `Linha(a)` para `Linha(a, limpo)`. `Canonica` ganha `bool limpo` e o repassa ao `case "Pendências"`; **todas** as chamadas de `Canonica` passam `limpo`, inclusive o laço do fim (`foreach (string canonica in new[] { "Decisões", "Pendências", "Pontos em aberto", "Riscos e alertas" }) Canonica(sb, canonica, ata, escritas, limpo);`).

Se o `MeetingApp.Nucleo.csproj` não tiver `InternalsVisibleTo` para `MeetingApp.Tests` (`grep -n InternalsVisibleTo app-net/Nucleo/*.csproj`), declarar `SemDonoNoComeco` como `public`.

- [ ] **Step 4: Rodar a suíte inteira**

Run: `dotnet test app-net/Tests/MeetingApp.Tests.csproj`
Expected: PASS, com as 4 referências `OAtaMdClassicoNaoMuda` verdes.

- [ ] **Step 5: Commit**

```bash
git add app-net/Nucleo/Atas/RedatorDeAta.cs app-net/Tests/AtaPorTemaTests.cs
git commit -m "feat(ata): sem prazo vazio, sem dono repetido e observações à parte no formato por tema"
```

---

### Task 6: O redator aninha os itens no tema

**Files:**
- Modify: `app-net/Nucleo/Atas/RedatorDeAta.cs`
- Modify: `app-net/Tests/ReuniaoDeExemplo.cs` (acrescenta `AtaComSecao`)
- Test: `app-net/Tests/AtaPorTemaTests.cs`

**Interfaces:**
- Consumes: `PromptDeAta.Agrupa` (Tarefa 4), `Linha(a, limpo)` (Tarefa 5), `ItemDaAta`, `AcaoDaAta.Secao`, `SecaoDaAta.Grupo/Responsavel` (Tarefa 2).
- Produces: o `ata.md` por tema. Nenhuma assinatura pública nova.

**A regra de ordem**, para o tipo marcado no formato por tema:

1. cabeçalho e Resumo, como hoje;
2. **o corpo:** as seções do modelo, na ordem em que vieram, menos as do rodapé (abaixo). Cada uma com seus itens. Seções com o mesmo `grupo` saem juntas, na posição da primeira, sob `## grupo`, com `###`;
3. **a sobra:** os itens cujo `secao` não casa com nenhuma seção, sob os títulos de hoje (`## Decisões`, `## Pontos em aberto`, `## Riscos e alertas`). As ações que sobram vão em `## Pendências` se nenhuma ação foi aninhada, ou em `## Outras pendências` se alguma foi;
4. **"Quem ficou com o quê"**, se alguma ação foi aninhada;
5. **o rodapé:** as seções que casam com títulos não canônicos do esqueleto que vêm **depois** da primeira canônica que não é Resumo ("Próxima reunião" no update, "Referências citadas" na sessão de trabalho, "Definições e glossário" no kickoff), na ordem do esqueleto.

**Uma seção dentro dele:**

```
### Pedido mínimo · Comercial          (## sem grupo; "· dono" só com responsavel)

**Situação:** em andamento             (só com situacao)

A regra nova soma App e SFA.           (o texto; some se vazio)

**Decidido:** …                        (um parágrafo por decisão)

**Atenção:** …                         (um por risco)

- [ ] Novo valor por canal — **Carla Nunes**
- [ ] Agendar com o PCP — **[responsável a definir]**

**Em aberto:** …                       (um por ponto)
```

Uma seção sem texto e sem itens não sai.

- [ ] **Step 1: Escrever a fixture com seção**

Acrescentar a `ReuniaoDeExemplo`:

```csharp
    /// <summary>A mesma reunião, como o modelo devolve no formato por tema.</summary>
    public static AtaGerada AtaComSecao() => new()
    {
        Resumo = "Acompanhamento do Portal de Pedidos.",
        Secoes =
        [
            new SecaoDaAta { Titulo = "Dashboard", Texto = "O filtro já foi ajustado." },
            new SecaoDaAta { Titulo = "Pedido mínimo", Texto = "A regra nova soma o aplicativo e o SFA.",
                             Grupo = "Portal: próximos passos", Responsavel = "Comercial" },
            new SecaoDaAta { Titulo = "Regra de ruptura", Texto = "",
                             Grupo = "Portal: próximos passos", Responsavel = "Trade" },
            new SecaoDaAta { Titulo = "Sem itens nem texto", Texto = "" },
            new SecaoDaAta { Titulo = "Próxima reunião", Texto = "A definir." },
        ],
        Decisoes =
        [
            new ItemDaAta("Começar antes da integração com a Diana.", "Dashboard"),
            new ItemDaAta("Estudar critérios parecidos com os da Hiper.", "regra de RUPTURA "),
        ],
        Acoes =
        [
            new AcaoDaAta { Acao = "Dashboard e relatório de aderência", Responsavel = "Bruno Teles",
                            Prazo = "[prazo a definir]", Lado = "nosso", Secao = "Dashboard" },
            new AcaoDaAta { Acao = "Novo valor do pedido mínimo por canal", Responsavel = "Carla Nunes",
                            Prazo = "[prazo a definir]", Lado = "cliente", Secao = "Pedido mínimo" },
            new AcaoDaAta { Acao = "Carla Nunes levantar as regras da Hiper", Responsavel = "Carla Nunes",
                            Prazo = "sexta", Lado = "cliente", Secao = "Regra de ruptura" },
            new AcaoDaAta { Acao = "Mandar a pauta", Responsavel = "Diego Prado",
                            Prazo = "[prazo a definir]", Lado = "nosso", Secao = "Tema que não existe" },
        ],
        PontosEmAberto = [new ItemDaAta("Como implementar a soma.", "Pedido mínimo")],
        Riscos =
        [
            new ItemDaAta("O planner atualiza às quartas.", "Pedido mínimo"),
            new ItemDaAta("Duas semanas de sugestão perdidas em julho."),
        ],
        Observacoes = ["Números citados na reunião que não aparecem nesta ata: 37."],
    };
```

- [ ] **Step 2: Escrever os testes que falham**

```csharp
    private static string PorTema(string tipo = "cliente-update") =>
        Md(tipo, FormatoDaAta.PorTema, ReuniaoDeExemplo.AtaComSecao());

    private static string Bloco(string md, string titulo)
    {
        int i = md.IndexOf(titulo, StringComparison.Ordinal);
        Assert.True(i >= 0, $"não achei {titulo}");
        int fim = md.IndexOf("\n#", i + titulo.Length, StringComparison.Ordinal);
        return fim < 0 ? md[i..] : md[i..fim];
    }

    [Fact]
    public void OsItensFicamDentroDoTema()
    {
        string pedido = Bloco(PorTema(), "### Pedido mínimo · Comercial");
        Assert.Contains("A regra nova soma o aplicativo e o SFA.", pedido);
        Assert.Contains("**Atenção:** O planner atualiza às quartas.", pedido);
        Assert.Contains("- [ ] Novo valor do pedido mínimo por canal — **Carla Nunes**", pedido);
        Assert.Contains("**Em aberto:** Como implementar a soma.", pedido);
    }

    [Fact]
    public void AOrdemDentroDoTemaEFixa()
    {
        string pedido = Bloco(PorTema(), "### Pedido mínimo");
        int atencao = pedido.IndexOf("**Atenção:**", StringComparison.Ordinal);
        int acao = pedido.IndexOf("- [ ]", StringComparison.Ordinal);
        int aberto = pedido.IndexOf("**Em aberto:**", StringComparison.Ordinal);
        Assert.True(atencao < acao && acao < aberto);

        string dash = Bloco(PorTema(), "## Dashboard");
        Assert.True(dash.IndexOf("**Decidido:**", StringComparison.Ordinal)
                    < dash.IndexOf("- [ ]", StringComparison.Ordinal));
    }

    [Fact]
    public void OSecaoCasaSemLigarParaCaixaNemEspaco()
    {
        // O modelo devolveu "regra de RUPTURA " para a seção "Regra de ruptura".
        Assert.Contains("**Decidido:** Estudar critérios parecidos com os da Hiper.",
                        Bloco(PorTema(), "### Regra de ruptura · Trade"));
    }

    [Fact]
    public void OGrupoViraTituloComOsTemasDentro()
    {
        string md = PorTema();
        int grupo = md.IndexOf("## Portal: próximos passos", StringComparison.Ordinal);
        Assert.True(grupo > md.IndexOf("## Dashboard", StringComparison.Ordinal));
        Assert.True(md.IndexOf("### Pedido mínimo", StringComparison.Ordinal) > grupo);
        Assert.True(md.IndexOf("### Regra de ruptura", StringComparison.Ordinal) > grupo);
    }

    [Fact]
    public void ItemSemTemaVaiParaAListaGeralENaoSome()
    {
        string md = PorTema();
        Assert.Contains("- [ ] Mandar a pauta — **Diego Prado**", Bloco(md, "## Outras pendências"));
        Assert.Contains("- Duas semanas de sugestão perdidas em julho.", Bloco(md, "## Riscos e alertas"));
    }

    [Fact]
    public void NenhumItemSome()
    {
        var ata = ReuniaoDeExemplo.AtaComSecao();
        string md = PorTema();
        foreach (var d in ata.Decisoes) Assert.Contains(d.Texto, md);
        foreach (var p in ata.PontosEmAberto) Assert.Contains(p.Texto, md);
        foreach (var r in ata.Riscos) Assert.Contains(r.Texto, md);
        Assert.Equal(ata.Acoes.Count, md.Split('\n').Count(l => l.StartsWith("- [ ]", StringComparison.Ordinal)));
    }

    [Fact]
    public void QuemFicouComOQueSeparaPorLado()
    {
        string quem = Bloco(PorTema(), "## Quem ficou com o quê");
        Assert.Contains("Do nosso lado: **Bruno Teles**: Dashboard · **Diego Prado**: Outras pendências", quem);
        Assert.Contains("Do lado do cliente: **Carla Nunes**: Pedido mínimo, Regra de ruptura", quem);
        Assert.DoesNotContain("### Do nosso lado", PorTema());
    }

    [Fact]
    public void ORodapeDoEsqueletoFechaAAta()
    {
        string md = PorTema();
        Assert.True(md.IndexOf("## Próxima reunião", StringComparison.Ordinal)
                    > md.IndexOf("## Quem ficou com o quê", StringComparison.Ordinal));
        Assert.DoesNotContain("Sem itens nem texto", md);
    }

    [Fact]
    public void TipoSemMarcaIgnoraASecao()
    {
        string md = Md("daily", FormatoDaAta.PorTema, ReuniaoDeExemplo.AtaComSecao());
        Assert.DoesNotContain("**Decidido:**", md);
        Assert.DoesNotContain("Quem ficou com o quê", md);
        Assert.Contains("## Decisões", md);
    }

    [Fact]
    public void ClassicoIgnoraASecaoMesmoComMarca()
    {
        string md = Md("cliente-update", FormatoDaAta.Classico, ReuniaoDeExemplo.AtaComSecao());
        Assert.DoesNotContain("**Decidido:**", md);
        Assert.Contains("### Do nosso lado", md);
        Assert.Contains("Observações sobre a transcrição", md);
    }

    [Fact]
    public void OTrabalhoMantemORodapeDoEsqueletoDele()
    {
        // Tipo marcado com outro esqueleto: "Referências citadas" vem depois de
        // "Ações" no trabalho.md, então é rodapé, e fecha a ata.
        var ata = ReuniaoDeExemplo.AtaComSecao();
        ata.Secoes.Add(new SecaoDaAta { Titulo = "Referências citadas", Texto = "O chamado 123." });
        string md = Md("trabalho", FormatoDaAta.PorTema, ata);
        Assert.True(md.IndexOf("## Referências citadas", StringComparison.Ordinal)
                    > md.IndexOf("## Quem ficou com o quê", StringComparison.Ordinal));
    }
```

- [ ] **Step 3: Rodar e ver falhar**

Run: `dotnet test app-net/Tests/MeetingApp.Tests.csproj --filter AtaPorTemaTests`
Expected: FAIL nos onze testes novos (as asserções sobre "Decidido", "Quem ficou com o quê" etc.).

- [ ] **Step 4: Implementar**

Em `RedatorDeAta.Escrever`, logo depois do Resumo, desviar para o caminho novo quando agrupa:

```csharp
        if (PromptDeAta.Agrupa(tipo, formato))
        {
            PorTema(sb, ata, tipo);
            return sb.ToString().TrimEnd() + "\n";
        }
```

E acrescentar à classe:

```csharp
    // ─────────────────────────────────────────────── a ata por tema (ATA-5)

    /// <summary>
    /// O corpo da ata com cada item dentro do tema dele.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A ata clássica é organizada pelo tipo de informação, e cada tema
    /// reaparece em cada seção — o pedido mínimo, em sete lugares na de
    /// 22/09/2026. Aqui o tema é a unidade, e a decisão, a ação e o ponto em
    /// aberto moram dentro dele.
    /// </para>
    /// <para>
    /// <b>O item que não casa com nenhum tema não some:</b> vai para a lista
    /// geral, sob os títulos de sempre. Fora do lugar é defeito visível; perdido
    /// é o pior defeito de uma ata (ver <see cref="SecaoDobrada"/>).
    /// </para>
    /// </remarks>
    private static void PorTema(StringBuilder sb, AtaGerada ata, ModeloDeAta tipo)
    {
        var rodape = Rodape(tipo);
        var corpo = ata.Secoes
            .Where(s => !EhCanonica(s.Titulo) && !rodape.Any(r => Parecido(s.Titulo, r)))
            .ToList();

        // O dono de cada item: a primeira seção do corpo que casa com o "secao".
        SecaoDaAta? Casa(string? secao) =>
            secao is { Length: > 0 } ? corpo.FirstOrDefault(s => Parecido(s.Titulo, secao)) : null;

        var decisoes = ata.Decisoes.ToLookup(i => Casa(i.Secao));
        var riscos = ata.Riscos.ToLookup(i => Casa(i.Secao));
        var acoes = ata.Acoes.Where(a => a.Acao.Trim().Length > 0).ToLookup(a => Casa(a.Secao));
        var abertos = ata.PontosEmAberto.ToLookup(i => Casa(i.Secao));

        // Grupos juntos, na posição do primeiro membro.
        var vistos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var s in corpo)
        {
            if (s.Grupo is not { Length: > 0 } grupo)
            {
                TemaComItens(sb, s, 2, decisoes[s], riscos[s], acoes[s], abertos[s]);
                continue;
            }
            if (!vistos.Add(grupo.Trim())) continue;

            var membros = corpo.Where(m => string.Equals(m.Grupo?.Trim(), grupo.Trim(),
                                                         StringComparison.OrdinalIgnoreCase)).ToList();
            if (!membros.Any(m => TemConteudo(m, decisoes[m], riscos[m], acoes[m], abertos[m])))
                continue;
            sb.AppendLine($"## {grupo.Trim()}");
            sb.AppendLine();
            foreach (var m in membros)
                TemaComItens(sb, m, 3, decisoes[m], riscos[m], acoes[m], abertos[m]);
        }

        // A sobra, sob os títulos de sempre.
        Lista(sb, "Decisões", Textos(decisoes[null]));
        bool algumaAninhada = acoes.Any(g => g.Key is not null);
        var soltas = acoes[null].ToList();
        if (soltas.Count > 0)
        {
            sb.AppendLine(algumaAninhada ? "## Outras pendências" : "## Pendências");
            sb.AppendLine();
            foreach (var a in soltas) sb.AppendLine(Linha(a, limpo: true));
            sb.AppendLine();
        }
        Lista(sb, "Pontos em aberto", Textos(abertos[null]));
        Lista(sb, "Riscos e alertas", Textos(riscos[null]));

        if (algumaAninhada) QuemFicouComOQue(sb, ata.Acoes, Casa);

        foreach (string r in rodape)
            if (ata.Secoes.FirstOrDefault(s => Parecido(s.Titulo, r)) is { } achada
                && achada.Texto.Trim().Length > 0)
                Secao(sb, achada);
    }

    /// <summary>
    /// As seções do esqueleto que fecham a ata: não canônicas, depois da
    /// primeira canônica que não é o Resumo.
    /// </summary>
    /// <remarks>
    /// "Próxima reunião" no update, "Referências citadas" na sessão de trabalho,
    /// "Definições e glossário" no kickoff. Sem isto elas sairiam no meio dos
    /// temas, porque o corpo segue a ordem do modelo.
    /// </remarks>
    private static List<string> Rodape(ModeloDeAta tipo)
    {
        var secoes = tipo.Secoes;
        int primeira = -1;
        for (int i = 0; i < secoes.Count; i++)
            if (Canonizar(secoes[i]) is { } c && c != "Resumo") { primeira = i; break; }
        return primeira < 0 ? [] : [.. secoes.Skip(primeira).Where(s => !EhCanonica(s))];
    }

    private static bool TemConteudo(SecaoDaAta s, IEnumerable<ItemDaAta> d, IEnumerable<ItemDaAta> r,
                                    IEnumerable<AcaoDaAta> a, IEnumerable<ItemDaAta> o) =>
        s.Texto.Trim().Length > 0 || d.Any() || r.Any() || a.Any() || o.Any();

    private static void TemaComItens(StringBuilder sb, SecaoDaAta s, int nivel,
                                     IEnumerable<ItemDaAta> decisoes, IEnumerable<ItemDaAta> riscos,
                                     IEnumerable<AcaoDaAta> acoes, IEnumerable<ItemDaAta> abertos)
    {
        if (!TemConteudo(s, decisoes, riscos, acoes, abertos)) return;

        string dono = s.Responsavel is { Length: > 0 } r ? $" · {r.Trim()}" : "";
        sb.AppendLine($"{new string('#', nivel)} {s.Titulo.Trim()}{dono}");
        sb.AppendLine();
        if (s.Situacao is { Length: > 0 } situacao)
        {
            sb.AppendLine($"**Situação:** {situacao.Trim()}");
            sb.AppendLine();
        }
        if (s.Texto.Trim().Length > 0)
        {
            sb.AppendLine(s.Texto.Trim());
            sb.AppendLine();
        }
        foreach (var d in decisoes) Rotulado(sb, "Decidido", d.Texto);
        foreach (var r2 in riscos) Rotulado(sb, "Atenção", r2.Texto);
        var lista = acoes.ToList();
        if (lista.Count > 0)
        {
            foreach (var a in lista) sb.AppendLine(Linha(a, limpo: true));
            sb.AppendLine();
        }
        foreach (var o in abertos) Rotulado(sb, "Em aberto", o.Texto);
    }

    /// <remarks>
    /// Negrito no começo do parágrafo, e não um título: é o que o
    /// <c>renderizar()</c> da tela e o <c>Exportacao.DocxDaAta</c> já entendem.
    /// </remarks>
    private static void Rotulado(StringBuilder sb, string rotulo, string texto)
    {
        if (texto.Trim().Length == 0) return;
        sb.AppendLine($"**{rotulo}:** {texto.Trim()}");
        sb.AppendLine();
    }

    /// <summary>
    /// Quem ficou com o quê, por lado, com os nomes dos temas.
    /// </summary>
    /// <remarks>
    /// Substitui as listas "Do nosso lado / Do lado do cliente" quando as ações
    /// já estão dentro dos temas. A skill chama a separação por lado de "a razão
    /// de existir" da ata de update — ela continua, sem repetir o texto de cada
    /// ação.
    /// </remarks>
    private static void QuemFicouComOQue(StringBuilder sb, IEnumerable<AcaoDaAta> acoes,
                                         Func<string?, SecaoDaAta?> casa)
    {
        sb.AppendLine("## Quem ficou com o quê");
        sb.AppendLine();
        foreach (var (lado, rotulo) in new[] { ("nosso", "Do nosso lado"), ("cliente", "Do lado do cliente") })
        {
            var doLado = acoes.Where(a => a.Acao.Trim().Length > 0
                                          && (lado == "cliente") == EhDoCliente(a)).ToList();
            if (doLado.Count == 0) continue;

            var pessoas = doLado
                .GroupBy(a => a.Responsavel.Trim() is { Length: > 0 } d ? d : "[responsável a definir]")
                .Select(g => $"**{g.Key}**: " + string.Join(", ", g
                    .GroupBy(a => casa(a.Secao)?.Titulo.Trim() ?? "Outras pendências")
                    .Select(t => t.Count() > 1 ? $"{t.Key} ({t.Count()})" : t.Key)));
            sb.AppendLine($"{rotulo}: {string.Join(" · ", pessoas)}");
            sb.AppendLine();
        }
    }
```

`ToLookup` com chave nula: `ILookup<SecaoDaAta?, T>` aceita `null` como chave e `lookup[null]` devolve o grupo; uma chave sem itens devolve sequência vazia. Se o compilador reclamar da nulidade da chave, usar `.ToLookup(i => (object?)Casa(i.Secao) ?? Sobra)` com `private static readonly object Sobra = new();` e consultar `lookup[Sobra]`.

- [ ] **Step 5: Rodar a suíte inteira**

Run: `dotnet test app-net/Tests/MeetingApp.Tests.csproj`
Expected: PASS, com as referências clássicas verdes.

- [ ] **Step 6: Olhar a saída uma vez**

Escrever um teste temporário que imprime `PorTema()` (`ITestOutputHelper`), rodar com `--logger "console;verbosity=detailed"`, ler o Markdown inteiro e conferir que ele tem a cara da camada "Redator + prompt" do protótipo. Apagar o teste temporário antes do commit.

- [ ] **Step 7: Commit**

```bash
git add app-net/Nucleo/Atas/RedatorDeAta.cs app-net/Tests
git commit -m "feat(ata): o redator escreve cada item dentro do tema dele (ATA-5)"
```

---

### Task 7: A geração grava o formato escolhido

**Files:**
- Modify: `app-net/Nucleo/Atas/GeracaoDeAta.cs`
- Modify: `app-net/Nucleo/Atas/MotorDeAta.cs` (`GerarAsync`, `PedirAsync(porta, prompt, ct)`)
- Modify: `app-net/Tests/GeracaoDeAtaTests.cs` (os motores falsos ganham o parâmetro `esquema`)
- Test: `app-net/Tests/GeracaoDeAtaTests.cs`

**Interfaces:**
- Consumes: `PromptDeAta.Montar(…, formato)`, `PromptDeAta.Agrupa`, `AtaGerada.EsquemaPara`, `RedatorDeAta.Escrever(…, formato)`, `RedatorDeAta.Observacoes` (Tarefas 4 a 6).
- Produces:
  - `GeracaoDeAta.Motor`: `delegate Task<AtaGerada> Motor(string prompt, string esquema, double duracaoS, Action<ProgressoDaAta>? progresso, CancellationToken ct)`;
  - `GeracaoDeAta.GerarAsync(string pasta, ModeloDeAta tipo, ResultadoDaTranscricao dados, ContextoDaReuniao ctx, Motor motor, Action<ProgressoDaAta>? progresso, CancellationToken ct, FormatoDaAta formato = FormatoDaAta.Classico)`;
  - `GeracaoDeAta.ArquivoDeObservacoes = "ata.observacoes.md"`, `GeracaoDeAta.ArquivoAnterior = "ata.anterior.md"`;
  - `GeracaoDeAta.LerObservacoes(string pasta): string?`;
  - `MotorDeAta.GerarAsync(string prompt, string esquema, double duracaoS, Action<ProgressoDaAta>? progresso = null, CancellationToken ct = default)`.

- [ ] **Step 1: Escrever os testes que falham**

Em `GeracaoDeAtaTests`, primeiro atualizar os motores falsos existentes de `(prompt, duracao, progresso, _)` para `(prompt, _, duracao, progresso, _)`. Depois acrescentar:

```csharp
    private static GeracaoDeAta.Motor Devolve(AtaGerada ata, Action<string>? viuEsquema = null) =>
        (_, esquema, _, _, _) => { viuEsquema?.Invoke(esquema); return Task.FromResult(ata); };

    private static AtaGerada ComObservacao() => new()
    {
        Resumo = "r",
        Acoes = [new AcaoDaAta { Acao = "subir o Kubernetes", Responsavel = "Ana Lima",
                                 Prazo = "sexta", Lado = "nosso", Secao = "Infra" }],
        Secoes = [new SecaoDaAta { Titulo = "Infra", Texto = "Kubernetes na sexta." }],
    };

    [Fact]
    public async Task PorTemaMandaOEsquemaNovoEGravaAsObservacoesAParte()
    {
        var tipo = ModelosDeAta.Buscar("cliente-update")!;
        var ctx = GeracaoDeAta.Contexto(_pasta, Dados(), ["casa.com"], null);
        string? esquema = null;

        await GeracaoDeAta.GerarAsync(_pasta, tipo, Dados(), ctx, Devolve(ComObservacao(), e => esquema = e),
                                      null, CancellationToken.None, FormatoDaAta.PorTema);

        Assert.Equal(AtaGerada.EsquemaPorTema, esquema);
        string md = File.ReadAllText(Path.Combine(_pasta, "ata.md"));
        Assert.DoesNotContain("Observações sobre a transcrição", md);
        // O verificador sempre escreve alguma observação nesta reunião? Se não
        // escrever, o arquivo não existe — e as duas coisas valem.
        string? obs = GeracaoDeAta.LerObservacoes(_pasta);
        if (obs is not null) Assert.StartsWith("## Observações sobre a transcrição", obs);
    }

    [Fact]
    public async Task ClassicoMandaOEsquemaDeHojeEApagaObservacoesVelhas()
    {
        var tipo = ModelosDeAta.Buscar("cliente-update")!;
        var ctx = GeracaoDeAta.Contexto(_pasta, Dados(), ["casa.com"], null);
        File.WriteAllText(Path.Combine(_pasta, GeracaoDeAta.ArquivoDeObservacoes), "velho");
        string? esquema = null;

        await GeracaoDeAta.GerarAsync(_pasta, tipo, Dados(), ctx, Devolve(ComObservacao(), e => esquema = e),
                                      null, CancellationToken.None, FormatoDaAta.Classico);

        Assert.Equal(AtaGerada.Esquema, esquema);
        Assert.False(File.Exists(Path.Combine(_pasta, GeracaoDeAta.ArquivoDeObservacoes)));
        Assert.Null(GeracaoDeAta.LerObservacoes(_pasta));
    }

    [Fact]
    public async Task GerarDeNovoGuardaAAtaAnterior()
    {
        var tipo = ModelosDeAta.Buscar("cliente-update")!;
        var ctx = GeracaoDeAta.Contexto(_pasta, Dados(), ["casa.com"], null);

        await GeracaoDeAta.GerarAsync(_pasta, tipo, Dados(), ctx, Devolve(ComObservacao()),
                                      null, CancellationToken.None, FormatoDaAta.Classico);
        string primeira = File.ReadAllText(Path.Combine(_pasta, "ata.md"));
        Assert.False(File.Exists(Path.Combine(_pasta, GeracaoDeAta.ArquivoAnterior)));

        await GeracaoDeAta.GerarAsync(_pasta, tipo, Dados(), ctx, Devolve(ComObservacao()),
                                      null, CancellationToken.None, FormatoDaAta.PorTema);
        string segunda = File.ReadAllText(Path.Combine(_pasta, "ata.md"));
        Assert.Equal(primeira, File.ReadAllText(Path.Combine(_pasta, GeracaoDeAta.ArquivoAnterior)));

        await GeracaoDeAta.GerarAsync(_pasta, tipo, Dados(), ctx, Devolve(ComObservacao()),
                                      null, CancellationToken.None, FormatoDaAta.Classico);
        Assert.Equal(segunda, File.ReadAllText(Path.Combine(_pasta, GeracaoDeAta.ArquivoAnterior)));
    }
```

- [ ] **Step 2: Rodar e ver falhar**

Run: `dotnet test app-net/Tests/MeetingApp.Tests.csproj --filter GeracaoDeAtaTests`
Expected: erro de compilação (o `Motor` com cinco parâmetros, `ArquivoDeObservacoes`, `LerObservacoes`).

- [ ] **Step 3: Implementar**

Em `GeracaoDeAta.cs`:

```csharp
    /// <summary>O que o motor faz: prompt, esquema e duração entram, a ata sai.</summary>
    /// <remarks>
    /// O esquema virou parâmetro com a ata por tema (ATA-5): ele é parte do
    /// prompt — as descrições dos campos instruem o modelo —, e muda com o formato.
    /// </remarks>
    public delegate Task<AtaGerada> Motor(
        string prompt, string esquema, double duracaoS,
        Action<ProgressoDaAta>? progresso, CancellationToken ct);

    /// <summary>O recado do verificador, fora do <c>ata.md</c> no formato por tema.</summary>
    public const string ArquivoDeObservacoes = "ata.observacoes.md";

    /// <summary>O <c>ata.md</c> de antes de gerar de novo: comparar formatos é trocar a chave e refazer.</summary>
    public const string ArquivoAnterior = "ata.anterior.md";

    public static string? LerObservacoes(string pasta)
    {
        string caminho = Path.Combine(pasta, ArquivoDeObservacoes);
        try { return File.Exists(caminho) ? File.ReadAllText(caminho) : null; }
        catch (IOException) { return null; }
    }
```

`GerarAsync`:

```csharp
    public static async Task<AtaGerada> GerarAsync(
        string pasta, ModeloDeAta tipo, ResultadoDaTranscricao dados, ContextoDaReuniao ctx,
        Motor motor, Action<ProgressoDaAta>? progresso, CancellationToken ct,
        FormatoDaAta formato = FormatoDaAta.Classico)
    {
        var roteiro = RoteiroDeFatos.De(dados.Segments);
        string prompt = PromptDeAta.Montar(tipo, ctx, dados.Segments, roteiro, formato);
        string esquema = AtaGerada.EsquemaPara(PromptDeAta.Agrupa(tipo, formato));

        var ata = await motor(prompt, esquema, ctx.DuracaoS, progresso, ct);

        VerificadorDeAta.Conferir(ata, dados.Segments,
            [.. ctx.Convidados.Concat(ctx.Falantes)], roteiro, ctx.Pessoas);

        string md = Path.Combine(pasta, "ata.md");
        if (File.Exists(md))
            File.Copy(md, Path.Combine(pasta, ArquivoAnterior), overwrite: true);
        await File.WriteAllTextAsync(md, RedatorDeAta.Escrever(ata, tipo, ctx, formato), ct);

        // No clássico as observações estão dentro do ata.md; um arquivo à parte
        // de uma geração anterior por tema contradiria a ata nova.
        string obs = Path.Combine(pasta, ArquivoDeObservacoes);
        if (formato == FormatoDaAta.PorTema && RedatorDeAta.Observacoes(ata) is { } texto)
            await File.WriteAllTextAsync(obs, texto, ct);
        else if (File.Exists(obs))
            File.Delete(obs);

        await File.WriteAllTextAsync(Path.Combine(pasta, "ata.json"), ata.ParaJson(), ct);
        return ata;
    }
```

Em `MotorDeAta.cs`, `GerarAsync` ganha `string esquema` depois de `prompt`, e a chamada `PedirAsync(porta, prompt, ct)` vira `PedirAsync(porta, prompt, esquema, ct)`, com:

```csharp
    private static Task<string> PedirAsync(int porta, string prompt, string esquema, CancellationToken ct) =>
        PedirAsync(porta, PromptDeAta.Sistema, prompt, "ata", esquema, TokensDeSaida, ct);
```

- [ ] **Step 4: Compilar o resto da solução**

Run: `dotnet build app-net/App/MeetingApp.App.csproj 2>&1 | tail -5; dotnet build app-net/Cli/MeetingApp.Cli.csproj 2>&1 | tail -5`
(Conferir o nome do `.csproj` do app com `ls app-net/App/*.csproj`.) A `Ponte.GerarAta` e o `Cli/GeradorDeAta` passam `motor.GerarAsync` como grupo de método e continuam compilando: o delegate e o método ganharam o mesmo parâmetro na mesma posição.
Expected: os dois compilam. O app é Windows-only; se o build dele no Linux falhar por razão alheia a esta mudança (WebView2, WinForms), registrar e seguir: o `publicar.sh` da Tarefa 9 é a régua dele.

- [ ] **Step 5: Rodar a suíte inteira**

Run: `dotnet test app-net/Tests/MeetingApp.Tests.csproj`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add app-net/Nucleo/Atas app-net/Tests/GeracaoDeAtaTests.cs
git commit -m "feat(ata): a geração escolhe o formato, guarda a anterior e separa as observações"
```

---

### Task 8: Ponte, linha de comando e telas

**Files:**
- Modify: `app-net/App/Ponte.cs` (`Resposta` perto da linha 201; `case "ata"` perto da 816; `GerarAta` perto da 2283)
- Modify: `app-net/Cli/GeradorDeAta.cs`, `app-net/Cli/Program.cs:44`
- Modify: `app-net/App/web/configuracoes.js` (`ABAS`, `desenharPainel`, nova `abaAtas`)
- Modify: `app-net/App/web/atas.js` (`carregarAta`)
- Modify: `app-net/App/web/app.css` (perto da linha 2187, `.ata__texto`)

**Interfaces:**
- Consumes: `ConfiguracoesDoApp.Formato`, `GeracaoDeAta.GerarAsync(…, formato)`, `GeracaoDeAta.LerObservacoes` (Tarefas 3 e 7).
- Produces: a resposta da operação `ata` ganha `observacoes: string|null`; o CLI aceita `--formato classico|por-tema`.

- [ ] **Step 1: A ponte**

Em `Resposta`, perto de `Ata`:

```csharp
    /// <summary>O <c>ata.observacoes.md</c>, quando a ata foi gerada por tema.</summary>
    [JsonPropertyName("observacoes")] public string? Observacoes { get; init; }
```

No `case "ata"`, acrescentar ao `new Resposta`:

```csharp
                        Observacoes = GeracaoDeAta.LerObservacoes(onde),
```

Em `GerarAta`, a chamada passa o formato:

```csharp
                await GeracaoDeAta.GerarAsync(pasta, tipo, dados, ctx, motor.GerarAsync, e =>
                {
                    _transcricoes.Progredir(pasta, e.Etapa, e.Fracao, e.Texto);
                    EmpurrarTranscricoes();
                }, trabalho.Token, cfg.Formato);
```

- [ ] **Step 2: A linha de comando**

`GeradorDeAta.ExecutarAsync` ganha `FormatoDaAta formato` depois de `modelo`. Usar `formato` em `PromptDeAta.Montar(tipo, ctx, dados.Segments, roteiro, formato)` (só imprime o tamanho) e em `GeracaoDeAta.GerarAsync(…, ct, formato)`. Acrescentar a linha `Console.WriteLine($"formato  {(formato == FormatoDaAta.PorTema ? "por tema" : "clássico")}");` junto do `tipo`. Em `Program.cs`:

```csharp
        return await GeradorDeAta.ExecutarAsync(
            pastaDaAta, arg.GetValueOrDefault("tipo") ?? "cliente-update",
            arg.GetValueOrDefault("modelo"),
            // Sem --formato vale o do app.json, como no app: a linha de comando
            // mede a ata que o usuário veria.
            arg.GetValueOrDefault("formato") is { Length: > 0 } f
                ? FormatosDaAta.Ler(f)
                : ConfiguracoesDoApp.Carregar().Formato,
            ataCts.Token);
```

Conferir que `Program.cs` tem `using MeetingApp.Nucleo.Atas;` e `using MeetingApp.Nucleo;`.

- [ ] **Step 3: A aba Atas em Ajustes**

Em `configuracoes.js`, `ABAS` ganha `["atas", "Atas"]` entre `transcricao` e `clientes`, e `desenharPainel` ganha `atas: () => abaAtas(config, gravar),`. A função, antes de `abaTranscricao`:

```js
// ─────────────────────────────────────────────────────────── aba Atas

/**
 * O formato da ata (ATA-5). A aba nasce com uma linha e recebe os modelos de
 * ata editáveis depois (spec da ata por tema, §8).
 *
 * Rádio, como o tema: são dois formatos, e nenhum é "desligado".
 */
function abaAtas(config, gravar) {
  const painel = document.createElement("div");
  painel.className = "painel";

  const OPCOES = [
    ["por-tema", "Por tema"],
    ["classico", "De hoje"],
  ];
  const atual = config.formato_da_ata === "classico" ? "classico" : "por-tema";

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
  return painel;
}
```

- [ ] **Step 4: As observações na tela da ata**

Em `atas.js`, `carregarAta`, depois de `corpo.appendChild(texto);`:

```js
  // As observações do verificador, quando a ata é por tema (ATA-5): moram fora
  // do ata.md para não irem ao cliente, e ficam aqui, recolhidas, para quem
  // gerou conferir números e compromissos.
  if (r.observacoes) {
    const obs = document.createElement("details");
    obs.className = "ata__observacoes";
    const titulo = document.createElement("summary");
    titulo.textContent = "Observações sobre a transcrição";
    const dentro = document.createElement("div");
    dentro.className = "ata__texto";
    // O arquivo começa com o próprio título; o <summary> já o diz.
    dentro.append(...renderizar(r.observacoes.replace(/^## .*\n+/, "")));
    obs.append(titulo, dentro);
    corpo.appendChild(obs);
  }
```

Em `app.css`, depois do bloco `.ata__texto`:

```css
.ata__observacoes { margin-top: var(--espaco-4); border-top: 1px solid var(--cor-borda); padding-top: var(--espaco-3); }
.ata__observacoes summary { cursor: pointer; font-size: var(--texto-pequeno); color: var(--cor-texto-suave); }
.ata__observacoes .ata__texto { margin-top: var(--espaco-2); }
```

- [ ] **Step 5: Conferir a sintaxe e compilar**

Run: `node --check app-net/App/web/configuracoes.js && node --check app-net/App/web/atas.js` (são módulos: se o `node --check` reclamar de `import`, usar `node --input-type=module --check < arquivo`).
Run: `dotnet build app-net/Cli/MeetingApp.Cli.csproj 2>&1 | tail -3` e a suíte inteira.
Expected: sem erro; suíte PASS.

- [ ] **Step 6: Commit**

```bash
git add app-net/App app-net/Cli
git commit -m "feat(ata): aba Atas em Ajustes, observações na tela e --formato na linha de comando"
```

---

### Task 9: Documentação

**Files:**
- Modify: `docs/BACKLOG.md` (tema 2, depois de `ATA-4`)
- Modify: `docs/ATA.md` §3 (o bloco do esquema)
- Modify: `docs/superpowers/specs/2026-10-08-ata-por-tema-design.md` (estado)

- [ ] **Step 1: O item no backlog**

Depois do `ATA-4` em `docs/BACKLOG.md`:

```markdown
### ATA-5 · A ata por tema — `feature` · em validação

A ata de update de 22/09/2026 voltou do cliente com "ficou extensa e
repetitiva": organizada pelo tipo de informação, ela repetia cada tema em até
sete lugares. Agora cada decisão, ação, ponto em aberto e risco mora dentro do
tema, e as observações do verificador ficam fora do arquivo exportado. A chave
`formato_da_ata` em Ajustes › Atas volta ao formato de antes, byte a byte.

Desenho e critérios: [spec](superpowers/specs/2026-10-08-ata-por-tema-design.md).
**Falta:** a validação com o modelo de verdade (spec §7.2), que decide se o
padrão fica em "por tema".
```

- [ ] **Step 2: O esquema no ATA.md**

No bloco de código do §3 de `docs/ATA.md`, trocar por:

```
resumo            texto
secoes[]          { titulo, situacao?, texto, grupo?, responsavel? }
decisoes[]        { texto, secao? }                    ← "secao" desde a ATA-5
acoes[]           { acao, responsavel, prazo, lado, secao? }
pontos_em_aberto[] { texto, secao? }
riscos[]          { texto, secao? }
observacoes[]     texto                                ← o que o verificador escreveu
```

e acrescentar, logo abaixo do bloco: *"Desde a ATA-5 (08/10/2026), `secao` diz em
qual seção do corpo o item mora, e o redator o escreve lá dentro nos tipos com a
marca `<!-- ata: itens-por-secao -->`. As listas antigas, em `string[]`, ainda
abrem. Ver a [spec](superpowers/specs/2026-10-08-ata-por-tema-design.md)."*

- [ ] **Step 3: Commit**

```bash
git add docs
git commit -m "docs(ata): ATA-5 no backlog e o esquema novo no ATA.md"
```

---

### Task 10: Validar com o modelo de verdade — **só com o dono autorizando**

**Antes de qualquer passo: perguntar ao dono se a placa está livre agora.** Ele tem reuniões, e a mesma RTX 2060 roda a legenda ao vivo delas. Sem um "pode" dito na hora, esta tarefa não começa. Com o "pode", conferir de novo com `nvidia-smi.exe` (via `/mnt/c/Windows/System32/nvidia-smi.exe`) que nenhum processo do PulseMeet está usando a placa.

**Files:**
- Create: `tools/validar_ata_por_tema.py`

- [ ] **Step 1: Montar o CLI**

Run:
```bash
export PATH="$HOME/.dotnet:$PATH"
dotnet publish app-net/Cli/MeetingApp.Cli.csproj -c Release -r win-x64 --self-contained \
  -p:PublishSingleFile=true -o /mnt/c/Users/andre/ata-por-tema-validacao/cli
```
Expected: `Sidecar.exe` em `C:\Users\andre\ata-por-tema-validacao\cli`. (A regra "nunca `dotnet publish` na mão" do CLAUDE.md é do app entregue ao usuário; este é o binário de medição, que nunca sai da máquina.)

- [ ] **Step 2: Escrever a ferramenta**

`tools/validar_ata_por_tema.py`:

```python
"""A ata por tema contra a clássica, com o modelo de verdade (ATA-5, spec §7.2).

Roda o Sidecar.exe --ata nos dois formatos sobre CÓPIAS das reuniões — o CLI
escreve ata.md na pasta que recebe, e a do acervo é a do dono — e mede os quatro
critérios da spec. Uso:

    uv run python tools/validar_ata_por_tema.py 2026-09-22_10-59-53:cliente-update ...
"""
import json
import os
import re
import shutil
import statistics
import subprocess
import sys
import unicodedata
from pathlib import Path

ACERVO = Path("/mnt/c/Users/andre/OneDrive/Documents/MeetingRecordings")
BASE = Path("/mnt/c/Users/andre/ata-por-tema-validacao")
CLI = BASE / "cli" / "Sidecar.exe"
MOTOR = r"C:\Users\andre\AppData\Local\Programs\MeetingApp\motores\ata"
MARCADOS = {"cliente-update", "trabalho", "kickoff"}


def win(p: Path) -> str:
    return subprocess.run(["wslpath", "-w", str(p)], capture_output=True, text=True).stdout.strip()


def normalizar(t: str) -> str:
    """A mesma régua do RedatorDeAta.Normalizar."""
    t = unicodedata.normalize("NFD", (t or "").lower())
    t = "".join(c for c in t if unicodedata.category(c) != "Mn")
    return "".join(c for c in t if c.isalnum() or c == " ").strip()


def parecido(a: str, b: str) -> bool:
    a, b = normalizar(a), normalizar(b)
    return bool(a and b) and (a == b or a.startswith(b) or b.startswith(a))


def palavras(md: str) -> int:
    # O que vai ao cliente: sem a seção de observações, que no clássico está dentro.
    md = md.split("## Observações sobre a transcrição")[0]
    return len(re.findall(r"[\wÀ-ú%]+", md))


def gerar(copia: Path, tipo: str, formato: str, saida: Path) -> dict:
    env = dict(os.environ, MEETINGAPP_MOTOR_ATA=MOTOR, WSLENV="MEETINGAPP_MOTOR_ATA")
    proc = subprocess.run([str(CLI), "--ata", win(copia), "--tipo", tipo, "--formato", formato],
                          capture_output=True, text=True, encoding="utf-8", errors="replace", env=env)
    if proc.returncode != 0:
        raise RuntimeError(f"{copia.name} {formato}: {proc.stdout}{proc.stderr}")
    for nome in ("ata.md", "ata.json", "ata.observacoes.md"):
        if (copia / nome).is_file():
            shutil.copy(copia / nome, saida / f"{copia.name}__{formato}__{nome}")
    md = (copia / "ata.md").read_text(encoding="utf-8")
    return {"md": md, "json": json.loads((copia / "ata.json").read_text(encoding="utf-8"))}


def itens(ata: dict) -> list[str | None]:
    """O 'secao' de cada decisão, ação, ponto e risco."""
    out = []
    for campo in ("decisoes", "pontos_em_aberto", "riscos", "acoes"):
        for i in ata.get(campo, []):
            out.append(i.get("secao") if isinstance(i, dict) else None)
    return out


def main() -> int:
    pedidos = [a.split(":", 1) for a in sys.argv[1:]]
    if not pedidos:
        print(__doc__)
        return 2
    saida = BASE / "resultados"
    saida.mkdir(parents=True, exist_ok=True)

    medidas, casados, total, encolheu = [], 0, 0, []
    for nome, tipo in pedidos:
        copia = BASE / nome
        if not copia.exists():
            shutil.copytree(ACERVO / nome, copia, ignore=shutil.ignore_patterns("*.wav"))
        classico = gerar(copia, tipo, "classico", saida)
        tema = gerar(copia, tipo, "por-tema", saida)

        m = {"reuniao": nome, "tipo": tipo,
             "palavras_classico": palavras(classico["md"]),
             "palavras_tema": palavras(tema["md"])}
        m["proporcao"] = m["palavras_tema"] / max(1, m["palavras_classico"])
        secoes = tema["json"].get("secoes", [])
        m["secao_com_subtitulos"] = any("###" in (s.get("texto") or "") for s in secoes)
        if tipo in MARCADOS:
            titulos = [s.get("titulo", "") for s in secoes]
            ss = itens(tema["json"])
            ok = sum(1 for s in ss if s and any(parecido(s, t) for t in titulos))
            m["itens"], m["itens_no_tema"] = len(ss), ok
            casados, total = casados + ok, total + len(ss)
            encolheu.append(m["proporcao"])
        else:
            def h2(md: str) -> list[str]:
                return [l for l in md.splitlines() if l.startswith("## ")
                        and "Observações" not in l]
            m["mesma_estrutura"] = h2(classico["md"]) == h2(tema["md"])
        medidas.append(m)
        print(json.dumps(m, ensure_ascii=False))

    criterios = {
        "1_itens_no_tema": casados / max(1, total),
        "2_sem_subtitulos": not any(m["secao_com_subtitulos"] for m in medidas
                                    if m["tipo"] in MARCADOS),
        "3_mediana_proporcao": statistics.median(encolheu) if encolheu else None,
        "4_daily_igual": all(m.get("mesma_estrutura", True) for m in medidas),
    }
    passou = (criterios["1_itens_no_tema"] >= 0.8 and criterios["2_sem_subtitulos"]
              and (criterios["3_mediana_proporcao"] or 1) <= 0.75 and criterios["4_daily_igual"])
    (saida / "medidas.json").write_text(
        json.dumps({"medidas": medidas, "criterios": criterios, "passou": passou},
                   ensure_ascii=False, indent=2), encoding="utf-8")
    print(json.dumps(criterios, ensure_ascii=False), "PASSOU" if passou else "FALHOU")
    return 0 if passou else 1


if __name__ == "__main__":
    sys.exit(main())
```

- [ ] **Step 3: Escolher as sete reuniões, com o dono**

Propor ao dono: a de 22/09 (`2026-09-22_10-59-53`, `cliente-update`); dois updates e duas sessões de trabalho recentes do acervo (achar com `grep -l "^## Status por frente"` e `grep -l "^## Problema trabalhado"` nos `ata.md`); o kickoff `2026-09-15_14-01-30`; e uma daily (`grep -l "^## Por pessoa"`). Ele confirma ou troca.

- [ ] **Step 4: Rodar**

Run: `uv run python tools/validar_ata_por_tema.py <lista>`. São 14 gerações; cada uma leva de 1 a 3 minutos na 2060.

- [ ] **Step 5: Julgar pelos critérios da spec §7.2**

1. ≥ 80% dos itens das seis reuniões marcadas casam com uma seção;
2. nenhuma seção com `###` dentro;
3. mediana de palavras por tema ≤ 75% da clássica;
4. a daily com os mesmos títulos `##` nos dois formatos.

Se todos passam, o padrão fica `por-tema`. Se algum falha: trocar `FormatosDaAta.Padrao` e o `Ler` para cair no clássico quando ausente, rodar a suíte, commitar, e levar as atas ao dono lado a lado **antes** de mexer no prompt.

- [ ] **Step 6: Mostrar ao dono**

Montar uma página de comparação como a do protótipo (`https://claude.ai/artifact/9VLKL5wqtmYN5Gr8GfuNeN`), agora com o que o modelo escreveu nas sete reuniões, e a tabela dos critérios. **Ela contém dado de cliente: fica privada e não vai para o repositório.**

- [ ] **Step 7: Commit da ferramenta e do resultado no backlog**

```bash
git add tools/validar_ata_por_tema.py docs/BACKLOG.md
git commit -m "tools(ata): validação da ata por tema com o modelo de verdade"
```

(Em `docs/BACKLOG.md`, o `ATA-5` passa a `feito` com os números medidos, ou fica `em validação` com o que falhou.)
