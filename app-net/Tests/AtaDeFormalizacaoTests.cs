using MeetingApp.Nucleo;
using MeetingApp.Nucleo.Atas;
using Xunit;
using static MeetingApp.Nucleo.Atas.AtaDeFormalizacao;

namespace MeetingApp.Tests;

/// <summary>
/// A ata de formalização: extrair com citação, conferir sem modelo, escrever no
/// estilo. O que cada teste guarda é um defeito medido em 06/11/2026.
/// </summary>
public sealed class AtaDeFormalizacaoTests : IDisposable
{
    private readonly string _pasta = Path.Combine(Path.GetTempPath(), "formal-" + Guid.NewGuid().ToString("N"));

    public AtaDeFormalizacaoTests() => Directory.CreateDirectory(_pasta);

    public void Dispose() => Directory.Delete(_pasta, recursive: true);

    private static SegmentoFinal S(double t, string quem, string texto) =>
        new() { Start = t, End = t + 2, Text = texto, Speaker = quem };

    private static readonly SegmentoFinal[] Reuniao =
    [
        S(0, "Ana", " Bom dia."),
        S(2, "Ana", " Vamos fechar o corte."),           // junta com o anterior: L1
        S(5, "Rafael", " Eu acho que 500 caixas."),       // L2
        S(9, "Ana", " Fechado, 500 caixas por mês."),     // L3
        S(13, "Joana", " Consegue me mandar a margem até amanhã?"), // L4
        S(17, "Rafael", " Deixa eu ver aqui."),           // L5
        S(19, "Joana", " É importante pro BP."),          // L6
        S(22, "Rafael", " Mando isso hoje."),             // L7
        S(25, "Ana", " Mantém os seis dias da semana."),  // L8
    ];

    private static ContextoDaReuniao Ctx() => new()
    {
        Titulo = "Planner",
        Cliente = "Acme",
        Falantes = ["Ana", "Rafael", "Joana"],
        Pessoas = [new Pessoa("Rafael Souza", "acme.com", false), new Pessoa("Ana Lima", "casa.com", true)],
    };

    [Fact]
    public void FalasSeguidasDaMesmaPessoaViramUmaLinhaNumerada()
    {
        var t = Turnos(Reuniao);

        Assert.Equal(8, t.Count);
        Assert.Equal("Bom dia. Vamos fechar o corte.", t[0].Texto);
        Assert.Equal(3, t[2].N);
        Assert.StartsWith("[L3 00:09] Ana: Fechado", TextoNumerado(t).Split('\n')[2]);
    }

    [Fact]
    public void PrazoQueNinguemDisseEApagadoEViraObservacao()
    {
        // O defeito que abriu o caminho: "28/10/2026" em todas as pendências.
        var ext = new Extracao([], [new Pendente("Margem por cliente", "Rafael", "28/10/2026", [4])]);

        var (conf, obs) = Conferir(ext, Turnos(Reuniao), Ctx());

        Assert.Equal("", conf.Pendentes[0].Prazo);
        Assert.Contains(obs, o => o.Contains("Prazo apagado") && o.Contains("28/10/2026"));
    }

    [Fact]
    public void PrazoDitoAlgumasFalasDepoisDaCitadaFica()
    {
        // O pedido está na L4 e o "hoje" na L7: três falas depois. Com uma
        // fala de vizinhança, a conferência apagava um prazo que foi dito.
        var ext = new Extracao([], [new Pendente("Margem por cliente", "Rafael", "hoje", [4])]);

        var (conf, obs) = Conferir(ext, Turnos(Reuniao), Ctx());

        Assert.Equal("hoje", conf.Pendentes[0].Prazo);
        Assert.Empty(obs);
    }

    [Theory]
    [InlineData("Colocar o André na agenda da reunião")]
    [InlineData("Confirmar se o William vai entrar")]
    [InlineData("Enviar a ata da reunião para a Aline")]
    [InlineData("Enviar ata com pontos discutidos")]
    [InlineData("Reagendar a reunião para a próxima quinta-feira")]
    public void LogisticaDaPropriaReuniaoNaoEPendente(string oQue)
    {
        var ext = new Extracao([], [new Pendente(oQue, "Ana", "", [4])]);

        var (conf, obs) = Conferir(ext, Turnos(Reuniao), Ctx());

        Assert.Empty(conf.Pendentes);
        Assert.Contains(obs, o => o.StartsWith("Fora da ata"));
    }

