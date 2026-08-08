using System.Diagnostics;
using MeetingRecorder.Capture;
using MeetingRecorder.Core;
using NAudio.CoreAudioApi;

namespace MeetingRecorder.Tray;

/// <summary>
/// A bandeja: o gravador como o usuário o vê.
/// </summary>
/// <remarks>
/// Toda a lógica de estado (cores, o que o clique faz, lembretes) vive em
/// <see cref="EstadoDaBandeja"/>, no Core, e está coberta por teste. Aqui fica só
/// a costura com o WinForms — que é o que exige sessão interativa e não dá para
/// verificar sem alguém olhando a tela.
/// </remarks>
internal static class Programa
{
    private static readonly EstadoDaBandeja Estado = new();
    private static Configuracoes _cfg = null!;
    private static NotifyIcon _icone = null!;
    private static System.Windows.Forms.Timer _relogio = null!;

    private static readonly List<WasapiTrackCapture> Capturas = [];
    private static string? _pastaAtual;
    private static DateTime _inicio;

    [STAThread]
    private static void Main()
    {
        // Requisito 3.4: duas bandejas disputariam os mesmos dispositivos.
        using var unica = new Mutex(true, @"Global\MeetingRecorder.Tray", out bool sozinho);
        if (!sozinho)
        {
            MessageBox.Show("O gravador já está rodando.", "Gravador",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        ApplicationConfiguration.Initialize();
        _cfg = Configuracoes.Carregar();
        Estado.NotificacoesLigadas = _cfg.Notifications;

        _icone = new NotifyIcon
        {
            Icon = IconeDaBandeja.De(CorDaBandeja.Cinza),
            Text = "Gravador — parado",
            Visible = true,
            ContextMenuStrip = MontarMenu(),
        };
        _icone.MouseClick += (_, e) => { if (e.Button == MouseButtons.Left) AoClicar(); };

        // Um segundo é suficiente: o que ele atualiza é duração, cor e lembrete,
        // nada que precise de resolução mais fina.
        _relogio = new System.Windows.Forms.Timer { Interval = 1000 };
        _relogio.Tick += (_, _) => Atualizar();
        _relogio.Start();

        Application.ApplicationExit += (_, _) => { Parar(); _icone.Visible = false; };
        Application.Run();
    }

    // ─────────────────────────────────────────────────────────── menu

    private static ContextMenuStrip MontarMenu()
    {
        var menu = new ContextMenuStrip();
        menu.Opening += (_, _) => Remontar(menu);
        Remontar(menu);
        return menu;
    }

    private static void Remontar(ContextMenuStrip menu)
    {
        menu.Items.Clear();

        menu.Items.Add(new ToolStripMenuItem(Estado.TextoDeStatus(
            DuracaoAtual(), Capturas.FirstOrDefault(c => c.Stats.Nome == "mic")?.NomeDispositivo))
        { Enabled = false });
        menu.Items.Add(new ToolStripSeparator());

        menu.Items.Add(new ToolStripMenuItem(
            Estado.Gravando ? (Estado.Mudo ? "Desmutar microfone" : "Mutar microfone")
                            : "Iniciar gravação",
            null, (_, _) => AoClicar())
        { Font = new Font(menu.Font, FontStyle.Bold) });   // a ação principal

        // Parar só pelo menu — nunca pelo clique no ícone.
        menu.Items.Add(new ToolStripMenuItem("Parar gravação", null, (_, _) => Parar())
        { Enabled = Estado.Gravando });

        menu.Items.Add(new ToolStripSeparator());

        // A escolha de dispositivo trava durante a gravação: reabrir o stream no
        // meio exigiria realinhar as faixas, e o alinhamento é o que dá valor às
        // duas terem sido gravadas em separado.
        menu.Items.Add(SubmenuDeDispositivos("Microfone", DataFlow.Capture, Role.Communications,
            _cfg.MicId, id => { _cfg.MicId = id; _cfg.Salvar(); }));
        menu.Items.Add(SubmenuDeDispositivos("Áudio do sistema", DataFlow.Render, Role.Multimedia,
            _cfg.LoopbackId, id => { _cfg.LoopbackId = id; _cfg.Salvar(); }));

        menu.Items.Add(SubmenuDaPasta());
        menu.Items.Add(SubmenuDoCalendario());

        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("Notificações", null,
            (_, _) => AlternarNotificacoes())
        { Checked = Estado.NotificacoesLigadas });

        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("Sair", null, (_, _) => Application.Exit()));
    }

