using System.Text.Json.Nodes;
using MeetingApp.Nucleo;
using MeetingApp.Nucleo.Claude;
using Xunit;

namespace MeetingApp.Tests;

/// <summary>
/// O acervo lido pelo Claude: a lista, a ata, a transcrição e a busca.
/// </summary>
/// <remarks>
/// Um acervo inventado em disco, com a forma do de verdade — <c>meta.json</c>,
/// <c>reuniao.json</c>, <c>transcricao.json</c>, <c>ata.md</c>, <c>notas.md</c>.
/// Nenhum nome de cliente de verdade: o repositório publica releases.
/// </remarks>
public sealed class AcervoTests : IDisposable
{
    private readonly string _raiz = Directory.CreateTempSubdirectory("acervo-testes").FullName;

    public void Dispose() => Directory.Delete(_raiz, recursive: true);

    internal static string Gravar(string raiz, string id, string? titulo = null, string? inicio = null,
                                  string? cliente = null, string? projeto = null,
                                  List<SegmentoFinal>? segmentos = null, string? ata = null,
                                  string? notas = null, double duracao = 600)
    {
        string pasta = Path.Combine(raiz, id);
        Directory.CreateDirectory(pasta);

        var meeting = new JsonObject { ["title"] = titulo, ["start"] = inicio,
                                       ["attendees"] = new JsonArray("Bruno Teles", "Carla Nunes") };
        File.WriteAllText(Path.Combine(pasta, "meta.json"),
            new JsonObject { ["duration_s"] = duracao, ["meeting"] = meeting }.ToJsonString());

        if (cliente is not null || projeto is not null)
            new DadosDaReuniao { Cliente = cliente, Projeto = projeto }.Salvar(pasta);
        if (segmentos is not null)
            File.WriteAllText(Path.Combine(pasta, "transcricao.json"),
                new ResultadoDaTranscricao { Language = "pt", Duration = duracao, Segments = segmentos }.ParaJson());
        if (ata is not null) File.WriteAllText(Path.Combine(pasta, "ata.md"), ata);
        if (notas is not null) Notas.Salvar(pasta, notas);
        return pasta;
    }

    internal static SegmentoFinal S(double inicio, string? quem, string texto) =>
        new() { Start = inicio, End = inicio + 2, Speaker = quem, Text = " " + texto };

    [Fact]
    public void ListaDaMaisAntigaParaAMaisNovaComOVinculo()
    {
        Gravar(_raiz, "2026-09-02_10-00-00", "Semanal", cliente: "Acme Bebidas", projeto: "Portal");
        Gravar(_raiz, "2026-08-20_15-00-00", "Kickoff", cliente: "Acme Bebidas");
        Gravar(_raiz, "2026-09-10_09-00-00", "Daily");

        var lista = new Acervo(_raiz).Listar();

        Assert.Equal(["2026-08-20_15-00-00", "2026-09-02_10-00-00", "2026-09-10_09-00-00"],
                     lista.Select(r => r.Id));
        Assert.Equal("Acme Bebidas", lista[1].Cliente);
        Assert.Equal("Portal", lista[1].Projeto);
        Assert.Null(lista[2].Cliente);
    }

    [Fact]
    public void AHoraDaAgendaVenceONomeDaPasta()
    {
        // O gravador começa antes da reunião; as pessoas falam "a das 9h".
        Gravar(_raiz, "2026-09-10_08-57-12", inicio: "2026-09-10T09:00:00-03:00");

        Assert.Equal(new DateTime(2026, 9, 10, 9, 0, 0), new Acervo(_raiz).Listar()[0].Quando);
    }

    [Fact]
    public void UmaGravacaoIlegivelNaoEscondeAsOutras()
    {
        Gravar(_raiz, "2026-09-02_10-00-00", "Boa");
        string ruim = Path.Combine(_raiz, "2026-09-03_10-00-00");
        Directory.CreateDirectory(ruim);
        File.WriteAllText(Path.Combine(ruim, "meta.json"), "{ isto não é json");
        Directory.CreateDirectory(Path.Combine(_raiz, "pasta-qualquer"));

        Assert.Equal(["2026-09-02_10-00-00"], new Acervo(_raiz).Listar().Select(r => r.Id));
    }