    [Fact]
    public void DataDitaComoDiaContaComoFonte()
    {
        // A ata escreve "até 23/10"; a fala disse "até o dia 23".
        var segs = new[] { S(0, "Rafael", " A margem eu mando até o dia 23.") };
        var ext = new Extracao([], [new Pendente("Margem", "Rafael", "até 23/10", [1])]);

        var (conf, obs) = Conferir(ext, Turnos(segs), Ctx());

        Assert.Equal("até 23/10", conf.Pendentes[0].Prazo);
        Assert.Empty(obs);
    }

    [Fact]
    public void ProximaReuniaoSoEntraSeAExtracaoCitouAFala()
    {
        var semFonte = Montar(new Extracao([new Resolvido("Corte", "500", [3])], []),
            new Escrita("", [(1, "Corte", "500")], [], "sexta, dia 19"), [], Ctx());
        var comFonte = Montar(new Extracao([new Resolvido("Próxima reunião", "quinta, 19/11", [3])], []),
            new Escrita("", [(1, "Próxima reunião", "quinta, 19/11, no mesmo horário")], [], ""), [], Ctx());

        Assert.Empty(semFonte.Secoes);
        Assert.Contains(semFonte.Observacoes, o => o.Contains("sexta, dia 19"));
        Assert.Equal("quinta, 19/11, no mesmo horário", Assert.Single(comFonte.Secoes).Texto);
        Assert.Empty(comFonte.Decisoes);   // não aparece duas vezes
    }

    [Fact]
    public void ItemSemLinhaQueExistaECortado()
    {
        var ext = new Extracao(
            [new Resolvido("Escala", "Folga no domingo", [99])],
            [new Pendente("Inventado", "Rafael", "", [])]);

        var (conf, obs) = Conferir(ext, Turnos(Reuniao), Ctx());

        Assert.Empty(conf.Resolvidos);
        Assert.Empty(conf.Pendentes);
        Assert.Equal(2, obs.Count(o => o.StartsWith("Descartado")));
    }

    [Fact]
    public void NumeroPorExtensoNaFalaContaComoFonte()
    {
        // "seis dias" dito, "6 dias" escrito: não é número inventado.
        var ext = new Extracao([new Resolvido("Escala", "Mantemos os 6 dias.", [8])], []);

        var (_, obs) = Conferir(ext, Turnos(Reuniao), Ctx());

        Assert.Empty(obs);
    }

    [Fact]
    public void NumeroQueNaoEstaNaFalaViraObservacaoMasOItemFica()
    {
        var ext = new Extracao([new Resolvido("Corte", "600 caixas por mês.", [3])], []);

        var (conf, obs) = Conferir(ext, Turnos(Reuniao), Ctx());

        Assert.Single(conf.Resolvidos);
        Assert.Contains(obs, o => o.Contains("600"));
    }

    [Fact]
    public void ResponsavelEPrazoVemDaExtracaoENaoDaEscrita()
    {
        var conf = new Extracao(
            [new Resolvido("Corte", "500 caixas por mês", [3])],
            [new Pendente("Enviar a margem por cliente", "Rafael", "hoje", [4])]);
        // A escrita tenta pôr outro dono no texto; o dono que vale é o da extração.
        var escrita = new Escrita("", [(1, "Corte de volume", "500 caixas unitárias por mês.")],
                                  [(1, "Margem por cliente")], "");

        var ata = Montar(conf, escrita, [], Ctx());

        Assert.Equal("Corte de volume: 500 caixas unitárias por mês.", ata.Decisoes[0].Texto);
        Assert.Equal("Rafael", ata.Acoes[0].Responsavel);
        Assert.Equal("hoje", ata.Acoes[0].Prazo);
        Assert.Equal("cliente", ata.Acoes[0].Lado);   // Rafael Souza é da acme.com
    }

    [Fact]
    public void ItemQueAEscritaNaoDevolveuFicaDito()
    {
        var conf = new Extracao(
            [new Resolvido("Corte", "500", [3]), new Resolvido("Escala", "6 dias", [8])],
            [new Pendente("Margem", "Rafael", "", [4])]);
        var escrita = new Escrita("", [(1, "Corte", "500")], [], "");

        var ata = Montar(conf, escrita, [], Ctx());

        Assert.Single(ata.Decisoes);
        Assert.Empty(ata.Acoes);
        Assert.Equal(2, ata.Observacoes.Count(o => o.StartsWith("Juntado a outro item")));
    }

