using System.ComponentModel;
using System.Runtime.InteropServices;

namespace MeetingRecorder.Tray.Nativo;

/// <summary>
/// O ícone na bandeja via <c>Shell_NotifyIcon</c>. Substitui o <c>NotifyIcon</c>
/// do WinForms: ícone, tooltip e balão de notificação.
/// </summary>
/// <remarks>
/// Registrado como <c>NOTIFYICON_VERSION_4</c>, que entrega os eventos como
/// <c>NIN_SELECT</c>/<c>WM_CONTEXTMENU</c> com as coordenadas certas em
/// multi-monitor — o contrato tratado em <see cref="JanelaDeMensagens"/>.
/// </remarks>
internal sealed class IconeDeNotificacao : IDisposable
{
    private readonly IntPtr _hwnd;
    private IntPtr _icone;
    private string _tooltip;

    public IconeDeNotificacao(IntPtr hwnd, IntPtr icone, string tooltip)
    {
        _hwnd = hwnd;
        _icone = icone;
        _tooltip = tooltip;

        // Sem ícone não há bandeja: nem menu, nem clique, nem aviso. Um processo
        // vivo e invisível seria pior que uma falha — não daria nem para sair
        // dele sem o gerenciador de tarefas.
        if (!Adicionar())
            throw new Win32Exception(Marshal.GetLastWin32Error(),
                "não foi possível adicionar o ícone à bandeja");
    }

    /// <summary>Também chamada quando o Explorer reinicia (TaskbarCreated).</summary>
    public bool Adicionar()
    {
        var dados = Dados();
        if (!Win32.Shell_NotifyIconW(Win32.NIM_ADD, ref dados)) return false;

        dados.uVersion = Win32.NOTIFYICON_VERSION_4;
        return Win32.Shell_NotifyIconW(Win32.NIM_SETVERSION, ref dados);
    }

    public void Atualizar(IntPtr icone, string tooltip)
    {
        _icone = icone;
        // O szTip da V2 tem 128 chars com o terminador (o limite de 63 era da V1).
        _tooltip = tooltip.Length > 127 ? tooltip[..127] : tooltip;
        var dados = Dados();
        Win32.Shell_NotifyIconW(Win32.NIM_MODIFY, ref dados);
    }

    public void Balao(string titulo, string texto, uint niif)
    {
        var dados = Dados();
        dados.uFlags |= Win32.NIF_INFO;
        dados.szInfoTitle = titulo.Length > 63 ? titulo[..63] : titulo;
        dados.szInfo = texto.Length > 255 ? texto[..255] : texto;
        dados.dwInfoFlags = niif;
        Win32.Shell_NotifyIconW(Win32.NIM_MODIFY, ref dados);
    }

    private Win32.NOTIFYICONDATA Dados() => new()
    {
        cbSize = (uint)Marshal.SizeOf<Win32.NOTIFYICONDATA>(),
        hWnd = _hwnd,
        uID = 1,
        uFlags = Win32.NIF_MESSAGE | Win32.NIF_ICON | Win32.NIF_TIP | Win32.NIF_SHOWTIP,
        uCallbackMessage = Win32.WM_BANDEJA,
        hIcon = _icone,
        szTip = _tooltip,
        szInfo = "",
        szInfoTitle = "",
    };

    public void Dispose()
    {
        var dados = Dados();
        Win32.Shell_NotifyIconW(Win32.NIM_DELETE, ref dados);
    }
}
