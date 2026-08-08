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

        var status = new ToolStripMenuItem(Estado.TextoDeStatus(
            DuracaoAtual(), Capturas.FirstOrDefault(c => c.Stats.Nome == "mic")?.NomeDispositivo))
        { Enabled = false };
        menu.Items.Add(status);
        menu.Items.Add(new ToolStripSeparator());

        menu.Items.Add(new ToolStripMenuItem(
            Estado.Gravando ? (Estado.Mudo ? "Desmutar microfone" : "Mutar microfone")
                            : "Iniciar gravação",
            null, (_, _) => AoClicar()));

        // Parar só pelo menu — nunca pelo clique no ícone.
        menu.Items.Add(new ToolStripMenuItem("Parar gravação", null, (_, _) => Parar())
        { Enabled = Estado.Gravando });

        menu.Items.Add(new ToolStripSeparator());

        menu.Items.Add(new ToolStripMenuItem("Notificações", null,
            (_, _) => AlternarNotificacoes())
        { Checked = Estado.NotificacoesLigadas, CheckOnClick = false });

        menu.Items.Add(new ToolStripMenuItem("Abrir pasta das gravações", null,
            (_, _) => AbrirPasta()));
        menu.Items.Add(new ToolStripMenuItem("Escolher outra pasta...", null,
            (_, _) => EscolherPasta())
        { Enabled = !Estado.Gravando });   // trocar no meio desalinharia as faixas

        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("Sair", null, (_, _) => Application.Exit()));
    }

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
            var alto = enumerador.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            var micDev = enumerador.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications);

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
