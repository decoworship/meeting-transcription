using System.Runtime.InteropServices;

namespace MeetingRecorder.Tray.Nativo;

/// <summary>
/// As declarações do Win32 que a bandeja usa.
/// </summary>
/// <remarks>
/// Existe para o projeto não depender do WinForms. O <c>NotifyIcon</c> resolvia
/// tudo isto, mas <c>UseWindowsForms</c> arrasta o framework WindowsDesktop
/// inteiro e recusa trimming (<c>NETSDK1175</c>): o gravador que cabe em 12,6 MB
/// no CLI voltava para 155 MB só por causa da casca. Ver PLANO §5.
/// <para>
/// <b>Estado: escrito, ainda não ligado.</b> O <see cref="Programa"/> continua
/// no WinForms. Esta camada é o começo da troca descrita no PLANO §5 e no
/// handoff em <c>docs/FASE1-HANDOFF.md</c>.
/// </para>
/// </remarks>
internal static partial class Win32
{
    // ───────────────────────────────────────────────── janela

    internal const int WS_OVERLAPPED = 0x00000000;
    internal const uint WM_DESTROY = 0x0002;
    internal const uint WM_CLOSE = 0x0010;
    internal const uint WM_TIMER = 0x0113;
    internal const uint WM_NULL = 0x0000;
    internal const uint WM_COMMAND = 0x0111;

    /// <summary>Mensagem do ícone da bandeja. Faixa WM_APP, reservada ao app.</summary>
    internal const uint WM_BANDEJA = 0x0400 + 1;

    /// <summary>Pedido de execução na thread da UI, vindo de outra thread.</summary>
    internal const uint WM_EXECUTAR = 0x0400 + 2;

    internal const uint WM_LBUTTONUP = 0x0202;
    internal const uint WM_RBUTTONUP = 0x0205;
    internal const uint WM_CONTEXTMENU = 0x007B;

    [StructLayout(LayoutKind.Sequential)]
    internal struct POINT { public int X; public int Y; }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MSG
    {
        public IntPtr hwnd;
        public uint message;
        public IntPtr wParam;
        public IntPtr lParam;
        public uint time;
        public POINT pt;
    }

    internal delegate IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct WNDCLASSEX
    {
        public uint cbSize;
        public uint style;
        public IntPtr lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpszMenuName;
        [MarshalAs(UnmanagedType.LPWStr)] public string lpszClassName;
        public IntPtr hIconSm;
    }

    // DllImport e nao LibraryImport: a WNDCLASSEX tem campos string, que o
    // gerador de origem recusa marshalar (SYSLIB1051).
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern ushort RegisterClassExW(ref WNDCLASSEX lpwcx);

    [LibraryImport("user32.dll", EntryPoint = "CreateWindowExW", StringMarshalling = StringMarshalling.Utf16)]
    internal static partial IntPtr CreateWindowEx(
        uint dwExStyle, string lpClassName, string lpWindowName, uint dwStyle,
        int x, int y, int nWidth, int nHeight,
        IntPtr hWndParent, IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);

