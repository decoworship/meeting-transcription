using System.Runtime.InteropServices;
using MeetingApp.App.Nativo;

namespace MeetingApp.App.Bandeja;

/// <summary>
/// O menu de contexto via <c>CreatePopupMenu</c>/<c>TrackPopupMenuEx</c>.
/// Substitui o <c>ContextMenuStrip</c>: itens, separadores, submenus, marcado,
/// desabilitado e o item padrão (negrito).
/// </summary>
/// <remarks>
/// Construído do zero a cada abertura e descartado ao fechar, como o
/// <c>Remontar</c> fazia — nunca há estado velho para sincronizar. O menu do
/// Win32 não quebra linha (armadilha do handoff §3): texto de mais de uma linha
/// vira uma sequência de itens desabilitados, um por linha.
/// </remarks>
internal sealed class MenuNativo : IDisposable
{
    private readonly Dictionary<uint, Action> _acoes = [];
    private uint _proximoId = 1;

    public Secao Raiz { get; }

    public MenuNativo() => Raiz = new Secao(this, Win32.CreatePopupMenu());

    /// <summary>Mostra, espera a escolha e executa a ação escolhida.</summary>
    public void Mostrar(IntPtr hwnd, int x, int y)
    {
        // SetForegroundWindow antes, WM_NULL depois: sem esse par o menu não
        // fecha ao clicar fora (armadilha do handoff §3).
        Win32.SetForegroundWindow(hwnd);
        int id = Win32.TrackPopupMenuEx(Raiz.Handle,
            Win32.TPM_RIGHTBUTTON | Win32.TPM_RETURNCMD | Win32.TPM_NONOTIFY,
            x, y, hwnd, IntPtr.Zero);
        Win32.PostMessageW(hwnd, Win32.WM_NULL, IntPtr.Zero, IntPtr.Zero);

        if (id != 0 && _acoes.TryGetValue((uint)id, out var acao)) acao();
    }

    // DestroyMenu é recursivo: leva os submenus pendurados por MF_POPUP junto.
    public void Dispose() => Win32.DestroyMenu(Raiz.Handle);

    /// <summary>Um nível do menu: a raiz ou um submenu.</summary>
    public sealed class Secao
    {
        private readonly MenuNativo _dono;
        internal IntPtr Handle { get; }

        internal Secao(MenuNativo dono, IntPtr handle)
        {
            _dono = dono;
            Handle = handle;
        }

        /// <param name="negrito">Item padrão do menu — a ação principal.</param>
        public void Item(string texto, Action? acao = null, bool habilitado = true,
                         bool marcado = false, bool negrito = false)
        {
            uint flags = Win32.MF_STRING;
            if (!habilitado) flags |= Win32.MF_GRAYED;
            if (marcado) flags |= Win32.MF_CHECKED;

            uint id = 0;
            if (acao is not null)
            {
                id = _dono._proximoId++;
                _dono._acoes[id] = acao;
            }
            Win32.AppendMenu(Handle, flags, (UIntPtr)id, texto);

            if (negrito)
            {
                var info = new Win32.MENUITEMINFO
                {
                    cbSize = (uint)Marshal.SizeOf<Win32.MENUITEMINFO>(),
                    fMask = Win32.MIIM_STATE,
                    fState = Win32.MFS_DEFAULT,
                };
                Win32.SetMenuItemInfo(Handle, id, fByPosition: false, ref info);
            }
        }

        public void Separador() =>
            Win32.AppendMenu(Handle, Win32.MF_SEPARATOR, UIntPtr.Zero, null);

        public Secao Submenu(string texto, bool habilitado = true)
        {
            IntPtr sub = Win32.CreatePopupMenu();
            uint flags = Win32.MF_POPUP | (habilitado ? 0 : Win32.MF_GRAYED);
            Win32.AppendMenu(Handle, flags, (UIntPtr)(nuint)(nint)sub, texto);
            return new Secao(_dono, sub);
        }
    }
}
