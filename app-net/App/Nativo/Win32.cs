using System.Runtime.InteropServices;

namespace MeetingApp.App.Nativo;

/// <summary>
/// O Win32 que a janela do app usa.
/// </summary>
/// <remarks>
/// Mesma abordagem da bandeja (<c>recorder-net/Tray/Nativo/Win32.cs</c>) e pelo
/// mesmo motivo medido: <c>UseWindowsForms</c> ou WPF arrastariam o framework
/// WindowsDesktop, que recusa trimming e custou 140 MB lá. A janela aqui é um
/// retângulo que hospeda o WebView2 e repassa o redimensionamento.
/// </remarks>
internal static partial class Win32
{
    internal const uint WM_DESTROY = 0x0002;
    internal const uint WM_SIZE = 0x0005;
    internal const uint WM_GETMINMAXINFO = 0x0024;

    internal const uint WS_OVERLAPPEDWINDOW = 0x00CF0000;
    internal const int SW_SHOW = 5;
    internal const int CW_USEDEFAULT = unchecked((int)0x80000000);

    internal const uint MB_OK = 0x00000000;
    internal const uint MB_ICONERROR = 0x00000010;

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

    [StructLayout(LayoutKind.Sequential)]
    internal struct RECT { public int Left, Top, Right, Bottom; }

    /// <summary>Limites de tamanho da janela — usado no WM_GETMINMAXINFO.</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct MINMAXINFO
    {
        public POINT ptReserved, ptMaxSize, ptMaxPosition, ptMinTrackSize, ptMaxTrackSize;
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

    // DllImport e não LibraryImport: a WNDCLASSEX tem campos string, que o
    // gerador de origem recusa marshalar (SYSLIB1051).
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern ushort RegisterClassExW(ref WNDCLASSEX lpwcx);

    [LibraryImport("user32.dll", EntryPoint = "CreateWindowExW",
                   StringMarshalling = StringMarshalling.Utf16)]
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
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [LibraryImport("user32.dll")]
    internal static partial int GetMessageW(out MSG lpMsg, IntPtr hWnd, uint min, uint max);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool TranslateMessage(ref MSG lpMsg);

    [LibraryImport("user32.dll")]
    internal static partial IntPtr DispatchMessageW(ref MSG lpMsg);

    [LibraryImport("user32.dll")]
    internal static partial void PostQuitMessage(int nExitCode);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetClientRect(IntPtr hWnd, out RECT lpRect);

    [LibraryImport("user32.dll")]
    internal static partial IntPtr LoadCursorW(IntPtr hInstance, IntPtr lpCursorName);

    /// <summary>Cursor padrão de seta. Sem isto a janela herda a ampulheta.</summary>
    internal static readonly IntPtr IDC_ARROW = 32512;

    [LibraryImport("kernel32.dll", EntryPoint = "GetModuleHandleW",
                   StringMarshalling = StringMarshalling.Utf16)]
    internal static partial IntPtr GetModuleHandle(string? lpModuleName);

    [LibraryImport("user32.dll", EntryPoint = "MessageBoxW",
                   StringMarshalling = StringMarshalling.Utf16)]
    internal static partial int MessageBox(IntPtr hWnd, string text, string caption, uint type);

    /// <summary>DPI por monitor, para a janela não sair borrada em tela 4K.</summary>
    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool SetProcessDpiAwarenessContext(IntPtr value);

    internal static readonly IntPtr DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2 = -4;
}
