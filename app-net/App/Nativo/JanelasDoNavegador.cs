using System.Runtime.InteropServices;

namespace MeetingApp.App.Nativo;

/// <summary>
/// As janelas do Chrome e do Edge abertas agora, com o título de cada uma.
/// </summary>
/// <remarks>
/// <b>Pela classe, e não pelo processo.</b> O Chrome, o Edge, o Brave e os apps
/// instalados (PWA) desenham toda janela de topo como
/// <c>Chrome_WidgetWin_1</c>, e assim nenhum nome de executável precisa ser
/// conhecido. Qual delas é a reunião é <c>JanelaDaReuniao.Escolher</c>.
/// </remarks>
internal static class JanelasDoNavegador
{
    private const string Classe = "Chrome_WidgetWin_1";

    internal sealed record Janela(IntPtr Hwnd, string Titulo);

    public static unsafe List<Janela> Listar()
    {
        var lista = new List<Janela>();
        var alca = GCHandle.Alloc(lista);
        try
        {
            Win32.EnumWindows(&AoAchar, GCHandle.ToIntPtr(alca));
        }
        finally
        {
            alca.Free();
        }
        return lista;
    }

    /// <summary>O título de agora — é ele que muda quando a pessoa troca de aba.</summary>
    public static unsafe string Titulo(IntPtr hwnd)
    {
        char* texto = stackalloc char[512];
        int n = Win32.GetWindowText(hwnd, texto, 512);
        return n > 0 ? new string(texto, 0, n) : "";
    }

    [UnmanagedCallersOnly]
    private static unsafe int AoAchar(IntPtr hwnd, IntPtr dados)
    {
        if (!Win32.IsWindowVisible(hwnd)) return 1;
        char* classe = stackalloc char[64];
        int n = Win32.GetClassName(hwnd, classe, 64);
        if (n != Classe.Length || !new ReadOnlySpan<char>(classe, n).SequenceEqual(Classe)) return 1;
        string titulo = Titulo(hwnd);
        if (titulo.Length == 0) return 1;   // as janelas auxiliares do Chrome não têm título
        ((List<Janela>)GCHandle.FromIntPtr(dados).Target!).Add(new Janela(hwnd, titulo));
        return 1;
    }
}
