using MeetingRecorder.Core;
using NAudio.CoreAudioApi;

namespace MeetingRecorder.Capture;

/// <summary>
/// Captura uma faixa (microfone ou loopback) direto do <c>AudioCaptureClient</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Por que não o <c>WasapiCapture</c> pronto.</b> A camada de cima do NAudio
/// não expõe a posição do dispositivo, e é ela que o requisito 3.1 exige: o
/// <c>GetBuffer</c> devolve <c>devicePosition</c> e <c>qpcPosition</c> por
/// pacote, carimbando <em>os dados</em> em vez do instante da pergunta. Com
/// isso, backlog de fila, GC e stall de disco deixam de virar correção de deriva
/// espúria — que é o defeito do gravador Python.
/// </para>
/// <para>
/// <b>Polling, não eventos.</b> O loopback com <c>eventSync</c> tem a pegadinha
/// clássica de não sinalizar enquanto nada toca: numa reunião silenciosa, o laço
/// dormiria para sempre. Dormir metade da duração do buffer elimina a classe
/// inteira de problema, e o custo é um <c>Sleep</c> de 10 ms.
/// </para>
/// <para>
/// Esta classe é fina de propósito. Toda a aritmética — buracos, conversão de
/// taxa, deriva, contabilidade — vive no <c>Core</c> portátil e testável sem
/// dispositivo. Aqui fica só o I/O irredutível.
/// </para>
/// </remarks>
public sealed class WasapiTrackCapture : IDisposable
{
    private readonly MMDevice _dispositivo;
    private readonly bool _loopback;
    private readonly TrackStats _stats;
    private readonly CrashSafeWavWriter _writer;
    private readonly CancellationTokenSource _parar = new();

    private AudioClient? _cliente;
    private PacketTimeline? _linha;
    private StreamingResampler? _resampler;
    private DriftAnchor? _ancora;
    private Thread? _thread;

    /// <remarks>
    /// Guardado depois da primeira leitura: <c>FriendlyName</c> abre o property
    /// store do dispositivo e custa ~170 ms, e a bandeja pergunta isso toda vez
    /// que o menu abre. O nome não muda durante uma gravação.
    /// </remarks>
    public string NomeDispositivo => _nome ??= LerNome();
    private string? _nome;

    private string LerNome()
    {
        try { return _dispositivo.FriendlyName; }
        catch (Exception) { return "(nome indisponível)"; }
    }
    public int TaxaNativa { get; private set; }
    public TrackStats Stats => _stats;
    public PacketTimeline? Linha => _linha;

    /// <summary>Mudo escreve silêncio, não interrompe a escrita.</summary>
    /// <remarks>
    /// Parar a escrita deslocaria a faixa em relação à outra. É a mesma decisão
    /// do gravador Python, e a razão de o mute do Teams não bastar.
    /// </remarks>
    public volatile bool Mudo;

    /// <summary>A faixa parou porque o dispositivo sumiu (requisito 3.7).</summary>
    public volatile bool Desconectado;
    public string? MotivoDaFalha { get; private set; }

    /// <summary>Erro de escrita em disco (requisito 3.3): degrada, não morre.</summary>
    public volatile bool FalhaDeEscrita;

    public WasapiTrackCapture(MMDevice dispositivo, bool loopback, string caminhoWav, string nome)
    {
        _dispositivo = dispositivo;
        _loopback = loopback;
        _stats = new TrackStats { Nome = nome };
        _writer = new CrashSafeWavWriter(caminhoWav);
    }