    [LibraryImport("user32.dll")]
    internal static partial IntPtr DefWindowProcW(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool DestroyWindow(IntPtr hWnd);

    [LibraryImport("user32.dll")]
    internal static partial int GetMessageW(out MSG lpMsg, IntPtr hWnd, uint min, uint max);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool TranslateMessage(ref MSG lpMsg);

    [LibraryImport("user32.dll")]
    internal static partial IntPtr DispatchMessageW(ref MSG lpMsg);

    [LibraryImport("user32.dll")]
    internal static partial void PostQuitMessage(int nExitCode);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool PostMessageW(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [LibraryImport("user32.dll", EntryPoint = "RegisterWindowMessageW",
                   StringMarshalling = StringMarshalling.Utf16)]
    internal static partial uint RegisterWindowMessage(string lpString);

    [LibraryImport("user32.dll", SetLastError = true)]
    internal static partial UIntPtr SetTimer(IntPtr hWnd, UIntPtr nIDEvent, uint uElapse, IntPtr lpTimerFunc);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool KillTimer(IntPtr hWnd, UIntPtr uIDEvent);

    [LibraryImport("kernel32.dll", EntryPoint = "GetModuleHandleW",
                   StringMarshalling = StringMarshalling.Utf16)]
    internal static partial IntPtr GetModuleHandle(string? lpModuleName);

    // ───────────────────────────────────────────────── bandeja

    internal const uint NIM_ADD = 0x00000000;
    internal const uint NIM_MODIFY = 0x00000001;
    internal const uint NIM_DELETE = 0x00000002;
    internal const uint NIM_SETVERSION = 0x00000004;

    internal const uint NIF_MESSAGE = 0x00000001;
    internal const uint NIF_ICON = 0x00000002;
    internal const uint NIF_TIP = 0x00000004;
    internal const uint NIF_INFO = 0x00000010;

    internal const uint NIIF_NONE = 0x00000000;
    internal const uint NIIF_INFO = 0x00000001;
    internal const uint NIIF_WARNING = 0x00000002;
    internal const uint NIIF_ERROR = 0x00000003;

    /// <summary>Versão 4: o clique chega com as coordenadas certas em multi-monitor.</summary>
    internal const uint NOTIFYICON_VERSION_4 = 4;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct NOTIFYICONDATA
    {
        public uint cbSize;
        public IntPtr hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szTip;
        public uint dwState;
        public uint dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string szInfo;
        public uint uVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string szInfoTitle;
        public uint dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool Shell_NotifyIconW(uint dwMessage, ref NOTIFYICONDATA lpData);

    // ───────────────────────────────────────────────── menu

    internal const uint MF_STRING = 0x00000000;
    internal const uint MF_POPUP = 0x00000010;
    internal const uint MF_SEPARATOR = 0x00000800;
    internal const uint MF_GRAYED = 0x00000001;
    internal const uint MF_CHECKED = 0x00000008;

    internal const uint TPM_RIGHTBUTTON = 0x0002;
    internal const uint TPM_RETURNCMD = 0x0100;
    internal const uint TPM_NONOTIFY = 0x0080;

    internal const uint MIIM_STATE = 0x00000001;
    internal const uint MIIM_ID = 0x00000002;
    internal const uint MIIM_FTYPE = 0x00000100;
    internal const uint MFS_DEFAULT = 0x00001000;

    [StructLayout(LayoutKind.Sequential)]
    internal struct MENUITEMINFO
    {
        public uint cbSize;
        public uint fMask;
        public uint fType;
        public uint fState;
        public uint wID;
        public IntPtr hSubMenu;
        public IntPtr hbmpChecked;
        public IntPtr hbmpUnchecked;
        public IntPtr dwItemData;
        public IntPtr dwTypeData;
        public uint cch;
        public IntPtr hbmpItem;
    }

    [LibraryImport("user32.dll", SetLastError = true)]
    internal static partial IntPtr CreatePopupMenu();

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool DestroyMenu(IntPtr hMenu);

    [LibraryImport("user32.dll", EntryPoint = "AppendMenuW", StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool AppendMenu(IntPtr hMenu, uint uFlags, UIntPtr uIDNewItem, string? lpNewItem);

    [LibraryImport("user32.dll", EntryPoint = "SetMenuItemInfoW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool SetMenuItemInfo(IntPtr hMenu, uint item,
        [MarshalAs(UnmanagedType.Bool)] bool fByPosition, ref MENUITEMINFO lpmii);

    [LibraryImport("user32.dll")]
    internal static partial int TrackPopupMenuEx(IntPtr hMenu, uint uFlags, int x, int y,
                                                 IntPtr hwnd, IntPtr lptpm);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool SetForegroundWindow(IntPtr hWnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetCursorPos(out POINT lpPoint);

    // ───────────────────────────────────────────────── ícones

    internal const int SM_CXSMICON = 49;

    [LibraryImport("user32.dll")]
    internal static partial int GetSystemMetrics(int nIndex);

    [LibraryImport("user32.dll", SetLastError = true)]
    internal static partial IntPtr CreateIconFromResourceEx(
        IntPtr presbits, uint dwResSize, [MarshalAs(UnmanagedType.Bool)] bool fIcon,
        uint dwVer, int cxDesired, int cyDesired, uint Flags);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool DestroyIcon(IntPtr hIcon);

    // ───────────────────────────────────────────────── diálogo

    internal const uint MB_OK = 0x00000000;
    internal const uint MB_ICONINFORMATION = 0x00000040;

    [LibraryImport("user32.dll", EntryPoint = "MessageBoxW", StringMarshalling = StringMarshalling.Utf16)]
    internal static partial int MessageBox(IntPtr hWnd, string text, string caption, uint type);
}
