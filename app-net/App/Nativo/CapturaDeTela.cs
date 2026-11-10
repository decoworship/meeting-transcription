using System.ComponentModel;
using System.Runtime.InteropServices;

namespace MeetingApp.App.Nativo;

/// <summary>
/// O print de um monitor, por GDI.
/// </summary>
/// <remarks>
/// <para>
/// <b><c>BitBlt</c>, e não Windows.Graphics.Capture.</b> A API moderna é WinRT
/// — o TFM teria de virar <c>net8.0-windows10.0.19041.0</c>, arrastando a
/// projeção do SDK e o problema dela com o trimming — e desenha uma borda
/// amarela em volta do que captura. Para um quadro só, de um monitor inteiro, o
/// GDI faz o mesmo em vinte linhas: com o DWM ligado, a tela do desktop é o que
/// o compositor montou, aceleração de GPU incluída.
/// </para>
/// <para>
/// O que nenhum dos dois captura é janela protegida: a reunião do Teams com
/// "impedir captura de tela" sai preta, e é de propósito.
/// </para>
/// </remarks>
internal static class CapturaDeTela
{
    /// <summary>Pixels BGRA de cima para baixo, sem enchimento (stride = largura × 4).</summary>
    internal sealed record Quadro(int Largura, int Altura, byte[] Bgra);

    /// <summary>
    /// O monitor da janela que está na frente — a da reunião, quando o atalho é
    /// apertado com o Teams aberto.
    /// </summary>
    /// <param name="nossaJanela">
    /// A janela do app. Quando é ela que está na frente — o botão "Capturar
    /// tela" foi clicado nela —, o print é do monitor dela, <b>sem ela</b>: o que
    /// se quer é a reunião que está atrás, não o PulseMeet.
    /// </param>
    public static Quadro DoMonitorEmFoco(IntPtr nossaJanela)
    {
        IntPtr frente = Win32.GetForegroundWindow();
        bool somosNos = nossaJanela != IntPtr.Zero && frente != IntPtr.Zero
                        && Win32.GetAncestor(frente, Win32.GA_ROOT) == nossaJanela;

        IntPtr monitor = Win32.MonitorFromWindow(frente, Win32.MONITOR_DEFAULTTOPRIMARY);
        var info = new Win32.MONITORINFO { cbSize = (uint)Marshal.SizeOf<Win32.MONITORINFO>() };
        if (!Win32.GetMonitorInfo(monitor, ref info))
            throw new InvalidOperationException("não foi possível saber qual é o monitor");

        // Esconder só pelo instante do print. Deixar ligado esconderia o app
        // também do compartilhamento de tela do Teams — e quem compartilha a
        // tela para mostrar a ata não a veria do outro lado.
        bool escondeu = somosNos
                        && Win32.SetWindowDisplayAffinity(nossaJanela, Win32.WDA_EXCLUDEFROMCAPTURE);
        try
        {
            if (escondeu)
            {
                // Duas composições: a primeira pode já estar montada com a
                // janela dentro quando a afinidade muda.
                Win32.DwmFlush();
                Win32.DwmFlush();
            }
            return Copiar(info.rcMonitor);
        }
        finally
        {
            if (escondeu) Win32.SetWindowDisplayAffinity(nossaJanela, Win32.WDA_NONE);
        }
    }

