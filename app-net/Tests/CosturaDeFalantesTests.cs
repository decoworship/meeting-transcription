using MeetingApp.Nucleo;
using Xunit;

namespace MeetingApp.Tests;

/// <summary>
/// A costura: rótulo local de bloco → identidade global, por vetor de voz.
/// </summary>
/// <remarks>
/// <para>
/// <b>O extrator é um delegado, e é isso que torna estes testes possíveis.</b>
/// Quem transforma áudio em vetor é o pyannote, e amarrar a costura a ele
/// tornaria a decisão que importa — <i>este falante é alguém que já falou?</i> —
/// inverificável sem placa. Aqui os vetores são escritos à mão, e o que se mede
/// é a decisão.
/// </para>
/// <para>
/// Os números que mandam no desenho estão na <c>docs/FASE7-RESULTADOS.md</c>
/// §11.2, e o resumo deles é contraintuitivo: <b>o limiar não regula acerto,
/// regula quanta gente o app inventa</b>. Em 0,75 a costura conclui que há 44
/// falantes onde há 8, e a métrica de acerto não enxerga isso porque dividir
/// custa menos que fundir.
/// </para>
/// </remarks>
public sealed class CosturaDeFalantesTests
{
    private static SegmentoFinal Seg(double inicio, double fim, string rotulo) =>
        new() { Start = inicio, End = fim, Text = " oi", Speaker = rotulo };

    /// <summary>Um vetor unitário no eixo indicado — pessoas ortogonais entre si.</summary>
    private static float[] Eixo(int qual, int dimensoes = 8)
    {
        var v = new float[dimensoes];
        v[qual] = 1f;
        return v;
    }

    /// <summary>Um extrator que devolve o vetor combinado com quem fala em cada trecho.</summary>
    private static CosturaDeFalantes.ExtratorDeVoz Extrator(
        Func<IReadOnlyList<(double Inicio, double Fim)>, float[]?> quem) =>
        (trechos, _) => Task.FromResult(quem(trechos));

    [Fact]
    public async Task DoisBlocosDaMesmaPessoaViramUmaIdentidade()
    {
        // É o trabalho que a régua da §7.4 dava de graça ao MOSS: saber que o S0
        // do bloco 0 é a mesma pessoa que o S0 do bloco 1.
        var segmentos = new List<SegmentoFinal>
        {
            Seg(0, 5, "b0_S0"), Seg(10, 15, "b0_S1"),
            Seg(180, 185, "b1_S0"),
        };

        var c = await CosturaDeFalantes.CosturarAsync(segmentos,
            Extrator(t => t[0].Inicio < 10 || t[0].Inicio >= 180 ? Eixo(0) : Eixo(1)));

        Assert.Equal(3, c.RotulosLocais);
        Assert.Equal(2, c.Identidades);
        // O primeiro e o terceiro segmento são a mesma pessoa; o do meio, outra.
        Assert.Equal(c.Falantes[0].Falante, c.Falantes[2].Falante);
        Assert.NotEqual(c.Falantes[0].Falante, c.Falantes[1].Falante);
    }

    [Fact]
    public async Task VozesDiferentesNaoSaoFundidas()
    {
        var segmentos = new List<SegmentoFinal>
        {
            Seg(0, 5, "b0_S0"), Seg(180, 185, "b1_S0"), Seg(360, 365, "b2_S0"),
        };

        int n = 0;
        var c = await CosturaDeFalantes.CosturarAsync(segmentos, Extrator(_ => Eixo(n++)));

        Assert.Equal(3, c.Identidades);
    }

