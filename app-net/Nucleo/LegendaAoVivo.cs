using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Channels;
using MeetingApp.Sidecar;

namespace MeetingApp.Nucleo;

/// <summary>Um pedaço de legenda, como a tela o recebe.</summary>
/// <param name="Novo">
/// <b>Só o que firmou agora</b>, e não o texto acumulado.
/// </param>
/// <param name="Tentativo">A hipótese corrente, que se reescreve.</param>
/// <param name="Dono">
/// <c>true</c> quando a faixa do microfone domina no trecho que acabou de
/// firmar — isto é, quando é você falando.
/// </param>
/// <remarks>
/// <b>Incremental por construção</b>, e é decisão de contrato, não otimização:
/// mandar o transcrito inteiro a cada parcial seria <c>O(n²)</c> ao longo de uma
/// reunião de uma hora, com <c>JSON.parse</c> na thread que desenha. É o
/// <c>F-12</c> da docs/FASE7-FRONTEND.md, e ele tinha de estar certo antes da
/// primeira linha de C#.
/// <para>
/// <b>O <c>Dono</c> é o que agrupa.</b> A tela acrescenta ao balão corrente
/// enquanto ele não muda, e abre outro quando muda — que é como uma conversa se
/// parece. O motor não devolve segmento; quem dá forma é isto.
/// </para>
/// </remarks>
public sealed record PedacoDaLegenda(string Novo, string Tentativo, bool Dono);

/// <summary>Uma fala corrida da legenda, como ela fica em disco.</summary>
public sealed class TurnoDaLegenda
{
    [JsonPropertyName("dono")] public required bool Dono { get; init; }
    [JsonPropertyName("texto")] public required string Texto { get; set; }
}

/// <summary>O que a legenda deixou para ler depois da reunião.</summary>
public sealed class LegendaGravada
{
    [JsonPropertyName("turnos")] public required List<TurnoDaLegenda> Turnos { get; init; }
}

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(LegendaGravada))]
internal sealed partial class LegendaJson : JsonSerializerContext;

/// <summary>
/// A legenda ao vivo: texto sub-segundo durante a própria reunião.
/// </summary>
/// <remarks>
/// <para>
/// <b>É a camada 1 das três</b> (docs/FASE7-ROTA.md §3). Ela mostra o texto
/// quase no instante da fala — 0,11 s de mediana, medido em 60 minutos —, e
/// <b>não diz quem falou</b>, além de separar você dos outros. Quem é cada um
/// vem na passada final, que é a única que vê a reunião inteira.
/// </para>
/// <para>
/// <b>Ela não é a prévia por blocos.</b> A <see cref="SessaoAoVivo"/> entrega
/// blocos de 3 minutos com falante local; esta entrega palavras. As duas podem
/// conviver, mas <b>custa caro</b>: medido em 11/09/2026, a legenda sozinha com
/// o Meet aberto roda a 2,46× o tempo real; com o bloco do MOSS junto, despenca
/// para 0,45× — abaixo do tempo real, e a fila cresceria sem parar. Três
/// contextos CUDA não cabem numa placa de 6 GB.
/// </para>
/// <para>
/// <b>O que ela lê, e de onde.</b> As duas faixas, pela
/// <see cref="Faixas.LerJanela"/> — a mesma que o <c>T0.5</c> provou funcionar
/// num WAV que está sendo gravado. O <b>núcleo</b> mistura e decide o dono; o
/// sidecar só faz ASR. Duas implementações da mesma soma divergiriam, e a
/// legenda e a passada final passariam a discordar sobre o que é seu.
/// </para>
/// <para>
/// <b>Todo caminho é protegido.</b> O pior desfecho de um erro aqui é uma linha
/// a menos na tela — nunca uma reunião perdida. Nada em <c>Gravacao/</c> ou
/// <c>Captura/</c> é tocado.
/// </para>
/// </remarks>
public sealed class LegendaAoVivo : IDisposable
{
    /// <summary>
    /// O quadro de áudio mandado ao motor, em segundos.
    /// </summary>
    /// <remarks>
    /// 200 ms é o compromisso medido no <c>R1</c>: menor faz o custo por
    /// chamada dominar, maior põe um piso artificial no atraso — que é
    /// justamente o que esta classe existe para manter baixo.
    /// </remarks>
    public const double QuadroS = 0.2;

