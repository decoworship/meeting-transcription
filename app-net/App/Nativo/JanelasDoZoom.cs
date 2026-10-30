using System.Diagnostics;
using System.Runtime.InteropServices;
using MeetingRecorder.Core;

namespace MeetingApp.App.Nativo;

/// <summary>
/// As janelas de topo do Zoom desktop, com a classe e o estado de cada uma.
/// </summary>
/// <remarks>
/// <b>Pelo processo e pela classe.</b> O Zoom mantém dezenas de janelas
/// escondidas desde que abre — miniaturas, menus, a barra de compartilhar —, e
/// o título de várias é o mesmo "Zoom Workplace". A classe é o que separa a
/// reunião da janela inicial; qual gravar é <see cref="ReuniaoDoZoom.Decidir"/>.
/// </remarks>
internal static class JanelasDoZoom
{
    internal sealed record Janela(IntPtr Hwnd, JanelaDoZoom Dados);

    public static unsafe List<Janela> Listar()
    {
        var pids = new HashSet<uint>();
        foreach (var p in Process.GetProcessesByName(ReuniaoDoZoom.Processo))
        {
            pids.Add((uint)p.Id);
            p.Dispose();
        }
        var lista = new List<Janela>();
        if (pids.Count == 0) return lista;

        var todas = new List<IntPtr>();
        var alca = GCHandle.Alloc(todas);
        try
        {
            Win32.EnumWindows(&AoAchar, GCHandle.ToIntPtr(alca));
        }
        finally
        {
            alca.Free();
        }

        char* classe = stackalloc char[128];
        foreach (var hwnd in todas)
        {
            Win32.GetWindowThreadProcessId(hwnd, out uint pid);
            if (!pids.Contains(pid)) continue;
            int n = Win32.GetClassName(hwnd, classe, 128);
            if (n <= 0) continue;
            Win32.GetWindowRect(hwnd, out var r);
            lista.Add(new Janela(hwnd, new JanelaDoZoom(new string(classe, 0, n), Win32.IsWindowVisible(hwnd),
                                                        Win32.IsIconic(hwnd), r.Right - r.Left, r.Bottom - r.Top)));
        }
        return lista;
    }

    [UnmanagedCallersOnly]
    private static int AoAchar(IntPtr hwnd, IntPtr dados)
    {
        ((List<IntPtr>)GCHandle.FromIntPtr(dados).Target!).Add(hwnd);
        return 1;
    }
}