    [Fact]
    public void ClienteCasaPorPedacoSemAcentoNemCaixa()
    {
        Gravar(_raiz, "2026-09-01_10-00-00", cliente: "Acme Bebidas");
        Gravar(_raiz, "2026-09-02_10-00-00", cliente: "ACME Logística");
        Gravar(_raiz, "2026-09-03_10-00-00", cliente: "Outro");
        var acervo = new Acervo(_raiz);

        Assert.Equal(2, acervo.Listar(new FiltroDoAcervo(Cliente: "acme")).Count);
        Assert.Single(acervo.Listar(new FiltroDoAcervo(Cliente: "logistica")));
    }

    [Fact]
    public void DataFiltraOsDoisLadosInclusive()
    {
        Gravar(_raiz, "2026-09-01_10-00-00");
        Gravar(_raiz, "2026-09-05_10-00-00");
        Gravar(_raiz, "2026-09-09_10-00-00");

        var lista = new Acervo(_raiz).Listar(new FiltroDoAcervo(
            Desde: new DateOnly(2026, 9, 1), Ate: new DateOnly(2026, 9, 5)));

        Assert.Equal(["2026-09-01_10-00-00", "2026-09-05_10-00-00"], lista.Select(r => r.Id));
    }

    [Theory]
    [InlineData("..")]
    [InlineData("../2026-09-01_10-00-00")]
    [InlineData("sub/2026-09-01_10-00-00")]
    [InlineData(@"C:\Windows")]
    [InlineData("/etc")]
    [InlineData("")]
    public void OIdNaoViraCaminhoParaForaDoAcervo(string id)
    {
        Gravar(_raiz, "2026-09-01_10-00-00");

        Assert.Null(new Acervo(_raiz).Achar(id));
    }

    [Fact]
    public void OIdDaListaAchaAReuniao()
    {
        Gravar(_raiz, "2026-09-01_10-00-00", "Semanal");

        Assert.Equal("Semanal", new Acervo(_raiz).Achar(" 2026-09-01_10-00-00 ")?.Titulo);
    }

    [Fact]
    public void ClientesVemComOsProjetosEOPeriodo()
    {
        Gravar(_raiz, "2026-08-01_10-00-00", cliente: "Acme", projeto: "Portal");
        Gravar(_raiz, "2026-08-08_10-00-00", cliente: "Acme", projeto: "Portal");
        Gravar(_raiz, "2026-09-01_10-00-00", cliente: "acme", projeto: "Ruptura");
        Gravar(_raiz, "2026-09-02_10-00-00", cliente: "Outro");

        var clientes = new Acervo(_raiz).Clientes();

        Assert.Equal("Acme", clientes[0].Nome);
        Assert.Equal(3, clientes[0].Reunioes);
        Assert.Equal(new DateTime(2026, 8, 1, 10, 0, 0), clientes[0].Primeira);
        Assert.Equal(new DateTime(2026, 9, 1, 10, 0, 0), clientes[0].Ultima);
        Assert.Equal([("Portal", 2), ("Ruptura", 1)], clientes[0].Projetos);
    }

    [Fact]
    public void AsFalasJuntamOMesmoFalanteECortamNoMinuto()
    {
        var falas = Acervo.Falas(
        [
            S(0, "Bruno Teles", "Bom dia."), S(2, "Bruno Teles", "Vamos lá."),
            S(4, "Carla Nunes", "Oi."), S(5, "Carla Nunes", "  "),
            S(70, "Carla Nunes", "Ainda eu."),
        ]);

        Assert.Equal(3, falas.Count);
        Assert.Equal("Bom dia. Vamos lá.", falas[0].Texto);
        Assert.Equal("[01:10] Carla Nunes: Ainda eu.", Acervo.Linha(falas[2]));
    }

