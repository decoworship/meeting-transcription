using MeetingApp.App.Nativo;
using MeetingApp.Nucleo;
using MeetingRecorder.Capture;
using System.Runtime.InteropServices;

namespace MeetingApp.App.Bandeja;

/// <summary>
/// Um monitor gravado em vídeo, sem som, enquanto as faixas gravam.
/// </summary>
/// <remarks>
/// <para>
/// <b>O relógio é o das faixas.</b> Cada quadro é carimbado com o QPC menos a
/// origem que o <see cref="Gravador"/> deu ao áudio, e o primeiro carimbo vai
/// para o <c>tela.json</c> (<see cref="VideoDaTela"/>). É o que dispensa juntar
/// áudio e vídeo depois: os dois já nascem no mesmo eixo.
/// </para>
/// <para>
/// <b>Nada aqui pode parar o áudio.</b> Uma thread própria, abaixo do normal, e
/// toda falha vira <see cref="Falha"/> e encerra só o vídeo. Quadro que não deu
/// para copiar — a tela do UAC, um monitor que acabou de sair — é pulado, e o
/// anterior fica na tela por mais tempo.
/// </para>
/// <para>
/// <b>GDI, como o print</b> (<see cref="CapturaDeTela"/>), e pelo mesmo motivo:
/// o Windows.Graphics.Capture arrastaria a projeção WinRT e desenha uma borda
/// amarela na tela gravada. Dez quadros por segundo de uma tela de reunião é o
/// que o <c>BitBlt</c> faz sem esforço; janela protegida sai preta, de propósito.
/// </para>
/// </remarks>
internal sealed class GravadorDeTela : IDisposable
{
    /// <summary>
    /// Suficiente para acompanhar slide, documento e quem está com a palavra, e
    /// leve o bastante para não disputar a máquina com a legenda ao vivo.
    /// </summary>
    public const int QuadrosPorSegundo = 10;

    private readonly Monitores.Monitor _monitor;
    private readonly string _pasta;
    private readonly long _origem;
    private readonly ManualResetEventSlim _parar = new();
    private Thread? _thread;

    private long _primeiro = -1;
    private int _quadros;

    /// <summary>Por que o vídeo parou antes da hora, ou nulo.</summary>
    public string? Falha { get; private set; }

    public GravadorDeTela(Monitores.Monitor monitor, string pasta, long origemQpc)
    {
        _monitor = monitor;
        _pasta = pasta;
        _origem = origemQpc;
    }

    public void Iniciar()
    {
        _thread = new Thread(Laco)
        {
            Name = "gravador-de-tela",
            IsBackground = true,
            Priority = ThreadPriority.BelowNormal,
        };
        _thread.Start();
    }

    /// <summary>Fecha o MP4 e escreve o <c>tela.json</c>. Espera o laço terminar.</summary>
    public void Parar()
    {
        if (_thread is null) return;
        _parar.Set();
        _thread.Join();
        _thread = null;

        if (_quadros == 0)
        {
            Registro.Escrever("tela", $"nenhum quadro gravado{(Falha is null ? "" : $": {Falha}")}");
            return;
        }
        try
        {
            new VideoDaTela(_primeiro / 10_000_000.0, Par(_monitor.Largura), Par(_monitor.Altura),
                            QuadrosPorSegundo, _quadros, _monitor.Id, Falha).Escrever(_pasta);
        }
        catch (Exception e)
        {
            Registro.Escrever("tela", $"não foi possível escrever o {VideoDaTela.Indice}: {e.Message}");
        }
    }

    public void Dispose()
    {
        Parar();
        _parar.Dispose();
    }

    private static int Par(int n) => n & ~1;