    [Fact]
    public void OMarkdownEOFormatoDoEmail()
    {
        var ata = new AtaGerada
        {
            Resumo = "A rodada ficou com 312 lojas.",
            Decisoes = ["Corte de volume: 500 caixas por mês."],
            Acoes =
            [
                new AcaoDaAta { Acao = "Margem por cliente", Responsavel = "Rafael", Prazo = "hoje" },
                new AcaoDaAta { Acao = "Lista das lojas novas", Responsavel = "" },
            ],
            Secoes = [new SecaoDaAta { Titulo = "Próxima reunião", Texto = "quinta, 23/10" }],
            Observacoes = ["Prazo apagado"],
        };

        string md = Markdown(ata);

        Assert.Equal("""
            A rodada ficou com 312 lojas.

            **Resolvidos em reunião**

            - Corte de volume: 500 caixas por mês.

            **Pendentes**

            - Margem por cliente: Rafael, hoje
            - Lista das lojas novas

            Próxima reunião: quinta, 23/10

            """.Replace("\r\n", "\n"), md.Replace("\r\n", "\n"));
        // A conferência nunca vai no e-mail.
        Assert.DoesNotContain("Prazo apagado", md);
    }

    private sealed class SessaoFalsa(Func<string, string> responder, List<string> vistos) : ISessaoDoMotor
    {
        public Task<string> PerguntarAsync(string prompt, CancellationToken ct)
        {
            vistos.Add(prompt);
            return Task.FromResult(responder(prompt));
        }

        public void Dispose() { }
    }

    private const string RespostaDaExtracao =
        """{"resolvidos":[{"tema":"Corte","decisao":"500 caixas por mês","linhas":[3]}],"pendentes":[{"o_que":"Margem por cliente","responsavel":"Rafael","prazo":"28/10","linhas":[4]}]}""";

    private const string RespostaDaEscrita =
        """{"abertura":"","resolvidos":[{"n":1,"tema":"Corte de volume","texto":"500 caixas por mês."}],"pendentes":[{"n":1,"texto":"Margem por cliente"}],"proxima_reuniao":""}""";

    [Fact]
    public async Task OTipoFormalizacaoDesviaParaDuasEtapasEGravaOsArquivos()
    {
        var dados = new ResultadoDaTranscricao { Duration = 30, Segments = [.. Reuniao] };
        var tipo = ModelosDeAta.Buscar(Id)!;
        var sistemas = new List<string>();
        var vistos = new List<string>();
        bool motorComumChamado = false;

        GeracaoDeAta.Motor comum = (_, _, _, _, _) =>
        {
            motorComumChamado = true;
            return Task.FromResult(new AtaGerada());
        };
        GeracaoDeAta.AbrirSessao abrir = (sistema, _, _, _, _, _) =>
        {
            sistemas.Add(sistema);
            return Task.FromResult<ISessaoDoMotor>(new SessaoFalsa(_ => sistema == SistemaDaExtracao
                ? RespostaDaExtracao : RespostaDaEscrita, vistos));
        };

        var ata = await GeracaoDeAta.GerarAsync(_pasta, tipo, dados, Ctx(), comum, null,
            CancellationToken.None, FormatoDaAta.Classico, abrir);

        Assert.False(motorComumChamado);
        Assert.Equal([SistemaDaExtracao, SistemaDaEscrita], sistemas);
        Assert.Contains("[L3 00:09] Ana: Fechado, 500 caixas por mês.", vistos[0]);
        Assert.Equal("", ata.Acoes[0].Prazo);   // "28/10" não foi dito
        string md = File.ReadAllText(Path.Combine(_pasta, "ata.md"));
        Assert.Contains("- Corte de volume: 500 caixas por mês.", md);
        Assert.Contains("- Margem por cliente: Rafael\n", md.Replace("\r\n", "\n"));
        Assert.Contains("Prazo apagado", File.ReadAllText(Path.Combine(_pasta, GeracaoDeAta.ArquivoDeObservacoes)));
        Assert.True(File.Exists(Path.Combine(_pasta, "ata.json")));
    }

    [Theory]
    [InlineData(EsquemaDaExtracao)]
    [InlineData(EsquemaDaEscrita)]
    public void OsEsquemasSaoJsonValido(string esquema)
    {
        // A sessão falsa não lê o esquema; o llama-server lê, e recusa o pedido
        // inteiro com um 500 se ele não for JSON. Foi assim que um "}" a mais
        // passou pelos testes e quebrou a primeira ata de verdade.
        using var doc = System.Text.Json.JsonDocument.Parse(esquema);
        Assert.Equal("object", doc.RootElement.GetProperty("type").GetString());
    }

    [Fact]
    public void OExemploDeEstiloNaoLevaDadoDeClienteReal()
    {
        // O estilo veio das atas do RTM Merchan; o exemplo vai no executável.
        foreach (string proibido in new[] { "Coca", "Sorocaba", "Mariana", "Scantech", "Merchan", "Paulo", "Marcelo" })
            Assert.DoesNotContain(proibido, Exemplo, StringComparison.OrdinalIgnoreCase);
    }
}
