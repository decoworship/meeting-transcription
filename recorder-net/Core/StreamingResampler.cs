using NAudio.Wave;
using NAudio.Wave.SampleProviders;

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
/// O equivalente aqui é o <see cref="WdlResamplingSampleProvider"/>: streaming,
/// 100% gerenciado e — o que decide — dentro do <c>NAudio.Core</c>, então este
/// projeto continua <c>net8.0</c> portátil, sem depender do Media Foundation
/// (que o <c>MediaFoundationResampler</c> exigiria) num app de bandeja.
/// </para>
/// <para>
/// Qualidade do WDL é suficiente para fala a 16 kHz, que é o alvo — o áudio
/// existe para alimentar o Whisper, não para masterização.
/// </para>
/// </remarks>
public sealed class StreamingResampler
{
    private readonly FilaDeAmostras? _fonte;
    private readonly WdlResamplingSampleProvider? _resampler;
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

        _fonte = new FilaDeAmostras(taxaOrigem);
        _resampler = new WdlResamplingSampleProvider(_fonte, taxaDestino);
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

        _fonte!.Enfileirar(bloco);
        _amostrasEntrada += bloco.Length;

        // Teto proporcional à razão de taxas, com folga para o filtro.
        int maximo = (int)((long)bloco.Length * _taxaDestino / _taxaOrigem) + 64;
        var destino = new float[maximo];
        int lidas = _resampler.Read(destino, 0, maximo);
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

        _fonte!.Enfileirar(new float[_taxaOrigem / 20]);      // 50 ms para expulsar o atraso
        var destino = new float[devidas];
        int lidas = _resampler.Read(destino, 0, devidas);
        _amostrasSaida += lidas;
        return lidas == devidas ? destino : destino[..lidas];
    }

    /// <summary>
    /// Adaptador entre "empurrar blocos" e o modelo de puxar do NAudio.
    /// </summary>
    /// <remarks>
    /// <para>
    /// O <see cref="ISampleProvider"/> é pull: o resampler chama
    /// <see cref="Read"/> quando precisa. A captura é push: o driver entrega
    /// blocos. Esta fila costura os dois.
    /// </para>
    /// <para>
    /// <b>Leitura curta, nunca preenchida com zeros.</b> A primeira versão
    /// completava com silêncio quando a fila esvaziava, para "não travar o
    /// resampler". O teste de continuidade reprovou na hora: o resampler pede
    /// mais do que há para olhar à frente, recebia silêncio fantasma e o
    /// misturava ao sinal — 0,654 de salto nas fronteiras e 16640 amostras onde
    /// deveriam ser 16000. Devolver menos do que foi pedido é o contrato certo
    /// do <see cref="ISampleProvider"/> para fonte que ainda não tem dados.
    /// </para>
    /// </remarks>
    private sealed class FilaDeAmostras(int taxa) : ISampleProvider
    {
        private readonly Queue<float> _fila = new();
        public WaveFormat WaveFormat { get; } =
            WaveFormat.CreateIeeeFloatWaveFormat(taxa, 1);

        public int Disponivel => _fila.Count;

        public void Enfileirar(ReadOnlySpan<float> bloco)
        {
            foreach (float f in bloco) _fila.Enqueue(f);
        }

        public int Read(float[] destino, int offset, int quantidade)
        {
            int i = 0;
            while (i < quantidade && _fila.Count > 0) destino[offset + i++] = _fila.Dequeue();
            return i;
        }
    }
}
