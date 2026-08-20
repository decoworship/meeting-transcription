using System.Diagnostics;
using MeetingApp.App.Nativo;
using MeetingRecorder.Agenda;
using MeetingRecorder.Capture;
using MeetingRecorder.Core;

namespace MeetingApp.App.Bandeja;

/// <summary>
/// O ícone na bandeja: o gravador como ele sempre foi visto.
/// </summary>
/// <remarks>
/// <para>
/// <b>A bandeja continua.</b> Decisão registrada do dono do produto: a tela do
/// Gravador dentro da janela é adição, não substituição — gravar não pode
/// depender de ter uma janela aberta, e fechar a janela no meio de uma reunião
/// não pode parar a gravação (FASE2.5.md §1).
/// </para>
/// <para>
/// Continua sendo só a costura com o Win32. Toda a lógica de estado vive no
/// <see cref="EstadoDaBandeja"/>, e as ações no <see cref="Gravador"/>.
/// </para>
/// </remarks>
internal sealed class Bandeja : IDisposable
{
    private readonly Gravador _gravador;
    private readonly JanelaDeMensagens _janela;
    private readonly IconeDeNotificacao _icone;
    private readonly Action _abrirJanela;
    private readonly Action _sair;

    public Bandeja(Gravador gravador, JanelaDeMensagens janela,
                   Action abrirJanela, Action sair)
    {
        _gravador = gravador;
        _janela = janela;
        _abrirJanela = abrirJanela;
        _sair = sair;

        _icone = new IconeDeNotificacao(janela.Hwnd,
            IconesDaBandeja.Obter(CorDaBandeja.Cinza), $"{Nucleo.Marca.Nome} — parado");

        janela.AoEventoDaBandeja = AoEvento;
        janela.AoRenascerABarra = () => _icone.Adicionar();

        _gravador.AoMudar += Redesenhar;
        _gravador.AoAvisar += Avisar;
    }

    private void AoEvento(uint evento, int x, int y)
    {
        switch (evento)
        {
            // Clique muta e não para; parar só pelo menu.
            case Win32.NIN_SELECT or Win32.NIN_KEYSELECT:
                _gravador.AoClicar();
                break;
            case Win32.WM_CONTEXTMENU:
                using (var menu = MontarMenu()) menu.Mostrar(_janela.Hwnd, x, y);
                break;
        }
    }

    /// <summary>O ícone e o tooltip acompanhando o estado.</summary>
    public void Redesenhar()
    {
        string txt = Nucleo.Marca.Nome + " — "
            + _gravador.Estado.TextoDeStatus(_gravador.DuracaoAtual, null).Split('\n')[0];
        _icone.Atualizar(IconesDaBandeja.Obter(_gravador.Estado.Cor), txt);
    }

    /// <summary>
    /// Um aviso do app, não do gravador — a transcrição que acabou de terminar.
    /// </summary>
    /// <remarks>
    /// Respeita o mesmo interruptor de notificações do menu, e não um segundo
    /// só seu: quem desligou as notificações desligou os balões deste ícone, e
    /// inventar uma exceção para a transcrição seria contrariar o que a pessoa
    /// pediu na única frase que o menu oferece sobre o assunto.
    /// </remarks>
    public void AvisarDoApp(string texto) => Avisar(texto, Win32.NIIF_INFO, sempre: false);

    private void Avisar(string texto, uint tipo, bool sempre)
    {
        if (!sempre && !_gravador.Estado.NotificacoesLigadas) return;
        _icone.Balao(Nucleo.Marca.Nome, texto, tipo);
    }

    // ─────────────────────────────────────────────────────────── menu

