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
        Ponteiro.Desenhar(dc, monitor.Area, 0, 0, 1.0);
        return true;
    }
}

/// <summary>
/// O ponteiro do mouse por cima do quadro: o <c>BitBlt</c> não o copia, e
/// quem compartilha a tela aponta com ele.
/// </summary>
internal static class Ponteiro
{
    /// <param name="area">O pedaço da tela que foi copiado.</param>
    /// <param name="x">Onde ele caiu no quadro.</param>
    /// <param name="escala">Quanto ele foi reduzido ou ampliado.</param>
    public static void Desenhar(IntPtr dc, Win32.RECT area, int x, int y, double escala)
    {
        var info = new Win32.CURSORINFO { cbSize = (uint)Marshal.SizeOf<Win32.CURSORINFO>() };
        if (!Win32.GetCursorInfo(ref info) || (info.flags & Win32.CURSOR_SHOWING) == 0
            || info.hCursor == IntPtr.Zero) return;
        if (info.ptScreenPos.X < area.Left || info.ptScreenPos.X >= area.Right
            || info.ptScreenPos.Y < area.Top || info.ptScreenPos.Y >= area.Bottom) return;
        if (!Win32.GetIconInfo(info.hCursor, out var icone)) return;
        try
        {
            Win32.DrawIconEx(dc, x + (int)((info.ptScreenPos.X - area.Left) * escala) - (int)icone.xHotspot,
                             y + (int)((info.ptScreenPos.Y - area.Top) * escala) - (int)icone.yHotspot,
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
/// Uma janela inteira encaixada no quadro, por <c>PrintWindow</c> com
/// <c>PW_RENDERFULLCONTENT</c>, e não <c>BitBlt</c> da tela: ele pede ao DWM o
/// desenho da própria janela, então outra janela por cima não entra no vídeo.
/// Medido com o Chrome (coberto, 30/10/2026) e com a câmera do Zoom.
/// </summary>
internal sealed class CopiaDeJanela : IDisposable
{
    // O DIB do tamanho da janela, refeito quando ela muda de tamanho.
    private IntPtr _dc, _dib, _anterior;
    private int _largura, _altura;

    /// <returns>Falso quando a cópia falhou, e o quadro deve ser pulado.</returns>
    public bool Copiar(IntPtr hwnd, IntPtr dc, IntPtr tela, int quadroL, int quadroA)
    {
        if (!Win32.GetWindowRect(hwnd, out var r)) return false;
        // O retângulo da janela inclui a borda invisível de redimensionar; o
        // do DWM é o que se vê. Sem ele, sobra uma moldura transparente.
        if (Win32.DwmGetWindowAttribute(hwnd, Win32.DWMWA_EXTENDED_FRAME_BOUNDS, out var v,
                                        Marshal.SizeOf<Win32.RECT>()) != 0) v = r;
        int jl = r.Right - r.Left, ja = r.Bottom - r.Top;
        if (jl <= 0 || ja <= 0) return false;

        if (jl != _largura || ja != _altura)
        {
            Dispose();
            _dc = Win32.CreateCompatibleDC(tela);
            _dib = Dib.Criar(tela, jl, ja, out _);
            _anterior = Win32.SelectObject(_dc, _dib);
            (_largura, _altura) = (jl, ja);
        }
        if (!Win32.PrintWindow(hwnd, _dc, Win32.PW_RENDERFULLCONTENT)) return false;

        int ox = Math.Clamp(v.Left - r.Left, 0, jl - 1), oy = Math.Clamp(v.Top - r.Top, 0, ja - 1);
        int vl = Math.Min(v.Right - v.Left, jl - ox), va = Math.Min(v.Bottom - v.Top, ja - oy);
        Encaixar(dc, _dc, ox, oy, vl, va, quadroL, quadroA);
        return true;
    }

    /// <summary>
    /// Um retângulo de <paramref name="origem"/> no meio do quadro, sem
    /// distorcer, com faixa preta onde sobra (<see cref="JanelaDaReuniao.Encaixar"/>).
    /// </summary>
    /// <returns>Onde ele caiu e a escala, para o ponteiro.</returns>
    public static (int X, int Y, double Escala) Encaixar(IntPtr dc, IntPtr origem, int ox, int oy, int l, int a,
                                                         int quadroL, int quadroA)
    {
        var (x, y, el, ea) = JanelaDaReuniao.Encaixar(l, a, quadroL, quadroA);
        Win32.PatBlt(dc, 0, 0, quadroL, quadroA, Win32.BLACKNESS);
        if (el == l && ea == a)
        {
            Win32.BitBlt(dc, x, y, el, ea, origem, ox, oy, Win32.SRCCOPY | Win32.CAPTUREBLT);
        }
        else
        {
            // HALFTONE é o modo que reduz sem serrilhar o texto; ele exige
            // reposicionar a origem do pincel depois de escolhido.
            Win32.SetStretchBltMode(dc, Win32.HALFTONE);
            Win32.SetBrushOrgEx(dc, 0, 0, IntPtr.Zero);
            Win32.StretchBlt(dc, x, y, el, ea, origem, ox, oy, l, a, Win32.SRCCOPY | Win32.CAPTUREBLT);
        }
        return (x, y, l > 0 ? (double)el / l : 1.0);
    }

    public void Dispose()
    {
        if (_anterior != IntPtr.Zero) Win32.SelectObject(_dc, _anterior);
        if (_dib != IntPtr.Zero) Win32.DeleteObject(_dib);
        if (_dc != IntPtr.Zero) Win32.DeleteDC(_dc);
        (_dc, _dib, _anterior, _largura, _altura) = (IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 0, 0);
    }
}

/// <summary>
/// A janela do navegador com a aba da reunião, que a extensão aponta.
/// </summary>
/// <remarks>
/// <b>Só grava quando a aba da reunião está na frente</b>, pela regra de
/// <see cref="JanelaDaReuniao.Decidir"/>: a janela é do navegador, e trocar de
/// aba nela poria no vídeo o que entrou no lugar. Minimizada não há desenho, e
/// o quadro vira aviso.
/// </remarks>
/// <param name="aba">A aba como a extensão disse por último. Lida de outra thread.</param>
internal sealed class FonteDaJanelaDaReuniao(Func<AbaDaReuniao?> aba, int largura, int altura) : FonteDaTela
{
    public override int Largura { get; } = Par(largura);
    public override int Altura { get; } = Par(altura);
    public override string Descricao => JanelaDaReuniao.Fonte;

    private readonly CopiaDeJanela _copia = new();
    private IntPtr _hwnd;
    private long _ultimaBusca = -1000;   // procura já no primeiro quadro

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
        return aviso is not null || _copia.Copiar(_hwnd, dc, tela, Largura, Altura);
    }

    public override void Dispose() => _copia.Dispose();
}

/// <summary>
/// A reunião do Zoom desktop: a janela dela, ou — quando quem grava está
/// compartilhando a tela — o que está sendo compartilhado, que é o que a
/// reunião está vendo. A regra é <see cref="ReuniaoDoZoom.Decidir"/>.
/// </summary>
/// <remarks>
/// <b>Sem extensão e sem aba.</b> As janelas do <c>Zoom.exe</c> se acham pelo
/// processo e pela classe, e não há outra coisa que possa entrar na frente
/// dentro delas. O compartilhamento é copiado da tela (<c>BitBlt</c>), porque a
/// camada de anotação que o marca é transparente: o que está por baixo dela é
/// o que se compartilha.
/// </remarks>
internal sealed class FonteDoZoom(int largura, int altura) : FonteDaTela
{
    public override int Largura { get; } = Par(largura);
    public override int Altura { get; } = Par(altura);
    public override string Descricao => ReuniaoDoZoom.Fonte;

    private readonly CopiaDeJanela _copia = new();
    private IntPtr _hwnd;
    private SituacaoDoZoom _situacao = SituacaoDoZoom.Esperando;
    private long _ultimaBusca = -1000;

    /// <summary>
    /// Olha as janelas do Zoom e diz em que pé a reunião está. Chamado a cada
    /// quadro; a busca de verdade só roda quando precisa.
    /// </summary>
    public SituacaoDoZoom Atualizar()
    {
        // A janela guardada continua valendo enquanto estiver de pé e do jeito
        // que estava; qualquer mudança — começou ou parou de compartilhar,
        // minimizou — faz procurar de novo na hora. Minimizada, a cada quarto de
        // segundo, para voltar a gravar logo; sem reunião, a cada segundo, que é
        // o que custa listar os processos.
        bool gravando = _situacao is SituacaoDoZoom.Reuniao or SituacaoDoZoom.Compartilhando;
        bool perdeu = gravando && !(Win32.IsWindow(_hwnd) && Win32.IsWindowVisible(_hwnd) && !Win32.IsIconic(_hwnd));
        long intervalo = _situacao == SituacaoDoZoom.Minimizada ? 250 : 1000;
        if (perdeu || Environment.TickCount64 - _ultimaBusca >= intervalo)
        {
            _ultimaBusca = Environment.TickCount64;
            var janelas = JanelasDoZoom.Listar();
            var (s, i) = ReuniaoDoZoom.Decidir([.. janelas.Select(j => j.Dados)]);
            _situacao = s;
            _hwnd = i >= 0 ? janelas[i].Hwnd : IntPtr.Zero;
        }
        return _situacao;
    }

    public override bool Desenhar(IntPtr dc, IntPtr tela, out string? aviso)
    {
        Atualizar();
        aviso = ReuniaoDoZoom.Aviso(_situacao);
        if (aviso is not null) return true;

        if (_situacao == SituacaoDoZoom.Reuniao)
            return _copia.Copiar(_hwnd, dc, tela, Largura, Altura);

        if (!Win32.GetWindowRect(_hwnd, out var r) || r.Right <= r.Left || r.Bottom <= r.Top) return false;
        var (x, y, escala) = CopiaDeJanela.Encaixar(dc, tela, r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top,
                                                    Largura, Altura);
        Ponteiro.Desenhar(dc, r, x, y, escala);
        return true;
    }

    public override void Dispose() => _copia.Dispose();
}

/// <summary>
/// A reunião que estiver aberta, onde estiver: a do navegador que a extensão
/// aponta, ou a do Zoom desktop. A regra é <see cref="FonteAutomatica.Escolher"/>.
/// </summary>
/// <param name="aba">A aba da reunião no navegador, como a extensão disse por último.</param>
/// <param name="agenda">A plataforma do evento sendo gravado, ou nulo. Lida a cada quadro: a agenda responde depois de a gravação começar.</param>
internal sealed class FonteDaReuniaoAberta(Func<AbaDaReuniao?> aba, Func<string?> agenda, int largura, int altura)
    : FonteDaTela
{
    public override int Largura { get; } = Par(largura);
    public override int Altura { get; } = Par(altura);
    public override string Descricao => FonteAutomatica.Fonte;

    private readonly FonteDaJanelaDaReuniao _navegador = new(aba, largura, altura);
    private readonly FonteDoZoom _zoom = new(largura, altura);
    private EscolhaAutomatica _atual = EscolhaAutomatica.Nenhuma;

    public override bool Desenhar(IntPtr dc, IntPtr tela, out string? aviso)
    {
        var a = aba();
        string? plataforma = agenda();
        bool zoomAberto = _zoom.Atualizar() != SituacaoDoZoom.Esperando;
        var escolha = FonteAutomatica.Escolher(a is null ? null : a.App ?? "meet", zoomAberto, plataforma, _atual);
        if (escolha != _atual)
        {
            Registro.Escrever("tela", escolha switch
            {
                EscolhaAutomatica.Navegador => $"automático: a reunião do navegador ({a?.App ?? "?"})",
                EscolhaAutomatica.Zoom => "automático: a reunião do Zoom",
                _ => "automático: nenhuma reunião aberta",
            });
            _atual = escolha;
        }

        switch (escolha)
        {
            case EscolhaAutomatica.Navegador: return _navegador.Desenhar(dc, tela, out aviso);
            case EscolhaAutomatica.Zoom: return _zoom.Desenhar(dc, tela, out aviso);
            default:
                aviso = FonteAutomatica.Espera(plataforma);
                return true;
        }
    }

    public override void Dispose()
    {
        _navegador.Dispose();
        _zoom.Dispose();
    }
}
