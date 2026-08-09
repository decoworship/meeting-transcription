namespace MeetingApp.Nucleo;

/// <summary>
/// Descarta segmentos que o ASR inventou sobre silêncio digital.
/// </summary>
/// <remarks>
/// <para>
/// FASE0, resultado 6-A: <b>~5% das palavras transcritas caem sobre zeros
/// exatos</b>, e nenhum ajuste de VAD desce disso — desligar o VAD piora
/// (7,18%), e o melhor limiar testado ainda deixa 5,24%. Sobre ausência de
/// sinal, qualquer palavra é invenção; não é fala baixa que o modelo captou.
/// </para>
/// <para>
/// <b>Zeros exatos, e não energia baixa</b>, é o que dá rigor a isto sem
/// anotador humano. Ruído de sala, respiração e fala sussurrada não entram no
/// critério: só ausência real de sinal, que é o que o gravador escreve quando o
/// canal está mudo ou o dispositivo parou de entregar amostras.
/// </para>
/// </remarks>
public static class FiltroDeSilencio
{
    /// <summary>Quadro de análise, em segundos. O mesmo do <c>sweep_vad.py</c>.</summary>
    public const double Quadro = 0.1;

    /// <summary>
    /// Fração de amostras exatamente zero para o quadro ser silêncio digital.
    /// </summary>
    /// <remarks>Severo de propósito: 99% deixa passar o dither de um bit.</remarks>
    public const double FracaoDeZeros = 0.99;

    /// <summary>
    /// Fração do segmento que precisa cair sobre silêncio digital para ele ser
    /// descartado.
    /// </summary>
    /// <remarks>
    /// Dois terços, e não a maioria simples: um segmento que atravessa a
    /// fronteira entre fala e silêncio contém texto real na parte com sinal, e
    /// jogá-lo fora perderia palavras verdadeiras. O custo dos dois erros é
    /// assimétrico — invenção some no meio da ata sem ninguém notar, mas fala
    /// removida é conteúdo que não volta.
    /// </remarks>
    public const double FracaoParaDescartar = 2.0 / 3.0;

    /// <summary>Marca cada quadro de 100 ms como silêncio digital ou não.</summary>
    public static bool[] PerfilDeSilencio(float[] audio)
    {
        int amostrasPorQuadro = (int)(Faixas.TaxaDeAmostragem * Quadro);
        int n = audio.Length / amostrasPorQuadro;
        var silencio = new bool[n];

        for (int q = 0; q < n; q++)
        {
            int zeros = 0;
            int inicio = q * amostrasPorQuadro;
            for (int i = inicio; i < inicio + amostrasPorQuadro; i++)
                if (audio[i] == 0f) zeros++;

            silencio[q] = zeros / (double)amostrasPorQuadro > FracaoDeZeros;
        }
        return silencio;
    }

    /// <summary>
    /// Remove da lista os segmentos que caem majoritariamente sobre silêncio
    /// digital.
    /// </summary>
    /// <param name="audio">O mix — o mesmo áudio que o ASR transcreveu.</param>
    /// <returns>Os segmentos descartados, para o doc e para a UI poder explicar.</returns>
    public static List<SegmentoFinal> Filtrar(List<SegmentoFinal> segmentos, float[] audio)
    {
        var silencio = PerfilDeSilencio(audio);
        var descartados = new List<SegmentoFinal>();

        segmentos.RemoveAll(seg =>
        {
            int a = (int)(seg.Start / Quadro);
            int b = Math.Min((int)(seg.End / Quadro), silencio.Length);
            if (b <= a) return false;

            int mudos = 0;
            for (int q = a; q < b; q++) if (silencio[q]) mudos++;

            if (mudos / (double)(b - a) <= FracaoParaDescartar) return false;

            descartados.Add(seg);
            return true;
        });

        return descartados;
    }
}