    private MenuNativo MontarMenu()
    {
        var menu = new MenuNativo();
        var raiz = menu.Raiz;
        var estado = _gravador.Estado;

        string status = estado.TextoDeStatus(
            _gravador.DuracaoAtual, _gravador.Faixa("mic")?.NomeDispositivo);
        if (_gravador.Evento is { } ev) status += $"\n{Gravador.Cortar(ev.Titulo, 40)}";
        // Menu do Win32 não quebra linha: cada linha vira um item desabilitado.
        foreach (string linha in status.Split('\n'))
            raiz.Item(linha, habilitado: false);
        raiz.Separador();

        raiz.Item(
            estado.Gravando ? (estado.Mudo ? "Desmutar microfone" : "Mutar microfone")
                            : "Iniciar gravação",
            _gravador.AoClicar, negrito: true);   // a ação principal

        // Parar só pelo menu — nunca pelo clique no ícone.
        raiz.Item("Parar gravação", _gravador.Parar, habilitado: estado.Gravando);

        raiz.Separador();

        // O que a Fase 2.5 acrescentou ao menu: a janela. Fica acima dos
        // submenus de configuração porque é o que se usa todo dia.
        raiz.Item("Abrir a janela", _abrirJanela);

        raiz.Separador();

        var cat = _gravador.Dispositivos.Atual;
        SubmenuDeDispositivos(raiz, "Microfone", cat.Entradas, _gravador.Cfg.MicId,
                              id => _gravador.EscolherDispositivo("mic", id));
        SubmenuDeDispositivos(raiz, "Áudio do sistema", cat.Saidas, _gravador.Cfg.LoopbackId,
                              id => _gravador.EscolherDispositivo("loopback", id));

        SubmenuDaPasta(raiz);
        SubmenuDoCalendario(raiz);

        raiz.Separador();
        raiz.Item("Notificações", _gravador.AlternarNotificacoes,
                  marcado: estado.NotificacoesLigadas);

        raiz.Separador();
        raiz.Item("Sair", _sair);

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
    private void SubmenuDeDispositivos(
        MenuNativo.Secao menu, string titulo, IReadOnlyList<Dispositivo> lista,
        string? escolhidoId, Action<string?> escolher)
    {
        var raiz = menu.Submenu(titulo, habilitado: !_gravador.Estado.Gravando);

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

    private void SubmenuDaPasta(MenuNativo.Secao menu)
    {
        var raiz = menu.Submenu("Pasta das gravações");
        string atual = _gravador.PastaDeSaida;
        raiz.Item(Encurtar(atual), habilitado: false);
        raiz.Separador();
        raiz.Item("Abrir no Explorer", AbrirPasta);
        raiz.Item("Escolher outra pasta...", EscolherPasta,
                  habilitado: !_gravador.Estado.Gravando);
        if (_gravador.Cfg.OutputDir is not null)
            raiz.Item("Restaurar pasta padrão", () => _gravador.DefinirPastaDeSaida(null),
                      habilitado: !_gravador.Estado.Gravando);
    }

    /// <remarks>
    /// Sem <c>google_client_secret.json</c> o submenu vira uma instrução em vez
    /// de sumir: quem nunca configurou precisa descobrir o que falta, e um item
    /// invisível não ensina nada.
    /// </remarks>
    private void SubmenuDoCalendario(MenuNativo.Secao menu)
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

        raiz.Item("Usar esta agenda", () => _gravador.UsarAgenda(!_gravador.Cfg.UseCalendar),
                  marcado: _gravador.Cfg.UseCalendar);

        raiz.Item(autorizado ? "Trocar de conta..." : "Conectar conta...", _gravador.Autorizar);

        if (autorizado)
            raiz.Item("Desconectar", () =>
            {
                ClienteDaAgenda.Desconectar();
                Avisar("Conta do Google desconectada.", Win32.NIIF_INFO, sempre: true);
            });
    }

    /// <summary>Encurta caminho longo pelo meio, preservando início e fim.</summary>
    private static string Encurtar(string caminho, int max = 44) =>
        caminho.Length <= max ? caminho
            : caminho[..(max / 2 - 2)] + "..." + caminho[^(max / 2 - 1)..];

    private void AbrirPasta()
    {
        string alvo = _gravador.PastaAtual ?? _gravador.PastaDeSaida;
        Directory.CreateDirectory(alvo);
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{alvo}\"") { UseShellExecute = true });
    }

    private void EscolherPasta()
    {
        string? escolhida = SeletorDePasta.Escolher(_janela.Hwnd,
            _gravador.PastaDeSaida, "Onde salvar as gravações");
        if (escolhida is null) return;

        // Escrita de teste antes de aceitar: descobrir que a pasta é somente
        // leitura no meio de uma reunião seria tarde demais.
        if (!PodeEscrever(escolhida, out string? erro))
        {
            Avisar($"Não dá para escrever nessa pasta: {erro}", Win32.NIIF_ERROR, sempre: true);
            return;
        }

        _gravador.DefinirPastaDeSaida(escolhida);
    }

    /// <summary>Confere que dá para escrever, antes de aceitar a pasta.</summary>
    /// <remarks>
    /// Também usada pela tela do Gravador: a mesma pasta pode ser escolhida dos
    /// dois lados, e um dos dois aceitando o que o outro recusa seria pior que
    /// não conferir em nenhum.
    /// </remarks>
    public static bool PodeEscrever(string pasta, out string? erro)
    {
        try
        {
            Directory.CreateDirectory(pasta);
            string teste = Path.Combine(pasta, ".gravador-teste");
            File.WriteAllText(teste, "ok");
            File.Delete(teste);
            erro = null;
            return true;
        }
        catch (Exception e)
        {
            erro = e.Message;
            return false;
        }
    }

    public void Dispose()
    {
        _gravador.AoMudar -= Redesenhar;
        _gravador.AoAvisar -= Avisar;
        _icone.Dispose();
        IconesDaBandeja.Liberar();
    }
}
