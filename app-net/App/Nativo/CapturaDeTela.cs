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
