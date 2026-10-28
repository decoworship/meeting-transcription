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

    /// <summary>
    /// A reunião que o usuário escolheu na tela, e que vence a busca automática.
    /// </summary>
    /// <remarks>
    /// Existe porque a escolha automática acerta o caso comum e não tem como
    /// acertar o caso ambíguo: duas reuniões sobrepostas, ou a que começa em
    /// vinte minutos e é a que vai ser gravada. Fixar é dizer qual, antes de
    /// começar — e o rótulo errado só se descobre depois, na ata.
    /// </remarks>
    public Evento? Fixado { get; private set; }

    /// <summary>
    /// A última lista mostrada à tela, para <see cref="Fixar"/> resolver o id.
    /// </summary>
    /// <remarks>
    /// Fixar não pode fazer rede: é um clique, e o resultado tem de valer mesmo
    /// que a agenda caia entre a lista e o clique. O id sozinho não serve — o
    /// meta.json precisa do evento inteiro, com participantes e e-mails.
    /// </remarks>
    private IReadOnlyList<Evento> _ultimasProximas = [];

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

    /// <summary>
    /// Avisado quando uma gravação começa, e quando ela termina.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Existe porque gravar tem TRÊS portas</b>, e pendurar-se numa delas é
    /// pendurar-se em nenhuma: o botão da janela (<c>Ponte</c>, op
    /// <c>gravar</c>), o clique no ícone da bandeja e o item do menu dela — os
    /// dois últimos pelo <see cref="AoClicar"/>. Em 09/09/2026 a prévia ao vivo
    /// nasceu presa só à primeira, e o dono do produto gravou duas reuniões pela
    /// bandeja: ela nunca soube que a reunião tinha começado, e não deixou nem
    /// uma linha no registro dizendo por quê.
    /// </para>
    /// <para>
    /// Quem sabe que a gravação começou é <b>o gravador</b>, e é dele que o aviso
    /// tem de sair. Uma porta nova amanhã passa a ser coberta sem ninguém
    /// lembrar disto.
    /// </para>
    /// <para>
    /// <b>O gravador não sabe o que os assinantes fazem, e não pode morrer por
    /// causa deles.</b> Uma exceção num assinante é engolida: o pior que pode
    /// acontecer a quem escuta é não acontecer, e nada disso vale uma reunião.
    /// </para>
    /// </remarks>
    public event Action<string>? AoComecar;

    /// <inheritdoc cref="AoComecar"/>
    public event Action? AoTerminar;

    /// <summary>
    /// Alguém pediu um print da tela: o botão do Gravador, pela ponte.
    /// </summary>
    /// <remarks>
    /// O gravador só repassa. Quem tira o print é a <c>Aplicacao</c>, que tem a
    /// janela a esconder e a bandeja a avisar — as mesmas que o atalho global
    /// usa, para o botão e o atalho não divergirem.
    /// </remarks>
    public event Action? AoPedirCaptura;

    public void PedirCaptura()
    {
        if (Estado.Gravando) Avisar(() => AoPedirCaptura?.Invoke());
    }

    /// <summary>
    /// O atalho global de capturar a tela, quando a <c>Aplicacao</c> conseguiu
    /// registrá-lo nesta gravação. Ver <c>JanelaDeMensagens.LigarAtalhoDeCaptura</c>.
    /// </summary>
    public string? AtalhoDeCaptura { get; set; }

    private void Avisar(Action? quem)
    {
        try { quem?.Invoke(); }
        catch (Exception e)
        {
            MeetingApp.Nucleo.Registro.Escrever("gravador", $"assinante falhou: {e.Message}");
        }
    }

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
            // A escolhida na tela vence, e nem chega a consultar: já está em
            // memória, não custa rede, e é uma resposta que o usuário deu
            // olhando a agenda — melhor informada que qualquer heurística.
            Evento = Fixado;
            if (Cfg.UseCalendar && Fixado is null) ConsultarAgenda();
            if (Cfg.StartMuted) DefinirMudo(true);
            // Começar no meio de uma reunião do Teams segue o mute dela — vence o
            // start_muted, que é um palpite de antes de saber da reunião.
            Aplicar(_sincroniaDoTeams.AoComecarAGravar(Estado.Mudo));
            Aplicar(_sincroniaDoNavegador.AoComecarAGravar(Estado.Mudo));
            Atualizar();

            // No fim, e só se chegou até aqui: quem escuta recebe uma gravação
            // que já existe em disco, com as faixas abertas. Ver AoComecar.
            if (PastaAtual is { Length: > 0 } pasta)
                Avisar(() => AoComecar?.Invoke(pasta));
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

    /// <summary>A pessoa mutou ou desmutou: a bandeja ou a tela do Gravador.</summary>
    /// <remarks>
    /// É o único caminho que vai às chamadas. O mute que veio delas, e o do
    /// <c>start_muted</c>, passam pelo <see cref="DefinirMudo"/> direto — senão a
    /// chamada receberia de volta o próprio eco.
    /// </remarks>
    public void AlternarMudo()
    {
        if (!Estado.Gravando) return;
        DefinirMudo(!Estado.Mudo);
        var agora = DateTime.UtcNow;
        if (_sincroniaDoTeams.AoMutarNoApp(Estado.Mudo, agora) == AcaoDaSincronia.AlternarNaChamada)
            _ = _teams?.EnviarAsync(ProtocoloDoTeams.AlternarMudo);
        if (_sincroniaDoNavegador.AoMutarNoApp(Estado.Mudo, agora) == AcaoDaSincronia.AlternarNaChamada)
            _ = _navegador?.MutarAsync(Estado.Mudo);
    }

    private void DefinirMudo(bool mudo)
    {
        if (!Estado.Gravando || Estado.Mudo == mudo) return;
        Estado.DefinirMudo(mudo, DateTime.UtcNow);
        foreach (var c in _capturas.Where(c => c.Stats.Nome == "mic")) c.Mudo = mudo;
        Atualizar();
    }

    // ─────────────────────────────────────────── o mute junto com o Teams

    private readonly SincroniaDoMute _sincroniaDoTeams = new();
    private ConexaoComOTeams? _teams;

    /// <summary>
    /// "desligada", "procurando", "conectado", "pareado" ou "em-reuniao" — o que
    /// Ajustes e a bandeja dizem sobre a ponte com o Teams.
    /// </summary>
    public string SituacaoDoTeams =>
        _teams is null ? "desligada"
        : !_teams.Conectado ? "procurando"
        : _sincroniaDoTeams.Chamada is { EmReuniao: true, Mudo: not null } ? "em-reuniao"
        : Cfg.TokenDoTeams is { Length: > 0 } ? "pareado"
        : "conectado";

    /// <summary>Liga ou desliga a sincronia, e guarda a escolha.</summary>
    public void SincronizarMute(bool ligar)
    {
        Cfg.SincronizarMute = ligar;
        Cfg.Salvar();
        AplicarSincronia();
        Atualizar();
    }

    /// <summary>Sobe ou derruba as pontes — o Teams desktop e a extensão — conforme o que está salvo.</summary>
    /// <param name="versao">O que o Teams mostra ao pedir permissão, ao lado do nome.</param>
    public void AplicarSincronia(string? versao = null)
    {
        if (versao is not null) _versaoParaOTeams = versao;
        if (Cfg.SincronizarMute && _teams is null)
        {
            var c = new ConexaoComOTeams(MeetingApp.Nucleo.Marca.Nome, _versaoParaOTeams,
                                         () => Cfg.TokenDoTeams);
            c.AoReceber += m => naUi(() => AoMensagemDoTeams(m));
            c.AoMudarConexao += conectou => naUi(() => AoMudarConexaoDoTeams(conectou));
            _teams = c;
            c.Ligar();
            MeetingApp.Nucleo.Registro.Escrever("teams", "sincronia do mute ligada");
        }
        else if (!Cfg.SincronizarMute && _teams is { } velha)
        {
            _teams = null;
            _sincroniaDoTeams.AoPerderAChamada();
            // Fora da thread da UI: o laço pode levar até dois segundos para sair.
            _ = Task.Run(velha.Dispose);
            MeetingApp.Nucleo.Registro.Escrever("teams", "sincronia do mute desligada");
        }

        if (Cfg.SincronizarMuteNoNavegador && _navegador is null)
        {
            var s = new ServidorDaExtensao();
            s.AoReceber += e => naUi(() => AoMensagemDoNavegador(e));
            s.AoMudarConexao += conectou => naUi(() => AoMudarConexaoDoNavegador(conectou));
            _navegador = s;
            s.Ligar();
            MeetingApp.Nucleo.Registro.Escrever("extensao", "sincronia do mute ligada");
        }
        else if (!Cfg.SincronizarMuteNoNavegador && _navegador is { } velho)
        {
            _navegador = null;
            _sincroniaDoNavegador.AoPerderAChamada();
            _ = Task.Run(velho.Dispose);
            MeetingApp.Nucleo.Registro.Escrever("extensao", "sincronia do mute desligada");
        }
    }

    private string _versaoParaOTeams = "0";

    private void AoMudarConexaoDoTeams(bool conectou)
    {
        if (_teams is null) return;
        if (!conectou) _sincroniaDoTeams.AoPerderAChamada();
        MeetingApp.Nucleo.Registro.Escrever("teams", conectou ? "conectado"
            : $"desconectado{(_teams.UltimaFalha is { } f ? $": {f}" : "")}");
        Atualizar();
    }

    private void AoMensagemDoTeams(MensagemDoTeams m)
    {
        if (_teams is null) return;

        if (m.TokenNovo is { } token && token != Cfg.TokenDoTeams)
        {
            Cfg.TokenDoTeams = token;
            Cfg.Salvar();
            MeetingApp.Nucleo.Registro.Escrever("teams", "pareado: o Teams permitiu o app");
        }
        if (m.Erro is { } erro)
            MeetingApp.Nucleo.Registro.Escrever("teams", $"o Teams recusou: {erro}");

        if (m.Estado is { } e)
        {
            var antes = SituacaoDoTeams;
            Aplicar(_sincroniaDoTeams.AoEstadoDaChamada(e, Estado.Gravando, Estado.Mudo, DateTime.UtcNow));
            if (_sincroniaDoTeams.QuerParear())
            {
                MeetingApp.Nucleo.Registro.Escrever("teams", "pedindo permissão ao Teams");
                _ = _teams.EnviarAsync(ProtocoloDoTeams.PedirEstado);
            }
            if (SituacaoDoTeams != antes) Atualizar();
        }
    }

    // ──────────────────── o mute junto com o Meet e o Teams no navegador

    private readonly SincroniaDoMute _sincroniaDoNavegador = new();
    private ServidorDaExtensao? _navegador;

    /// <summary>
    /// "desligada", "esperando" (a extensão não conectou), "conectado" ou
    /// "em-reuniao" — o que Ajustes e a bandeja dizem sobre a extensão de
    /// navegador. Qual reunião, se do Meet ou do Teams, é <see cref="AppNoNavegador"/>.
    /// </summary>
    public string SituacaoDoNavegador =>
        _navegador is null ? "desligada"
        : !_navegador.Conectado ? "esperando"
        : _sincroniaDoNavegador.Chamada is { EmReuniao: true } ? "em-reuniao"
        : "conectado";

    /// <summary>"meet" ou "teams": a página da reunião que a extensão está seguindo.</summary>
    public string? AppNoNavegador { get; private set; }

    /// <summary>Liga ou desliga a sincronia pela extensão, e guarda a escolha.</summary>
    public void SincronizarMuteNoNavegador(bool ligar)
    {
        Cfg.SincronizarMuteNoNavegador = ligar;
        Cfg.Salvar();
        AplicarSincronia();
        Atualizar();
    }

    /// <summary>A pasta da extensão, que a pessoa carrega no Chrome ou no Edge.</summary>
    /// <remarks>Chama-se extensao-meet porque nasceu só do Meet; o Chrome guarda o caminho.</remarks>
    public static string PastaDaExtensao => Path.Combine(AppContext.BaseDirectory, "extensao-meet");

    private void AoMudarConexaoDoNavegador(bool conectou)
    {
        if (_navegador is null) return;
        if (!conectou)
        {
            _sincroniaDoNavegador.AoPerderAChamada();
            AppNoNavegador = null;
        }
        MeetingApp.Nucleo.Registro.Escrever("extensao", conectou ? "extensão conectada"
            : $"extensão saiu{(_navegador.UltimaFalha is { } f ? $" ({f})" : "")}");
        Atualizar();
    }

    private void AoMensagemDoNavegador(MensagemDaExtensao m)
    {
        if (_navegador is null) return;
        // O que a página achou do botão: é o que diz o que mudou quando o Google
        // ou a Microsoft mexem na página. Só chega quando muda.
        if (m.Diagnostico is { } d) MeetingApp.Nucleo.Registro.Escrever("extensao", d);
        if (m.Estado is not { } e) return;

        var antes = (SituacaoDoNavegador, AppNoNavegador);
        // Trocar de reunião — do Meet para o Teams — é outra chamada: o mute da
        // anterior não vale como "o que ela era antes".
        if (e.EmReuniao && m.App is { } app && AppNoNavegador is { } eraOutro && app != eraOutro)
            _sincroniaDoNavegador.AoPerderAChamada();
        if (e.EmReuniao) AppNoNavegador = m.App;
        Aplicar(_sincroniaDoNavegador.AoEstadoDaChamada(e, Estado.Gravando, Estado.Mudo, DateTime.UtcNow));
        if ((SituacaoDoNavegador, AppNoNavegador) != antes) Atualizar();
    }

    private void Aplicar(AcaoDaSincronia a)
    {
        if (a == AcaoDaSincronia.MutarNoApp) DefinirMudo(true);
        else if (a == AcaoDaSincronia.DesmutarNoApp) DefinirMudo(false);
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

        // Antes de fechar as faixas: quem lê o áudio enquanto ele é gravado
        // precisa parar primeiro, senão tenta ler um arquivo que acabou de
        // fechar e polui o registro com falhas que não são falhas.
        Avisar(() => AoTerminar?.Invoke());

        foreach (var c in _capturas) c.Parar();

        var system = Stats("system");
        var mic = Stats("mic");
        // Cancela uma consulta ainda em voo: a gravação acabou, o rótulo dela
        // não serve mais para nada e o resultado chegaria depois do meta.json.
        _consulta?.Cancel();
        var evento = Evento;
        Evento = null;
        // A escolha vale para uma gravação, não para o dia: deixá-la de pé
        // rotularia a próxima reunião com o título da anterior, em silêncio.
        Fixado = null;

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
        // Desligar é dizer que o calendário não rotula gravação nenhuma — uma
        // reunião fixada que sobrevivesse ao desligamento faria exatamente isso,
        // e a tela onde ela foi escolhida some junto com o ajuste.
        if (!usar) Fixar(null); else Atualizar();
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

    /// <summary>
    /// As próximas reuniões, para a tela oferecer a escolha. Faz rede.
    /// </summary>
    /// <remarks>
    /// Guarda a lista porque <see cref="Fixar"/> resolve o id contra ela. Fora
    /// do caminho de <see cref="Iniciar"/>, como tudo que fala com a agenda.
    /// </remarks>
    public async Task<Proximas> ProximasAsync(CancellationToken ct = default)
    {
        var r = await _agenda.ProximasAsync(ct: ct);
        _ultimasProximas = r.Eventos;
        return r;
    }

    /// <summary>
    /// Escolhe qual reunião rotula a gravação. Id nulo ou desconhecido solta.
    /// </summary>
    /// <remarks>
    /// Vale durante a gravação também: o <c>meta.json</c> só é escrito ao parar,
    /// então corrigir o rótulo no meio da reunião ainda chega a tempo — e é
    /// quando se percebe que a reunião é outra.
    /// </remarks>
    public void Fixar(string? id)
    {
        Fixado = id is { Length: > 0 }
            ? _ultimasProximas.FirstOrDefault(e => e.Id == id)
            : null;

        if (Estado.Gravando)
        {
            // Solta a consulta em voo pelo mesmo motivo do Parar: o resultado
            // dela chegaria depois e por cima de uma escolha explícita.
            _consulta?.Cancel();
            Evento = Fixado;
            // Soltar no meio da gravação devolve o rótulo à agenda em vez de
            // deixar a gravação sem nenhum: quem soltou disse "não é esta", e
            // não "esta não tem reunião".
            if (Fixado is null && Cfg.UseCalendar) ConsultarAgenda();
        }
        Atualizar();
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
                // Fixaram enquanto a consulta estava em voo: a resposta do
                // usuário vale mais que a nossa, e chegou depois.
                if (Fixado is not null) return;

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
        _teams?.Dispose();
        _navegador?.Dispose();
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