    [Fact]
    public void ATranscricaoSaiPorTrechoComCabecalho()
    {
        Gravar(_raiz, "2026-09-01_10-00-00", "Semanal", cliente: "Acme", projeto: "Portal",
               segmentos: [S(10, "Bruno Teles", "Antes."), S(130, "Carla Nunes", "Dentro."),
                           S(400, "Bruno Teles", "Depois.")]);
        var r = new Acervo(_raiz).Achar("2026-09-01_10-00-00")!;

        string texto = Acervo.TextoDaTranscricao(r, deS: 120, ateS: 300)!;

        Assert.Contains("# Semanal", texto);
        Assert.Contains("Acme › Portal", texto);
        Assert.Contains("Falaram: Bruno Teles, Carla Nunes", texto);
        Assert.Contains("[02:10] Carla Nunes: Dentro.", texto);
        Assert.DoesNotContain("Antes.", texto);
        Assert.DoesNotContain("Depois.", texto);
    }

    [Fact]
    public void ATranscricaoLongaParaNoTetoEDizDeOndeContinuar()
    {
        var segmentos = Enumerable.Range(0, 200)
            .Select(i => S(i * 61, i % 2 == 0 ? "Bruno Teles" : "Carla Nunes", new string('x', 100)))
            .ToList();
        Gravar(_raiz, "2026-09-01_10-00-00", segmentos: segmentos);
        var r = new Acervo(_raiz).Achar("2026-09-01_10-00-00")!;

        string texto = Acervo.TextoDaTranscricao(r, limite: 2000)!;

        Assert.True(texto.Length < 2200);
        Assert.Matches(@"\[a transcrição continua: peça de \d\d:\d\d em diante\]", texto);
    }

    [Fact]
    public void ReuniaoSemTranscricaoNaoTemTexto()
    {
        Gravar(_raiz, "2026-09-01_10-00-00");

        Assert.Null(Acervo.TextoDaTranscricao(new Acervo(_raiz).Achar("2026-09-01_10-00-00")!));
    }

    [Fact]
    public void AAtaVemComAsNotasSemAsImagens()
    {
        Gravar(_raiz, "2026-09-01_10-00-00", ata: "# Ata — Semanal\n\n## Resumo\n\nTudo certo.",
               notas: "- cobrar o valor por canal\n[00:12:34] ![print](capturas/print-1.png)");
        var r = new Acervo(_raiz).Achar("2026-09-01_10-00-00")!;

        string texto = Acervo.TextoDaAta(r)!;

        Assert.StartsWith("# Ata — Semanal", texto);
        Assert.Contains("## Notas de quem gravou", texto);
        Assert.Contains("cobrar o valor por canal", texto);
        Assert.DoesNotContain("capturas/", texto);
    }

    [Fact]
    public void AAtaAnteriorAUmaCorrecaoAvisa()
    {
        string pasta = Gravar(_raiz, "2026-09-01_10-00-00", segmentos: [S(0, "Bruno Teles", "Oi.")],
                              ata: "# Ata");
        File.SetLastWriteTimeUtc(Path.Combine(pasta, "ata.md"), DateTime.UtcNow.AddHours(-1));

        string texto = Acervo.TextoDaAta(new Acervo(_raiz).Achar("2026-09-01_10-00-00")!)!;

        Assert.StartsWith("> A transcrição foi corrigida depois", texto);
    }

    [Fact]
    public void ABuscaPedeTodasAsPalavrasNaMesmaFalaSemAcento()
    {
        Gravar(_raiz, "2026-09-01_10-00-00", cliente: "Acme",
               segmentos: [S(0, "Bruno Teles", "A régua de RUPTURA muda em outubro."),
                           S(10, "Carla Nunes", "Ruptura sozinha não conta."),
                           S(20, "Bruno Teles", "Régua sozinha também não.")]);

        var (achados, total) = new Acervo(_raiz).Buscar("regua ruptura");

        Assert.Equal(1, total);
        Assert.Equal("transcrição", achados[0].Fonte);
        Assert.Equal("Bruno Teles", achados[0].Falante);
        Assert.Equal(0, achados[0].Instante);
        Assert.Equal("A régua de RUPTURA muda em outubro.", achados[0].Trecho);
    }

    [Fact]
    public void ABuscaOlhaAAtaEAsNotasMasNaoOsTitulos()
    {
        Gravar(_raiz, "2026-09-01_10-00-00",
               ata: "# Ata — Ruptura\n\n## Ações\n\n- [ ] Levantar a ruptura por loja — **Carla Nunes**",
               notas: "ruptura da Hiper até sexta");

        var (achados, _) = new Acervo(_raiz).Buscar("ruptura");

        Assert.Equal(["ata", "notas"], achados.Select(a => a.Fonte));
    }

