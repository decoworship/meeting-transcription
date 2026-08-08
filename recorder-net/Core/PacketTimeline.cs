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
/// Traduz o carimbo QPC de cada pacote numa linha do tempo em amostras de
/// 16 kHz, detectando os buracos.
/// </summary>
/// <remarks>
/// <para>
/// <b>Por que QPC e não <c>u64DevicePosition</c>.</b> A primeira versão usava a
/// posição do dispositivo, supondo que ela contasse os mesmos quadros que
/// chegam nos dados. Medido no hardware: o microfone entrega <b>480 quadros por
/// pacote</b> num mix format de 48 kHz, enquanto a posição avança <b>320</b> —
/// porque o dispositivo roda a 32 kHz e o motor de áudio reamostra no caminho. O
/// <c>u64DevicePosition</c> conta <i>device frames</i>, os dados vêm em
/// <i>mix frames</i>, e as unidades não coincidem sempre que há reamostragem.
/// O sintoma foi a âncora achar que havia áudio demais e descartar 35% da faixa.
/// </para>
/// <para>
/// O <c>u64QPCPosition</c> não tem essa ambiguidade: é tempo absoluto em unidades
/// de 100 ns, igual para qualquer dispositivo. E é o mesmo relógio para as duas
/// faixas — então o alinhamento entre elas deixa de ser inferência e passa a ser
/// construção.
/// </para>
/// <para>
/// <b>Por que os buracos não podem ser inferidos por relógio de parede.</b> O
/// loopback WASAPI não entrega pacote enquanto nada toca (armadilha do NAudio
/// registrada no PLANO). O QPC do pacote seguinte diz exatamente quanto tempo
/// passou, com precisão de 100 ns.
/// </para>
/// </remarks>
public sealed class PacketTimeline(long qpcOrigem = -1, int taxaAlvo = CrashSafeWavWriter.TaxaAlvo)
{
    /// <summary>Unidades de QPC por segundo: o carimbo vem em múltiplos de 100 ns.</summary>
    public const long QpcPorSegundo = 10_000_000;

    /// <summary>
    /// Origem da linha do tempo. Quando as duas faixas recebem a <b>mesma</b>
    /// origem, o alinhamento entre elas deixa de ser inferência e passa a ser
    /// construção: cada pacote sabe seu instante numa régua comum.
    ///
    /// Medido com origens independentes (cada faixa marcando a sua no primeiro
    /// pacote): 302 ms de desalinhamento em 20 s de captura, porque o loopback e
    /// o microfone começam a entregar em instantes diferentes.
    /// </summary>
    private long _qpcInicial = qpcOrigem;
    private long _fimEscritoAlvo;

    public int PacotesComDescontinuidade { get; private set; }
    public int PacotesComErroDeTimestamp { get; private set; }
    public int PacotesDeSilencio { get; private set; }
    /// <summary>Total de amostras de silêncio inseridas para tapar buracos (taxa alvo).</summary>
    public long SilencioInserido { get; private set; }

    /// <summary>Processa a chegada de um pacote e diz quanto silêncio o precede.</summary>
    /// <param name="qpc"><c>u64QPCPosition</c> do primeiro quadro do pacote.</param>
    /// <param name="quadrosAlvo">
    /// Quadros deste pacote <b>já convertidos para a taxa alvo</b>. Quem chama
    /// sabe a razão; a linha do tempo trabalha só em amostras de saída.
    /// </param>
    public DecisaoPacote Chegou(long qpc, int quadrosAlvo, AnomaliaPacote flags)
    {
        if (flags.HasFlag(AnomaliaPacote.Silencio)) PacotesDeSilencio++;
        if (flags.HasFlag(AnomaliaPacote.Descontinuidade)) PacotesComDescontinuidade++;
        if (flags.HasFlag(AnomaliaPacote.ErroDeTimestamp)) PacotesComErroDeTimestamp++;

        if (_qpcInicial < 0)
        {
            // O primeiro pacote define a origem: o QPC é o relógio da máquina
            // desde o boot, e tomá-lo como deslocamento inseriria dias de
            // silêncio no começo do arquivo.
            _qpcInicial = qpc;
        }

        long inicioAlvo = QpcParaAmostras(qpc - _qpcInicial);

        // Salto: o tempo passou sem que nos entregassem áudio. No loopback é
        // silêncio real (ninguém falando); em qualquer faixa também pode ser
        // glitch. De todo modo a faixa precisa acompanhar, senão encolhe e
        // desalinha da outra.
        int silencio = 0;
        long buraco = inicioAlvo - _fimEscritoAlvo;
        if (buraco > 0)
        {
            silencio = (int)buraco;
            SilencioInserido += silencio;
        }
        // Buraco negativo é jitter do carimbo, não áudio sobrando: não se
        // insere silêncio, mas a linha do tempo continua ancorada no QPC.

        // Dois conceitos distintos, que uma versão anterior tinha fundido num só:
        //
        // * `fimPeloRelogio` é onde o TEMPO diz que este pacote termina. É o que
        //   a âncora precisa: comparado com o que foi escrito, revela deriva do
        //   dispositivo. Fundir isso com o acumulado de quadros tornava a
        //   comparação quase tautológica — 0 correções em 20 min de soak enquanto
        //   os gravadores se afastavam 1,6 ms por minuto.
        //
        // * `_fimEscritoAlvo` é um CURSOR DE ESCRITA, e só anda para a frente. É
        //   o que detecta buracos. Deixá-lo recuar com pacote atrasado fazia o
        //   pacote seguinte enxergar um buraco que não existe e inserir silêncio
        //   sobre tempo já coberto.
        long fimPeloRelogio = inicioAlvo + quadrosAlvo;
        _fimEscritoAlvo = Math.Max(_fimEscritoAlvo, fimPeloRelogio);
        return new DecisaoPacote(silencio, fimPeloRelogio);
    }

    /// <summary>
    /// Quanto silêncio falta para alcançar um instante, sem pacote nenhum.
    /// </summary>
    /// <remarks>
    /// Requisito 3.6 no seu caso extremo: se <b>nenhum</b> pacote chegar — reunião
    /// em que ninguém fala, loopback sem nada tocando — não há salto para
    /// detectar, e a faixa ficaria vazia em vez de conter silêncio. Medido: uma
    /// captura de 20 s produziu 0 s na faixa do sistema.
    /// A camada de captura consulta o relógio do dispositivo quando o polling
    /// não traz pacote e chama isto para preencher.
    /// </remarks>
    public int SilencioAte(long qpc)
    {
        if (_qpcInicial < 0) { _qpcInicial = qpc; return 0; }

        long alvo = QpcParaAmostras(qpc - _qpcInicial);
        long falta = alvo - _fimEscritoAlvo;
        if (falta <= 0) return 0;

        SilencioInserido += falta;
        _fimEscritoAlvo = alvo;
        return (int)falta;
    }

    public long QpcParaAmostras(long deltaQpc) =>
        (long)Math.Round(deltaQpc * (double)taxaAlvo / QpcPorSegundo);
}
