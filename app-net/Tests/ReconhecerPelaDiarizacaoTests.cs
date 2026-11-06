using MeetingApp.Nucleo;
using MeetingApp.Sidecar;
using Xunit;

namespace MeetingApp.Tests;

/// <summary>
/// O áudio com que se reconhece um falante, e o trecho sem nome no meio da fala
/// de outra pessoa.
/// </summary>
/// <remarks>
/// <para>
/// <b>O defeito medido em 05/11/2026.</b> Re-transcritas do zero as sete reuniões
/// com Gemini, 16% das palavras saíam com "Speaker N" ao lado da pessoa já
/// nomeada. A diarização tinha separado certo; quem falhava era o reconhecimento,
/// que tirava o vetor de janelas de 4 s dos segmentos do ASR — e elas esbarram
/// na fala de outra pessoa. Para o mesmo cluster, os quadros em que a diarização
/// diz que <b>só ele</b> fala davam ~0,1 a mais de semelhança (0,65 → 0,75;
/// 0,46 → 0,81), o que decide entre nome e "Speaker N" no limiar de 0,70.
/// </para>
/// <para>
/// Por isso os testes de <see cref="AprendizadoDeVozes.TrechosDaDiarizacao"/> são
/// sobre o que fica <b>de fora</b>: qualquer quadro com outra pessoa, ou com o
/// dono no microfone.
/// </para>
/// </remarks>
public sealed class ReconhecerPelaDiarizacaoTests
{
    private static SegmentoDeFalante D(double de, double ate, string quem) => new(de, ate, quem);

    private static SegmentoFinal Seg(double de, double ate, string? quem) =>
        new() { Start = de, End = ate, Text = "...", Speaker = quem };

    private static float[] MicMudo() => new float[120 * Faixas.TaxaDeAmostragem];

    private static float[] MicFalandoEntre(double de, double ate)
    {
        var mic = MicMudo();
        for (int i = (int)(de * Faixas.TaxaDeAmostragem);
             i < (int)(ate * Faixas.TaxaDeAmostragem) && i < mic.Length; i++)
            mic[i] = 0.2f;
        return mic;
    }

    // ------------------------------------------------ TrechosDaDiarizacao

    [Fact]
    public void ASobreposicaoComOutroFalanteFicaDeFora()
    {
        var linha = new[] { D(0, 10, "Speaker 1"), D(4, 6, "Speaker 2") };

        var trechos = AprendizadoDeVozes.TrechosDaDiarizacao(linha, "Speaker 1");

        Assert.Equal(2, trechos.Count);
        Assert.Contains(new TrechoLimpo(0, 4), trechos);
        Assert.Contains(new TrechoLimpo(6, 10), trechos);
    }

    [Fact]
    public void OTrechoDoDonoContaComoOutroFalante()
    {
        // A trilha do microfone entra na mesma linha do tempo: onde o dono fala
        // por cima, o system.wav pode carregar o retorno da voz dele.
        var linha = new[] { D(0, 10, "Speaker 1"), D(0, 5, VozDoDono.Rotulo) };

        var trechos = AprendizadoDeVozes.TrechosDaDiarizacao(linha, "Speaker 1");

        Assert.Equal(new TrechoLimpo(5, 10), Assert.Single(trechos));
    }

    [Fact]
    public void MicrofoneAtivoDescartaOTrecho()
    {
        var linha = new[] { D(0, 5, "Speaker 1"), D(20, 25, "Speaker 1") };

        var trechos = AprendizadoDeVozes.TrechosDaDiarizacao(
            linha, "Speaker 1", MicFalandoEntre(1, 2));

        Assert.Equal(new TrechoLimpo(20, 25), Assert.Single(trechos));
    }

    [Fact]
    public void RetalhoCurtoDemaisNaoEntra()
    {
        // O que sobra entre duas sobreposições pode ser um quadro: vetor de
        // retalho é ruído, e o piso total já cuida de exigir fala suficiente.
        var linha = new[]
        {
            D(0, 10, "Speaker 1"), D(0, 4.9, "Speaker 2"), D(5.1, 10, "Speaker 2"),
        };

        Assert.Empty(AprendizadoDeVozes.TrechosDaDiarizacao(linha, "Speaker 1"));
    }

    [Fact]
    public void DoMaisLongoAoMaisCurtoAteOTeto()
    {
        var linha = new[]
        {
            D(0, 5, "Speaker 1"), D(10, 30, "Speaker 1"), D(40, 52, "Speaker 1"),
        };

        var trechos = AprendizadoDeVozes.TrechosDaDiarizacao(linha, "Speaker 1");

        // 20 s + 12 s passa dos 30 s do teto: o de 5 s não é preciso.
        Assert.Equal([new TrechoLimpo(10, 30), new TrechoLimpo(40, 52)], trechos);
    }

