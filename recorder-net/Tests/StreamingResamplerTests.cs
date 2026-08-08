using MeetingRecorder.Core;
using Xunit;

namespace MeetingRecorder.Tests;

/// <summary>
/// O teste que teria pego o bug do resampler one-shot antes de qualquer ouvido.
/// </summary>
/// <remarks>
/// O comentário do <c>capture.py</c> registra a descoberta: chamar
/// <c>soxr.resample()</c> por bloco reinicia o filtro a cada chamada e injeta
/// descontinuidade em cada fronteira — audível como crepitação. Uma senoide
/// contínua alimentada em blocos expõe isso: se o estado se perdesse, haveria
/// saltos nas emendas.
/// </remarks>
public sealed class StreamingResamplerTests
{
    private static float[] Senoide(int amostras, int taxa, double hz, int faseInicial = 0)
    {
        var s = new float[amostras];
        for (int i = 0; i < amostras; i++)
            s[i] = (float)Math.Sin(2 * Math.PI * hz * (i + faseInicial) / taxa);
        return s;
    }

    /// <summary>Maior salto entre amostras consecutivas.</summary>
    private static float MaiorDescontinuidade(float[] sinal, int ignorarInicio)
    {
        float pior = 0f;
        for (int i = ignorarInicio + 1; i < sinal.Length; i++)
            pior = Math.Max(pior, Math.Abs(sinal[i] - sinal[i - 1]));
        return pior;
    }

    [Fact]
    public void SenoideEmBlocosNaoGeraDescontinuidadeNasFronteiras()
    {
        const int taxaOrigem = 48_000;
        const double hz = 440;
        const int tamanhoBloco = 480;              // 10 ms, como um pacote WASAPI

        var r = new StreamingResampler(taxaOrigem);
        var saida = new List<float>();

        for (int bloco = 0; bloco < 100; bloco++)  // 1 s em 100 pedaços
        {
            var entrada = Senoide(tamanhoBloco, taxaOrigem, hz, bloco * tamanhoBloco);
            saida.AddRange(r.Processar(entrada));
        }

        var sinal = saida.ToArray();
        Assert.True(sinal.Length > 15_000, $"esperado ~16000 amostras, veio {sinal.Length}");

        // A 440 Hz e 16 kHz, o passo máximo entre amostras vizinhas de uma
        // senoide é 2π·440/16000 ≈ 0,17. Uma emenda com o filtro reiniciado
        // produziria salto muito maior. A folga de 3× absorve o ripple do filtro.
        float pior = MaiorDescontinuidade(sinal, ignorarInicio: 200);
        Assert.True(pior < 0.55f,
            $"descontinuidade de {pior:F3} entre amostras — filtro perdendo estado nas fronteiras");
    }

    [Fact]
    public void PassthroughQuandoAsTaxasCoincidem()
    {
        var r = new StreamingResampler(CrashSafeWavWriter.TaxaAlvo);
        var entrada = Senoide(1000, CrashSafeWavWriter.TaxaAlvo, 440);
        var saida = r.Processar(entrada);

        // Sem filtro nenhum: 16 kHz para 16 kHz tem que ser identidade, não
        // passar por reamostragem "neutra" que ainda assim filtraria.
        Assert.Equal(entrada.Length, saida.Length);
        for (int i = 0; i < entrada.Length; i++) Assert.Equal(entrada[i], saida[i]);
    }

    [Fact]
    public void BlocoVazioNaoQuebra()
    {
        var r = new StreamingResampler(48_000);
        Assert.Empty(r.Processar([]));
    }

    [Fact]
    public void RazaoDeTaxasSeReflecteNaContagem()
    {
        var r = new StreamingResampler(48_000);
        var saida = new List<float>();
        for (int i = 0; i < 10; i++)
            saida.AddRange(r.Processar(new float[4800]));   // 1 s total

        // 48 kHz -> 16 kHz é 1/3. Tolerância para o atraso do filtro no começo.
        Assert.InRange(saida.Count, 15_500, 16_100);
    }

    [Fact]
    public void TotalEntregueBateComARazaoDeTaxas()
    {
        // A propriedade que importa não é "Drenar devolve algo" — é que a soma
        // do que saiu corresponde exatamente ao áudio que entrou. Sem o teto de
        // contabilidade, o silêncio empurrado para expulsar o atraso do filtro
        // entraria na faixa e inflaria a duração no meta.json.
        var r = new StreamingResampler(48_000);
        int total = 0;
        for (int i = 0; i < 10; i++)
            total += r.Processar(Senoide(4800, 48_000, 440, i * 4800)).Length;
        total += r.Drenar().Length;

        // 48000 amostras a 48 kHz = 1 s = 16000 amostras a 16 kHz, exatamente.
        Assert.Equal(16_000, total);
    }

    [Fact]
    public void DrenarNaoInventaAudioAlemDoQueEntrou()
    {
        var r = new StreamingResampler(48_000);
        r.Processar(Senoide(4800, 48_000, 440));
        int aposProcessar = 1600;                       // 4800 / 3

        var cauda = r.Drenar();
        var extra = r.Drenar();                          // segunda chamada não deve dar nada

        Assert.True(cauda.Length <= aposProcessar,
            "a cauda não pode exceder o que a razão de taxas permite");
        Assert.Empty(extra);
    }
}
