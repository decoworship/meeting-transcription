using NAudio.Dsp;

namespace MeetingRecorder.Core;

/// <summary>
/// Reamostra para 16 kHz mantendo o estado do filtro entre blocos.
/// </summary>
/// <remarks>
/// <para>
/// <b>Por que estado importa.</b> O gravador Python usa
/// <c>soxr.ResampleStream</c> e o comentário de lá explica: <c>soxr.resample()</c>
/// é one-shot — chamada por bloco, ela reinicia o filtro a cada chamada e injeta
/// uma descontinuidade em cada fronteira, audível como crepitação. Um resampler
/// de streaming carrega o estado, que é o que captura contínua exige.
/// </para>
/// <para>
/// O equivalente aqui é o <see cref="WdlResampler"/>: streaming, 100%
/// gerenciado e — o que decide — dentro do <c>NAudio.Core</c>, então este
/// projeto continua <c>net8.0</c> portátil, sem depender do Media Foundation
/// (que o <c>MediaFoundationResampler</c> exigiria) num app de bandeja.
/// </para>
/// <para>
/// <b>Com filtro sinc, e isso não é luxo.</b> A primeira versão usava o
/// <c>WdlResamplingSampleProvider</c>, que é o mesmo WDL configurado <i>sem</i>
/// sinc, sob a justificativa de que "a qualidade é suficiente para fala a
/// 16 kHz". Nunca foi medida, e estava errada: um tom de 10 kHz — acima do
/// Nyquist de 8 kHz, portanto obrigado a desaparecer — voltava rebatido em
/// 6 kHz a <b>−43 dB</b>. Numa gravação real de 57 min isso deixou a banda de
/// 7–8 kHz <b>36 dB acima</b> da do gravador Python, e o usuário ouviu como
/// craquelado antes de qualquer métrica apontar para lá.
/// </para>
/// <para>
/// Medido, e é por isso que o filtro é de 256 taps e não de 64: o alias fica em
/// −60,5 dB com 64, −68,6 dB com 128 e <b>−109,9 dB com 256</b>. O
/// <c>filtercnt</c> não muda nada nesta razão de taxas (0 e 1 dão o mesmo
/// número), então fica em 0. Ver
/// <c>StreamingResamplerTests.TomAcimaDoNyquistNaoVoltaComoAlias</c>.
/// </para>
/// </remarks>
public sealed class StreamingResampler
{
    private readonly WdlResampler? _resampler;
    private readonly int _taxaOrigem;
    private readonly int _taxaDestino;

    // Contabilidade de entrada e saída: é o que permite ao Drenar() distinguir
    // "cauda legítima retida no filtro" de "silêncio que empurrei para expulsá-la".
    private long _amostrasEntrada;
    private long _amostrasSaida;

    public StreamingResampler(int taxaOrigem, int taxaDestino = CrashSafeWavWriter.TaxaAlvo)
    {
        _taxaOrigem = taxaOrigem;
        _taxaDestino = taxaDestino;
        if (taxaOrigem == taxaDestino) return;      // passthrough, sem filtro

        _resampler = new WdlResampler();
        // sinc de 256 taps: é o que separa −43 dB de alias (audível) de −110 dB.
        _resampler.SetMode(interp: true, filtercnt: 0, sinc: true,
                           sinc_size: 256, sinc_interpsize: 64);
        _resampler.SetFilterParms();
        // Alimentado pela entrada: a captura empurra blocos do driver, não pede
        // uma quantidade de saída.
        _resampler.SetFeedMode(wantInputDriven: true);
        _resampler.SetRates(taxaOrigem, taxaDestino);
    }

    /// <summary>Reamostra um bloco, devolvendo o que o filtro já pode entregar.</summary>
    /// <remarks>
    /// A saída de um bloco não tem tamanho previsível: o filtro segura amostras
    /// para ter contexto, e no começo pode devolver vazio. Quem chama precisa
    /// tolerar bloco vazio em vez de tratá-lo como fim de fluxo — o gravador
    /// Python tem exatamente esse <c>if audio.size == 0: continue</c>.
    /// </remarks>
    public float[] Processar(ReadOnlySpan<float> bloco)
    {
        if (_resampler is null) return bloco.ToArray();
        if (bloco.IsEmpty) return [];

        _amostrasEntrada += bloco.Length;

        // O WDL entrega o próprio buffer de entrada para ser preenchido: copiar
        // para dentro dele é o contrato, não uma otimização.
        int aceitas = _resampler.ResamplePrepare(bloco.Length, 1, out float[] entrada, out int offset);
        bloco[..aceitas].CopyTo(entrada.AsSpan(offset));

        // Teto proporcional à razão de taxas, com folga para o filtro.
        int maximo = (int)((long)aceitas * _taxaDestino / _taxaOrigem) + 64;
        var destino = new float[maximo];
        int lidas = _resampler.ResampleOut(destino, 0, aceitas, maximo, 1);
        _amostrasSaida += lidas;
        return lidas == maximo ? destino : destino[..lidas];
    }

    /// <summary>
    /// Drena o que ficou dentro do filtro no fim da gravação.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Sem isto a cauda da gravação some — algumas dezenas de milissegundos, o
    /// suficiente para cortar a última palavra. O Python faz o mesmo com
    /// <c>resample_chunk(..., last=True)</c>.
    /// </para>
    /// <para>
    /// <b>A saída é limitada pela contabilidade, não pelo que o filtro devolve.</b>
    /// Para expulsar o atraso é preciso empurrar silêncio, e esse silêncio é
    /// áudio que não existiu: sem o teto, ele entraria na faixa, inflaria
    /// <c>frames_written</c> e portanto a duração no <c>meta.json</c> — e o
    /// critério A, que compara amostra a amostra com o gravador Python,
    /// esbarraria nisso. O teto é <c>entrada × razão − já entregue</c>, que é o
    /// número de amostras que legitimamente correspondem ao áudio capturado.
    /// </para>
    /// </remarks>
    public float[] Drenar()
    {
        if (_resampler is null) return [];

        long esperadas = (long)(_amostrasEntrada * (double)_taxaDestino / _taxaOrigem);
        int devidas = (int)Math.Max(0, esperadas - _amostrasSaida);
        if (devidas == 0) return [];

        // 50 ms de silêncio para expulsar o atraso retido no filtro.
        int empurrar = _taxaOrigem / 20;
        int aceitas = _resampler.ResamplePrepare(empurrar, 1, out float[] entrada, out int offset);
        Array.Clear(entrada, offset, aceitas);

        var destino = new float[devidas];
        int lidas = _resampler.ResampleOut(destino, 0, aceitas, devidas, 1);
        _amostrasSaida += lidas;
        return lidas == devidas ? destino : destino[..lidas];
    }

}