    [Fact]
    public void ABuscaPulaOCabecalhoDaAta()
    {
        Gravar(_raiz, "2026-09-01_10-00-00", ata: "# Ata — Semanal\n\n**Cliente:** Acme · **Projeto:** "
               + "Portal\n\n## Resumo\n\nO Portal entra em outubro.");

        var (achados, _) = new Acervo(_raiz).Buscar("portal");

        Assert.Equal(["O Portal entra em outubro."], achados.Select(a => a.Trecho));
    }

    [Fact]
    public void ABuscaVemDaMaisNovaEContaOQueFicouDeFora()
    {
        for (int dia = 1; dia <= 5; dia++)
            Gravar(_raiz, $"2026-09-0{dia}_10-00-00", segmentos: [S(0, "Bruno Teles", "o prazo do piloto")]);

        var (achados, total) = new Acervo(_raiz).Buscar("prazo", limite: 2);

        Assert.Equal(5, total);
        Assert.Equal(["2026-09-05_10-00-00", "2026-09-04_10-00-00"], achados.Select(a => a.Reuniao.Id));
    }

    [Fact]
    public void ABuscaRecortaEmVoltaDoQueAchouSemPartirPalavra()
    {
        string fala = string.Join(' ', Enumerable.Repeat("palavra", 120)) + " ALVO "
                      + string.Join(' ', Enumerable.Repeat("outra", 120));
        Gravar(_raiz, "2026-09-01_10-00-00", segmentos: [S(0, "Bruno Teles", fala)]);

        string trecho = new Acervo(_raiz).Buscar("alvo").Achados[0].Trecho;

        Assert.Contains("ALVO", trecho);
        Assert.StartsWith("…palavra ", trecho);
        Assert.EndsWith(" outra…", trecho);
        Assert.True(trecho.Length <= Acervo.TamanhoDoTrecho + 2);
    }
}

/// <summary>O MCP: o protocolo, as ferramentas, os recursos e os prompts.</summary>
public sealed class ServidorMcpTests : IDisposable
{
    private readonly string _raiz = Directory.CreateTempSubdirectory("mcp-testes").FullName;
    private readonly string _aberta;
    private readonly ServidorMcp _servidor;

    public ServidorMcpTests()
    {
        _aberta = Path.Combine(_raiz, "reuniao-aberta.json");
        string acervo = Path.Combine(_raiz, "acervo");
        AcervoTests.Gravar(acervo, "2026-09-01_10-00-00", "Kickoff", cliente: "Acme", projeto: "Portal",
                           segmentos: [AcervoTests.S(0, "Bruno Teles", "Começamos pelo dashboard.")],
                           ata: "# Ata — Kickoff\n\n## Resumo\n\nComeçar pelo dashboard.");
        AcervoTests.Gravar(acervo, "2026-09-08_10-00-00", "Semanal", cliente: "Acme", projeto: "Portal",
                           segmentos: [AcervoTests.S(65, "Carla Nunes", "O dashboard atrasou uma semana.")]);
        AcervoTests.Gravar(acervo, "2026-09-09_10-00-00", "Interna");
        _servidor = new ServidorMcp(new Acervo(acervo), "0.0.0-teste", _aberta);
    }

    public void Dispose() => Directory.Delete(_raiz, recursive: true);

    private int _proximo = 1;

    private JsonObject Pedir(string metodo, JsonObject? parametros = null)
    {
        var pedido = new JsonObject { ["jsonrpc"] = "2.0", ["id"] = _proximo++, ["method"] = metodo };
        if (parametros is not null) pedido["params"] = parametros;
        return JsonNode.Parse(_servidor.Responder(pedido.ToJsonString())!)!.AsObject();
    }

    private string Ferramenta(string nome, JsonObject? args, out bool erro)
    {
        var r = Pedir("tools/call", new JsonObject { ["name"] = nome, ["arguments"] = args ?? new JsonObject() });
        erro = r["result"]!["isError"]!.GetValue<bool>();
        return r["result"]!["content"]![0]!["text"]!.GetValue<string>();
    }

