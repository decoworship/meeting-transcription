using System.Diagnostics;
using MeetingRecorder.Agenda;
using MeetingRecorder.Capture;
using MeetingRecorder.Core;
using MeetingRecorder.Tray.Nativo;
using NAudio.CoreAudioApi;

namespace MeetingRecorder.Tray;

/// <summary>
/// A bandeja: o gravador como o usuário o vê.
/// </summary>
/// <remarks>
/// Toda a lógica de estado (cores, o que o clique faz, lembretes) vive em
/// <see cref="EstadoDaBandeja"/>, no Core, e está coberta por teste. Aqui fica só
/// a costura com o Win32 (camada <c>Nativo/</c>) — que é o que exige sessão
/// interativa e não dá para verificar sem alguém olhando a tela. O WinForms saiu
/// porque arrastava o framework WindowsDesktop inteiro e recusava trimming;
/// ver docs/FASE1-HANDOFF.md §3.
/// </remarks>
internal static class Programa
{
    private static readonly EstadoDaBandeja Estado = new();
    private static Configuracoes _cfg = null!;
    private static JanelaDeMensagens _janela = null!;
    private static IconeDeNotificacao _icone = null!;

    private static readonly List<WasapiTrackCapture> Capturas = [];
    private static CatalogoDeDispositivos _catalogo = null!;
    private static readonly ClienteDaAgenda Agenda = new();
    private static Evento? _evento;
    private static CancellationTokenSource? _consulta;
    private static string? _pastaAtual;
    private static DateTime _inicio;

    [STAThread]   // exigido pelo COM do IFileOpenDialog
    private static void Main()
    {
        // Requisito 3.4: duas bandejas disputariam os mesmos dispositivos.
        using var unica = new Mutex(true, @"Global\MeetingRecorder.Tray", out bool sozinho);
        if (!sozinho)
        {
            Win32.MessageBox(IntPtr.Zero, "O gravador já está rodando.", "Gravador",
                Win32.MB_OK | Win32.MB_ICONINFORMATION);
            return;
        }

        _cfg = Configuracoes.Carregar();
        Estado.NotificacoesLigadas = _cfg.Notifications;

        // Lê os nomes dos dispositivos uma vez, em segundo plano, e só relê
        // quando o Windows avisa que mudaram. Ver CatalogoDeDispositivos: cada
        // nome custa ~170 ms, e lê-los ao abrir o menu travava a bandeja.
        _catalogo = new CatalogoDeDispositivos();

        try
        {
            _janela = new JanelaDeMensagens
            {
                // Um segundo é suficiente: o que ele atualiza é duração, cor e
                // lembrete, nada que precise de resolução mais fina.
                AoTick = Atualizar,
                AoEventoDaBandeja = AoEvento,
                AoRenascerABarra = () => _icone.Adicionar(),
            };
            _icone = new IconeDeNotificacao(_janela.Hwnd,
                IconesDaBandeja.Obter(CorDaBandeja.Cinza), "Gravador — parado");
        }
        catch (Exception e)
        {
            // Falhar aqui é falhar em ter interface. Sem esta mensagem o
            // executável simplesmente não apareceria, sem dizer por quê.
            Win32.MessageBox(IntPtr.Zero, $"O gravador não conseguiu iniciar:\n{e.Message}",
                "Gravador", Win32.MB_OK | Win32.MB_ICONINFORMATION);
            return;
        }

        _janela.Rodar();

        Parar();
        _consulta?.Cancel();
        Agenda.Dispose();
        _catalogo.Dispose();
        _icone.Dispose();
        _janela.Dispose();
        IconesDaBandeja.Liberar();
    }

    private static void AoEvento(uint evento, int x, int y)
    {
        switch (evento)
        {
            // Clique muta e não para; parar só pelo menu.
            case Win32.NIN_SELECT or Win32.NIN_KEYSELECT:
                AoClicar();
                break;
            case Win32.WM_CONTEXTMENU:
                using (var menu = MontarMenu()) menu.Mostrar(_janela.Hwnd, x, y);
                break;
        }
    }

