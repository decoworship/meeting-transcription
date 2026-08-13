using System.Collections.Concurrent;
using System.ComponentModel;
using System.Runtime.InteropServices;
using MeetingApp.App.Nativo;

namespace MeetingApp.App.Bandeja;

/// <summary>
/// A janela invisível que ancora a bandeja: WndProc, laço de mensagens, o timer
/// de 1 s e a fila de ações vindas de outras threads.
/// </summary>
/// <remarks>
/// <para>
/// <b>Janela escondida, não message-only.</b> Uma janela <c>HWND_MESSAGE</c> não
/// recebe broadcast, e é por broadcast que chega o <c>TaskbarCreated</c> — o
/// aviso de que o Explorer reiniciou e o ícone precisa ser readicionado. Sem
/// isso, um crash do Explorer faria a bandeja sumir para sempre.
/// </para>
/// <para>
/// Substitui o <c>Application.Run</c>, o <c>Forms.Timer</c> e o
/// <c>SynchronizationContext</c> do WinForms. Ver docs/FASE1-HANDOFF.md §3.
/// </para>
/// <para>
/// <b>Desde a Fase 2.5 este é o laço do processo inteiro.</b> A janela do app
/// (<see cref="JanelaDoApp"/>) é outro <c>HWND</c> na mesma thread, e não tem
/// laço próprio: o <c>GetMessage</c> daqui despacha as mensagens das duas. É por
/// isso que a janela pode ir e vir sem o processo morrer — quem encerra é a
/// destruição <b>desta</b> janela, e ela só acontece pelo "Sair" do menu.
/// </para>
/// </remarks>
internal sealed class JanelaDeMensagens : IDisposable
{
    /// <summary>
    /// O nome é procurado por <c>FindWindow</c> quando uma segunda instância
    /// sobe: é assim que ela pede a janela e sai. Mudar aqui e não lá faz a
    /// segunda instância abrir um segundo ícone na bandeja.
    /// </summary>
    public const string NomeDaClasse = "MeetingApp.JanelaDaBandeja";

    private static readonly UIntPtr IdDoTimer = 1;
    private static readonly UIntPtr IdDoTimerRapido = 2;

    // Campo, não variável local: se o GC coletar o delegate do WndProc, o
    // processo morre na próxima callback do Windows.
    private readonly Win32.WndProc _wndProc;
    private readonly ConcurrentQueue<Action> _acoes = new();
    private readonly uint _msgTaskbarCreated;
    private readonly uint _msgMostrarJanela;

    public IntPtr Hwnd { get; }

    /// <summary>A cada segundo — duração, cor e lembrete não pedem mais que isso.</summary>
    public Action? AoTick { get; set; }

    /// <summary>
    /// A cada 200 ms, e só enquanto a janela está aberta: os medidores de nível.
    /// </summary>
    /// <remarks>
    /// Timer separado do de um segundo porque o custo é outro. Repintar o ícone
    /// da bandeja cinco vezes por segundo seria cinco <c>Shell_NotifyIcon</c>
    /// por segundo a troco de nada — o tooltip mostra hh:mm:ss. Já o medidor
    /// existe para denunciar microfone mudo <em>no primeiro minuto</em>, e uma
    /// barra que anda de segundo em segundo não parece um medidor de nível.
    /// </remarks>
    public Action? AoTickRapido { get; set; }

    /// <summary>Evento do ícone (V4): código NIN_*/WM_* e as coordenadas do cursor.</summary>
    public Action<uint, int, int>? AoEventoDaBandeja { get; set; }

    /// <summary>O Explorer reiniciou; o ícone precisa ser readicionado.</summary>
    public Action? AoRenascerABarra { get; set; }

    /// <summary>Uma segunda instância pediu a janela, ou a bandeja pediu.</summary>
    public Action? AoPedirJanela { get; set; }