    [Theory]
    [InlineData("2025-06-18", "2025-06-18")]
    [InlineData("2024-11-05", "2024-11-05")]
    [InlineData("2099-01-01", "2025-11-25")]
    public void InitializeAceitaAVersaoQueConheceEOfereceAMaisNova(string pedida, string esperada)
    {
        var r = Pedir("initialize", new JsonObject { ["protocolVersion"] = pedida });

        Assert.Equal(esperada, r["result"]!["protocolVersion"]!.GetValue<string>());
        Assert.Equal("pulsemeet", r["result"]!["serverInfo"]!["name"]!.GetValue<string>());
        Assert.NotNull(r["result"]!["capabilities"]!["tools"]);
        Assert.NotNull(r["result"]!["capabilities"]!["resources"]);
        Assert.NotNull(r["result"]!["capabilities"]!["prompts"]);
    }

    [Fact]
    public void NotificacaoNaoTemResposta()
    {
        Assert.Null(_servidor.Responder("""{"jsonrpc":"2.0","method":"notifications/initialized"}"""));
    }

    [Fact]
    public void ORespondidoLevaOMesmoId()
    {
        var r = JsonNode.Parse(_servidor.Responder("""{"jsonrpc":"2.0","id":"abc","method":"ping"}""")!)!;

        Assert.Equal("abc", r["id"]!.GetValue<string>());
        Assert.NotNull(r["result"]);
    }

    [Fact]
    public void LixoEMetodoDesconhecidoViramErroDoProtocolo()
    {
        var lixo = JsonNode.Parse(_servidor.Responder("{ não é json")!)!;
        Assert.Equal(-32700, lixo["error"]!["code"]!.GetValue<int>());

        Assert.Equal(-32601, Pedir("nada/disso")["error"]!["code"]!.GetValue<int>());
    }

    [Fact]
    public void AsFerramentasSoLeem()
    {
        var ferramentas = Pedir("tools/list")["result"]!["tools"]!.AsArray();

        Assert.Equal(["listar_clientes", "listar_reunioes", "ler_ata", "ler_transcricao", "buscar",
                      "reuniao_aberta"],
                     ferramentas.Select(f => f!["name"]!.GetValue<string>()));
        Assert.All(ferramentas, f => Assert.True(f!["annotations"]!["readOnlyHint"]!.GetValue<bool>()));
    }

    [Fact]
    public void ListarReunioesFiltraEDizOQueCadaUmaTem()
    {
        string texto = Ferramenta("listar_reunioes", new JsonObject { ["cliente"] = "acme" }, out bool erro);

        Assert.False(erro);
        Assert.Contains("2 reuniões (cliente \"acme\")", texto);
        Assert.Contains("- 2026-09-01_10-00-00 · 01/09/2026 10:00 · Kickoff · 10min 00s · Acme › Portal · ata, transcrição",
                        texto);
        Assert.DoesNotContain("Interna", texto);
    }

    [Fact]
    public void IdErradoVoltaComoErroDaFerramentaParaOModeloCorrigir()
    {
        string texto = Ferramenta("ler_ata", new JsonObject { ["reuniao"] = "ontem" }, out bool erro);

        Assert.True(erro);
        Assert.Contains("listar_reunioes", texto);
    }

    [Fact]
    public void DataMalEscritaVoltaComoErroDaFerramenta()
    {
        Ferramenta("listar_reunioes", new JsonObject { ["desde"] = "semana passada" }, out bool erro);

        Assert.True(erro);
    }

    [Fact]
    public void ReuniaoSemAtaApontaParaATranscricao()
    {
        string texto = Ferramenta("ler_ata", new JsonObject { ["reuniao"] = "2026-09-08_10-00-00" }, out bool erro);

        Assert.False(erro);
        Assert.Contains("ler_transcricao", texto);
    }

    [Fact]
    public void LerTranscricaoAceitaMinutoESegundo()
    {
        string texto = Ferramenta("ler_transcricao", new JsonObject
        {
            ["reuniao"] = "2026-09-08_10-00-00", ["de"] = "01:00", ["ate"] = "1:10",
        }, out _);

        Assert.Contains("[01:05] Carla Nunes: O dashboard atrasou uma semana.", texto);
    }