    /// <param name="qpcOrigem">
    /// Origem comum da linha do tempo, marcada uma vez antes de iniciar todas as
    /// faixas. É o que garante o alinhamento entre elas por construção.
    /// </param>
    public void Iniciar(long qpcOrigem)
    {
        _cliente = _dispositivo.AudioClient;
        var formato = _cliente.MixFormat;
        TaxaNativa = formato.SampleRate;

        _cliente.Initialize(
            AudioClientShareMode.Shared,
            _loopback ? AudioClientStreamFlags.Loopback : AudioClientStreamFlags.None,
            // 100 ms de buffer: folga suficiente para o polling de 10 ms não
            // perder pacote se a máquina engasgar, sem inflar a latência de
            // parada.
            refTimesPerSecond, 0, formato, Guid.Empty);

        _linha = new PacketTimeline(qpcOrigem);
        _resampler = new StreamingResampler(TaxaNativa);
        _ancora = new DriftAnchor();

        _cliente.Start();
        _thread = new Thread(Laco) { IsBackground = true, Name = $"captura-{_stats.Nome}" };
        _thread.Start();
    }

    private const long refTimesPerSecond = 100 * 10_000;   // 100 ms em unidades de 100 ns

    private void Laco()
    {
        var captura = _cliente!.AudioCaptureClient;
        int canais = _cliente.MixFormat.Channels;
        // Metade da duração do buffer: com 100 ms de buffer, 50 ms seria
        // arriscado sob carga; 10 ms dá margem de 10× e o custo é desprezível.
        var intervalo = TimeSpan.FromMilliseconds(10);

        try
        {
            while (!_parar.IsCancellationRequested)
            {
                int disponivel = captura.GetNextPacketSize();
                if (disponivel == 0)
                {
                    // Requisito 3.6 no caso extremo: sem pacote nenhum não há
                    // salto para detectar, e a faixa ficaria vazia em vez de
                    // conter silêncio — medido, 20 s pedidos deram 0 s. O
                    // relógio do dispositivo dá o QPC sem depender de pacote.
                    PreencherOcioso();
                    Thread.Sleep(intervalo);
                    continue;
                }

                while (disponivel > 0 && !_parar.IsCancellationRequested)
                {
                    IntPtr buffer = captura.GetBuffer(out int quadros, out var flags,
                                                      out long posicao, out long qpc);
                    try
                    {
                        if (quadros > 0) Processar(buffer, quadros, canais, flags, posicao, qpc);
                    }
                    finally
                    {
                        captura.ReleaseBuffer(quadros);
                    }
                    disponivel = captura.GetNextPacketSize();
                }
            }
        }
        catch (OperationCanceledException) { /* parada normal */ }
        catch (Exception e)
        {
            // Requisito 3.7: o headset caindo no meio da reunião não pode
            // derrubar a gravação em silêncio. A faixa para, o motivo fica
            // registrado, e a outra continua — meia reunião é muito melhor que
            // nenhuma.
            Desconectado = true;
            MotivoDaFalha = e.Message;
        }
    }

    /// <summary>Imprime os primeiros pacotes, para diagnóstico.</summary>
    public static bool Diagnostico;
    private int _pacotesVistos;
    private long _qpcInicial;