    // ─────────────────────────────────────────────────────────── menu

    private static MenuNativo MontarMenu()
    {
        var menu = new MenuNativo();
        var raiz = menu.Raiz;

        string status = Estado.TextoDeStatus(
            DuracaoAtual(), Capturas.FirstOrDefault(c => c.Stats.Nome == "mic")?.NomeDispositivo);
        if (_evento is { } ev) status += $"\n{Cortar(ev.Titulo, 40)}";
        // Menu do Win32 não quebra linha: cada linha vira um item desabilitado.
        foreach (string linha in status.Split('\n'))
            raiz.Item(linha, habilitado: false);
        raiz.Separador();

        raiz.Item(
            Estado.Gravando ? (Estado.Mudo ? "Desmutar microfone" : "Mutar microfone")
                            : "Iniciar gravação",
            AoClicar, negrito: true);   // a ação principal

        // Parar só pelo menu — nunca pelo clique no ícone.
        raiz.Item("Parar gravação", Parar, habilitado: Estado.Gravando);

        raiz.Separador();

        // A escolha de dispositivo trava durante a gravação: reabrir o stream no
        // meio exigiria realinhar as faixas, e o alinhamento é o que dá valor às
        // duas terem sido gravadas em separado.
        var cat = _catalogo.Atual;
        SubmenuDeDispositivos(raiz, "Microfone", cat.Entradas,
            _cfg.MicId, id => { _cfg.MicId = id; _cfg.Salvar(); });
        SubmenuDeDispositivos(raiz, "Áudio do sistema", cat.Saidas,
            _cfg.LoopbackId, id => { _cfg.LoopbackId = id; _cfg.Salvar(); });

        SubmenuDaPasta(raiz);
        SubmenuDoCalendario(raiz);

        raiz.Separador();
        raiz.Item("Notificações", AlternarNotificacoes, marcado: Estado.NotificacoesLigadas);

        raiz.Separador();
        raiz.Item("Sair", () =>
            Win32.PostMessageW(_janela.Hwnd, Win32.WM_CLOSE, IntPtr.Zero, IntPtr.Zero));

        return menu;
    }

    /// <summary>Lista os dispositivos ativos, com o escolhido marcado.</summary>
    /// <remarks>
    /// Lê só do cache do <see cref="CatalogoDeDispositivos"/>: consultar o
    /// Windows aqui custaria mais de um segundo com o menu já aberto.
    /// O item "Padrão do Windows" existe e é o default porque seguir o padrão do
    /// sistema é o que a maioria quer, e fixar um dispositivo específico quebra
    /// quando o headset é desconectado.
    /// </remarks>
    private static void SubmenuDeDispositivos(
        MenuNativo.Secao menu, string titulo, IReadOnlyList<Dispositivo> lista,
        string? escolhidoId, Action<string?> escolher)
    {
        var raiz = menu.Submenu(titulo, habilitado: !Estado.Gravando);

        raiz.Item("Padrão do Windows", () => escolher(null), marcado: escolhidoId is null);
        raiz.Separador();

        if (lista.Count == 0)
            raiz.Item("(carregando...)", habilitado: false);

        foreach (var d in lista)
        {
            // Nomes de endpoint passam de 60 caracteres com facilidade e esticam
            // o menu inteiro; 44 é o corte que o tray.py já usava.
            string nome = d.Nome.Length > 44 ? d.Nome[..44] + "..." : d.Nome;
            if (d.EhPadrao) nome += "  (padrão)";

            string id = d.Id;
            raiz.Item(nome, () => escolher(id), marcado: id == escolhidoId);
        }
    }

    private static void SubmenuDaPasta(MenuNativo.Secao menu)
    {
        var raiz = menu.Submenu("Pasta das gravações");
        string atual = _cfg.OutputDir ?? PastaPadrao();
        raiz.Item(Encurtar(atual), habilitado: false);
        raiz.Separador();
        raiz.Item("Abrir no Explorer", AbrirPasta);
        raiz.Item("Escolher outra pasta...", EscolherPasta, habilitado: !Estado.Gravando);
        if (_cfg.OutputDir is not null)
            raiz.Item("Restaurar pasta padrão",
                () => { _cfg.OutputDir = null; _cfg.Salvar(); }, habilitado: !Estado.Gravando);
    }

