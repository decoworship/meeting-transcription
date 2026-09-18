using MeetingApp.Sidecar;

namespace MeetingApp.Nucleo;

/// <summary>Um bloco de 3 minutos, transcrito enquanto a reunião acontece.</summary>
/// <param name="Numero">O índice do bloco, contado do zero.</param>
/// <param name="InicioS">Onde ele começa na linha do tempo da reunião.</param>
/// <param name="Estado">
/// <c>"provisorio"</c> — o texto por bloco, sujeito à passada final — ou
/// <c>"mudo"</c>, quando o portão de RMS não deixou o bloco sair daqui.
/// </param>
public sealed record BlocoAoVivo(
    int Numero, double InicioS, double FimS, string Estado,
    IReadOnlyList<SegmentoFinal> Trechos);

/// <summary>
/// A transcrição durante a própria reunião, em blocos de 3 minutos.
/// </summary>
/// <remarks>
/// <para>
/// <b>Não é tempo real, e o nome importa.</b> Uma frase dita no minuto 10
/// aparece entre o 12 e o 13: o bloco fecha aos 12 e leva ~20 s para ser
/// transcrito. São <b>0 a 3 minutos de espera mais o processamento</b>, conforme
/// quando a pessoa falou. Quem espera legenda e recebe bloco de três minutos
/// acha que está quebrado — por isso a tela chama isto de <i>consciência da
/// reunião</i>, nunca de tempo real (docs/FASE7-FRONTEND.md §9).
/// </para>
/// <para>
/// <b>Ela não toca no gravador, e essa é a decisão central.</b> O
/// <c>CrashSafeWavWriter</c> mantém o header válido a cada 10 s e abre com
/// <c>FileShare.Read</c>; esta classe só <b>lê</b> as duas faixas por janela
/// (<see cref="Faixas.LerJanela"/>), mistura em memória e escreve um WAV
/// temporário por bloco. Nada em <c>Gravacao/</c> ou <c>Captura/</c> muda, e o
/// pior desfecho de um erro aqui é um bloco sem texto na tela — nunca uma
/// reunião perdida. É por isso que <b>todo caminho é protegido</b>: o que a
/// prévia não conseguir fazer, a passada final faz depois.
/// </para>
/// <para>
/// <b>Só com o MOSS, por enquanto.</b> Ele devolve texto e falante numa passada
/// a ~10× o tempo real, então um bloco de 3 min custa ~18 s de placa — ~10% de
/// ciclo. O caminho clássico daria texto sem falante nenhum: a diarização por
/// bloco funde pessoas (§3) e o desenho que resolve isso é a janela cumulativa,
/// que manda o <c>system.wav</c> vivo ao pyannote — e <b>isso</b> é o T0.5, que
/// ninguém mediu. Enquanto ele não for medido, o caminho clássico não tem prévia.
/// </para>
/// <para>
/// <b>A prévia é descartável.</b> Ela não vira o <c>transcricao.json</c> e não
/// substitui nada: quando a reunião acaba, a transcrição roda como sempre rodou.
/// O último bloco incompleto — até 3 minutos — nunca aparece ao vivo, e é a
/// passada final que o resolve (§2.3).
/// </para>
/// </remarks>
public sealed class SessaoAoVivo : IDisposable
{
    /// <summary>
    /// O bloco, em segundos.
    /// </summary>
    /// <remarks>
    /// <b>Três minutos, e o número é medido.</b> O bloco de 3 min tem a
    /// diarização do de 5 sem os órfãos do de 1
    /// (docs/FASE7-RESULTADOS.md §3). A constante morava na
    /// <c>MossEmBlocos</c>, que saiu em 17/09/2026 com o MOSS.
    /// </remarks>
    public const double BlocoS = 180.0;

    /// <summary>
    /// Quanto esperar além do fim do bloco antes de lê-lo.
    /// </summary>
    /// <remarks>
    /// O header do WAV é reescrito a cada 10 s, então o áudio dos últimos
    /// segundos pode ainda não estar declarado. Ler cedo demais devolveria um
    /// bloco curto e o motor transcreveria menos do que foi dito. Quinze
    /// segundos cobrem o flush com folga e custam nada: já se está esperando 3
    /// minutos.
    /// </remarks>
    public const double FolgaDoFlushS = 15.0;

    private readonly string _pasta;
    private readonly Motores _motores;
    private readonly IReadOnlyDictionary<string, string> _ambiente;
    private readonly Action<BlocoAoVivo> _aoBloco;
    private readonly string _motor;
    private readonly string? _modelo;
    private readonly CancellationTokenSource _cancelar = new();
    private Task? _laco;

    public SessaoAoVivo(string pastaDaGravacao, Motores motores,
                        IReadOnlyDictionary<string, string> ambiente,
                        Action<BlocoAoVivo> aoBloco,
                        string? motor = null, string? modelo = null)
    {
        _pasta = pastaDaGravacao;
        _motores = motores;
        _ambiente = ambiente;
        _aoBloco = aoBloco;
        _motor = ConfiguracoesDoApp.MotorAceito(motor);
        _modelo = modelo;
    }