    [Fact]
    public async Task OsRotulosSaemNoFormatoQueOPipelineJaEsperava()
    {
        // **SPEAKER_00 e não P1**, e a razão é numérica: o Montagem.AtribuirFalantes
        // numera os falantes pela ordem ALFABÉTICA do rótulo cru, e "P1, P10, P11,
        // P2" faria a décima pessoa a falar virar "Speaker 2". Com dois dígitos a
        // ordem alfabética é a ordem de chegada.
        var segmentos = new List<SegmentoFinal> { Seg(0, 5, "b0_S0"), Seg(180, 185, "b1_S0") };

        int n = 0;
        var c = await CosturaDeFalantes.CosturarAsync(segmentos, Extrator(_ => Eixo(n++)));

        Assert.Equal("SPEAKER_00", c.Falantes[0].Falante);
        Assert.Equal("SPEAKER_01", c.Falantes[1].Falante);
    }

    [Fact]
    public async Task AOrdemEADeChegadaENaoADoNomeDoRotulo()
    {
        // O centroide de cada identidade depende de quem entrou nela antes, então
        // ordenar por outra coisa daria outro resultado. Ao vivo não há futuro
        // para olhar, e é isso que a ordem de chegada preserva.
        var segmentos = new List<SegmentoFinal>
        {
            Seg(300, 305, "b1_S0"),      // fora de ordem na lista, tarde no tempo
            Seg(0, 5, "b0_S9"),          // e este é o primeiro a falar
        };

        var vistos = new List<double>();
        await CosturaDeFalantes.CosturarAsync(segmentos, Extrator(t =>
        {
            vistos.Add(t[0].Inicio);
            return Eixo(vistos.Count - 1);
        }));

        Assert.Equal([0, 300], vistos);
    }

    [Fact]
    public async Task SemFalaLimpaBastanteORotuloViraGenteNova()
    {
        // Inventar um vínculo sem vetor é pior que admitir que não se sabe. E
        // errar dividindo é o erro barato: quem lê junta duas linhas.
        var segmentos = new List<SegmentoFinal>
        {
            Seg(0, 5, "b0_S0"),
            Seg(180, 185, "b1_S0"),
        };

        var c = await CosturaDeFalantes.CosturarAsync(segmentos,
            Extrator(t => t[0].Inicio == 0 ? Eixo(0) : null));

        Assert.Equal(1, c.SemVoz);
        Assert.Equal(2, c.Identidades);
    }

    [Fact]
    public async Task FalaCurtaDemaisNemChegaAoMotor()
    {
        // Abaixo do piso o motor devolveria um vetor ruidoso — e vetor ruidoso
        // casa com qualquer um, que é o modo de falha que envenena em silêncio.
        var segmentos = new List<SegmentoFinal> { Seg(0, 0.4, "b0_S0") };

        bool chamou = false;
        var c = await CosturaDeFalantes.CosturarAsync(segmentos,
            Extrator(_ => { chamou = true; return Eixo(0); }));

        Assert.False(chamou);
        Assert.Equal(1, c.SemVoz);
    }

    [Fact]
    public void OTrechoEhTruncadoNaMesmaJanelaQueOBancoDeVozes()
    {
        // A janela que o vetor enxerga tem de ser a janela que alguém conseguiria
        // auditar. Mesmo truncamento do AprendizadoDeVozes, e pelo mesmo motivo.
        var trechos = CosturaDeFalantes.TrechosDoRotulo(
            [Seg(0, 60, "b0_S0")]);

        var (inicio, fim) = Assert.Single(trechos);
        Assert.Equal(0, inicio);
        Assert.Equal(AprendizadoDeVozes.SegundosDoTrecho, fim);
    }

    [Fact]
    public void OLimiarDaReuniaoENaoODoBanco()
    {
        // Duas medições independentes disseram que 0,70 é apertado demais dentro
        // da mesma reunião — o T3.1 achou 0,60 para nomear cedo e a costura achou
        // 0,55 (§8.3 e §11.3). O do banco, que governa o caso difícil, não muda.
        Assert.Equal(0.55, Vozes.LimiarDeCosturaNaReuniao);
        Assert.Equal(0.70, Vozes.LimiarDeReconhecimento);
        Assert.True(Vozes.LimiarDeCosturaNaReuniao < Vozes.LimiarDeReconhecimento);
    }
}
