using System.Collections.Concurrent;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace MeetingRecorder.Tray.Nativo;

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
/// </remarks>
internal sealed class JanelaDeMensagens : IDisposable
{
    private const string NomeDaClasse = "MeetingRecorder.Janela";
    private static readonly UIntPtr IdDoTimer = 1;

    // Campo, não variável local: se o GC coletar o delegate do WndProc, o
    // processo morre na próxima callback do Windows.
    private readonly Win32.WndProc _wndProc;
    private readonly ConcurrentQueue<Action> _acoes = new();
    private readonly uint _msgTaskbarCreated;

    public IntPtr Hwnd { get; }

    /// <summary>A cada segundo — duração, cor e lembrete não pedem mais que isso.</summary>
    public Action? AoTick { get; set; }

    /// <summary>Evento do ícone (V4): código NIN_*/WM_* e as coordenadas do cursor.</summary>
    public Action<uint, int, int>? AoEventoDaBandeja { get; set; }

    /// <summary>O Explorer reiniciou; o ícone precisa ser readicionado.</summary>
    public Action? AoRenascerABarra { get; set; }

    public JanelaDeMensagens()
    {
        _wndProc = Processar;
        _msgTaskbarCreated = Win32.RegisterWindowMessage("TaskbarCreated");

        var classe = new Win32.WNDCLASSEX
        {
            cbSize = (uint)Marshal.SizeOf<Win32.WNDCLASSEX>(),
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProc),
            hInstance = Win32.GetModuleHandle(null),
            lpszClassName = NomeDaClasse,
        };
        if (Win32.RegisterClassExW(ref classe) == 0)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "RegisterClassEx falhou");

        Hwnd = Win32.CreateWindowEx(0, NomeDaClasse, "Gravador", Win32.WS_OVERLAPPED,
            0, 0, 0, 0, IntPtr.Zero, IntPtr.Zero, classe.hInstance, IntPtr.Zero);
        if (Hwnd == IntPtr.Zero)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "CreateWindowEx falhou");

        Win32.SetTimer(Hwnd, IdDoTimer, 1000, IntPtr.Zero);
    }

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

        switch (msg)
        {
            case Win32.WM_TIMER:
                AoTick?.Invoke();
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
        Win32.DestroyWindow(Hwnd);
    }
}