    [Fact]
    public void BuscarAgrupaPorReuniaoComOMinuto()
    {
        string texto = Ferramenta("buscar", new JsonObject { ["termo"] = "dashboard" }, out _);

        Assert.Contains("ocorrências de \"dashboard\"", texto);
        Assert.True(texto.IndexOf("## 2026-09-08", StringComparison.Ordinal)
                    < texto.IndexOf("## 2026-09-01", StringComparison.Ordinal));
        Assert.Contains("- [01:05] Carla Nunes: O dashboard atrasou uma semana.", texto);
        Assert.Contains("- ata: Começar pelo dashboard.", texto);
    }

    [Fact]
    public void AReuniaoAbertaVemDoQueOAppMarcou()
    {
        string antes = Ferramenta("reuniao_aberta", null, out _);
        Assert.Contains("Nenhuma reunião foi aberta", antes);

        ReuniaoAberta.Marcar(Path.Combine(_raiz, "acervo", "2026-09-01_10-00-00"), _aberta);
        string depois = Ferramenta("reuniao_aberta", null, out _);

        Assert.Contains("é a 2026-09-01_10-00-00", depois);
        Assert.Contains("Ata — Kickoff",
                        Ferramenta("ler_ata", new JsonObject { ["reuniao"] = "aberta" }, out _));
    }

    [Fact]
    public void OsRecursosPoemAAbertaNaFrenteEAMaisNovaDepois()
    {
        ReuniaoAberta.Marcar(Path.Combine(_raiz, "acervo", "2026-09-01_10-00-00"), _aberta);

        var uris = Pedir("resources/list")["result"]!["resources"]!.AsArray()
            .Select(r => r!["uri"]!.GetValue<string>()).ToList();

        Assert.Equal(
        [
            "pulsemeet://reuniao-aberta/ata",
            "pulsemeet://reuniao-aberta/transcricao",
            "pulsemeet://reuniao/2026-09-08_10-00-00/transcricao",
            "pulsemeet://reuniao/2026-09-01_10-00-00/ata",
            "pulsemeet://reuniao/2026-09-01_10-00-00/transcricao",
        ], uris);
    }

    [Fact]
    public void LerUmRecursoDevolveOTexto()
    {
        var r = Pedir("resources/read", new JsonObject { ["uri"] = "pulsemeet://reuniao/2026-09-08_10-00-00/transcricao" });
        var conteudo = r["result"]!["contents"]![0]!;

        Assert.Equal("text/plain", conteudo["mimeType"]!.GetValue<string>());
        Assert.Contains("Carla Nunes: O dashboard atrasou", conteudo["text"]!.GetValue<string>());
    }

    [Theory]
    [InlineData("pulsemeet://reuniao/2026-09-08_10-00-00/ata")]
    [InlineData("pulsemeet://reuniao/../ata")]
    [InlineData("pulsemeet://outra-coisa")]
    public void RecursoQueNaoExisteEErro32002(string uri)
    {
        var r = Pedir("resources/read", new JsonObject { ["uri"] = uri });

        Assert.Equal(-32002, r["error"]!["code"]!.GetValue<int>());
    }

    [Fact]
    public void OPromptDaTimelinePedeOClienteELevaONome()
    {
        var sem = Pedir("prompts/get", new JsonObject { ["name"] = "timeline" });
        Assert.Equal(-32602, sem["error"]!["code"]!.GetValue<int>());

        var com = Pedir("prompts/get", new JsonObject
        {
            ["name"] = "timeline", ["arguments"] = new JsonObject { ["cliente"] = "Acme" },
        });
        string texto = com["result"]!["messages"]![0]!["content"]!["text"]!.GetValue<string>();

        Assert.Contains("cliente Acme", texto);
        Assert.Contains("listar_reunioes", texto);
    }