    private void Laco()
    {
        int largura = Par(_monitor.Largura), altura = Par(_monitor.Altura);
        long periodo = 10_000_000 / QuadrosPorSegundo;
        var area = _monitor.Area;

        IntPtr tela = IntPtr.Zero, memoria = IntPtr.Zero, dib = IntPtr.Zero, anterior = IntPtr.Zero;
        CodificadorH264? codificador = null;
        try
        {
            codificador = new CodificadorH264(Path.Combine(_pasta, VideoDaTela.Video),
                                              largura, altura, QuadrosPorSegundo);

            tela = Win32.GetDC(IntPtr.Zero);
            if (tela == IntPtr.Zero) throw new InvalidOperationException("sem acesso à tela");
            memoria = Win32.CreateCompatibleDC(tela);
            var cabecalho = new Win32.BITMAPINFOHEADER
            {
                biSize = (uint)Marshal.SizeOf<Win32.BITMAPINFOHEADER>(),
                biWidth = largura,
                biHeight = -altura,     // de cima para baixo, como o codificador espera
                biPlanes = 1,
                biBitCount = 32,
            };
            dib = Win32.CreateDIBSection(tela, ref cabecalho, 0, out IntPtr bits, IntPtr.Zero, 0);
            if (dib == IntPtr.Zero || bits == IntPtr.Zero)
                throw new InvalidOperationException("sem memória para o quadro");
            anterior = Win32.SelectObject(memoria, dib);

            Registro.Escrever("tela", $"gravando {_monitor.Id} em {largura}×{altura} a {QuadrosPorSegundo} qps");

            long proximo = WasapiTrackCapture.QpcAgora();
            int falhasSeguidas = 0;
            while (!_parar.IsSet)
            {
                long agora = WasapiTrackCapture.QpcAgora();
                if (Win32.BitBlt(memoria, 0, 0, largura, altura, tela, area.Left, area.Top,
                                 Win32.SRCCOPY | Win32.CAPTUREBLT))
                {
                    DesenharCursor(memoria, area);
                    if (_primeiro < 0) _primeiro = Math.Max(0, agora - _origem);
                    long tempo = Math.Max(0, agora - _origem - _primeiro);
                    codificador.Escrever(bits, tempo, periodo);
                    _quadros++;
                    falhasSeguidas = 0;
                }
                else if (++falhasSeguidas == 1)
                {
                    Registro.Escrever("tela", $"BitBlt falhou ({Marshal.GetLastWin32Error()}), quadro pulado");
                }

                // Atrasou, e não recupera: correr atrás com quadros colados
                // não mostra nada a mais, e toma a máquina de quem está gravando.
                proximo += periodo;
                long falta = proximo - WasapiTrackCapture.QpcAgora();
                if (falta <= 0) { proximo = WasapiTrackCapture.QpcAgora(); continue; }
                _parar.Wait(TimeSpan.FromTicks(falta));
            }
        }
        catch (Exception e)
        {
            Falha = e.Message;
            Registro.Escrever("tela", $"o vídeo parou: {e.Message}");
        }
        finally
        {
            try { codificador?.Dispose(); }
            catch (Exception e)
            {
                Falha ??= e.Message;
                _quadros = 0;   // sem índice, o MP4 não abre
                Registro.Escrever("tela", $"não foi possível fechar o vídeo: {e.Message}");
            }
            if (anterior != IntPtr.Zero) Win32.SelectObject(memoria, anterior);
            if (dib != IntPtr.Zero) Win32.DeleteObject(dib);
            if (memoria != IntPtr.Zero) Win32.DeleteDC(memoria);
            if (tela != IntPtr.Zero) Win32.ReleaseDC(IntPtr.Zero, tela);
        }
    }

    /// <summary>
    /// O ponteiro do mouse por cima do quadro: o <c>BitBlt</c> não o copia, e
    /// quem compartilha a tela aponta com ele.
    /// </summary>
    private static void DesenharCursor(IntPtr dc, Win32.RECT area)
    {
        var info = new Win32.CURSORINFO { cbSize = (uint)Marshal.SizeOf<Win32.CURSORINFO>() };
        if (!Win32.GetCursorInfo(ref info) || (info.flags & Win32.CURSOR_SHOWING) == 0
            || info.hCursor == IntPtr.Zero) return;
        if (!Win32.GetIconInfo(info.hCursor, out var icone)) return;
        try
        {
            Win32.DrawIconEx(dc, info.ptScreenPos.X - area.Left - (int)icone.xHotspot,
                             info.ptScreenPos.Y - area.Top - (int)icone.yHotspot,
                             info.hCursor, 0, 0, 0, IntPtr.Zero, Win32.DI_NORMAL);
        }
        finally
        {
            // O GetIconInfo devolve cópias dos bitmaps, e quem pediu apaga.
            if (icone.hbmMask != IntPtr.Zero) Win32.DeleteObject(icone.hbmMask);
            if (icone.hbmColor != IntPtr.Zero) Win32.DeleteObject(icone.hbmColor);
        }
    }
}