    /// <remarks>
    /// Sem <c>google_client_secret.json</c> o submenu vira uma instrução em vez
    /// de sumir: quem nunca configurou precisa descobrir o que falta, e um item
    /// invisível não ensina nada.
    /// </remarks>
    private static void SubmenuDoCalendario(MenuNativo.Secao menu)
    {
        var raiz = menu.Submenu("Google Calendar");

        if (!ClienteDaAgenda.EstaConfigurado())
        {
            // Só aparece em build publicado sem credencial embutida — quem monta
            // o próprio executável precisa saber onde pôr o arquivo.
            raiz.Item("Falta google_client_secret.json em", habilitado: false);
            raiz.Item(@"%USERPROFILE%\.meeting-recorder", habilitado: false);
            return;
        }

        bool autorizado = ClienteDaAgenda.EstaAutorizado();
        string conta = ClienteDaAgenda.EmailDaConta();
        raiz.Item(!autorizado ? "Nenhuma conta conectada"
            : conta.Length > 0 ? $"Conectado: {conta}" : "Conectado", habilitado: false);
        raiz.Separador();

        raiz.Item("Usar esta agenda",
            () => { _cfg.UseCalendar = !_cfg.UseCalendar; _cfg.Salvar(); },
            marcado: _cfg.UseCalendar);

        raiz.Item(autorizado ? "Trocar de conta..." : "Conectar conta...", Autorizar);

        if (autorizado)
            raiz.Item("Desconectar", () =>
            {
                ClienteDaAgenda.Desconectar();
                Avisar("Conta do Google desconectada.", Win32.NIIF_INFO);
            });
    }

    /// <summary>Abre o navegador uma vez para autorizar. Nunca na thread da UI.</summary>
    private static void Autorizar()
    {
        _ = Task.Run(async () =>
        {
            string? token = await Autorizacao.AutorizarAsync();
            if (token is not null) await Agenda.GuardarContaAsync(token);

            string conta = ClienteDaAgenda.EmailDaConta();
            string msg = token is null ? "Autorização falhou."
                : conta.Length > 0 ? $"Conectado: {conta}"
                : "Calendário autorizado.";
            NaUi(() => Avisar(msg, token is null ? Win32.NIIF_ERROR : Win32.NIIF_INFO,
                              sempre: true));
        });
    }

    /// <summary>Encurta caminho longo pelo meio, preservando início e fim.</summary>
    private static string Encurtar(string caminho, int max = 44) =>
        caminho.Length <= max ? caminho
            : caminho[..(max / 2 - 2)] + "..." + caminho[^(max / 2 - 1)..];

    // ─────────────────────────────────────────────────────── ações

    private static void AoClicar()
    {
        if (Estado.AcaoDoCliqueAtual == AcaoDoClique.Iniciar) Iniciar();
        else AlternarMudo();
    }

