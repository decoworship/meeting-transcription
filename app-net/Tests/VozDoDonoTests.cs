using MeetingApp.Nucleo;
using MeetingApp.Sidecar;
using Xunit;

namespace MeetingApp.Tests;

/// <summary>
/// A faixa do microfone virando trechos de falante.
/// </summary>
/// <remarks>
/// Os casos aqui são os medidos em 21/08/2026 contra a transcrição do Meet, em
/// duas reuniões — não são hipóteses sobre o que poderia dar errado. O que
/// interessa provar é que <b>sobreposição vira corte, e não troca de dono</b>:
/// era esse o defeito. Medido: ele reduz o erro de palavra da fala do dono pela
/// metade na gravação com sobreposição de verdade (67 palavras erradas para 31).
/// </remarks>
public sealed class VozDoDonoTests
{
    private const int Taxa = Faixas.TaxaDeAmostragem;

    /// <summary>Um bloco de "fala": ruído no nível certo, e não silêncio.</summary>
    private static void Preencher(float[] destino, double de, double ate, float nivel)
    {
        int i0 = (int)(de * Taxa), i1 = Math.Min((int)(ate * Taxa), destino.Length);
        // Alterna o sinal para o RMS bater com o nível pedido sem virar DC.
        for (int i = i0; i < i1; i++) destino[i] = i % 2 == 0 ? nivel : -nivel;
    }

    private static Faixas Montar(double segundos, Action<float[], float[]> encher)
    {
        var mic = new float[(int)(segundos * Taxa)];
        var sistema = new float[(int)(segundos * Taxa)];
        encher(mic, sistema);
        return new Faixas(mic, sistema);
    }

    [Fact]
    public void FalaDoDonoViraTrecho()
    {
        var faixas = Montar(5, (mic, _) => Preencher(mic, 1.0, 2.0, 0.08f));

        var trilha = VozDoDono.Trilha(faixas);

        var trecho = Assert.Single(trilha);
        Assert.Equal(VozDoDono.Rotulo, trecho.Falante);
        Assert.InRange(trecho.Inicio, 0.95, 1.05);
        Assert.InRange(trecho.Fim, 1.95, 2.10);
    }

    [Fact]
    public void SobreposicaoNaoApagaOTrechoDoDono()
    {
        // O caso medido: o dono fala e alguém fala junto, mais alto. A regra
        // antiga (rmsMic > rmsSistema * 2) reprovava e o segmento inteiro ia
        // para o outro falante. Aqui o trecho do dono existe do mesmo jeito,
        // porque a faixa dele não é comparada com nada.
        var faixas = Montar(5, (mic, sistema) =>
        {
            Preencher(mic, 1.0, 2.0, 0.084f);
            Preencher(sistema, 0.5, 3.0, 0.076f);
        });

        var trecho = Assert.Single(VozDoDono.Trilha(faixas));
        Assert.InRange(trecho.Inicio, 0.95, 1.05);
    }

    [Fact]
    public void RuidoDeFundoNaoViraFala()
    {
        // 0,0002 é o nível medido do microfone com fone enquanto outra pessoa
        // fala. Se isto virasse trecho, a fala dos outros seria roubada.
        var faixas = Montar(5, (mic, _) => Preencher(mic, 0, 5, 0.0002f));

        Assert.Empty(VozDoDono.Trilha(faixas));
    }

    [Fact]
    public void PausaCurtaNaoParteOTrecho()
    {
        var faixas = Montar(5, (mic, _) =>
        {
            Preencher(mic, 1.0, 1.6, 0.08f);
            Preencher(mic, 1.7, 2.3, 0.08f);   // 100 ms de pausa: é entre palavras
        });

        var trecho = Assert.Single(VozDoDono.Trilha(faixas));
        Assert.InRange(trecho.Fim, 2.25, 2.40);
    }

    [Fact]
    public void PausaLongaParteOTrecho()
    {
        var faixas = Montar(8, (mic, _) =>
        {
            Preencher(mic, 1.0, 2.0, 0.08f);
            Preencher(mic, 4.0, 5.0, 0.08f);
        });

        Assert.Equal(2, VozDoDono.Trilha(faixas).Count);
    }

    [Fact]
    public void EstaloNaoViraTrecho()
    {
        var faixas = Montar(5, (mic, _) => Preencher(mic, 1.0, 1.1, 0.5f));

        Assert.Empty(VozDoDono.Trilha(faixas));
    }

