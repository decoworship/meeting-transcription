using MeetingApp.App.Nativo;
using MeetingApp.Nucleo;
using MeetingRecorder.Capture;
using MeetingRecorder.Core;
using System.Runtime.InteropServices;

namespace MeetingApp.App.Bandeja;

/// <summary>
/// A tela gravada em vídeo, sem som, enquanto as faixas gravam.
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
/// <b>O que entra no quadro é a <see cref="FonteDaTela"/></b>: um monitor
/// inteiro, ou a janela do navegador com a reunião. Quando a fonte não tem o
/// que mostrar — a reunião saiu da frente —, o quadro é um aviso, e não um
/// buraco: o vídeo continua com a duração do áudio, e a sincronia continua
/// sendo uma subtração.
/// </para>
/// </remarks>
internal sealed class GravadorDeTela : IDisposable
{
    /// <summary>
    /// Suficiente para acompanhar slide, documento e quem está com a palavra, e
    /// leve o bastante para não disputar a máquina com a legenda ao vivo.
    /// </summary>
    public const int QuadrosPorSegundo = 10;

    private readonly FonteDaTela _fonte;
    private readonly string _pasta;
    private readonly long _origem;
    private readonly ManualResetEventSlim _parar = new();
    private Thread? _thread;

    private long _primeiro = -1;
    private int _quadros;

    /// <summary>Por que o vídeo parou antes da hora, ou nulo.</summary>
    public string? Falha { get; private set; }