    /// <summary>
    /// A janela da reunião que a gravação de tela está mostrando, do tamanho
    /// dela — o mesmo recorte do vídeo, sem as faixas pretas do encaixe.
    /// </summary>
    /// <returns>Nulo quando ela sumiu, minimizou ou não deixou copiar: aí vale o monitor.</returns>
    public static Quadro? DaJanela(Bandeja.AlvoDoPrint alvo, IntPtr nossaJanela)
    {
        IntPtr hwnd = alvo.Hwnd;
        if (hwnd == IntPtr.Zero || !Win32.IsWindow(hwnd) || Win32.IsIconic(hwnd)) return null;
        if (!Win32.GetWindowRect(hwnd, out var r) || r.Right <= r.Left || r.Bottom <= r.Top) return null;
        // O que se vê, sem a borda invisível de redimensionar.
        if (Win32.DwmGetWindowAttribute(hwnd, Win32.DWMWA_EXTENDED_FRAME_BOUNDS, out var v,
                                        Marshal.SizeOf<Win32.RECT>()) != 0) v = r;
        try
        {
            if (alvo.DaTela)
            {
                // O compartilhamento do Zoom: o que está por baixo da camada
                // transparente. Da tela, então o PulseMeet sai dela antes.
                bool escondeu = nossaJanela != IntPtr.Zero
                                && Win32.SetWindowDisplayAffinity(nossaJanela, Win32.WDA_EXCLUDEFROMCAPTURE);
                try
                {
                    if (escondeu) { Win32.DwmFlush(); Win32.DwmFlush(); }
                    return Copiar(v);
                }
                finally
                {
                    if (escondeu) Win32.SetWindowDisplayAffinity(nossaJanela, Win32.WDA_NONE);
                }
            }
            return PelaJanela(hwnd, r, v);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// <c>PrintWindow</c> com <c>PW_RENDERFULLCONTENT</c>, como o vídeo
    /// (<c>CopiaDeJanela</c>): outra janela por cima — o próprio PulseMeet —
    /// não entra.
    /// </summary>
    private static Quadro? PelaJanela(IntPtr hwnd, Win32.RECT r, Win32.RECT v)
    {
        int jl = r.Right - r.Left, ja = r.Bottom - r.Top;
        int ox = Math.Clamp(v.Left - r.Left, 0, jl - 1), oy = Math.Clamp(v.Top - r.Top, 0, ja - 1);
        int vl = Math.Min(v.Right - v.Left, jl - ox), va = Math.Min(v.Bottom - v.Top, ja - oy);
        if (vl <= 0 || va <= 0) return null;

        IntPtr tela = Win32.GetDC(IntPtr.Zero);
        if (tela == IntPtr.Zero) return null;
        IntPtr memoria = IntPtr.Zero, dib = IntPtr.Zero, anterior = IntPtr.Zero;
        try
        {
            memoria = Win32.CreateCompatibleDC(tela);
            var cabecalho = new Win32.BITMAPINFOHEADER
            {
                biSize = (uint)Marshal.SizeOf<Win32.BITMAPINFOHEADER>(),
                biWidth = jl,
                biHeight = -ja,
                biPlanes = 1,
                biBitCount = 32,
            };
            dib = Win32.CreateDIBSection(tela, ref cabecalho, 0, out IntPtr bits, IntPtr.Zero, 0);
            if (dib == IntPtr.Zero || bits == IntPtr.Zero) return null;
            anterior = Win32.SelectObject(memoria, dib);
            if (!Win32.PrintWindow(hwnd, memoria, Win32.PW_RENDERFULLCONTENT)) return null;
            Win32.GdiFlush();

            // Só o retângulo visível, linha a linha.
            byte[] bgra = new byte[vl * va * 4];
            for (int y = 0; y < va; y++)
                Marshal.Copy(bits + ((oy + y) * jl + ox) * 4, bgra, y * vl * 4, vl * 4);
            return new Quadro(vl, va, bgra);
        }
        finally
        {
            if (anterior != IntPtr.Zero) Win32.SelectObject(memoria, anterior);
            if (dib != IntPtr.Zero) Win32.DeleteObject(dib);
            if (memoria != IntPtr.Zero) Win32.DeleteDC(memoria);
            Win32.ReleaseDC(IntPtr.Zero, tela);
        }
    }

    private static Quadro Copiar(Win32.RECT r)
    {
        int largura = r.Right - r.Left, altura = r.Bottom - r.Top;
        if (largura <= 0 || altura <= 0) throw new InvalidOperationException("monitor sem tamanho");

        IntPtr tela = Win32.GetDC(IntPtr.Zero);
        if (tela == IntPtr.Zero) throw new InvalidOperationException("sem acesso à tela");
        IntPtr memoria = IntPtr.Zero, dib = IntPtr.Zero, anterior = IntPtr.Zero;
        try
        {
            memoria = Win32.CreateCompatibleDC(tela);
            var cabecalho = new Win32.BITMAPINFOHEADER
            {
                biSize = (uint)Marshal.SizeOf<Win32.BITMAPINFOHEADER>(),
                biWidth = largura,
                biHeight = -altura,     // negativo: de cima para baixo, como o PNG
                biPlanes = 1,
                biBitCount = 32,
            };
            dib = Win32.CreateDIBSection(tela, ref cabecalho, 0, out IntPtr bits, IntPtr.Zero, 0);
            if (dib == IntPtr.Zero || bits == IntPtr.Zero)
                throw new InvalidOperationException("sem memória para o print");
            anterior = Win32.SelectObject(memoria, dib);

            if (!Win32.BitBlt(memoria, 0, 0, largura, altura, tela, r.Left, r.Top,
                              Win32.SRCCOPY | Win32.CAPTUREBLT))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "BitBlt falhou");

            byte[] bgra = new byte[largura * altura * 4];
            Marshal.Copy(bits, bgra, 0, bgra.Length);
            return new Quadro(largura, altura, bgra);
        }
        finally
        {
            if (anterior != IntPtr.Zero) Win32.SelectObject(memoria, anterior);
            if (dib != IntPtr.Zero) Win32.DeleteObject(dib);
            if (memoria != IntPtr.Zero) Win32.DeleteDC(memoria);
            Win32.ReleaseDC(IntPtr.Zero, tela);
        }
    }
}
