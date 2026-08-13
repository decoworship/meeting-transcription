using MeetingRecorder.Agenda;
using MeetingRecorder.Capture;
using MeetingRecorder.Core;
using NAudio.CoreAudioApi;

namespace MeetingApp.App.Bandeja;

/// <summary>
/// A gravação como serviço em processo: quem inicia, muta e para as duas faixas.
/// </summary>
/// <remarks>
/// <para>
/// É o <c>Tray/Program.cs</c> da Fase 1 com a costura do Win32 removida. Antes o
/// menu da bandeja era o único cliente e a lógica podia morar nele; agora são
/// dois — o menu e a tela do Gravador — e um estado que só um deles conhecesse
/// apareceria como janela e bandeja discordando.
/// </para>
/// <para>
/// <b>Em processo, e não sidecar.</b> Diferente dos motores, a captura não tem
/// modelo pesado para carregar, não usa GPU, e não pode pagar a latência de um
/// pipe entre o clique e o começo do áudio. O isolamento que os motores exigem,
/// ela não exige — e os motores continuam isolados, que é o que mantém a
/// transcrição fora do caminho da gravação (FASE2.5.md §5).
/// </para>
/// <para>
/// Nada aqui decide política de UI: cor de ícone, texto de status e lembrete de
/// mute continuam no <see cref="EstadoDaBandeja"/>, que é testável sem áudio.
/// </para>
/// </remarks>
/// <param name="naUi">
/// Executa na thread do laço de mensagens. A agenda responde de uma thread de
/// trabalho, e mexer no estado de lá faria a bandeja e a janela lerem um estado
/// pela metade.
/// </param>
internal sealed class Gravador(Action<Action> naUi) : IDisposable
{
    private readonly List<WasapiTrackCapture> _capturas = [];
    private readonly ClienteDaAgenda _agenda = new();
    private CancellationTokenSource? _consulta;
    private DateTime _inicio;

    public EstadoDaBandeja Estado { get; } = new();

    /// <remarks>
    /// Lê os nomes dos dispositivos uma vez, em segundo plano, e só relê quando
    /// o Windows avisa que mudaram: cada nome custa ~170 ms, e lê-los ao abrir o
    /// menu travava a bandeja.
    /// </remarks>
    public CatalogoDeDispositivos Dispositivos { get; } = new();

    public Configuracoes Cfg { get; } = Configuracoes.Carregar();

    /// <summary>A reunião da agenda que está sendo gravada, quando há uma.</summary>
    public Evento? Evento { get; private set; }

    /// <summary>A pasta desta gravação, ou a da última, ou nulo.</summary>
    public string? PastaAtual { get; private set; }

    /// <summary>Mudou alguma coisa que a bandeja e a janela mostram.</summary>
    public event Action? AoMudar;

    /// <param name="sempre">
    /// Ignora o A14: desligar notificações silencia o lembrete de mute, que é
    /// sobre algo que você pediu — não silencia dispositivo caindo ou disco
    /// enchendo, que são coisas que você precisa saber.
    /// </param>
    public event Action<string, uint, bool>? AoAvisar;

    public double DuracaoAtual =>
        Estado.Gravando ? (DateTime.UtcNow - _inicio).TotalSeconds : 0;

    /// <summary>
    /// Onde gravar, quando o <c>--gravacoes</c> mandou.
    /// </summary>
    /// <remarks>
    /// Fundidos os dois programas, o argumento passou a valer para os dois
    /// papéis. Se valesse só para a leitura, o app listaria uma pasta e gravaria
    /// noutra — e é justamente com esse argumento que se abre um acervo de
    /// teste, onde ver a gravação nova aparecer é o ponto.
    /// </remarks>
    public string? PastaForcada { get; init; }

    public string PastaDeSaida =>
        PastaForcada ?? Cfg.OutputDir ?? MeetingApp.Nucleo.PastaDasGravacoes.Padrao;