    /// <summary>Quantos blocos já foram entregues.</summary>
    public int Blocos { get; private set; }

    /// <summary>
    /// Os blocos já entregues, na ordem.
    /// </summary>
    /// <remarks>
    /// <b>Porque a tela pode chegar depois.</b> O painel só existe enquanto o
    /// Gravador está montado: quem estava em Reuniões no minuto 20 e volta ao
    /// Gravador encontraria um painel vazio, e vazio para sempre — os blocos de
    /// antes já passaram pelo canal de eventos e ninguém os guardou. É o mesmo
    /// motivo pelo qual o registro de transcrições vive no núcleo e não na tela
    /// (<c>web/transcricoes.js</c>): uma tela não pode ser dona de um estado que
    /// dura a reunião inteira.
    /// <para>
    /// Uma reunião de duas horas são 40 blocos. Não há teto porque não precisa
    /// haver: o que cresce sem limite aqui é o texto, e ele já cabe na memória
    /// da transcrição inteira que o app carrega todo dia.
    /// </para>
    /// </remarks>
    public IReadOnlyList<BlocoAoVivo> Entregues => _entregues;

    private readonly List<BlocoAoVivo> _entregues = [];

    /// <summary>O que impede esta sessão de existir, ou <c>null</c>.</summary>
    /// <remarks>
    /// Perguntado <b>antes</b> de a gravação começar a ser observada, para o
    /// motivo aparecer na tela em vez de a prévia simplesmente não acontecer.
    /// </remarks>
    public static string? OQueImpede(Motores motores, ConfiguracoesDoApp config)
    {
        // **Os dois motores servem, desde 10/09/2026.** A prévia exigia o MOSS
        // porque ela mostrava o falante, e só ele dava texto e falante numa
        // passada. Com a tela em formato de conversa — a sua fala de um lado, a
        // dos outros do outro, sem nome —, o que a prévia precisa é **texto**, e
        // o dono vem da faixa do microfone sem custar GPU. O motor clássico faz
        // texto. Ver docs/FASE7-ROTA.md §0.6.
        return motores.OQueFalta();
    }

    /// <summary>Começa a acompanhar a gravação. Devolve na hora.</summary>
    public void Comecar() => _laco ??= Task.Run(() => LacoAsync(_cancelar.Token));

    public void Dispose()
    {
        try { _cancelar.Cancel(); } catch (ObjectDisposedException) { }
        _cancelar.Dispose();
    }

    /// <summary>
    /// O laço: espera o bloco fechar, transcreve, entrega, repete.
    /// </summary>
    /// <remarks>
    /// <b>Um motor quente para a sessão inteira.</b> Subir o MOSS por bloco
    /// pagaria a carga do GGUF a cada três minutos. Se ele morrer, a sessão
    /// acaba em silêncio — a prévia é um extra, e a gravação continua.
    /// </remarks>
    private async Task LacoAsync(CancellationToken ct)
    {
        string mic = Path.Combine(_pasta, "mic.wav");
        string sistema = Path.Combine(_pasta, "system.wav");

        MotorSidecar? motor = null;
        try
        {
            // Um motor só desde 17/09/2026 (docs/CONVERGENCIA.md).
            string[] args = _modelo is { Length: > 0 }
                ? [_motores.ScriptAsr, "--modelo", _modelo]
                : [_motores.ScriptAsr];

            motor = await MotorSidecar.IniciarAsync(_motores.Python, args, ct, _ambiente);
            motor.AoRegistrar += l => Registro.Escrever("aovivo", l);
            Registro.Escrever("aovivo",
                $"prévia ligada em {Path.GetFileName(_pasta)} — motor {_motor} · "
                + $"blocos de {BlocoS:F0} s");

            while (!ct.IsCancellationRequested)
            {
                double fim = (Blocos + 1) * BlocoS;
                if (!await EsperarOBlocoFecharAsync(sistema, fim, ct)) return;

                var bloco = await UmBlocoAsync(motor, mic, sistema, Blocos, ct);
                Blocos++;
                if (bloco is null) continue;

                _entregues.Add(bloco);
                _aoBloco(bloco);
            }
        }
        catch (OperationCanceledException)
        {
            // Parar a gravação cancela a sessão. É o fim normal.
        }
        catch (Exception e)
        {
            // A prévia morre; a reunião não. Nunca deixe isto subir.
            Registro.Escrever("aovivo", $"a prévia parou: {e.Message}");
        }
        finally
        {
            motor?.Dispose();
            Registro.Escrever("aovivo", $"prévia encerrada — {Blocos} bloco(s)");
        }
    }