    /// <summary>
    /// Quanto áudio deixar em disco antes de ler um quadro.
    /// </summary>
    /// <remarks>
    /// O <c>CrashSafeWavWriter</c> reescreve o header a cada 10 s, mas os bytes
    /// chegam antes; esperar meio segundo cobre a escrita em curso sem somar
    /// atraso perceptível. É bem menor que a folga de 15 s da
    /// <see cref="SessaoAoVivo"/>, e pode ser: lá o custo de ler cedo é um bloco
    /// curto, aqui é um quadro a repetir.
    /// </remarks>
    public const double FolgaS = 0.5;

    private readonly string _pasta;
    private readonly Motores _motores;
    private readonly IReadOnlyDictionary<string, string> _ambiente;
    private readonly Action<PedacoDaLegenda> _aoPedaco;
    private readonly string? _idioma;
    private readonly CancellationTokenSource _cancelar = new();

    /// <summary>
    /// A parada <b>suave</b>: encerra a leitura sem matar o motor.
    /// </summary>
    /// <remarks>
    /// <b>Por que são dois.</b> Cancelar o <see cref="_cancelar"/> mata o
    /// processo do sidecar (<c>MotorSidecar.LegendarAsync</c> registra o
    /// <c>Matar</c> no token), e matar o processo pula o <c>finalize()</c> — que
    /// é justamente quem devolve o texto quando nada firmou durante a reunião.
    /// Em 15/09/2026 isso custou 46 minutos de fala: o motor tinha 14.428
    /// caracteres guardados no prefixo tentativo e o app o matou antes de pedir.
    /// <para>
    /// O suave fecha o canal, o canal manda <c>encerrar</c>, o motor finaliza e
    /// devolve. O duro continua existindo como último recurso, com prazo.
    /// </para>
    /// </remarks>
    private readonly CancellationTokenSource _parar = new();
    private Task? _laco;

    public LegendaAoVivo(string pastaDaGravacao, Motores motores,
                         IReadOnlyDictionary<string, string> ambiente,
                         Action<PedacoDaLegenda> aoPedaco, string? idioma = "pt-BR")
    {
        _pasta = pastaDaGravacao;
        _motores = motores;
        _ambiente = ambiente;
        _aoPedaco = aoPedaco;
        _idioma = idioma;
    }

    /// <summary>Quantos quadros já foram mandados ao motor.</summary>
    public int Quadros { get; private set; }

    /// <summary>O texto firme acumulado, para a tela que chega depois.</summary>
    /// <remarks>
    /// O evento é incremental, mas alguém precisa guardar o todo: quem estava em
    /// Reuniões no minuto 20 e volta ao Gravador encontraria a tela vazia. É o
    /// mesmo motivo do <c>Entregues</c> da <see cref="SessaoAoVivo"/>.
    /// </remarks>
    public string Firme { get; private set; } = "";

    /// <summary>O nome do arquivo que a legenda deixa na pasta da gravação.</summary>
    /// <remarks>
    /// <b>Um arquivo separado, e nunca o <c>transcricao.json</c>.</b> É a regra
    /// do §7 da docs/FASE7.md, e aqui ela vale com força total: o texto da
    /// legenda vem de <b>outro modelo</b>, e se chegasse ao parcial a
    /// <see cref="Retomada"/> o leria como ASR feito — devolvendo o texto de um
    /// motor rotulado como de outro, em silêncio. É o defeito da 0.4.0 de volta.
    /// <para>
    /// O que ele serve é para <b>ler</b>: terminada a reunião, dá para conferir
    /// se o que foi dito está lá antes de decidir pela transcrição inteira.
    /// </para>
    /// </remarks>
    public const string Arquivo = "legenda.json";

    private readonly List<TurnoDaLegenda> _turnos = [];

    /// <summary>O que impede a legenda de existir, ou <c>null</c>.</summary>
    /// <remarks>
    /// Perguntado <b>antes</b> de a gravação ser observada, para o motivo
    /// aparecer na tela em vez de a legenda simplesmente não acontecer.
    /// </remarks>
    public static string? OQueImpede(Motores motores, ConfiguracoesDoApp config)
    {
        if (!config.LegendaAoVivo) return "a legenda ao vivo está desligada em Ajustes.";

        // **A prévia por blocos e a legenda não convivem nesta placa.** Não é
        // conservadorismo: 2,46x cai para 0,45x com as duas ligadas, e abaixo de
        // 1x a legenda atrasa sem parar (docs/FASE7-ROTA.md §4).
        if (config.TranscricaoAoVivo)
            return "a legenda ao vivo e a prévia em blocos não cabem juntas na placa "
                 + "— desligue uma das duas em Ajustes › Transcrição.";

        return motores.OQueFaltaParaLegenda();
    }

