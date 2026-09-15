using MeetingApp.Nucleo;
using MeetingApp.Sidecar;
using Xunit;

namespace MeetingApp.Tests;

/// <summary>
/// O dono, quando o motor não carimba palavra.
/// </summary>
/// <remarks>
/// <para>
/// <b>O defeito que este arquivo guarda, medido em campo.</b> Na primeira
/// transcrição de verdade com o MOSS, em 09/09/2026, a faixa do microfone rendeu
/// <b>9 trechos do dono</b> e a saída teve <b>zero</b> trechos <c>You</c>: o dono
/// da gravação apareceu como mais um participante, nomeado pelo banco de vozes
/// como qualquer outro.
/// </para>
/// <para>
/// <b>A causa é uma dependência escondida entre duas peças.</b> Desde 21/08/2026
/// o dono vence por <i>corte</i>: o <see cref="VozDoDono.Juntar"/> põe os trechos
/// dele na linha do tempo <b>sem recortar</b> os do diarizador — o comentário
/// dele diz isso — e quem resolve a sobreposição é o
/// <see cref="Montagem.RepartirPorFalante"/>, cortando o segmento na fronteira.
/// <b>E o corte precisa das palavras.</b> Sem elas, o
/// <see cref="Montagem.AtribuirFalantes"/> soma sobreposição sobre o segmento
/// inteiro: o intervalo do diarizador cobre 100% dele e o do dono cobre uma
/// fração, então o diarizador vence sempre.
/// </para>
/// <para>
/// O caminho clássico nunca sentiu isso porque o <c>faster-whisper</c> sempre
/// devolveu alinhamento por palavra. O MOSS carimba por segmento — e o sintoma é
/// mudo: nada falha, nada avisa, o dono simplesmente não está lá.
/// </para>
/// </remarks>
public sealed class DonoSemPalavrasTests
{
    /// <summary>Um segmento longo, sem alinhamento por palavra — como o MOSS o produz.</summary>
    private static SegmentoFinal Longo() =>
        new() { Start = 0, End = 10, Text = " uma fala longa e sem palavras carimbadas" };

    [Fact]
    public void SemPalavrasOSegmentoNaoPodeSerCortado()
    {
        // É a raiz de tudo: o corte é o mecanismo, e ele não tem como agir.
        var segmentos = new List<SegmentoFinal> { Longo() };
        var linha = new List<SegmentoDeFalante>
        {
            new(0, 10, "SPEAKER_00"),          // o diarizador cobre o segmento inteiro
            new(4, 6, VozDoDono.Rotulo),       // o dono fala 2 s no meio
        };

        Assert.Equal(0, Montagem.RepartirPorFalante(segmentos, linha));
        Assert.Single(segmentos);              // continua um só, inteiro
    }

    [Fact]
    public void ESemCorteODonoPerdeAContagemDeSobreposicao()
    {
        // O que aconteceu na reunião de 09/09: o dono existe na linha do tempo,
        // e mesmo assim não ganha um único trecho.
        var segmentos = new List<SegmentoFinal> { Longo() };
        var linha = new List<SegmentoDeFalante>
        {
            new(0, 10, "SPEAKER_00"),
            new(4, 6, VozDoDono.Rotulo),
        };

        Montagem.RepartirPorFalante(segmentos, linha);
        Montagem.AtribuirFalantes(segmentos, linha);

        Assert.NotEqual(VozDoDono.Rotulo, segmentos[0].Speaker);
    }

    [Fact]
    public void ComPalavrasODonoVence()
    {
        // O contraste que prova que o mecanismo é o corte, e não outra coisa: o
        // mesmo segmento, a mesma linha do tempo, só que com alinhamento.
        var seg = Longo();
        seg.Words = [.. Enumerable.Range(0, 10).Select(i => new PalavraDita
        {
            Start = i, End = i + 1, Text = $" p{i}",
        })];
        var segmentos = new List<SegmentoFinal> { seg };
        var linha = new List<SegmentoDeFalante>
        {
            new(0, 10, "SPEAKER_00"),
            new(4, 6, VozDoDono.Rotulo),
        };

        Assert.True(Montagem.RepartirPorFalante(segmentos, linha) > 0);
        Montagem.AtribuirFalantes(segmentos, linha);

        Assert.Contains(segmentos, s => s.Speaker == VozDoDono.Rotulo);
    }

    [Fact]
    public void OAtribuirDonoResolveSemPrecisarDePalavra()
    {
        // A saída que o caminho do MOSS usa: decidir por CANAL, segmento a
        // segmento. Ela não corta nada — dá o segmento inteiro a quem dominou o
        // microfone —, e é essa a troca que o Transcritor documenta. O que se
        // ganha é o dono existir; a faixa do microfone não estima, ela sabe.
        int n = (int)(10 * Faixas.TaxaDeAmostragem);
        var mic = new float[n];
        var sistema = new float[n];
        for (int i = 0; i < n; i++)
        {
            double t = i / (double)Faixas.TaxaDeAmostragem;
            // O dono domina o microfone do começo ao fim deste segmento.
            mic[i] = (float)(0.30 * Math.Sin(2 * Math.PI * 200 * t));
            sistema[i] = (float)(0.01 * Math.Sin(2 * Math.PI * 300 * t));
        }

        var segmentos = new List<SegmentoFinal> { Longo() };
        segmentos[0].Speaker = "SPEAKER_00";

        Assert.Equal(1, Montagem.AtribuirDono(segmentos, new Faixas(mic, sistema)));
        Assert.Equal(VozDoDono.Rotulo, segmentos[0].Speaker);
    }
}
