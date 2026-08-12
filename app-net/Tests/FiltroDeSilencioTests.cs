using MeetingApp.Nucleo;
using Xunit;

namespace MeetingApp.Tests;

/// <summary>
/// O filtro de invenção sobre silêncio digital (FASE0, resultado 6-A).
/// </summary>
public sealed class FiltroDeSilencioTests
{
    /// <summary>Áudio de <paramref name="segundos"/> s, com sinal só onde pedido.</summary>
    private static float[] Audio(double segundos, params (double De, double Ate)[] comSinal)
    {
        var a = new float[(int)(segundos * Faixas.TaxaDeAmostragem)];
        foreach (var (de, ate) in comSinal)
        {
            int i0 = (int)(de * Faixas.TaxaDeAmostragem);
            int i1 = (int)(ate * Faixas.TaxaDeAmostragem);
            for (int i = i0; i < i1 && i < a.Length; i++)
                a[i] = 0.3f * MathF.Sin(2 * MathF.PI * 440 * i / Faixas.TaxaDeAmostragem);
        }
        return a;
    }

    [Fact]
    public void SegmentoSobreZerosEhDescartado()
    {
        // Sobre ausência de sinal qualquer palavra é invenção — não é fala baixa
        // que o modelo captou.
        var segmentos = new List<SegmentoFinal>
        {
            new() { Start = 2, End = 4, Text = "obrigado por assistir" },
        };

        var fora = FiltroDeSilencio.Filtrar(segmentos, Audio(10, (0, 1)));

        Assert.Empty(segmentos);
        Assert.Equal("obrigado por assistir", Assert.Single(fora).Text);
    }

    [Fact]
    public void SegmentoSobreFalaEhMantido()
    {
        var segmentos = new List<SegmentoFinal>
        {
            new() { Start = 2, End = 4, Text = "bom dia a todos" },
        };

        Assert.Empty(FiltroDeSilencio.Filtrar(segmentos, Audio(10, (0, 10))));
        Assert.Single(segmentos);
    }

    [Fact]
    public void SegmentoQueAtravessaAFronteiraEhMantido()
    {
        // Metade sobre sinal: contém fala real, e removê-lo perderia palavras
        // verdadeiras. Invenção some no meio da ata sem ninguém notar; fala
        // removida é conteúdo que não volta — o custo dos dois erros é
        // assimétrico, e o limiar de 2/3 vem daí.
        var segmentos = new List<SegmentoFinal>
        {
            new() { Start = 2, End = 6, Text = "estava dizendo que" },
        };

        Assert.Empty(FiltroDeSilencio.Filtrar(segmentos, Audio(10, (0, 4))));
        Assert.Single(segmentos);
    }

    [Fact]
    public void RuidoBaixoNaoContaComoSilencioDigital()
    {
        // O critério é zeros exatos, não energia baixa: ruído de sala e
        // respiração não podem virar licença para apagar transcrição.
        var audio = new float[10 * Faixas.TaxaDeAmostragem];
        for (int i = 0; i < audio.Length; i++) audio[i] = 1e-5f;   // inaudível, mas não zero

        var segmentos = new List<SegmentoFinal> { new() { Start = 2, End = 4, Text = "sim" } };

        Assert.Empty(FiltroDeSilencio.Filtrar(segmentos, audio));
        Assert.Single(segmentos);
    }
}
