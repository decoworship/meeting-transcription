using MeetingApp.Nucleo;
using MeetingApp.Sidecar;
using Xunit;

namespace MeetingApp.Tests;

/// <summary>
/// Quem falou em cada trecho da legenda, depois da reunião.
/// </summary>
/// <remarks>
/// É o <c>VIVO-2</c>. O que se prova aqui é a atribuição — que roda sem placa,
/// sem áudio e sem sidecar. O percurso (rodar o pyannote em segundo plano) é
/// encanamento, e vive na Ponte.
/// </remarks>
public sealed class FalantesDaLegendaTests
{
    private static TrechoDaLegenda T(long de, long ate, bool dono = false) => new()
    {
        InicioMs = de, FimMs = ate, Dono = dono, Texto = $"trecho {de}",
    };

    private static SegmentoDeFalante D(double de, double ate, string quem) => new(de, ate, quem);

    [Fact]
    public void CadaTrechoRecebeOFalanteDeMaiorSobreposicao()
    {
        var trechos = new[] { T(0, 2000), T(2000, 4000) };
        var diarizacao = new[] { D(0, 1.9, "Speaker 1"), D(1.9, 4.1, "Speaker 2") };

        var saida = FalantesDaLegenda.Atribuir(trechos, diarizacao);

        Assert.Equal("Speaker 1", saida[0].Falante);
        Assert.Equal("Speaker 2", saida[1].Falante);
    }

    [Fact]
    public void OTrechoDoDonoNaoEDecididoPelaDiarizacao()
    {
        // **A faixa do microfone sabe; o pyannote acha.** O dono vem do canal,
        // e deixar a diarização sobrescrevê-lo trocaria certeza por estimativa —
        // é a mesma razão do desempate no Montagem.DonoDoIntervalo.
        var trechos = new[] { T(0, 2000, dono: true) };
        var diarizacao = new[] { D(0, 2.0, "Speaker 3") };

        var saida = FalantesDaLegenda.Atribuir(trechos, diarizacao);

        Assert.Equal(VozDoDono.Rotulo, saida[0].Falante);
    }

    [Fact]
    public void TrechoSemDiarizacaoEmCimaHerdaOAnterior()
    {
        // Silêncio entre turnos, ou fala que o pyannote não pegou. Herdar não
        // abre troca de falante onde não há informação de troca — mesma regra
        // que a Montagem aplica às palavras.
        var trechos = new[] { T(0, 1000), T(5000, 6000), T(6000, 7000) };
        var diarizacao = new[] { D(0, 1.0, "Speaker 1"), D(6.0, 7.0, "Speaker 2") };

        var saida = FalantesDaLegenda.Atribuir(trechos, diarizacao);

        Assert.Equal("Speaker 1", saida[0].Falante);
        Assert.Equal("Speaker 1", saida[1].Falante);   // herdado
        Assert.Equal("Speaker 2", saida[2].Falante);
    }

    [Fact]
    public void SemDiarizacaoNenhumaOsTrechosSaemIntactos()
    {
        // O pior caso é o comportamento de antes, nunca um resultado pior.
        var trechos = new[] { T(0, 1000), T(1000, 2000) };

        var saida = FalantesDaLegenda.Atribuir(trechos, []);

        Assert.Equal(2, saida.Count);
        Assert.All(saida, t => Assert.Null(t.Falante));
        Assert.Equal("trecho 0", saida[0].Texto);
    }

    [Fact]
    public void OTextoEOTempoAtravessamSemMudar()
    {
        // A atribuição só acrescenta falante: mexer no texto aqui faria a
        // legenda divergir do que a pessoa leu durante a reunião.
        var trechos = new[] { T(1234, 5678) };

        var saida = FalantesDaLegenda.Atribuir(trechos, [D(1.2, 5.7, "Speaker 1")]);

        Assert.Equal(1234, saida[0].InicioMs);
        Assert.Equal(5678, saida[0].FimMs);
        Assert.Equal("trecho 1234", saida[0].Texto);
    }

    [Fact]
    public void UmaPalavraPartidaNaoGanhaDoisFalantes()
    {
        // **O defeito que este método existe para impedir**, visto em
        // 18/09/2026: "Paloma" saiu como 'a Palo' (SPEAKER_03) e
        // 'ma tem Uberlândia' (SPEAKER_02). Uma palavra tem um dono só.
        var trechos = new[]
        {
            new TrechoDaLegenda { InicioMs = 0, FimMs = 2000, Dono = false, Texto = "a Palo" },
            new TrechoDaLegenda
            {
                InicioMs = 2000, FimMs = 3000, Dono = false,
                Texto = "ma tem Uberlândia", Colado = true,
            },
        };
        var diarizacao = new[] { D(0, 2.0, "Speaker 1"), D(2.0, 3.0, "Speaker 2") };

        var saida = FalantesDaLegenda.Atribuir(trechos, diarizacao);

        Assert.Single(saida);
        Assert.Equal("a Paloma tem Uberlândia", saida[0].Texto);
        // O intervalo é a união, e o falante é o de maior sobreposição nela.
        Assert.Equal(0, saida[0].InicioMs);
        Assert.Equal(3000, saida[0].FimMs);
        Assert.Equal("Speaker 1", saida[0].Falante);
    }

    [Fact]
    public void TrechoSoltoNaoEFundidoComOVizinho()
    {
        var trechos = new[]
        {
            new TrechoDaLegenda { InicioMs = 0, FimMs = 1000, Dono = false, Texto = "bom dia" },
            new TrechoDaLegenda { InicioMs = 1000, FimMs = 2000, Dono = false, Texto = "tudo bem" },
        };

        var saida = FalantesDaLegenda.Atribuir(trechos, [D(0, 2.0, "Speaker 1")]);

        Assert.Equal(2, saida.Count);
    }
}