    /// <summary>Espera até haver áudio gravado além do fim do bloco.</summary>
    /// <returns><c>false</c> quando a gravação sumiu — a sessão acabou.</returns>
    private static async Task<bool> EsperarOBlocoFecharAsync(
        string sistema, double fim, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            if (!File.Exists(sistema)) return false;

            // Pelo tamanho do arquivo, e não pelo relógio: quem manda é o áudio
            // que existe em disco. Um relógio adiantaria a leitura sempre que o
            // gravador atrasasse, e o bloco sairia curto.
            double gravado = SegundosEmDisco(sistema);
            if (gravado >= fim + FolgaDoFlushS) return true;

            await Task.Delay(TimeSpan.FromSeconds(5), ct);
        }
        return false;
    }

    /// <summary>Quanto áudio o WAV tem, pelo tamanho do arquivo.</summary>
    /// <remarks>
    /// Sem abrir o WAV: são 2 bytes por amostra a 16 kHz mono, e o header do
    /// gravador tem 44 bytes. Uma estimativa por baixo basta — ela só decide
    /// <b>quando</b> ler, e quem lê de verdade recorta ao que existe.
    /// </remarks>
    private static double SegundosEmDisco(string caminho)
    {
        try
        {
            long bytes = new FileInfo(caminho).Length - 44;
            return bytes <= 0 ? 0 : bytes / 2.0 / Faixas.TaxaDeAmostragem;
        }
        catch (IOException) { return 0; }
    }

    /// <summary>Um bloco: ler, misturar, transcrever.</summary>
    private async Task<BlocoAoVivo?> UmBlocoAsync(
        MotorSidecar motor, string mic, string sistema, int n, CancellationToken ct)
    {
        double de = n * BlocoS, ate = de + BlocoS;
        string temporario = Path.Combine(
            Path.GetTempPath(), $"pulsemeet-aovivo-{Environment.ProcessId}-{n}.wav");

        try
        {
            // As duas faixas, só a janela deste bloco, do arquivo que está sendo
            // gravado. Ver Faixas.LerJanela — é ela que abre com FileShare.ReadWrite.
            var janela = new Faixas(Faixas.LerJanela(mic, de, ate),
                                    Faixas.LerJanela(sistema, de, ate));
            var mix = janela.Mix();
            if (mix.Length < Faixas.TaxaDeAmostragem) return null;   // menos de 1 s

            // O mesmo portão do MossEmBlocos, e pelo mesmo motivo: bloco mudo faz
            // o MOSS alucinar em chinês, completando o prompt embutido no GGUF.
            if (Faixas.Rms(mix, 0, mix.Length / (double)Faixas.TaxaDeAmostragem)
                < VozDoDono.LimiarDeFala)
                return new BlocoAoVivo(n, de, ate, "mudo", []);

            Faixas.Escrever(temporario, mix);

            // **O falante do motor é descartado**, e sempre foi: ao vivo a tela
            // afirma só o que a faixa do microfone sabe, e o `AtribuirDono`
            // adiante põe isso. Ver docs/FASE7-ROTA.md §0.6.
            List<SegmentoFinal> locais =
                [.. (await motor.TranscreverAsync(temporario, null, null, null, ct))
                        .Segmentos.Select(s => new SegmentoFinal
                        { Start = s.Inicio, End = s.Fim, Text = s.Texto })];

            return new BlocoAoVivo(n, de, ate, "provisorio",
                                   DonoEDepoisORelogio(locais, janela, de));
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception e)
        {
            // Um bloco custa um bloco. A passada final vê tudo de novo.
            Registro.Escrever("aovivo", $"o bloco {n + 1} falhou e foi pulado: {e.Message}");
            return null;
        }
        finally
        {
            try { File.Delete(temporario); } catch (IOException) { }
        }
    }

    /// <summary>
    /// Decide o dono na janela do bloco e só então põe os tempos no relógio da
    /// reunião.
    /// </summary>
    /// <param name="locais">
    /// Os trechos com os carimbos <b>locais ao bloco</b>, como o motor os
    /// devolve. Modificados no lugar quanto ao falante.
    /// </param>
    /// <param name="janela">As duas faixas recortadas neste bloco — começam em zero.</param>
    /// <param name="de">O início do bloco, em segundos de reunião.</param>
    /// <remarks>
    /// <para>
    /// <b>Existe como função só para segurar a ordem</b>, que é onde estava o
    /// defeito. Deslocar antes e medir depois pede o RMS do minuto 12 a um vetor
    /// de três minutos: o <see cref="Faixas.Rms"/> corta o intervalo pedido pelo
    /// tamanho do que tem, <c>b &lt;= a</c>, devolve 0, e o dono desaparece.
    /// </para>
    /// <para>
    /// <b>E desaparece só a partir do segundo bloco</b> — no primeiro, <c>de</c>
    /// é zero e as duas contas coincidem. Foi por isso que virou função: o erro
    /// é invisível justamente no caso que qualquer teste escreve primeiro.
    /// </para>
    /// </remarks>
    public static List<SegmentoFinal> DonoEDepoisORelogio(
        List<SegmentoFinal> locais, Faixas janela, double de)
    {
        // O dono, de graça e na hora: a faixa do microfone diz o que é seu sem
        // custar GPU nenhuma. É a mesma regra que o caminho do MOSS usa na
        // passada final — decidir por canal, já que não há palavra para cortar.
        // Ver Transcritor, o comentário do AtribuirDono.
        Montagem.AtribuirDono(locais, janela);

        return [.. locais.Select(s => new SegmentoFinal
        {
            Start = s.Start + de,
            End = s.End + de,
            Text = s.Text,
            Speaker = s.Speaker,
        })];
    }
}