    private static void Iniciar()
    {
        try
        {
            string raiz = _cfg.OutputDir ?? PastaPadrao();
            _pastaAtual = Path.Combine(raiz, DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss"));
            Directory.CreateDirectory(_pastaAtual);

            var guarda = new GuardaDeDisco();
            try
            {
                long livres = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(_pastaAtual))!)
                    .AvailableFreeSpace;
                if (!guarda.PodeComecar(livres, out string? motivo))
                {
                    Avisar(motivo!, Win32.NIIF_ERROR, sempre: true);
                    return;
                }
                if (motivo is not null) Avisar(motivo, Win32.NIIF_WARNING, sempre: true);
            }
            catch
            {
                // Falha em medir espaço nunca impede gravar.
            }

            var enumerador = new MMDeviceEnumerator();
            var alto = Escolhido(enumerador, DataFlow.Render, _cfg.LoopbackId, Role.Multimedia);
            var micDev = Escolhido(enumerador, DataFlow.Capture, _cfg.MicId, Role.Communications);

            Capturas.Add(new WasapiTrackCapture(alto, true,
                Path.Combine(_pastaAtual, "system.wav"), "system"));
            Capturas.Add(new WasapiTrackCapture(micDev, false,
                Path.Combine(_pastaAtual, "mic.wav"), "mic"));

            // Origem única: é o que faz as duas faixas ficarem alinhadas por
            // construção em vez de por sorte.
            long origem = WasapiTrackCapture.QpcAgora();
            foreach (var c in Capturas) c.Iniciar(origem);

            _inicio = DateTime.UtcNow;
            Estado.Iniciou();
            // Depois de Iniciar(), nunca antes: a agenda é chamada de rede e não
            // pode atrasar o começo da captura. Ver ClienteDaAgenda.
            _evento = null;
            if (_cfg.UseCalendar) ConsultarAgenda();
            if (_cfg.StartMuted) AlternarMudo();
            Atualizar();
        }
        catch (Exception e)
        {
            Avisar($"Não foi possível iniciar: {e.Message}", Win32.NIIF_ERROR, sempre: true);
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

    private static void AlternarMudo()
    {
        bool novo = !Estado.Mudo;
        Estado.DefinirMudo(novo, DateTime.UtcNow);
        foreach (var c in Capturas.Where(c => c.Stats.Nome == "mic")) c.Mudo = novo;
        Atualizar();
    }

    private static void Parar()
    {
        if (!Estado.Gravando) return;

        foreach (var c in Capturas) c.Parar();

        var system = Stats("system");
        var mic = Stats("mic");
        // Cancela uma consulta ainda em voo: a gravação acabou, o rótulo dela
        // não serve mais para nada e o resultado chegaria depois do meta.json.
        _consulta?.Cancel();
        var evento = _evento;
        _evento = null;

        var meta = Meta.Montar(DateTimeOffset.Now, system, mic,
            Dispositivo("system"), Dispositivo("mic"),
            Taxa("system"), Taxa("mic"), evento?.ParaMeta());

        if (_pastaAtual is not null)
            File.WriteAllText(Path.Combine(_pastaAtual, "meta.json"), meta.ParaJson());

        // Avisar sobre faixa suspeita agora, não depois: é o aprendizado de 06/08,
        // quando uma gravação 95% muda só foi descoberta na transcrição.
        foreach (var (nome, s) in new[] { ("system", system), ("mic", mic) })
        {
            if (s.SemAudio)
                Avisar($"A faixa '{nome}' não teve áudio nenhum.", Win32.NIIF_WARNING, sempre: true);
            else if (s.PercentualUtil(meta.DurationS) < 20)
                Avisar($"A faixa '{nome}' tem só {s.PercentualUtil(meta.DurationS):F0}% de conteúdo útil.",
                       Win32.NIIF_WARNING, sempre: true);
        }

        foreach (var c in Capturas) c.Dispose();
        Capturas.Clear();
        Estado.Parou();
        Atualizar();
    }

    /// <summary>
    /// Procura na agenda a reunião correspondente. Roda fora da thread da UI.
    /// </summary>
    private static void ConsultarAgenda()
    {
        _consulta?.Cancel();
        var cts = new CancellationTokenSource();
        _consulta = cts;

        _ = Task.Run(async () =>
        {
            var r = await Agenda.EventoAtualAsync(ct: cts.Token);
            NaUi(() =>
            {
                if (!Estado.Gravando || cts.IsCancellationRequested) return;

                if (r.ExigeAtencao)
                {
                    // Não achar reunião é normal; ter autorizado e o token morrer
                    // não é. Sem este aviso o gravador pararia de identificar
                    // reuniões em silêncio, e só se descobriria semanas depois.
                    Avisar("Calendário indisponível. Reautorize pelo menu.\n"
                           + "A gravação continua normalmente.",
                           Win32.NIIF_WARNING, sempre: true);
                    return;
                }

                if (r.Evento is not { } ev) return;

                _evento = ev;
                int n = ev.NomesDosParticipantes().Count;
                Avisar($"{Cortar(ev.Titulo, 40)}\n{n} participantes", Win32.NIIF_INFO);
                Atualizar();
            });
        });
    }

    /// <summary>Executa na thread da UI, venha de onde vier.</summary>
    private static void NaUi(Action acao) => _janela.Executar(acao);

    private static string Cortar(string texto, int max) =>
        texto.Length <= max ? texto : texto[..max];

    private static void AlternarNotificacoes()
    {
        Estado.NotificacoesLigadas = !Estado.NotificacoesLigadas;
        _cfg.Notifications = Estado.NotificacoesLigadas;
        _cfg.Salvar();
    }

    private static void AbrirPasta()
    {
        string alvo = _pastaAtual ?? _cfg.OutputDir ?? PastaPadrao();
        Directory.CreateDirectory(alvo);
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{alvo}\"") { UseShellExecute = true });
    }

    private static void EscolherPasta()
    {
        string? escolhida = SeletorDePasta.Escolher(_janela.Hwnd,
            _cfg.OutputDir ?? PastaPadrao(), "Onde salvar as gravações");
        if (escolhida is null) return;

        // Escrita de teste antes de aceitar: descobrir que a pasta é somente
        // leitura no meio de uma reunião seria tarde demais.
        try
        {
            string teste = Path.Combine(escolhida, ".gravador-teste");
            File.WriteAllText(teste, "ok");
            File.Delete(teste);
        }
        catch (Exception e)
        {
            Avisar($"Não dá para escrever nessa pasta: {e.Message}", Win32.NIIF_ERROR, sempre: true);
            return;
        }

        _cfg.OutputDir = escolhida;
        _cfg.Salvar();
    }

    // ────────────────────────────────────────────────────── suporte

    private static void Atualizar()
    {
        Estado.CanalSemAudio = Estado.Gravando &&
            Capturas.Any(c => !c.Stats.JaOuviu && !c.Mudo &&
                              DuracaoAtual() > 45);   // o mesmo limiar do Python

        // Requisito 3.3: sem isto o ícone continuaria vermelho enquanto a
        // gravação não chega ao disco.
        Estado.FalhaDeEscrita = Capturas.Any(c => c.FalhaDeEscrita);

        string txt = "Gravador — " + Estado.TextoDeStatus(DuracaoAtual(), null).Split('\n')[0];
        _icone.Atualizar(IconesDaBandeja.Obter(Estado.Cor), txt);

        if (Estado.LembreteDeMute(DateTime.UtcNow) is { } lembrete)
            Avisar(lembrete, Win32.NIIF_WARNING);

        foreach (var c in Capturas)
        {
            if (c.Desconectado)
                Avisar($"Dispositivo de '{c.Stats.Nome}' desconectado.", Win32.NIIF_ERROR, sempre: true);
            if (c.FalhaDeEscrita)
                Avisar($"Falha ao gravar '{c.Stats.Nome}': {c.MotivoDaFalha}", Win32.NIIF_ERROR, sempre: true);
        }
    }

    /// <param name="sempre">
    /// Avisos de falha ignoram o A14: desligar notificações silencia o lembrete
    /// de mute, que é sobre algo que você pediu — não silencia dispositivo caindo
    /// ou disco enchendo, que são coisas que você precisa saber.
    /// </param>
    private static void Avisar(string texto, uint tipo, bool sempre = false)
    {
        if (!sempre && !Estado.NotificacoesLigadas) return;
        _icone.Balao("Gravador", texto, tipo);
    }

    private static double DuracaoAtual() =>
        Estado.Gravando ? (DateTime.UtcNow - _inicio).TotalSeconds : 0;

    private static TrackStats Stats(string nome) =>
        Capturas.FirstOrDefault(c => c.Stats.Nome == nome)?.Stats ?? new TrackStats { Nome = nome };

    private static string Dispositivo(string nome) =>
        Capturas.FirstOrDefault(c => c.Stats.Nome == nome)?.NomeDispositivo ?? "-";

    private static int Taxa(string nome) =>
        Capturas.FirstOrDefault(c => c.Stats.Nome == nome)?.TaxaNativa ?? 0;

    private static string PastaPadrao() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "MeetingRecordings");
}