    [Fact]
    public async Task UmPedidoPorLinhaUmaRespostaPorLinha()
    {
        var entrada = new StringReader(string.Join('\n',
            """{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18"}}""",
            """{"jsonrpc":"2.0","method":"notifications/initialized"}""",
            "",
            """{"jsonrpc":"2.0","id":2,"method":"tools/list"}""") + "\n");
        var saida = new StringWriter();

        await _servidor.RodarAsync(entrada, saida);

        string[] linhas = saida.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(2, linhas.Length);
        Assert.Equal(1, JsonNode.Parse(linhas[0])!["id"]!.GetValue<int>());
        Assert.Equal(2, JsonNode.Parse(linhas[1])!["id"]!.GetValue<int>());
    }

    [Fact]
    public void OAcentoSaiLiteral()
    {
        Assert.Contains("reunião", _servidor.Responder("""{"jsonrpc":"2.0","id":1,"method":"tools/list"}"""));
    }
}

/// <summary>A entrada do PulseMeet no arquivo de configuração do Claude Desktop.</summary>
public sealed class ConexaoComClaudeTests : IDisposable
{
    private readonly string _raiz = Directory.CreateTempSubdirectory("claude-testes").FullName;
    private const string Exe = @"C:\Programas\MeetingApp\PulseMeet.exe";

    public void Dispose() => Directory.Delete(_raiz, recursive: true);

    private string Arquivo(string conteudo)
    {
        string caminho = Path.Combine(_raiz, ConexaoComClaude.NomeDoArquivo);
        File.WriteAllText(caminho, conteudo);
        return caminho;
    }

    [Fact]
    public void ConectarMexeSoNaEntradaDoPulseMeet()
    {
        string arquivo = Arquivo("""
            {
              "preferences": { "sidebarMode": "chat", "pastas": ["C:\\Users\\x\\Áreas"] },
              "mcpServers": { "outro": { "command": "node", "args": ["x.js"] } }
            }
            """);

        ConexaoComClaude.Conectar(arquivo, Exe);

        var raiz = JsonNode.Parse(File.ReadAllText(arquivo))!;
        Assert.Equal("chat", raiz["preferences"]!["sidebarMode"]!.GetValue<string>());
        Assert.Equal(@"C:\Users\x\Áreas", raiz["preferences"]!["pastas"]![0]!.GetValue<string>());
        Assert.Equal("node", raiz["mcpServers"]!["outro"]!["command"]!.GetValue<string>());
        Assert.Equal(Exe, raiz["mcpServers"]!["pulsemeet"]!["command"]!.GetValue<string>());
        Assert.Equal("--mcp", raiz["mcpServers"]!["pulsemeet"]!["args"]![0]!.GetValue<string>());
        Assert.True(File.Exists(arquivo + ConexaoComClaude.SufixoDaCopia));
    }

    [Fact]
    public void ArquivoQueNaoSeLeNaoEReescrito()
    {
        const string quebrado = """{ "preferences": { "sidebarMode": "chat" """;
        string arquivo = Arquivo(quebrado);

        Assert.Throws<InvalidOperationException>(() => ConexaoComClaude.Conectar(arquivo, Exe));

        Assert.Equal(quebrado, File.ReadAllText(arquivo));
        Assert.False(File.Exists(arquivo + ConexaoComClaude.SufixoDaCopia));
    }

    [Fact]
    public void DesconectarTiraSoAEntrada()
    {
        string arquivo = Arquivo("""{ "mcpServers": { "outro": { "command": "node" } } }""");
        ConexaoComClaude.Conectar(arquivo, Exe);

        ConexaoComClaude.Desconectar(arquivo);

        var servidores = JsonNode.Parse(File.ReadAllText(arquivo))!["mcpServers"]!.AsObject();
        Assert.False(servidores.ContainsKey("pulsemeet"));
        Assert.True(servidores.ContainsKey("outro"));
    }

    [Fact]
    public void OEstadoDizSeAEntradaApontaParaEsteExecutavel()
    {
        string arquivo = Arquivo("{}");
        Assert.False(ConexaoComClaude.Estado(arquivo, Exe).Conectado);

        ConexaoComClaude.Conectar(arquivo, @"D:\antigo\PulseMeet.exe");
        var velho = ConexaoComClaude.Estado(arquivo, Exe);
        Assert.True(velho.Conectado);
        Assert.False(velho.NesteExecutavel);

        ConexaoComClaude.Conectar(arquivo, Exe);
        Assert.True(ConexaoComClaude.Estado(arquivo, Exe.ToLowerInvariant()).NesteExecutavel);
    }