    /// <summary>Começa a legendar. Devolve na hora.</summary>
    public void Comecar() => _laco ??= Task.Run(() => LacoAsync(_cancelar.Token));

    /// <summary>
    /// Para de ler, espera o motor devolver o texto, e só então desiste.
    /// </summary>
    /// <remarks>
    /// <b>Devolve rápido no caso sadio</b> — o motor já mandou tudo pelos
    /// parciais e o <c>encerrar</c> só confirma. A espera existe para o caso
    /// doente, que é onde está o texto que ninguém viu.
    /// </remarks>
    public async Task EncerrarAsync(TimeSpan espera)
    {
        try { _parar.Cancel(); } catch (ObjectDisposedException) { }

        if (_laco is { } laco)
        {
            var venceu = await Task.WhenAny(laco, Task.Delay(espera));
            if (venceu != laco)
                Registro.Escrever("legenda",
                    $"o motor não devolveu o texto em {espera.TotalSeconds:F0}s — desistindo.");
        }

        Dispose();
    }

    public void Dispose()
    {
        try { _parar.Cancel(); } catch (ObjectDisposedException) { }
        try { _cancelar.Cancel(); } catch (ObjectDisposedException) { }
        _parar.Dispose();
        _cancelar.Dispose();
    }

    private async Task LacoAsync(CancellationToken ct)
    {
        string mic = Path.Combine(_pasta, "mic.wav");
        string sistema = Path.Combine(_pasta, "system.wav");

        MotorSidecar? motor = null;
        var canal = Channel.CreateUnbounded<float[]>(
            new UnboundedChannelOptions { SingleReader = true, SingleWriter = true });

        try
        {
            motor = await MotorSidecar.IniciarAsync(
                _motores.Python, [_motores.ScriptLegenda], ct, _ambiente);
            motor.AoRegistrar += l => Registro.Escrever("legenda", l);
            Registro.Escrever("legenda",
                $"legenda ligada em {Path.GetFileName(_pasta)} — "
                + $"quadros de {QuadroS * 1000:F0} ms");

            // **A leitura para no suave; o motor, só no duro.** É o que dá ao
            // `encerrar` a chance de rodar o `finalize()` antes de o processo
            // morrer.
            using var suave = CancellationTokenSource.CreateLinkedTokenSource(
                ct, _parar.Token);
            var lendo = Task.Run(
                () => LerAsync(canal.Writer, mic, sistema, suave.Token), suave.Token);

            string firme = await motor.LegendarAsync(
                canal.Reader, p => Entregar(p, mic, sistema), _idioma, ct);

            Firme = firme;
            SalvarOFinal(firme);
            await lendo;
        }
        catch (OperationCanceledException)
        {
            // Parar a gravação cancela a legenda. É o fim normal.
        }
        catch (Exception e)
        {
            // A legenda morre; a reunião não.
            Registro.Escrever("legenda", $"a legenda parou: {e.Message}");
        }
        finally
        {
            canal.Writer.TryComplete();
            motor?.Dispose();
            Registro.Escrever("legenda", $"legenda encerrada — {Quadros} quadro(s)");
        }
    }