    private unsafe void Processar(IntPtr buffer, int quadros, int canais,
                                  AudioClientBufferFlags flags, long posicao, long qpc)
    {
        var anomalias = Traduzir(flags);
        // Os quadros vêm no mix format; a linha do tempo só trabalha em amostras
        // de saída. Converter aqui mantém a aritmética de taxa fora dela.
        int quadrosAlvo = (int)Math.Round(quadros * (double)CrashSafeWavWriter.TaxaAlvo / TaxaNativa);
        var decisao = _linha!.Chegou(qpc, quadrosAlvo, anomalias);

        // Requisito 3.5: DATA_DISCONTINUITY significa que o driver perdeu
        // amostras antes de nos entregar. Preferimos perder áudio a travar o
        // callback, mas a perda tem que ficar registrada — o gravador Python
        // descarta em silêncio quando a fila enche.
        if (anomalias.HasFlag(AnomaliaPacote.Descontinuidade) && _pacotesVistos > 0)
            _stats.AmostrasDescartadas += decisao.SilencioAntes;

        if (Diagnostico && _pacotesVistos < 12)
        {
            _pacotesVistos++;
            _qpcInicial = _qpcInicial == 0 ? qpc : _qpcInicial;
            double msQpc = (qpc - _qpcInicial) / 10_000.0;
            Console.WriteLine($"\n  [{_stats.Nome}] taxa={TaxaNativa} pos={posicao} quadros={quadros} " +
                              $"qpc_ms={msQpc:F1} escritas={_stats.AmostrasEscritas} flags={flags}");
        }

        // Buraco: o hardware digitalizou áudio que não nos foi entregue (no
        // loopback, silêncio). O tempo passou e a faixa tem que acompanhar,
        // senão encolhe e desalinha da outra.
        if (decisao.SilencioAntes > 0)
        {
            if (!Escrever(new float[decisao.SilencioAntes])) return;
            _stats.AmostrasEscritas += decisao.SilencioAntes;
        }

        // SILENT: o WASAPI garante que o conteúdo é silêncio. Converter seria
        // trabalho jogado fora, e ler o buffer nem é obrigatório.
        bool silencio = anomalias.HasFlag(AnomaliaPacote.Silencio) || Mudo;

        var mono = new float[quadros];
        if (!silencio)
        {
            var origem = (float*)buffer;
            if (canais == 1)
            {
                for (int i = 0; i < quadros; i++) mono[i] = origem[i];
            }
            else
            {
                // Média dos canais, como o gravador Python. Somar estouraria.
                for (int i = 0; i < quadros; i++)
                {
                    float soma = 0f;
                    for (int c = 0; c < canais; c++) soma += origem[i * canais + c];
                    mono[i] = soma / canais;
                }
            }
        }

        AtualizarEstatisticas(mono, quadros, silencio);

        var reamostrado = _resampler!.Processar(mono);
        if (reamostrado.Length == 0) return;      // o filtro ainda está enchendo

        long correcao = _ancora!.Calcular(decisao.PosicaoAlvo,
                                          _stats.AmostrasEscritas, reamostrado.Length);
        var final = correcao == 0 ? reamostrado : DriftAnchor.Aplicar(reamostrado, correcao);

        if (!Escrever(final)) return;
        _stats.AmostrasEscritas += final.Length;
        _stats.CorrecoesDeriva = _ancora.Correcoes;
        _stats.DerivaLiquidaAmostras = _ancora.AmostrasLiquidas;
    }

    /// <summary>
    /// Escreve o silêncio correspondente ao tempo decorrido quando o dispositivo
    /// não entrega pacote.
    /// </summary>
    /// <remarks>
    /// O relógio usado é o <b>mesmo</b> dos carimbos de pacote. O
    /// <c>u64QPCPosition</c> do WASAPI é o performance counter convertido para
    /// unidades de 100 ns, e o <see cref="System.Diagnostics.Stopwatch"/> lê esse
    /// mesmo contador — a conversão pela <c>Frequency</c> põe os dois na mesma
    /// escala.
    ///
    /// Uma tentativa anterior usou o <c>IAudioClock</c>, e o resultado foi
    /// instrutivo: os dois relógios <b>não compartilham época</b>. Misturá-los
    /// produziu ora um estouro aritmético (silêncio de dias a inserir), ora
    /// silêncio zero. Duas fontes de tempo numa mesma linha é o tipo de erro que
    /// só aparece em execução.
    /// </remarks>
    /// <summary>
    /// Margem de segurança do preenchimento ocioso.
    /// </summary>
    /// <remarks>
    /// Pacotes sempre descrevem o <b>passado</b>: o carimbo é do instante em que
    /// o hardware digitalizou, e ele só chega até 100 ms depois (o tamanho do
    /// buffer). Preencher silêncio até "agora" garante escrever por cima do
    /// intervalo que o próximo pacote vai cobrir.
    ///
    /// Medido antes desta margem: 4849 correções de deriva em 100 s, cada uma
    /// descartando exatos 160 quadros — um pacote inteiro, escrito duas vezes.
    /// </remarks>
    private static readonly long MargemOciosa = PacketTimeline.QpcPorSegundo / 5;   // 200 ms