    /// <summary>Lista os dispositivos ativos, com o escolhido marcado.</summary>
    /// <remarks>
    /// O item "Padrão do Windows" existe e é o default: seguir o padrão do
    /// sistema é o que a maioria quer, e fixar um dispositivo específico quebra
    /// quando o headset é desconectado.
    /// </remarks>
    private static ToolStripMenuItem SubmenuDeDispositivos(
        string titulo, DataFlow fluxo, Role papel, string? escolhidoId, Action<string?> escolher)
    {
        var raiz = new ToolStripMenuItem(titulo) { Enabled = !Estado.Gravando };

        raiz.DropDownItems.Add(new ToolStripMenuItem("Padrão do Windows", null,
            (_, _) => escolher(null))
        { Checked = escolhidoId is null });
        raiz.DropDownItems.Add(new ToolStripSeparator());

        try
        {
            using var e = new MMDeviceEnumerator();
            var padrao = e.HasDefaultAudioEndpoint(fluxo, papel)
                ? e.GetDefaultAudioEndpoint(fluxo, papel).ID : null;

            var lista = e.EnumerateAudioEndPoints(fluxo, DeviceState.Active).ToList();
            if (lista.Count == 0)
                raiz.DropDownItems.Add(new ToolStripMenuItem("(nenhum encontrado)")
                { Enabled = false });

            foreach (var d in lista)
            {
                string id = d.ID;
                // Nomes de endpoint passam de 60 caracteres com facilidade e
                // esticam o menu inteiro; 44 é o corte que o tray.py já usava.
                string nome = d.FriendlyName.Length > 44
                    ? d.FriendlyName[..44] + "..." : d.FriendlyName;
                if (id == padrao) nome += "  (padrão)";

                raiz.DropDownItems.Add(new ToolStripMenuItem(nome, null, (_, _) => escolher(id))
                { Checked = id == escolhidoId });
            }
        }
        catch (Exception ex)
        {
            raiz.DropDownItems.Add(new ToolStripMenuItem($"(erro ao listar: {ex.Message})")
            { Enabled = false });
        }

        if (Estado.Gravando)
            raiz.ToolTipText = "Não dá para trocar de dispositivo durante a gravação.";
        return raiz;
    }

    private static ToolStripMenuItem SubmenuDaPasta()
    {
        var raiz = new ToolStripMenuItem("Pasta das gravações");
        string atual = _cfg.OutputDir ?? PastaPadrao();
        raiz.DropDownItems.Add(new ToolStripMenuItem(Encurtar(atual)) { Enabled = false });
        raiz.DropDownItems.Add(new ToolStripSeparator());
        raiz.DropDownItems.Add(new ToolStripMenuItem("Abrir no Explorer", null, (_, _) => AbrirPasta()));
        raiz.DropDownItems.Add(new ToolStripMenuItem("Escolher outra pasta...", null,
            (_, _) => EscolherPasta())
        { Enabled = !Estado.Gravando });
        if (_cfg.OutputDir is not null)
            raiz.DropDownItems.Add(new ToolStripMenuItem("Restaurar pasta padrão", null,
                (_, _) => { _cfg.OutputDir = null; _cfg.Salvar(); })
            { Enabled = !Estado.Gravando });
        return raiz;
    }