    public JanelaDeMensagens()
    {
        _wndProc = Processar;
        _msgTaskbarCreated = Win32.RegisterWindowMessage("TaskbarCreated");
        _msgMostrarJanela = Win32.RegisterWindowMessage(Win32.MSG_MOSTRAR_JANELA);

        var classe = new Win32.WNDCLASSEX
        {
            cbSize = (uint)Marshal.SizeOf<Win32.WNDCLASSEX>(),
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProc),
            hInstance = Win32.GetModuleHandle(null),
            lpszClassName = NomeDaClasse,
        };
        if (Win32.RegisterClassExW(ref classe) == 0)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "RegisterClassEx falhou");

        Hwnd = Win32.CreateWindowEx(0, NomeDaClasse, "Reuniões", Win32.WS_OVERLAPPED,
            0, 0, 0, 0, IntPtr.Zero, IntPtr.Zero, classe.hInstance, IntPtr.Zero);
        if (Hwnd == IntPtr.Zero)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "CreateWindowEx falhou");

        Win32.SetTimer(Hwnd, IdDoTimer, 1000, IntPtr.Zero);
    }

    /// <summary>Liga e desliga o tick de 200 ms. Ver <see cref="AoTickRapido"/>.</summary>
    public void MedidoresLigados(bool ligado)
    {
        if (ligado == _rapidoLigado) return;
        _rapidoLigado = ligado;
        if (ligado) Win32.SetTimer(Hwnd, IdDoTimerRapido, 200, IntPtr.Zero);
        else Win32.KillTimer(Hwnd, IdDoTimerRapido);
    }

    private bool _rapidoLigado;

    /// <summary>Executa na thread da UI, venha de onde vier.</summary>
    public void Executar(Action acao)
    {
        _acoes.Enqueue(acao);
        Win32.PostMessageW(Hwnd, Win32.WM_EXECUTAR, IntPtr.Zero, IntPtr.Zero);
    }

    /// <summary>Bloqueia até o WM_QUIT. Substitui o <c>Application.Run</c>.</summary>
    public void Rodar()
    {
        while (Win32.GetMessageW(out var msg, IntPtr.Zero, 0, 0) > 0)
        {
            Win32.TranslateMessage(ref msg);
            Win32.DispatchMessageW(ref msg);
        }
    }

    private IntPtr Processar(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == _msgTaskbarCreated)
        {
            AoRenascerABarra?.Invoke();
            return IntPtr.Zero;
        }

        if (msg == _msgMostrarJanela)
        {
            AoPedirJanela?.Invoke();
            return IntPtr.Zero;
        }

        switch (msg)
        {
            case Win32.WM_TIMER:
                if (wParam == (IntPtr)IdDoTimerRapido) AoTickRapido?.Invoke();
                else AoTick?.Invoke();
                return IntPtr.Zero;

            case Win32.WM_EXECUTAR:
                while (_acoes.TryDequeue(out var acao)) acao();
                return IntPtr.Zero;

            case Win32.WM_BANDEJA:
                // Contrato da NOTIFYICON_VERSION_4: o evento vem em
                // LOWORD(lParam) e as coordenadas empacotadas no wParam — não
                // como mensagens cruas de mouse (armadilha do handoff §3).
                AoEventoDaBandeja?.Invoke(
                    (uint)(lParam.ToInt64() & 0xFFFF),
                    unchecked((short)(wParam.ToInt64() & 0xFFFF)),
                    unchecked((short)((wParam.ToInt64() >> 16) & 0xFFFF)));
                return IntPtr.Zero;

            case Win32.WM_DESTROY:
                Win32.PostQuitMessage(0);
                return IntPtr.Zero;
        }
        return Win32.DefWindowProcW(hwnd, msg, wParam, lParam);
    }

    public void Dispose()
    {
        Win32.KillTimer(Hwnd, IdDoTimer);
        Win32.KillTimer(Hwnd, IdDoTimerRapido);
        Win32.DestroyWindow(Hwnd);
    }
}