    public GravadorDeTela(FonteDaTela fonte, string pasta, long origemQpc)
    {
        _fonte = fonte;
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
            new VideoDaTela(_primeiro / 10_000_000.0, _fonte.Largura, _fonte.Altura,
                            QuadrosPorSegundo, _quadros, _fonte.Descricao, Falha).Escrever(_pasta);
        }
        catch (Exception e)
        {
            Registro.Escrever("tela", $"não foi possível escrever o {VideoDaTela.Indice}: {e.Message}");
        }
    }

    public void Dispose()
    {
        Parar();
        _fonte.Dispose();
        _parar.Dispose();
    }

    private void Laco()
    {
        int largura = _fonte.Largura, altura = _fonte.Altura;
        long periodo = 10_000_000 / QuadrosPorSegundo;

        IntPtr tela = IntPtr.Zero, memoria = IntPtr.Zero, dib = IntPtr.Zero, anterior = IntPtr.Zero;
        IntPtr fonteDoAviso = IntPtr.Zero, fonteAnterior = IntPtr.Zero;
        CodificadorH264? codificador = null;
        try
        {
            codificador = new CodificadorH264(Path.Combine(_pasta, VideoDaTela.Video),
                                              largura, altura, QuadrosPorSegundo);

            tela = Win32.GetDC(IntPtr.Zero);
            if (tela == IntPtr.Zero) throw new InvalidOperationException("sem acesso à tela");
            memoria = Win32.CreateCompatibleDC(tela);
            dib = Dib.Criar(tela, largura, altura, out IntPtr bits);
            anterior = Win32.SelectObject(memoria, dib);

            // O aviso de pausa: branco sobre preto, do tamanho de uma legenda grande.
            fonteDoAviso = Win32.CreateFont(-Math.Max(16, altura / 24), 0, 0, 0, 400, 0, 0, 0,
                                            1, 0, 0, 4, 0, "Segoe UI");
            fonteAnterior = Win32.SelectObject(memoria, fonteDoAviso);
            Win32.SetBkMode(memoria, Win32.TRANSPARENT);
            Win32.SetTextColor(memoria, 0x00E0E0E0);

            Registro.Escrever("tela", $"gravando {_fonte.Descricao} em {largura}×{altura} a {QuadrosPorSegundo} qps");

            long proximo = WasapiTrackCapture.QpcAgora();
            int falhasSeguidas = 0;
            string? avisoAnterior = null;
            while (!_parar.IsSet)
            {
                long agora = WasapiTrackCapture.QpcAgora();
                if (_fonte.Desenhar(memoria, tela, out string? aviso))
                {
                    if (aviso is not null)
                    {
                        Win32.PatBlt(memoria, 0, 0, largura, altura, Win32.BLACKNESS);
                        var area = new Win32.RECT { Left = 0, Top = 0, Right = largura, Bottom = altura };
                        Win32.DrawText(memoria, aviso, -1, ref area,
                                       Win32.DT_CENTER | Win32.DT_VCENTER | Win32.DT_SINGLELINE);
                    }
                    if (aviso != avisoAnterior)
                    {
                        Registro.Escrever("tela", aviso ?? "voltou a gravar");
                        avisoAnterior = aviso;
                    }
                    Win32.GdiFlush();
                    if (_primeiro < 0) _primeiro = Math.Max(0, agora - _origem);
                    long tempo = Math.Max(0, agora - _origem - _primeiro);
                    codificador.Escrever(bits, tempo, periodo);
                    _quadros++;
                    falhasSeguidas = 0;
                }
                else if (++falhasSeguidas == 1)
                {
                    Registro.Escrever("tela", $"cópia falhou ({Marshal.GetLastWin32Error()}), quadro pulado");
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
            if (fonteAnterior != IntPtr.Zero) Win32.SelectObject(memoria, fonteAnterior);
            if (fonteDoAviso != IntPtr.Zero) Win32.DeleteObject(fonteDoAviso);
            if (anterior != IntPtr.Zero) Win32.SelectObject(memoria, anterior);
            if (dib != IntPtr.Zero) Win32.DeleteObject(dib);
            if (memoria != IntPtr.Zero) Win32.DeleteDC(memoria);
            if (tela != IntPtr.Zero) Win32.ReleaseDC(IntPtr.Zero, tela);
        }
    }
}

/// <summary>Um DIB de 32 bits de cima para baixo, como o codificador espera.</summary>
internal static class Dib
{
    public static IntPtr Criar(IntPtr dc, int largura, int altura, out IntPtr bits)
    {
        var cabecalho = new Win32.BITMAPINFOHEADER
        {
            biSize = (uint)Marshal.SizeOf<Win32.BITMAPINFOHEADER>(),
            biWidth = largura,
            biHeight = -altura,
            biPlanes = 1,
            biBitCount = 32,
        };
        IntPtr dib = Win32.CreateDIBSection(dc, ref cabecalho, 0, out bits, IntPtr.Zero, 0);
        if (dib == IntPtr.Zero || bits == IntPtr.Zero)
            throw new InvalidOperationException("sem memória para o quadro");
        return dib;
    }
}

/// <summary>O que vai para dentro de cada quadro. Chamada só da thread do vídeo.</summary>
internal abstract class FonteDaTela : IDisposable
{
    /// <summary>Par, como o H.264 exige. Não muda durante a gravação.</summary>
    public abstract int Largura { get; }
    public abstract int Altura { get; }

    /// <summary>Vai para o registro e para o <c>tela.json</c>.</summary>
    public abstract string Descricao { get; }

    /// <summary>Desenha o quadro em <paramref name="dc"/>, do tamanho da fonte.</summary>
    /// <param name="tela">O DC da tela inteira, para quem copia dela.</param>
    /// <param name="aviso">A frase do quadro de pausa, quando não há o que mostrar.</param>
    /// <returns>Falso para pular o quadro: a cópia falhou, e o anterior fica.</returns>
    public abstract bool Desenhar(IntPtr dc, IntPtr tela, out string? aviso);

    public virtual void Dispose() { }

    protected static int Par(int n) => Math.Max(2, n & ~1);
}

/// <summary>
/// Um monitor inteiro, por <c>BitBlt</c> — como o print (<see cref="CapturaDeTela"/>),
/// e pelo mesmo motivo: o Windows.Graphics.Capture arrastaria a projeção WinRT e
/// desenha uma borda amarela na tela gravada. Janela protegida sai preta, de propósito.
/// </summary>
internal sealed class FonteDoMonitor(Monitores.Monitor monitor) : FonteDaTela
{
    public override int Largura { get; } = Par(monitor.Largura);
    public override int Altura { get; } = Par(monitor.Altura);
    public override string Descricao => monitor.Id;

    public override bool Desenhar(IntPtr dc, IntPtr tela, out string? aviso)
    {
        aviso = null;
        if (!Win32.BitBlt(dc, 0, 0, Largura, Altura, tela, monitor.Area.Left, monitor.Area.Top,
                          Win32.SRCCOPY | Win32.CAPTUREBLT))
            return false;
        DesenharCursor(dc, monitor.Area);
        return true;
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

/// <summary>
/// A janela do navegador com a aba da reunião, que a extensão aponta.
/// </summary>
/// <remarks>
/// <para>
/// <b><c>PrintWindow</c> com <c>PW_RENDERFULLCONTENT</c>, e não <c>BitBlt</c> da
/// tela.</b> Ele pede ao DWM o desenho da própria janela, então outra janela
/// por cima não entra no vídeo. Minimizada não há desenho, e o quadro vira aviso.
/// </para>
/// <para>
/// <b>Só grava quando a aba da reunião está na frente</b>, pela regra de
/// <see cref="JanelaDaReuniao.Decidir"/>: a janela é do navegador, e trocar de
/// aba nela poria no vídeo o que entrou no lugar.
/// </para>
/// </remarks>
/// <param name="aba">A aba como a extensão disse por último. Lida de outra thread.</param>
internal sealed class FonteDaJanelaDaReuniao(Func<AbaDaReuniao?> aba, int largura, int altura) : FonteDaTela
{
    public override int Largura { get; } = Par(largura);
    public override int Altura { get; } = Par(altura);
    public override string Descricao => JanelaDaReuniao.Fonte;

    private IntPtr _hwnd;
    private long _ultimaBusca = -1000;   // procura já no primeiro quadro

    // O DIB do tamanho da janela, refeito quando ela muda de tamanho.
    private IntPtr _dc, _dib, _anterior;
    private int _dibL, _dibA;

    public override bool Desenhar(IntPtr dc, IntPtr tela, out string? aviso)
    {
        var a = aba();
        bool achou = _hwnd != IntPtr.Zero && Win32.IsWindow(_hwnd);
        string? titulo = achou ? JanelasDoNavegador.Titulo(_hwnd) : null;

        // Procura de novo quando não tem janela, ou quando a extensão diz que a
        // aba está na frente e a janela guardada não a mostra — ela foi
        // arrastada para outra janela. Uma vez por segundo, no máximo: listar as
        // janelas a cada quadro seria trabalho jogado fora.
        if (a is not null && (!achou || (a.NaFrente && !JanelaDaReuniao.Mostra(titulo, a.Titulo)))
            && Environment.TickCount64 - _ultimaBusca >= 1000)
        {
            _ultimaBusca = Environment.TickCount64;
            var janelas = JanelasDoNavegador.Listar();
            int i = JanelaDaReuniao.Escolher([.. janelas.Select(j => j.Titulo)], a.Titulo);
            if (i >= 0)
            {
                _hwnd = janelas[i].Hwnd;
                achou = true;
                titulo = janelas[i].Titulo;
            }
        }

        var situacao = JanelaDaReuniao.Decidir(a, achou, achou && Win32.IsIconic(_hwnd), titulo);
        aviso = JanelaDaReuniao.Aviso(situacao);
        if (aviso is not null) return true;

        if (!Win32.GetWindowRect(_hwnd, out var r)) return false;
        // O retângulo da janela inclui a borda invisível de redimensionar; o
        // do DWM é o que se vê. Sem ele, sobra uma moldura transparente.
        if (Win32.DwmGetWindowAttribute(_hwnd, Win32.DWMWA_EXTENDED_FRAME_BOUNDS, out var v,
                                        Marshal.SizeOf<Win32.RECT>()) != 0) v = r;
        int jl = r.Right - r.Left, ja = r.Bottom - r.Top;
        if (jl <= 0 || ja <= 0) return false;

        if (jl != _dibL || ja != _dibA)
        {
            Soltar();
            _dc = Win32.CreateCompatibleDC(tela);
            _dib = Dib.Criar(tela, jl, ja, out _);
            _anterior = Win32.SelectObject(_dc, _dib);
            (_dibL, _dibA) = (jl, ja);
        }
        if (!Win32.PrintWindow(_hwnd, _dc, Win32.PW_RENDERFULLCONTENT)) return false;

        int ox = Math.Clamp(v.Left - r.Left, 0, jl - 1), oy = Math.Clamp(v.Top - r.Top, 0, ja - 1);
        int vl = Math.Min(v.Right - v.Left, jl - ox), va = Math.Min(v.Bottom - v.Top, ja - oy);
        var (x, y, l, h) = JanelaDaReuniao.Encaixar(vl, va, Largura, Altura);

        Win32.PatBlt(dc, 0, 0, Largura, Altura, Win32.BLACKNESS);
        if (l == vl && h == va)
        {
            Win32.BitBlt(dc, x, y, l, h, _dc, ox, oy, Win32.SRCCOPY);
        }
        else
        {
            // HALFTONE é o modo que reduz sem serrilhar o texto; ele exige
            // reposicionar a origem do pincel depois de escolhido.
            Win32.SetStretchBltMode(dc, Win32.HALFTONE);
            Win32.SetBrushOrgEx(dc, 0, 0, IntPtr.Zero);
            Win32.StretchBlt(dc, x, y, l, h, _dc, ox, oy, vl, va, Win32.SRCCOPY);
        }
        return true;
    }

    private void Soltar()
    {
        if (_anterior != IntPtr.Zero) Win32.SelectObject(_dc, _anterior);
        if (_dib != IntPtr.Zero) Win32.DeleteObject(_dib);
        if (_dc != IntPtr.Zero) Win32.DeleteDC(_dc);
        (_dc, _dib, _anterior, _dibL, _dibA) = (IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 0, 0);
    }

    public override void Dispose() => Soltar();
}
