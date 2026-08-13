using MeetingApp.Nucleo;
using Xunit;

namespace MeetingApp.Tests;

/// <summary>
/// O vínculo da gravação com cliente e projeto.
/// </summary>
/// <remarks>
/// Nasceu de um defeito relatado em 13/08/2026: escolher cliente e projeto,
/// sair da tela de preparo e voltar, e encontrar os campos vazios. O dado
/// morava só dentro do <c>transcricao.json</c> — que ainda não existe quando se
/// está justamente preparando a transcrição.
/// </remarks>
public sealed class DadosDaReuniaoTests : IDisposable
{
    private readonly string _pasta =
        Directory.CreateTempSubdirectory("reuniao-testes").FullName;

    public void Dispose() => Directory.Delete(_pasta, recursive: true);

    [Fact]
    public void SobreviveAoIdaEVoltaAntesDeQualquerTranscricao()
    {
        new DadosDaReuniao { Cliente = "Vivo", Projeto = "Faturamento B2B" }.Salvar(_pasta);

        var lido = DadosDaReuniao.Ler(_pasta);

        Assert.Equal("Vivo", lido.Cliente);
        Assert.Equal("Faturamento B2B", lido.Projeto);
        Assert.True(File.Exists(Path.Combine(_pasta, "reuniao.json")));
    }

    [Fact]
    public void GravacaoSemNadaDevolveVazioEmVezDeExplodir()
    {
        var lido = DadosDaReuniao.Ler(_pasta);

        Assert.True(lido.Vazio);
        Assert.Null(lido.Cliente);
    }

    [Fact]
    public void OHistoricoAparecePreenchidoPelaTranscricaoAntiga()
    {
        // As reuniões transcritas antes deste arquivo existir têm o vínculo
        // dentro do transcricao.json. Sem esta segunda fonte, o histórico
        // inteiro apareceria em branco no dia em que a feature subiu.
        File.WriteAllText(Path.Combine(_pasta, "transcricao.json"),
            """{"language":"pt","client":"Algar","project":"Agentes (Interno)","segments":[]}""");

        var lido = DadosDaReuniao.Ler(_pasta);

        Assert.Equal("Algar", lido.Cliente);
        Assert.Equal("Agentes (Interno)", lido.Projeto);
    }

    [Fact]
    public void OArquivoProprioTemPrecedenciaSobreATranscricao()
    {
        // O reuniao.json é o que a pessoa escolheu por último; a transcrição
        // guarda o que valia quando ela rodou.
        File.WriteAllText(Path.Combine(_pasta, "transcricao.json"),
            """{"language":"pt","client":"Antigo","project":"Velho","segments":[]}""");
        new DadosDaReuniao { Cliente = "Novo", Projeto = "Atual" }.Salvar(_pasta);

        var lido = DadosDaReuniao.Ler(_pasta);

        Assert.Equal("Novo", lido.Cliente);
        Assert.Equal("Atual", lido.Projeto);
    }

    [Fact]
    public void LimparOsDoisCamposApagaOArquivo()
    {
        new DadosDaReuniao { Cliente = "Vivo", Projeto = "X" }.Salvar(_pasta);
        new DadosDaReuniao { Cliente = "", Projeto = "" }.Salvar(_pasta);

        Assert.False(File.Exists(Path.Combine(_pasta, "reuniao.json")));
        Assert.True(DadosDaReuniao.Ler(_pasta).Vazio);
    }

    [Fact]
    public void SoOClienteJaVale()
    {
        // Escolher o cliente e ainda não saber o projeto é o meio do caminho
        // normal de quem está preparando a transcrição.
        new DadosDaReuniao { Cliente = "Vivo" }.Salvar(_pasta);

        var lido = DadosDaReuniao.Ler(_pasta);

        Assert.Equal("Vivo", lido.Cliente);
        Assert.Null(lido.Projeto);
        Assert.False(lido.Vazio);
    }

    [Fact]
    public void JsonCorrompidoNaoImpedeDeAbrirAReuniao()
    {
        File.WriteAllText(Path.Combine(_pasta, "reuniao.json"), "{isto não é json");

        var lido = DadosDaReuniao.Ler(_pasta);

        Assert.True(lido.Vazio);
    }

    [Fact]
    public void OAcentoVaiLiteralParaODisco()
    {
        // Como todo JSON deste projeto: nome de cliente tem acento, e um
        // ão no arquivo é ilegível para quem o abre no editor.
        new DadosDaReuniao { Cliente = "Construção Ltda", Projeto = "Manutenção" }.Salvar(_pasta);

        string json = File.ReadAllText(Path.Combine(_pasta, "reuniao.json"));

        Assert.Contains("Construção", json);
        Assert.DoesNotContain("\\u00e7", json);
    }
}