    private void PreencherOcioso()
    {
        long qpc = QpcAgora() - MargemOciosa;

        int silencio = _linha!.SilencioAte(qpc);
        if (Diagnostico && silencio > 0 && _pacotesVistos < 12)
        {
            _pacotesVistos++;
            Console.WriteLine($"\n  [{_stats.Nome}] OCIOSO qpc_relogio={qpc} -> silencio={silencio}");
        }
        if (silencio <= 0) return;

        if (!Escrever(new float[silencio])) return;
        _stats.AmostrasEscritas += silencio;
        _stats.TotalSilencioS += silencio / (double)CrashSafeWavWriter.TaxaAlvo;
        _stats.SilencioAtualS += silencio / (double)CrashSafeWavWriter.TaxaAlvo;
        _stats.MaiorSilencioS = Math.Max(_stats.MaiorSilencioS, _stats.SilencioAtualS);
    }

    /// <summary>
    /// Escreve, degradando visivelmente se o disco falhar.
    /// </summary>
    /// <remarks>
    /// Requisito 3.3. O gravador Python deixa a exceção morrer dentro da thread:
    /// a gravação "continua" sem gravar, o ícone segue vermelho e o usuário só
    /// descobre depois. Aqui a falha marca a faixa, e o que já foi escrito
    /// permanece — o header do WAV é atualizado a cada 10 s justamente por isso.
    /// </remarks>
    private bool Escrever(float[] amostras)
    {
        if (FalhaDeEscrita) return false;
        try
        {
            _writer.Escrever(amostras);
            return true;
        }
        catch (IOException e)
        {
            FalhaDeEscrita = true;
            MotivoDaFalha = $"falha ao escrever: {e.Message}";
            return false;
        }
    }

    /// <summary>Agora, na mesma escala do <c>u64QPCPosition</c> dos pacotes.</summary>
    public static long QpcAgora() =>
        (long)(System.Diagnostics.Stopwatch.GetTimestamp()
               * (double)PacketTimeline.QpcPorSegundo / System.Diagnostics.Stopwatch.Frequency);

    private void AtualizarEstatisticas(float[] mono, int quadros, bool silencio)
    {
        double bloco = quadros / (double)TaxaNativa;

        if (Mudo) { _stats.MudoS += bloco; return; }

        double soma = 0;
        foreach (float f in mono) soma += f * f;
        double rms = quadros > 0 ? Math.Sqrt(soma / quadros) : 0;
        _stats.PicoRms = Math.Max(_stats.PicoRms, rms);

        const double limiarSilencio = 1e-4;
        if (rms >= limiarSilencio)
        {
            _stats.JaOuviu = true;
            _stats.SilencioAtualS = 0;
        }
        else
        {
            _stats.TotalSilencioS += bloco;
            _stats.SilencioAtualS += bloco;
            _stats.MaiorSilencioS = Math.Max(_stats.MaiorSilencioS, _stats.SilencioAtualS);
        }
    }

    private static AnomaliaPacote Traduzir(AudioClientBufferFlags f)
    {
        var a = AnomaliaPacote.Nenhuma;
        if (f.HasFlag(AudioClientBufferFlags.Silent)) a |= AnomaliaPacote.Silencio;
        if (f.HasFlag(AudioClientBufferFlags.DataDiscontinuity)) a |= AnomaliaPacote.Descontinuidade;
        if (f.HasFlag(AudioClientBufferFlags.TimestampError)) a |= AnomaliaPacote.ErroDeTimestamp;
        return a;
    }

    public void Parar()
    {
        _parar.Cancel();
        _thread?.Join(TimeSpan.FromSeconds(2));
        try { _cliente?.Stop(); } catch { /* dispositivo pode ter sumido */ }

        // A cauda retida no filtro é áudio real; sem drenar, corta a última
        // palavra. O teto de contabilidade impede que o silêncio de expulsão entre.
        var cauda = _resampler?.Drenar() ?? [];
        if (cauda.Length > 0)
        {
            _writer.Escrever(cauda);
            _stats.AmostrasEscritas += cauda.Length;
        }
        _writer.Dispose();
    }

    public void Dispose()
    {
        _parar.Dispose();
        _cliente?.Dispose();
        _dispositivo.Dispose();
    }
}