    [Fact]
    public void OutroFalanteNaoEntraNaConta()
    {
        var linha = new[] { D(0, 10, "Speaker 1"), D(20, 30, "Speaker 2") };

        Assert.Equal(new TrechoLimpo(20, 30),
            Assert.Single(AprendizadoDeVozes.TrechosDaDiarizacao(linha, "Speaker 2")));
    }

    // ------------------------------------------------ Rotulos

    [Fact]
    public void OsRotulosSaoOsMesmosDaAtribuicao()
    {
        // O reconhecimento procura pelo rótulo que está no segmento; se os dois
        // mapas divergissem, ele extrairia a voz de outra pessoa.
        var diarizacao = new[]
        {
            D(0, 5, "SPEAKER_01"), D(5, 10, "SPEAKER_00"), D(10, 15, VozDoDono.Rotulo),
        };
        var segmentos = new List<SegmentoFinal> { Seg(0, 5, null), Seg(5, 10, null) };

        Montagem.AtribuirFalantes(segmentos, diarizacao);
        var rotulos = Montagem.Rotulos(diarizacao);

        Assert.Equal(segmentos[0].Speaker, rotulos["SPEAKER_01"]);
        Assert.Equal(segmentos[1].Speaker, rotulos["SPEAKER_00"]);
        Assert.Equal(VozDoDono.Rotulo, rotulos[VozDoDono.Rotulo]);
    }

    // ------------------------------------------------ AbsorverIlhas

    [Fact]
    public void IlhaSemNomeNoMeioDaFalaVoltaParaQuemFalava()
    {
        // O sintoma relatado: no meio da fala longa da Vanessa, um "Speaker 4"
        // de uma palavra. Medido nas sete reuniões: das ilhas assim, metade era
        // do vizinho.
        var segmentos = new List<SegmentoFinal>
        {
            Seg(0, 10, "Vanessa"), Seg(10.2, 11, "Speaker 4"), Seg(11.3, 20, "Vanessa"),
        };

        Assert.Equal(1, Montagem.AbsorverIlhas(segmentos));
        Assert.Equal("Vanessa", segmentos[1].Speaker);
    }

    [Fact]
    public void IlhaUnknownTambemVolta()
    {
        var segmentos = new List<SegmentoFinal>
        {
            Seg(0, 10, "Vanessa"), Seg(10, 11, "Unknown"), Seg(11, 20, "Vanessa"),
        };

        Montagem.AbsorverIlhas(segmentos);

        Assert.Equal("Vanessa", segmentos[1].Speaker);
    }

    [Fact]
    public void IlhaComNomeFica()
    {
        // Um "sim" nomeado de outra pessoa é aparte de verdade, e a medição não
        // autoriza mexer nele: absorver todas as ilhas piorou o acerto.
        var segmentos = new List<SegmentoFinal>
        {
            Seg(0, 10, "Vanessa"), Seg(10, 11, "Dimi"), Seg(11, 20, "Vanessa"),
        };

        Assert.Equal(0, Montagem.AbsorverIlhas(segmentos));
        Assert.Equal("Dimi", segmentos[1].Speaker);
    }

    [Fact]
    public void OsVizinhosPrecisamSerAMesmaPessoa()
    {
        var segmentos = new List<SegmentoFinal>
        {
            Seg(0, 10, "Vanessa"), Seg(10, 11, "Speaker 4"), Seg(11, 20, "Dimi"),
        };

        Montagem.AbsorverIlhas(segmentos);

        Assert.Equal("Speaker 4", segmentos[1].Speaker);
    }

    [Fact]
    public void TrechoLongoNaoEIlha()
    {
        var segmentos = new List<SegmentoFinal>
        {
            Seg(0, 10, "Vanessa"), Seg(10, 14, "Speaker 4"), Seg(14, 20, "Vanessa"),
        };

        Montagem.AbsorverIlhas(segmentos);

        Assert.Equal("Speaker 4", segmentos[1].Speaker);
    }

    [Fact]
    public void PausaLongaSeparaAIlhaDosVizinhos()
    {
        // Com mais de um segundo de silêncio em volta, o trecho não está no meio
        // da fala de ninguém: é uma fala sua.
        var segmentos = new List<SegmentoFinal>
        {
            Seg(0, 10, "Vanessa"), Seg(12, 13, "Speaker 4"), Seg(13.2, 20, "Vanessa"),
        };

        Montagem.AbsorverIlhas(segmentos);

        Assert.Equal("Speaker 4", segmentos[1].Speaker);
    }
}