    [Fact]
    public void SemClaudeNaoHaArquivo()
    {
        Assert.Null(ConexaoComClaude.ArquivoDeConfiguracao(Path.Combine(_raiz, "local"),
                                                           Path.Combine(_raiz, "roaming")));
        Assert.False(ConexaoComClaude.Estado(null, Exe).ClaudeEncontrado);
    }

    [Fact]
    public void AStoreComDadosForaDoPacoteVenceAPastaVirtualizada()
    {
        string local = Path.Combine(_raiz, "local");
        string fora = Path.Combine(local, "Claude-Data", "claude_abc123");
        string virtualizada = Path.Combine(local, "Packages", "Claude_abc123", "LocalCache", "Roaming", "Claude");
        Directory.CreateDirectory(fora);
        Directory.CreateDirectory(virtualizada);
        File.WriteAllText(Path.Combine(virtualizada, ".user-data-left.claude_abc123"), "");
        File.WriteAllText(Path.Combine(fora, ConexaoComClaude.NomeDoArquivo), "{}");

        Assert.Equal(Path.Combine(fora, ConexaoComClaude.NomeDoArquivo),
                     ConexaoComClaude.ArquivoDeConfiguracao(local, Path.Combine(_raiz, "roaming")));
    }

    [Fact]
    public void APastaVirtualizadaComAMarcaDeSaidaNaoConta()
    {
        string local = Path.Combine(_raiz, "local");
        string virtualizada = Path.Combine(local, "Packages", "Claude_abc123", "LocalCache", "Roaming", "Claude");
        Directory.CreateDirectory(virtualizada);
        File.WriteAllText(Path.Combine(virtualizada, ".user-data-left.claude_abc123"), "");
        string classica = Path.Combine(_raiz, "roaming", "Claude");
        Directory.CreateDirectory(classica);

        Assert.Equal(Path.Combine(classica, ConexaoComClaude.NomeDoArquivo),
                     ConexaoComClaude.ArquivoDeConfiguracao(local, Path.Combine(_raiz, "roaming")));
    }

    [Fact]
    public void OInstaladorClassicoSemArquivoAindaRecebeUm()
    {
        string classica = Path.Combine(_raiz, "roaming", "Claude");
        Directory.CreateDirectory(classica);
        string arquivo = ConexaoComClaude.ArquivoDeConfiguracao(Path.Combine(_raiz, "local"),
                                                                Path.Combine(_raiz, "roaming"))!;

        ConexaoComClaude.Conectar(arquivo, Exe);

        Assert.True(ConexaoComClaude.Estado(arquivo, Exe).NesteExecutavel);
    }
}

/// <summary>A marca da reunião aberta, que o app escreve e o MCP lê.</summary>
public sealed class ReuniaoAbertaTests : IDisposable
{
    private readonly string _raiz = Directory.CreateTempSubdirectory("aberta-testes").FullName;

    public void Dispose() => Directory.Delete(_raiz, recursive: true);

    [Fact]
    public void OQueFoiMarcadoVoltaComAHora()
    {
        string arquivo = Path.Combine(_raiz, "sub", "reuniao-aberta.json");
        var quando = new DateTimeOffset(2026, 10, 22, 10, 31, 0, TimeSpan.FromHours(-3));

        ReuniaoAberta.Marcar(Path.Combine(_raiz, "2026-10-22_09-01-36") + Path.DirectorySeparatorChar,
                             arquivo, quando);

        Assert.Equal(("2026-10-22_09-01-36", quando), ReuniaoAberta.Ler(arquivo));
    }

    [Fact]
    public void SemMarcaOuComLixoNaoHaReuniaoAberta()
    {
        string arquivo = Path.Combine(_raiz, "reuniao-aberta.json");
        Assert.Null(ReuniaoAberta.Ler(arquivo));

        File.WriteAllText(arquivo, "lixo");
        Assert.Null(ReuniaoAberta.Ler(arquivo));
    }
}