    public IReadOnlyList<WasapiTrackCapture> Capturas => _capturas;

    public WasapiTrackCapture? Faixa(string nome) =>
        _capturas.FirstOrDefault(c => c.Stats.Nome == nome);

    // ─────────────────────────────────────────────────────────── ações

    public void Iniciar()
    {
        if (Estado.Gravando) return;

        try
        {
            string raiz = PastaDeSaida;
            PastaAtual = Path.Combine(raiz, DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss"));
            Directory.CreateDirectory(PastaAtual);

            var guarda = new GuardaDeDisco();
            try
            {
                long livres = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(PastaAtual))!)
                    .AvailableFreeSpace;
                if (!guarda.PodeComecar(livres, out string? motivo))
                {
                    Avisar(motivo!, Win32Aviso.Erro, sempre: true);
                    return;
                }
                if (motivo is not null) Avisar(motivo, Win32Aviso.Atencao, sempre: true);
            }
            catch
            {
                // Falha em medir espaço nunca impede gravar.
            }

            var enumerador = new MMDeviceEnumerator();
            var alto = Escolhido(enumerador, DataFlow.Render, Cfg.LoopbackId, Role.Multimedia);
            var micDev = Escolhido(enumerador, DataFlow.Capture, Cfg.MicId, Role.Communications);

            _capturas.Add(new WasapiTrackCapture(alto, true,
                Path.Combine(PastaAtual, "system.wav"), "system"));
            _capturas.Add(new WasapiTrackCapture(micDev, false,
                Path.Combine(PastaAtual, "mic.wav"), "mic"));

            // Origem única: é o que faz as duas faixas ficarem alinhadas por
            // construção em vez de por sorte.
            long origem = WasapiTrackCapture.QpcAgora();
            foreach (var c in _capturas) c.Iniciar(origem);

            _inicio = DateTime.UtcNow;
            _jaAvisou.Clear();
            Estado.Iniciou();
            // Depois de Iniciar(), nunca antes: a agenda é chamada de rede e não
            // pode atrasar o começo da captura. Ver ClienteDaAgenda.
            Evento = null;
            if (Cfg.UseCalendar) ConsultarAgenda();
            if (Cfg.StartMuted) AlternarMudo();
            Atualizar();
        }
        catch (Exception e)
        {
            Avisar($"Não foi possível iniciar: {e.Message}", Win32Aviso.Erro, sempre: true);
            Parar();
        }
    }

    /// <summary>
    /// O dispositivo salvo nas configurações, ou o padrão do Windows.
    /// </summary>
    /// <remarks>
    /// Cair no padrão quando o dispositivo salvo sumiu é deliberado: fixar um
    /// headset específico e ele estar desconectado não pode impedir a gravação —
    /// gravar pelo alto-falante do notebook é muito melhor que não gravar.
    /// </remarks>
    private static MMDevice Escolhido(MMDeviceEnumerator e, DataFlow fluxo,
                                      string? id, Role papel)
    {
        if (id is not null)
        {
            try
            {
                var d = e.GetDevice(id);
                if (d.State == DeviceState.Active) return d;
            }
            catch { /* sumiu; cai no padrão */ }
        }
        return e.GetDefaultAudioEndpoint(fluxo, papel);
    }

    public void AlternarMudo()
    {
        if (!Estado.Gravando) return;
        bool novo = !Estado.Mudo;
        Estado.DefinirMudo(novo, DateTime.UtcNow);
        foreach (var c in _capturas.Where(c => c.Stats.Nome == "mic")) c.Mudo = novo;
        Atualizar();
    }

    /// <summary>O que o clique no ícone faz: iniciar quando parado, mutar quando gravando.</summary>
    public void AoClicar()
    {
        if (Estado.AcaoDoCliqueAtual == AcaoDoClique.Iniciar) Iniciar();
        else AlternarMudo();
    }

    public void Parar()
    {
        if (!Estado.Gravando) return;

        foreach (var c in _capturas) c.Parar();

        var system = Stats("system");
        var mic = Stats("mic");
        // Cancela uma consulta ainda em voo: a gravação acabou, o rótulo dela
        // não serve mais para nada e o resultado chegaria depois do meta.json.
        _consulta?.Cancel();
        var evento = Evento;
        Evento = null;

        var meta = Meta.Montar(DateTimeOffset.Now, system, mic,
            Dispositivo("system"), Dispositivo("mic"),
            Taxa("system"), Taxa("mic"), evento?.ParaMeta());

        if (PastaAtual is not null)
            File.WriteAllText(Path.Combine(PastaAtual, "meta.json"), meta.ParaJson());

        // Avisar sobre faixa suspeita agora, não depois: é o aprendizado de 06/08,
        // quando uma gravação 95% muda só foi descoberta na transcrição.
        foreach (var (nome, s) in new[] { ("system", system), ("mic", mic) })
        {
            if (s.SemAudio)
                Avisar($"A faixa '{nome}' não teve áudio nenhum.", Win32Aviso.Atencao, sempre: true);
            else if (s.PercentualUtil(meta.DurationS) < 20)
                Avisar($"A faixa '{nome}' tem só {s.PercentualUtil(meta.DurationS):F0}% de conteúdo útil.",
                       Win32Aviso.Atencao, sempre: true);
        }

        foreach (var c in _capturas) c.Dispose();
        _capturas.Clear();
        Estado.Parou();
        Atualizar();
    }

    /// <summary>
    /// Escolhe o dispositivo de uma faixa. Trava durante a gravação.
    /// </summary>
    /// <remarks>
    /// Reabrir o stream no meio exigiria realinhar as faixas, e o alinhamento é
    /// o que dá valor às duas terem sido gravadas em separado.
    /// </remarks>
    public void EscolherDispositivo(string faixa, string? id)
    {
        if (Estado.Gravando) return;
        if (faixa == "mic") Cfg.MicId = id; else Cfg.LoopbackId = id;
        Cfg.Salvar();
        Atualizar();
    }

    public void DefinirPastaDeSaida(string? pasta)
    {
        if (Estado.Gravando || PastaForcada is not null) return;
        Cfg.OutputDir = pasta is { Length: > 0 } ? pasta : null;
        Cfg.Salvar();
        Atualizar();
    }

    public void AlternarNotificacoes()
    {
        Estado.NotificacoesLigadas = !Estado.NotificacoesLigadas;
        Cfg.Notifications = Estado.NotificacoesLigadas;
        Cfg.Salvar();
        Atualizar();
    }

    public void UsarAgenda(bool usar)
    {
        Cfg.UseCalendar = usar;
        Cfg.Salvar();
        Atualizar();
    }

    /// <summary>Abre o navegador uma vez para autorizar. Nunca na thread da UI.</summary>
    public void Autorizar()
    {
        _ = Task.Run(async () =>
        {
            string? token = await Autorizacao.AutorizarAsync();
            if (token is not null) await _agenda.GuardarContaAsync(token);

            string conta = ClienteDaAgenda.EmailDaConta();
            string msg = token is null ? "Autorização falhou."
                : conta.Length > 0 ? $"Conectado: {conta}"
                : "Calendário autorizado.";
            naUi(() =>
            {
                Avisar(msg, token is null ? Win32Aviso.Erro : Win32Aviso.Info, sempre: true);
                Atualizar();
            });
        });
    }

    // ────────────────────────────────────────────────────────── suporte

    /// <summary>
    /// Procura na agenda a reunião correspondente. Roda fora da thread da UI.
    /// </summary>
    private void ConsultarAgenda()
    {
        _consulta?.Cancel();
        var cts = new CancellationTokenSource();
        _consulta = cts;

        _ = Task.Run(async () =>
        {
            var r = await _agenda.EventoAtualAsync(ct: cts.Token);
            naUi(() =>
            {
                if (!Estado.Gravando || cts.IsCancellationRequested) return;

                if (r.ExigeAtencao)
                {
                    // Não achar reunião é normal; ter autorizado e o token morrer
                    // não é. Sem este aviso o gravador pararia de identificar
                    // reuniões em silêncio, e só se descobriria semanas depois.
                    Avisar("Calendário indisponível. Reautorize pelo menu.\n"
                           + "A gravação continua normalmente.",
                           Win32Aviso.Atencao, sempre: true);
                    return;
                }

                if (r.Evento is not { } ev) return;

                Evento = ev;
                int n = ev.NomesDosParticipantes().Count;
                Avisar($"{Cortar(ev.Titulo, 40)}\n{n} participantes", Win32Aviso.Info);
                Atualizar();
            });
        });
    }

    public static string Cortar(string texto, int max) =>
        texto.Length <= max ? texto : texto[..max];

    /// <summary>
    /// Uma vez por segundo: recalcula o que é derivado e dispara os avisos.
    /// </summary>
    /// <remarks>
    /// Chamada também depois de cada ação, porque o que a bandeja e a janela
    /// mostram sai daqui — esperar até um segundo para o ícone mudar de cor
    /// depois do clique pareceria travamento.
    /// </remarks>
    public void Atualizar()
    {
        Estado.CanalSemAudio = Estado.Gravando &&
            _capturas.Any(c => !c.Stats.JaOuviu && !c.Mudo &&
                               DuracaoAtual > 45);   // o mesmo limiar do Python

        // Requisito 3.3: sem isto o ícone continuaria vermelho enquanto a
        // gravação não chega ao disco.
        Estado.FalhaDeEscrita = _capturas.Any(c => c.FalhaDeEscrita);

        if (Estado.LembreteDeMute(DateTime.UtcNow) is { } lembrete)
            Avisar(lembrete, Win32Aviso.Atencao);

        foreach (var c in _capturas)
        {
            if (c.Desconectado && _jaAvisou.Add($"desconectado:{c.Stats.Nome}"))
                Avisar($"Dispositivo de '{c.Stats.Nome}' desconectado.", Win32Aviso.Erro, sempre: true);
            if (c.FalhaDeEscrita && _jaAvisou.Add($"escrita:{c.Stats.Nome}"))
                Avisar($"Falha ao gravar '{c.Stats.Nome}': {c.MotivoDaFalha}", Win32Aviso.Erro, sempre: true);
        }

        AoMudar?.Invoke();
    }

    /// <remarks>
    /// A bandeja da Fase 1 repetia estes dois avisos a cada segundo enquanto a
    /// falha durasse — um balão por segundo até a reunião acabar. Passava
    /// despercebido porque só a bandeja avisava; com a janela mostrando o mesmo
    /// estado, virava ruído em dobro. Limpo a cada gravação nova.
    /// </remarks>
    private readonly HashSet<string> _jaAvisou = [];

    private void Avisar(string texto, uint tipo, bool sempre = false) =>
        AoAvisar?.Invoke(texto, tipo, sempre);

    private TrackStats Stats(string nome) =>
        Faixa(nome)?.Stats ?? new TrackStats { Nome = nome };

    private string Dispositivo(string nome) => Faixa(nome)?.NomeDispositivo ?? "-";

    private int Taxa(string nome) => Faixa(nome)?.TaxaNativa ?? 0;

    public void Dispose()
    {
        Parar();
        _consulta?.Cancel();
        _agenda.Dispose();
        Dispositivos.Dispose();
    }
}

/// <summary>Os tipos de balão, para o Gravador não depender do Win32.</summary>
internal static class Win32Aviso
{
    public const uint Info = Nativo.Win32.NIIF_INFO;
    public const uint Atencao = Nativo.Win32.NIIF_WARNING;
    public const uint Erro = Nativo.Win32.NIIF_ERROR;
}
