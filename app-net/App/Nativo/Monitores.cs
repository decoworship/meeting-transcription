using System.Runtime.InteropServices;

namespace MeetingApp.App.Nativo;

/// <summary>
/// Os monitores ligados, para escolher qual gravar em vídeo.
/// </summary>
/// <remarks>
/// O id é o nome do dispositivo do Windows (<c>\\.\DISPLAY1</c>): é o que
/// sobrevive a reiniciar, e não o <c>HMONITOR</c>, que muda a cada sessão. Os
/// retângulos são em pixels físicos — o processo é DPI-aware por monitor
/// (<c>Aplicacao.cs</c>), e é neles que o <c>BitBlt</c> copia.
/// </remarks>
internal static class Monitores
{
    internal sealed record Monitor(string Id, Win32.RECT Area, bool Principal)
    {
        public int Largura => Area.Right - Area.Left;
        public int Altura => Area.Bottom - Area.Top;
    }

    public static unsafe List<Monitor> Listar()
    {
        var lista = new List<Monitor>();
        var alca = GCHandle.Alloc(lista);
        try
        {
            Win32.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, &AoAchar, GCHandle.ToIntPtr(alca));
        }
        finally
        {
            alca.Free();
        }
        // O principal primeiro, e os outros da esquerda para a direita: é a
        // ordem em que a pessoa os vê na mesa.
        return [.. lista.OrderByDescending(m => m.Principal).ThenBy(m => m.Area.Left)];
    }

    /// <summary>O monitor com este id, ou nulo se ele foi desligado.</summary>
    public static Monitor? Achar(string? id) =>
        id is { Length: > 0 } ? Listar().FirstOrDefault(m => m.Id == id) : null;

    /// <summary>"Tela 1 (principal) · 1920×1080", na ordem de <see cref="Listar"/>.</summary>
    public static string Nome(Monitor m, int posicao) =>
        $"Tela {posicao + 1}{(m.Principal ? " (principal)" : "")} · {m.Largura}×{m.Altura}";

    [UnmanagedCallersOnly]
    private static unsafe int AoAchar(IntPtr monitor, IntPtr dc, Win32.RECT* area, IntPtr dados)
    {
        var info = new Win32.MONITORINFOEX { cbSize = (uint)sizeof(Win32.MONITORINFOEX) };
        if (Win32.GetMonitorInfoEx(monitor, ref info))
        {
            var lista = (List<Monitor>)GCHandle.FromIntPtr(dados).Target!;
            string id = new string(info.szDevice);
            lista.Add(new Monitor(id, info.rcMonitor, (info.dwFlags & Win32.MONITORINFOF_PRIMARY) != 0));
        }
        return 1;   // continua enumerando
    }
}
