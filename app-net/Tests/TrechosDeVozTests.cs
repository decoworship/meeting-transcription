using MeetingApp.Nucleo;
using Xunit;

namespace MeetingApp.Tests;

/// <summary>
/// A escolha dos blocos que ensinam uma voz (docs/FASE6.md §4.2).
/// </summary>
/// <remarks>
/// É a etapa cujo erro <b>persiste entre reuniões</b>: um vetor contaminado não
/// aparece na transcrição em que foi criado, aparece meses depois pondo o nome
/// errado em outra pessoa. Por isso os testes aqui são sobre o que é
/// <b>descartado</b>, e não sobre o que passa.
/// </remarks>
public sealed class TrechosDeVozTests
{
    private static SegmentoFinal Seg(double de, double ate, string quem) =>
        new() { Start = de, End = ate, Text = "...", Speaker = quem };

    /// <summary>Microfone mudo do começo ao fim: 60 s de zeros.</summary>
    private static float[] MicMudo() => new float[60 * Faixas.TaxaDeAmostragem];

    /// <summary>Microfone com fala de verdade entre dois instantes.</summary>
    private static float[] MicFalandoEntre(double de, double ate)
    {
        var mic = MicMudo();
        for (int i = (int)(de * Faixas.TaxaDeAmostragem);
             i < (int)(ate * Faixas.TaxaDeAmostragem) && i < mic.Length; i++)
            mic[i] = 0.2f;                       // bem acima do piso de 5e-3
        return mic;
    }

    [Fact]
    public void OVetorOlhaAMesmaJanelaQueOTrechoGuardado()
    {
        // O defeito medido: o .wav guardado era cortado em 4 s e o vetor usava
        // o intervalo inteiro. Quem auditasse a amostra ouvindo o arquivo não
        // encontrava a contaminação, porque ela estava fora do que se ouve.
        var trechos = AprendizadoDeVozes.TrechosDe(
            [Seg(0, 43.7, "Speaker 1")], "Speaker 1");

        var t = Assert.Single(trechos);
        Assert.Equal(AprendizadoDeVozes.SegundosDoTrecho, t.Duracao, 3);
        Assert.Equal(0, t.Inicio);
    }

    [Fact]
    public void BlocoComODonoFalandoDentroEDescartado()
    {
        // O caso medido: 13,2 s da voz do dono do microfone dentro do bloco nº 1
        // do vetor de uma participante. A guarda de vizinhos não pegava, porque
        // a contaminação está DENTRO do segmento, não colada nele.
        var segmentos = new List<SegmentoFinal> { Seg(10, 30, "Speaker 1") };

        Assert.Empty(AprendizadoDeVozes.TrechosDe(
            segmentos, "Speaker 1", MicFalandoEntre(11, 13)));
    }

    [Fact]
    public void MicrofoneMudoNaoDescartaNada()
    {
        var segmentos = new List<SegmentoFinal> { Seg(10, 30, "Speaker 1") };

        Assert.Single(AprendizadoDeVozes.TrechosDe(segmentos, "Speaker 1", MicMudo()));
    }

    [Fact]
    public void OMicrofoneSoImportaForaDaJanelaTruncada()
    {
        // O dono fala no segundo 20, e a janela do vetor vai de 10 a 14: o bloco
        // continua limpo. Truncar não é só economia — é o que faz a guarda
        // julgar exatamente o áudio que o vetor vai ver.
        var segmentos = new List<SegmentoFinal> { Seg(10, 30, "Speaker 1") };

        Assert.Single(AprendizadoDeVozes.TrechosDe(
            segmentos, "Speaker 1", MicFalandoEntre(20, 25)));
    }

    [Fact]
    public void ADoDonoNaoEDescartadaPeloProprioMicrofone()
    {
        // A voz do dono sai do mic.wav, onde ele falando é o sinal e não o
        // ruído. Aplicar a guarda a ele apagaria a única inscrição que a
        // gravação de duas faixas dá com certeza.
        var segmentos = new List<SegmentoFinal> { Seg(10, 30, "You") };

        Assert.Single(AprendizadoDeVozes.TrechosDe(
            segmentos, "You", MicFalandoEntre(10, 25)));
    }

    [Fact]
    public void SemAFaixaOResultadoEODeAntes()
    {
        // Quem chama sem o microfone não fica pior do que estava: a guarda não
        // roda, o truncamento sim.
        var segmentos = new List<SegmentoFinal> { Seg(10, 30, "Speaker 1") };

        Assert.Single(AprendizadoDeVozes.TrechosDe(segmentos, "Speaker 1"));
    }

    [Fact]
    public void AGuardaDeVizinhosContinuaValendo()
    {
        // Cross-talk colado no fim do turno: o que a guarda antiga já pegava, e
        // que a nova não substitui.
        var segmentos = new List<SegmentoFinal>
        {
            Seg(0, 10, "Speaker 1"),
            Seg(10.2, 20, "Speaker 2"),
        };

        Assert.Empty(AprendizadoDeVozes.TrechosDe(segmentos, "Speaker 1", MicMudo()));
    }

    [Fact]
    public void SobraOBlocoLimpoQuandoUmDosDoisEstaSujo()
    {
        // O que a guarda deve fazer no caso comum: podar, não zerar. Dois turnos
        // da mesma pessoa, o dono falando só no primeiro.
        var segmentos = new List<SegmentoFinal>
        {
            Seg(0, 8, "Speaker 1"),
            Seg(20, 28, "Speaker 1"),
        };

        var trechos = AprendizadoDeVozes.TrechosDe(
            segmentos, "Speaker 1", MicFalandoEntre(1, 3));

        Assert.Equal(20, Assert.Single(trechos).Inicio);
    }
}