    [Fact]
    public void VazamentoDeCaixaDeSomDesligaATrilha()
    {
        // Sem fone, o microfone carrega uma cópia atenuada da reunião inteira.
        // Nenhum limiar absoluto separaria o dono dos outros, então a trilha não
        // é produzida e o pipeline volta a ser o de antes — que é seguro.
        var faixas = Montar(10, (mic, sistema) =>
        {
            Preencher(sistema, 0, 10, 0.10f);
            Preencher(mic, 0, 10, 0.03f);      // vazamento contínuo
        });

        Assert.False(VozDoDono.SemVazamento(faixas));
        Assert.Empty(VozDoDono.Trilha(faixas));
    }

    [Fact]
    public void FoneNaoTemVazamento()
    {
        var faixas = Montar(10, (mic, sistema) =>
        {
            Preencher(sistema, 0, 10, 0.10f);
            Preencher(mic, 2, 3, 0.08f);       // o dono fala uma vez, só
        });

        Assert.True(VozDoDono.SemVazamento(faixas));
        Assert.NotEmpty(VozDoDono.Trilha(faixas));
    }

    [Fact]
    public void JuntarPreservaOsDoisLados()
    {
        IReadOnlyList<SegmentoDeFalante> pyannote =
            [new(0, 5, "SPEAKER_00"), new(5, 9, "SPEAKER_01")];
        IReadOnlyList<SegmentoDeFalante> dono = [new(3, 6, VozDoDono.Rotulo)];

        var junto = VozDoDono.Juntar(pyannote, dono);

        Assert.Equal(3, junto.Count);
        // Os trechos do pyannote não são recortados: sobreposição é o estado
        // normal, e quem a resolve é o corte por palavra, depois.
        Assert.Contains(junto, t => t is { Inicio: 0, Fim: 5, Falante: "SPEAKER_00" });
        Assert.Contains(junto, t => t.Falante == VozDoDono.Rotulo);
    }

    [Fact]
    public void SemTrilhaJuntarNaoMexeNaDiarizacao()
    {
        IReadOnlyList<SegmentoDeFalante> pyannote = [new(0, 5, "SPEAKER_00")];

        Assert.Same(pyannote, VozDoDono.Juntar(pyannote, []));
    }

    [Fact]
    public void ODonoNaoViraSpeakerNumerado()
    {
        // Ele entra na mesma lista que o pyannote, mas o rótulo dele é um nome,
        // não uma etiqueta a resolver depois.
        var segmentos = new List<SegmentoFinal>
        {
            new() { Start = 0, End = 2, Text = "oi" },
            new() { Start = 3, End = 5, Text = "tudo bem" },
        };
        IReadOnlyList<SegmentoDeFalante> diarizacao =
            [new(0, 2, "SPEAKER_00"), new(3, 5, VozDoDono.Rotulo)];

        Montagem.AtribuirFalantes(segmentos, diarizacao);

        Assert.Equal("Speaker 1", segmentos[0].Speaker);
        Assert.Equal(VozDoDono.Rotulo, segmentos[1].Speaker);
    }

    [Fact]
    public void SegmentoComDonoDentroEhCortadoEmVezDeTrocarDeDono()
    {
        // O desfecho que justifica a mudança inteira: o outro fala, o dono
        // responde por cima no meio, e o ASR devolveu um segmento só. Antes,
        // o segmento inteiro ia para um dos dois. Agora ele parte.
        var segmento = new SegmentoFinal
        {
            Start = 0,
            End = 4,
            Text = " então eu acho exato que sim",
            Words =
            [
                new() { Start = 0.0, End = 0.5, Text = " então" },
                new() { Start = 0.5, End = 1.0, Text = " eu" },
                new() { Start = 1.0, End = 1.5, Text = " acho" },
                new() { Start = 2.0, End = 2.5, Text = " exato" },
                new() { Start = 3.0, End = 3.5, Text = " que" },
                new() { Start = 3.5, End = 4.0, Text = " sim" },
            ],
        };
        var segmentos = new List<SegmentoFinal> { segmento };
        IReadOnlyList<SegmentoDeFalante> diarizacao =
            [new(0, 4, "SPEAKER_00"), new(1.9, 2.6, VozDoDono.Rotulo)];

        int cortados = Montagem.RepartirPorFalante(segmentos, diarizacao);
        Montagem.AtribuirFalantes(segmentos, diarizacao);

        Assert.Equal(1, cortados);
        Assert.Equal(3, segmentos.Count);
        Assert.Equal(VozDoDono.Rotulo, segmentos[1].Speaker);
        Assert.Contains("exato", segmentos[1].Text);
        // E o texto continua somando o original — nada se perde no corte.
        Assert.Equal(segmento.Text, string.Concat(segmentos.Select(s => s.Text)));
    }
}