    private static ToolStripMenuItem SubmenuDoCalendario()
    {
        var raiz = new ToolStripMenuItem("Google Calendar");
        raiz.DropDownItems.Add(new ToolStripMenuItem("(ainda não portado — item 4 da Fase 1)")
        { Enabled = false });
        raiz.DropDownItems.Add(new ToolStripSeparator());
        raiz.DropDownItems.Add(new ToolStripMenuItem("Usar a agenda", null,
            (_, _) => { _cfg.UseCalendar = !_cfg.UseCalendar; _cfg.Salvar(); })
        { Checked = _cfg.UseCalendar, Enabled = false });
        return raiz;
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
                    Avisar(motivo!, ToolTipIcon.Error, sempre: true);
                    return;
                }
                if (motivo is not null) Avisar(motivo, ToolTipIcon.Warning, sempre: true);
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
            if (_cfg.StartMuted) AlternarMudo();
            Atualizar();
        }
        catch (Exception e)
        {
            Avisar($"Não foi possível iniciar: {e.Message}", ToolTipIcon.Error, sempre: true);
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
        var meta = Meta.Montar(DateTimeOffset.Now, system, mic,
            Dispositivo("system"), Dispositivo("mic"),
            Taxa("system"), Taxa("mic"));

        if (_pastaAtual is not null)
            File.WriteAllText(Path.Combine(_pastaAtual, "meta.json"), meta.ParaJson());

        // Avisar sobre faixa suspeita agora, não depois: é o aprendizado de 06/08,
        // quando uma gravação 95% muda só foi descoberta na transcrição.
        foreach (var (nome, s) in new[] { ("system", system), ("mic", mic) })
        {
            if (s.SemAudio)
                Avisar($"A faixa '{nome}' não teve áudio nenhum.", ToolTipIcon.Warning, sempre: true);
            else if (s.PercentualUtil(meta.DurationS) < 20)
                Avisar($"A faixa '{nome}' tem só {s.PercentualUtil(meta.DurationS):F0}% de conteúdo útil.",
                       ToolTipIcon.Warning, sempre: true);
        }

        foreach (var c in Capturas) c.Dispose();
        Capturas.Clear();
        Estado.Parou();
        Atualizar();
    }

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
        using var dlg = new FolderBrowserDialog
        {
            Description = "Onde salvar as gravações",
            SelectedPath = _cfg.OutputDir ?? PastaPadrao(),
        };
        if (dlg.ShowDialog() != DialogResult.OK) return;

        // Escrita de teste antes de aceitar: descobrir que a pasta é somente
        // leitura no meio de uma reunião seria tarde demais.
        try
        {
            string teste = Path.Combine(dlg.SelectedPath, ".gravador-teste");
            File.WriteAllText(teste, "ok");
            File.Delete(teste);
        }
        catch (Exception e)
        {
            Avisar($"Não dá para escrever nessa pasta: {e.Message}", ToolTipIcon.Error, sempre: true);
            return;
        }

        _cfg.OutputDir = dlg.SelectedPath;
        _cfg.Salvar();
    }

    // ────────────────────────────────────────────────────── suporte

    private static void Atualizar()
    {
        Estado.CanalSemAudio = Estado.Gravando &&
            Capturas.Any(c => !c.Stats.JaOuviu && !c.Mudo &&
                              DuracaoAtual() > 45);   // o mesmo limiar do Python

        _icone.Icon = IconeDaBandeja.De(Estado.Cor);
        string txt = "Gravador — " + Estado.TextoDeStatus(DuracaoAtual(), null).Split('\n')[0];
        // O Text do NotifyIcon estoura em 63 caracteres e lança se passar.
        _icone.Text = txt.Length > 62 ? txt[..62] : txt;

        if (Estado.LembreteDeMute(DateTime.UtcNow) is { } lembrete)
            Avisar(lembrete, ToolTipIcon.Warning);

        foreach (var c in Capturas)
        {
            if (c.Desconectado)
                Avisar($"Dispositivo de '{c.Stats.Nome}' desconectado.", ToolTipIcon.Error, sempre: true);
            if (c.FalhaDeEscrita)
                Avisar($"Falha ao gravar '{c.Stats.Nome}': {c.MotivoDaFalha}", ToolTipIcon.Error, sempre: true);
        }
    }

    /// <param name="sempre">
    /// Avisos de falha ignoram o A14: desligar notificações silencia o lembrete
    /// de mute, que é sobre algo que você pediu — não silencia dispositivo caindo
    /// ou disco enchendo, que são coisas que você precisa saber.
    /// </param>
    private static void Avisar(string texto, ToolTipIcon tipo, bool sempre = false)
    {
        if (!sempre && !Estado.NotificacoesLigadas) return;
        _icone.ShowBalloonTip(5000, "Gravador", texto, tipo);
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