    /// <summary>Lê as faixas em quadros e os entrega ao canal, no ritmo do áudio.</summary>
    private async Task LerAsync(ChannelWriter<float[]> canal, string mic,
                                string sistema, CancellationToken ct)
    {
        try
        {
            // **A posição se conta em AMOSTRAS, não em segundos.** Endereçar
            // amostra com double custou o defeito mais caro desta feature: o
            // quadro voltava com 3199 amostras onde a conta pedia 3200 — erro
            // de arredondamento —, o laço recusava o quadro curto e pedia o
            // mesmo pedaço para sempre. A legenda congelava no terceiro quadro,
            // sem erro em lugar nenhum.
            long lidas = 0;
            int porQuadro = (int)(QuadroS * Faixas.TaxaDeAmostragem);

            while (!ct.IsCancellationRequested)
            {
                double de = lidas / (double)Faixas.TaxaDeAmostragem;
                double ate = (lidas + porQuadro) / (double)Faixas.TaxaDeAmostragem;

                if (PrecisaEsperar(File.Exists(sistema), SegundosEmDisco(sistema), ate))
                {
                    // **Sem relógio de parede** — quem manda é o que existe em
                    // disco, e um relógio adiantado leria silêncio como fala.
                    await Task.Delay(TimeSpan.FromSeconds(QuadroS), ct);
                    continue;
                }

                // **Viva**, e não a leitura de sempre: o header do WAV só é
                // reescrito a cada 10 s, e a leitura que o respeita devolveria
                // vazio na largada e depois entregaria em degraus de 10 s.
                var janela = new Faixas(Faixas.LerJanelaViva(mic, de, ate),
                                        Faixas.LerJanelaViva(sistema, de, ate));
                var quadro = janela.Mix();

                // **Nada lido é "espere"; qualquer coisa lida serve.** Exigir o
                // quadro exato foi o que travou o laço: o motor não se importa
                // com um quadro de 3199 amostras, e avançar pelo que de fato
                // veio torna o arredondamento inofensivo por construção.
                if (quadro.Length == 0)
                {
                    await Task.Delay(TimeSpan.FromSeconds(QuadroS), ct);
                    continue;
                }

                lidas += quadro.Length;
                await canal.WriteAsync(quadro, ct);
                Quadros++;
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception e)
        {
            Registro.Escrever("legenda", $"a leitura parou: {e.Message}");
        }
        finally
        {
            canal.TryComplete();
        }
    }

    /// <summary>Decide o dono do trecho que firmou e entrega à tela.</summary>
    /// <remarks>
    /// <b>Pelo mesmo critério do resto do app</b> — energia do microfone contra
    /// a do sistema, o <see cref="Montagem.AtribuirDono"/>. Repetir a regra aqui
    /// em vez de chamá-lo é deliberado: ele opera sobre
    /// <see cref="SegmentoFinal"/>, e a legenda não tem segmentos, tem um ponto
    /// no tempo. O que não pode divergir é o <b>critério</b>, e ele é o mesmo.
    /// </remarks>
    private void Entregar(MotorSidecar.ParcialDaLegenda p, string mic, string sistema)
    {
        bool dono = false;
        try
        {
            // A janela que acabou de firmar: do fim anterior até onde o motor
            // diz ter fechado. Um segundo é o bastante para o RMS decidir.
            double ate = p.AteMs / 1000.0;
            double de = Math.Max(0, ate - 1.0);
            if (ate > de)
            {
                double rmsMic = Faixas.Rms(Faixas.LerJanela(mic, de, ate), 0, ate - de);
                double rmsSis = Faixas.Rms(Faixas.LerJanela(sistema, de, ate), 0, ate - de);
                dono = rmsMic >= Montagem.RmsMinimoDoDono
                    && rmsMic > rmsSis * Montagem.MargemDoDono;
            }
        }
        catch (Exception)
        {
            // Sem dono decidido a linha sai como "de outra pessoa", que é o
            // palpite seguro: afirmar que é seu sem saber é pior.
        }

        // **O delta, e não o acumulado.** O motor manda o prefixo firme inteiro
        // a cada parcial; o que a tela precisa é do que cresceu. Comparar por
        // prefixo é seguro porque o `stable_prefix` do LocalAgreement só
        // acrescenta — é o que a palavra "stable" promete.
        string novo = p.Firme.StartsWith(Firme, StringComparison.Ordinal)
            ? p.Firme[Firme.Length..]
            : p.Firme;
        Firme = p.Firme;

        if (novo.Length > 0)
        {
            // A mesma regra de agrupamento da tela: enquanto o dono não muda, o
            // texto cresce no mesmo turno. Duas implementações da mesma regra
            // fariam o arquivo e a tela contarem histórias diferentes.
            if (_turnos.Count == 0 || _turnos[^1].Dono != dono)
                _turnos.Add(new TurnoDaLegenda { Dono = dono, Texto = novo.TrimStart() });
            else
                _turnos[^1].Texto += novo;

            Gravar();
        }

        if (novo.Length == 0 && p.Tentativo.Length == 0) return;
        _aoPedaco(new PedacoDaLegenda(novo, p.Tentativo, dono));
    }

    /// <summary>
    /// Escreve o que já foi dito, para a reunião que acabar de repente.
    /// </summary>
    /// <remarks>
    /// <b>A cada turno, e não só no fim.</b> O caso que este arquivo existe para
    /// atender é justamente a máquina que desliga sozinha (o <c>SUP-2</c>):
    /// escrever só ao encerrar perderia tudo exatamente quando mais importa.
    /// Escrever a cada turno custa alguns KB e é barato.
    /// <para>
    /// <b>Nunca levanta.</b> Um erro de disco não pode derrubar a legenda, e a
    /// legenda não pode derrubar a gravação.
    /// </para>
    /// </remarks>
    private void Gravar()
    {
        try
        {
            File.WriteAllText(
                Path.Combine(_pasta, Arquivo),
                JsonSerializer.Serialize(new LegendaGravada { Turnos = _turnos },
                                         LegendaJson.Default.LegendaGravada));
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    /// <summary>
    /// O texto que só apareceu no <c>finalize()</c>, quando nada firmou antes.
    /// </summary>
    /// <remarks>
    /// <b>Só quando não há turno nenhum</b>, e a restrição é deliberada. O caso
    /// que isto atende é o da sessão que nunca confirma: o prefixo tentativo
    /// cresce por 46 minutos, o <c>Entregar</c> nunca cria turno, e o texto
    /// inteiro só existe depois do <c>finalize()</c>. Se houver **algum** turno,
    /// o caminho incremental funcionou, e emendar o acumulado por cima
    /// arriscaria duplicar texto — o que é pior que não emendar.
    /// <para>
    /// <b>Sai sem dono.</b> O texto veio de uma vez, sem um ponto no tempo onde
    /// medir o RMS das duas faixas; afirmar que é seu sem saber é o erro que a
    /// <see cref="Entregar"/> evita pelo mesmo motivo.
    /// </para>
    /// </remarks>
    private void SalvarOFinal(string firme)
    {
        if (_turnos.Count > 0 || firme.Trim().Length == 0) return;

        _turnos.Add(new TurnoDaLegenda { Dono = false, Texto = firme.TrimStart() });
        Gravar();
        Registro.Escrever("legenda",
            $"nada firmou durante a reunião — {firme.Length} caracteres salvos "
            + "pelo finalize().");
    }

    /// <summary>O que a legenda deixou numa gravação, ou <c>null</c>.</summary>
    public static LegendaGravada? Ler(string pastaDaGravacao)
    {
        try
        {
            string caminho = Path.Combine(pastaDaGravacao, Arquivo);
            if (!File.Exists(caminho)) return null;
            return JsonSerializer.Deserialize(File.ReadAllText(caminho),
                                              LegendaJson.Default.LegendaGravada);
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
        catch (JsonException) { return null; }
    }

    /// <summary>
    /// Esperar mais, ou já dá para ler o quadro que termina em <paramref name="ate"/>?
    /// </summary>
    /// <remarks>
    /// <b>Função separada porque a resposta errada é invisível.</b> Ela devolvia
    /// "acabou" onde devia devolver "espere": a legenda começa no mesmo instante
    /// que a gravação, o <c>CrashSafeWavWriter</c> ainda não criou o WAV, e a
    /// sessão encerrava com <b>0 quadros no mesmo segundo</b> em que o modelo
    /// terminava de carregar na GPU. Nada no log apontava para a causa.
    /// <para>
    /// <b>Arquivo que não existe é "espere", nunca "acabou".</b> Quem termina o
    /// laço é o cancelamento, que vem de parar a gravação.
    /// </para>
    /// </remarks>
    public static bool PrecisaEsperar(bool existe, double segundosEmDisco, double ate) =>
        !existe || segundosEmDisco < ate + FolgaS;

    /// <summary>Quanto áudio o WAV tem, pelo tamanho do arquivo.</summary>
    private static double SegundosEmDisco(string caminho)
    {
        try
        {
            long bytes = new FileInfo(caminho).Length - 44;
            return bytes <= 0 ? 0 : bytes / 2.0 / Faixas.TaxaDeAmostragem;
        }
        catch (IOException) { return 0; }
    }
}
