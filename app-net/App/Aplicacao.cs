using MeetingApp.App.Bandeja;
using MeetingApp.App.Nativo;
using MeetingApp.Nucleo;

namespace MeetingApp.App;

/// <summary>
/// O app inteiro: a bandeja que grava e a janela que transcreve, num processo.
/// </summary>
/// <remarks>
/// <para>
/// <b>O ciclo de vida é o desta classe, não o da janela.</b> Até a Fase 2 o
/// <c>MeetingApp</c> era um app de janela — fechar a janela encerrava o
/// processo. Aqui ele é um app de bandeja que <em>também</em> tem janela:
/// fechar esconde, e sair de verdade só acontece pelo menu da bandeja. Errar
/// isso perde gravação (FASE2.5.md §2).
/// </para>
/// <para>
/// <b>Dois HWND, um laço.</b> A janela invisível da bandeja e a janela do
/// WebView2 são duas janelas Win32 na mesma thread, cada uma com seu
/// <c>WndProc</c>; o <c>GetMessage</c> da primeira despacha as duas. Não há
/// segunda thread de UI, e é por isso que o estado do gravador pode ser lido
/// dos dois lados sem trava.
/// </para>
/// </remarks>
internal sealed class Aplicacao : IDisposable
{
    private readonly JanelaDeMensagens _ancora;
    private readonly Gravador _gravador;
    private readonly Bandeja.Bandeja _bandeja;
    private readonly string _pastaDasGravacoes;
    private readonly string? _telaInicial;

    private JanelaDoApp? _janela;
    private bool _saindo;

    public Aplicacao(string? pastaDoArgumento, string? telaInicial)
    {
        // Antes de qualquer janela: depois da primeira, o Windows ignora.
        Win32.SetProcessDpiAwarenessContext(Win32.DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);

        _telaInicial = telaInicial;

        _ancora = new JanelaDeMensagens
        {
            AoTick = Tick,
            AoTickRapido = EmpurrarNiveis,
            AoPedirJanela = MostrarJanela,
        };

        _gravador = new Gravador(_ancora.Executar) { PastaForcada = pastaDoArgumento };
        _gravador.Estado.NotificacoesLigadas = _gravador.Cfg.Notifications;
        _gravador.AoMudar += EmpurrarEstado;

        _pastaDasGravacoes = PastaDasGravacoes.Resolver(_gravador.Cfg, pastaDoArgumento);

        _bandeja = new Bandeja.Bandeja(_gravador, _ancora, MostrarJanela, Sair);
    }

    /// <param name="comJanela">
    /// Falso quando o app subiu com <c>--bandeja</c> — o modo de iniciar com o
    /// Windows. Ali abrir uma janela seria abrir uma janela na cara de quem
    /// acabou de ligar o computador.
    /// </param>
    public void Rodar(bool comJanela)
    {
        if (comJanela) MostrarJanela();
        _ancora.Rodar();
    }

    // ───────────────────────────────────────────────────────── janela

    /// <remarks>
    /// A janela é criada na primeira vez que alguém a pede, e daí em diante é
    /// escondida e mostrada. Criar de saída custaria o WebView2 inteiro na
    /// memória de quem só quer gravar; recriar a cada abertura custaria os
    /// segundos de subida do WebView2 toda vez, e perderia a tela onde se estava.
    /// </remarks>
    private void MostrarJanela()
    {
        if (_saindo) return;

        if (_janela is null)
        {
            _janela = new JanelaDoApp("Reuniões", _gravador, _pastaDasGravacoes, _telaInicial)
            {
                AoEsconder = () => _ancora.MedidoresLigados(false),
            };
        }
        else
        {
            _janela.Mostrar();
        }

        _ancora.MedidoresLigados(_gravador.Estado.Gravando);
    }

    // ───────────────────────────────────────────────── relógio e eventos

    /// <summary>Uma vez por segundo, esteja a janela aberta ou não.</summary>
    private void Tick()
    {
        _gravador.Atualizar();

        // Os medidores só correm com a janela à vista e algo para medir. Ligar e
        // desligar aqui, e não em cada ação, cobre também o caso de a gravação
        // ter começado pela bandeja com a janela já aberta.
        _ancora.MedidoresLigados(_gravador.Estado.Gravando && _janela is { Visivel: true });
    }

    /// <summary>O estado do gravador indo para a página, sem ela pedir.</summary>
    /// <remarks>
    /// É a única mudança estrutural no contrato da ponte desde que ele existe:
    /// toda resposta era reação a um pedido, e o nível de áudio precisa fluir
    /// sem ninguém perguntar (FASE2.5.md §5).
    /// </remarks>
    private void EmpurrarEstado()
    {
        _bandeja.Redesenhar();
        if (_janela is { Pronta: true, Visivel: true } j)
            j.Enviar(Ponte.EventoDoGravador(_gravador));
    }

    private void EmpurrarNiveis()
    {
        if (_janela is { Pronta: true, Visivel: true } j)
            j.Enviar(Ponte.EventoDoGravador(_gravador));
    }

    // ─────────────────────────────────────────────────────────── saída

    /// <remarks>
    /// Confirma quando há gravação em andamento. É a única confirmação do app
    /// inteiro, e existe porque este é o único clique que perde uma reunião: o
    /// "Sair" fica logo abaixo de "Notificações" no menu, e o áudio não se refaz.
    /// </remarks>
    private void Sair()
    {
        if (_gravador.Estado.Gravando)
        {
            const uint MB_YESNO = 0x00000004;
            const int IDYES = 6;
            int r = Win32.MessageBox(_ancora.Hwnd,
                "Uma gravação está em andamento.\n\n"
                + "Sair agora encerra a gravação e salva o que já foi gravado. Sair?",
                "Reuniões", MB_YESNO | Win32.MB_ICONINFORMATION);
            if (r != IDYES) return;
        }

        _saindo = true;
        // Fecha o laço destruindo a âncora: é o WM_DESTROY dela que posta o
        // WM_QUIT. A limpeza de verdade acontece no Dispose, depois do laço.
        Win32.PostMessageW(_ancora.Hwnd, Win32.WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
    }

    public void Dispose()
    {
        // Ordem: a gravação primeiro, porque é ela que tem um meta.json para
        // fechar e um WAV para finalizar. O resto são handles.
        _gravador.AoMudar -= EmpurrarEstado;
        _gravador.Dispose();
        _janela?.Dispose();
        _bandeja.Dispose();
        _ancora.Dispose();
    }
}
