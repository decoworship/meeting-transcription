namespace MeetingRecorder.Core;

/// <summary>Anomalias que o WASAPI sinaliza por pacote.</summary>
[Flags]
public enum AnomaliaPacote
{
    Nenhuma = 0,
    /// <summary>AUDCLNT_BUFFERFLAGS_SILENT — o pacote é silêncio; nem precisa converter.</summary>
    Silencio = 1,
    /// <summary>AUDCLNT_BUFFERFLAGS_DATA_DISCONTINUITY — houve descontinuidade nos dados.</summary>
    Descontinuidade = 2,
    /// <summary>AUDCLNT_BUFFERFLAGS_TIMESTAMP_ERROR — o carimbo de tempo não é confiável.</summary>
    ErroDeTimestamp = 4,
}

/// <summary>O que fazer com um pacote recém-chegado.</summary>
/// <param name="SilencioAntes">Amostras de silêncio a inserir antes do pacote (na taxa alvo).</param>
/// <param name="PosicaoAlvo">Posição do fim deste pacote na linha do tempo, em amostras da taxa alvo.</param>
public readonly record struct DecisaoPacote(int SilencioAntes, long PosicaoAlvo);

/// <summary>
/// Traduz a posição carimbada em cada pacote pelo WASAPI numa linha do tempo em
/// amostras de 16 kHz, detectando os buracos.
/// </summary>
/// <remarks>
/// <para>
/// <b>Por que os buracos não podem ser inferidos por relógio.</b> O loopback
/// WASAPI não entrega pacote enquanto nada toca — numa reunião em que ninguém
/// fala por 30 s, o <c>DataAvailable</c> simplesmente não dispara (armadilha do
/// NAudio registrada no PLANO). Se o laço de escrita não preencher esse vazio, a
/// faixa encolhe e desalinha da outra.
/// </para>
/// <para>
/// Com o laço próprio sobre <c>AudioCaptureClient</c>, o buraco deixa de ser
/// inferência: o <c>u64DevicePosition</c> do pacote seguinte diz exatamente
/// quantas amostras o hardware digitalizou no intervalo. O salto entre "onde o
/// pacote anterior terminou" e "onde este começa" é o silêncio a inserir, com
/// precisão de amostra.
/// </para>
/// <para>
/// Toda a aritmética vive aqui, no <c>Core</c> portátil, para poder ser testada
/// sem dispositivo de áudio. A camada Windows só entrega
/// <c>(posicao, quadros, flags)</c>.
/// </para>
/// </remarks>
public sealed class PacketTimeline(int taxaNativa, int taxaAlvo = CrashSafeWavWriter.TaxaAlvo)
{
    private long _posicaoInicial = -1;
    private long _fimAnteriorNativo;

    public int PacotesComDescontinuidade { get; private set; }
    public int PacotesComErroDeTimestamp { get; private set; }
    public int PacotesDeSilencio { get; private set; }
    /// <summary>Total de amostras de silêncio inseridas para tapar buracos (taxa alvo).</summary>
    public long SilencioInserido { get; private set; }

    /// <summary>
    /// Processa a chegada de um pacote e diz quanto silêncio o precede.
    /// </summary>
    /// <param name="posicaoDispositivo">
    /// <c>u64DevicePosition</c>: amostras que o dispositivo digitalizou até o
    /// primeiro quadro deste pacote, na taxa nativa.
    /// </param>
    /// <param name="quadros">Quadros neste pacote, na taxa nativa.</param>
    public DecisaoPacote Chegou(long posicaoDispositivo, int quadros, AnomaliaPacote flags)
    {
        if (flags.HasFlag(AnomaliaPacote.Silencio)) PacotesDeSilencio++;
        if (flags.HasFlag(AnomaliaPacote.Descontinuidade)) PacotesComDescontinuidade++;
        if (flags.HasFlag(AnomaliaPacote.ErroDeTimestamp)) PacotesComErroDeTimestamp++;

        if (_posicaoInicial < 0)
        {
            // O primeiro pacote define a origem. A posição absoluta do
            // dispositivo é arbitrária (conta desde que o endpoint iniciou, não
            // desde que começamos a gravar).
            _posicaoInicial = posicaoDispositivo;
            _fimAnteriorNativo = 0;
        }

        long inicioNativo = posicaoDispositivo - _posicaoInicial;

        // Um salto significa que o hardware digitalizou áudio que não nos foi
        // entregue: no loopback, silêncio; em qualquer faixa, também pode ser
        // glitch. De todo modo, o tempo passou e a faixa precisa acompanhar.
        long buracoNativo = inicioNativo - _fimAnteriorNativo;
        int silencio = 0;
        if (buracoNativo > 0)
        {
            silencio = (int)ParaAlvo(buracoNativo);
            SilencioInserido += silencio;
        }
        // Salto negativo (posição retrocedeu) é dado corrompido, não buraco;
        // ignoramos o retrocesso em vez de descartar áudio bom.

        _fimAnteriorNativo = inicioNativo + quadros;
        return new DecisaoPacote(silencio, ParaAlvo(_fimAnteriorNativo));
    }

    /// <summary>Converte contagem de amostras da taxa nativa para a taxa alvo.</summary>
    public long ParaAlvo(long amostrasNativas) =>
        (long)Math.Round(amostrasNativas * (double)taxaAlvo / taxaNativa);
}
