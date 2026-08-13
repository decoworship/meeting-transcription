using System.ComponentModel;
using System.Runtime.InteropServices;
using MeetingApp.App.Bandeja;
using MeetingApp.App.Nativo;
using MeetingApp.Nucleo;
using Microsoft.Web.WebView2.Core;

namespace MeetingApp.App;

/// <summary>
/// A janela do aplicativo: um retângulo Win32 hospedando o WebView2.
/// </summary>
/// <remarks>
/// <b>Fechar esconde; sair é pelo menu da bandeja.</b> Esta é a inversão de
/// ciclo de vida da Fase 2.5, e errá-la perde gravação: até a Fase 2 a janela
/// <em>era</em> o programa, e o <c>WM_DESTROY</c> dela encerrava o processo. Aqui
/// ela é um dos dois papéis do mesmo processo, e o outro — gravar — não pode
/// depender de ela estar aberta. Ver <see cref="Aplicacao"/>.
/// </remarks>
internal sealed class JanelaDoApp : IDisposable
{
    private const string NomeDaClasse = "MeetingApp.Janela";

    // Campo, não local: se o GC coletar o delegate, o processo morre na próxima
    // callback do Windows. Mesma armadilha da bandeja.
    private readonly Win32.WndProc _wndProc;
    private Ponte? _ponte;
    private readonly string _pastaDasGravacoes;
    private readonly string? _telaInicial;
    private readonly Gravador _gravador;

    private CoreWebView2Controller? _controlador;
    private CoreWebView2? _web;

    /// <summary>A janela foi fechada pelo X — escondida, não destruída.</summary>
    public Action? AoEsconder { get; set; }

    /// <summary>A página está pronta para receber eventos do gravador.</summary>
    public bool Pronta => _web is not null;

    public bool Visivel => Win32.IsWindowVisible(Hwnd);

    /// <remarks>
    /// Fila e não <see cref="SynchronizationContext"/>: numa janela Win32 crua
    /// não há contexto instalado — quem instala é o WinForms, que este projeto
    /// não usa de propósito. O par fila + <c>PostMessage</c> é o mesmo padrão já
    /// provado na bandeja.
    /// </remarks>
    private readonly System.Collections.Concurrent.ConcurrentQueue<Action> _acoes = new();

    public IntPtr Hwnd { get; }

    public JanelaDoApp(string titulo, Gravador gravador, string pastaDasGravacoes,
                       string? telaInicial = null)
    {
        _gravador = gravador;
        _pastaDasGravacoes = pastaDasGravacoes;
        _telaInicial = telaInicial;
        _wndProc = Processar;

        var classe = new Win32.WNDCLASSEX
        {
            cbSize = (uint)Marshal.SizeOf<Win32.WNDCLASSEX>(),
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProc),
            hInstance = Win32.GetModuleHandle(null),
            hCursor = Win32.LoadCursorW(IntPtr.Zero, Win32.IDC_ARROW),
            lpszClassName = NomeDaClasse,
        };
        if (Win32.RegisterClassExW(ref classe) == 0)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "RegisterClassEx falhou");

        Hwnd = Win32.CreateWindowEx(0, NomeDaClasse, titulo, Win32.WS_OVERLAPPEDWINDOW,
            Win32.CW_USEDEFAULT, Win32.CW_USEDEFAULT, 1200, 800,
            IntPtr.Zero, IntPtr.Zero, classe.hInstance, IntPtr.Zero);
        if (Hwnd == IntPtr.Zero)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "CreateWindowEx falhou");

        Win32.ShowWindow(Hwnd, Win32.SW_SHOW);

        // O WebView2 é assíncrono e completa pelo laço de mensagens, que quem
        // roda é a JanelaDeMensagens da bandeja. Aqui só se dispara.
        _ = IniciarWebAsync();
    }

    /// <summary>Traz a janela de volta, esteja escondida ou só atrás.</summary>
    public void Mostrar()
    {
        Win32.ShowWindow(Hwnd, Win32.IsIconic(Hwnd) ? Win32.SW_RESTORE : Win32.SW_SHOW);
        Win32.SetForegroundWindow(Hwnd);
    }

    /// <summary>Manda um evento à página, sem ela ter pedido.</summary>
    public void Enviar(string json) => NaUi(json);

    private async Task IniciarWebAsync()
    {
        // Dados do WebView2 (cache, localStorage) no perfil do usuário, não ao
        // lado do .exe: o executável pode estar em Arquivos de Programas, onde
        // escrever exige elevação.
        string dados = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MeetingApp", "webview");
        Directory.CreateDirectory(dados);

        var ambiente = await CoreWebView2Environment.CreateAsync(null, dados);
        _controlador = await ambiente.CreateCoreWebView2ControllerAsync(Hwnd);
        _web = _controlador.CoreWebView2;

        Ajustar();

        var opcoes = _web.Settings;
        opcoes.AreDefaultContextMenusEnabled = false;   // menu do navegador não é do app
        opcoes.IsStatusBarEnabled = false;
        opcoes.AreDevToolsEnabled = true;               // útil enquanto a UI está sendo feita

        // Tudo que a página pede é servido de dentro do executável; nada sai
        // para a rede. Ver Conteudo.
        // O áudio vem da pasta das gravações por mapeamento de host, e não pelo
        // nosso handler: assim o WebView2 serve o arquivo direto do disco, com
        // suporte nativo a Range — que é o que faz "pular para 12:34" funcionar
        // num WAV de 200 MB sem carregá-lo inteiro na memória.
        _web.SetVirtualHostNameToFolderMapping(
            "gravacoes.local", _pastaDasGravacoes,
            CoreWebView2HostResourceAccessKind.Allow);

        // Os trechos de voz, pelo mesmo mecanismo. São 4 segundos de WAV, então
        // o Range importa pouco aqui; o que importa é que a tela de vozes possa
        // tocá-los sem a ponte ter de carregar áudio em base64 dentro de um
        // JSON. Ninguém julga um vetor de 256 dimensões — qualquer um julga
        // quatro segundos de áudio, e é isso que torna a limpeza possível.
        Directory.CreateDirectory(Vozes.PastaPadrao);
        _web.SetVirtualHostNameToFolderMapping(
            "vozes.local", Vozes.PastaPadrao,
            CoreWebView2HostResourceAccessKind.Allow);

        _web.AddWebResourceRequestedFilter($"{Conteudo.Raiz}*",
                                           CoreWebView2WebResourceContext.All);
        _web.WebResourceRequested += (_, e) => Servir(ambiente, e);

        // A ponte: a página manda JSON, o núcleo responde JSON. O PostWebMessage
        // só pode ser chamado na thread da UI, e o pipeline responde de uma
        // thread de trabalho — daí o salto de volta pelo laço de mensagens.
        _ponte = new Ponte(_pastaDasGravacoes, NaUi, _gravador);
        _web.WebMessageReceived += (_, e) =>
        {
            string pedido = e.TryGetWebMessageAsString();
            _ = _ponte!.AtenderAsync(pedido);
        };

        _web.Navigate(_telaInicial is { Length: > 0 } t
            ? $"{Conteudo.Raiz}#{t}" : Conteudo.Raiz);
    }

    private static void Servir(CoreWebView2Environment ambiente,
                               CoreWebView2WebResourceRequestedEventArgs e)
    {
        string caminho = new Uri(e.Request.Uri).AbsolutePath;
        var achado = Conteudo.Buscar(caminho);

        if (achado is not { } r)
        {
            e.Response = ambiente.CreateWebResourceResponse(
                null, 404, "Not Found", "Content-Type: text/plain");
            return;
        }

        e.Response = ambiente.CreateWebResourceResponse(
            new MemoryStream(r.Bytes), 200, "OK",
            $"Content-Type: {r.Tipo}\r\nCache-Control: no-cache");
    }

    /// <summary>Manda uma mensagem à página, venha de onde vier a chamada.</summary>
    /// <remarks>
    /// O <c>PostWebMessageAsString</c> só pode ser chamado na thread que criou o
    /// controlador. O pipeline responde de uma thread de trabalho, então a
    /// mensagem entra numa fila e o laço a executa do lado certo.
    /// </remarks>
    private void NaUi(string json)
    {
        _acoes.Enqueue(() => _web?.PostWebMessageAsString(json));
        Win32.PostMessageW(Hwnd, Win32.WM_EXECUTAR, IntPtr.Zero, IntPtr.Zero);
    }

    /// <summary>Encaixa o WebView2 na área útil da janela.</summary>
    private void Ajustar()
    {
        if (_controlador is null) return;
        Win32.GetClientRect(Hwnd, out var r);
        _controlador.Bounds = new System.Drawing.Rectangle(0, 0, r.Right - r.Left, r.Bottom - r.Top);
    }

    private IntPtr Processar(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        switch (msg)
        {
            case Win32.WM_EXECUTAR:
                while (_acoes.TryDequeue(out var acao)) acao();
                return IntPtr.Zero;

            case Win32.WM_SIZE:
                Ajustar();
                return IntPtr.Zero;

            case Win32.WM_GETMINMAXINFO:
                // Abaixo disto a lista de segmentos vira uma coluna ilegível.
                var limites = Marshal.PtrToStructure<Win32.MINMAXINFO>(lParam);
                limites.ptMinTrackSize = new Win32.POINT { X = 900, Y = 600 };
                Marshal.StructureToPtr(limites, lParam, fDeleteOld: false);
                return IntPtr.Zero;

            // O X da janela esconde. Não destrói a janela, não encerra o
            // processo, e sobretudo não para a gravação: quem fecha a janela no
            // meio de uma reunião quer a tela fora do caminho, não a reunião
            // perdida (FASE2.5.md, critério C).
            //
            // Esconder e não destruir também é o que faz reabrir ser instantâneo:
            // o WebView2 já está de pé, com a página no estado em que ficou.
            case Win32.WM_CLOSE:
                Win32.ShowWindow(Hwnd, Win32.SW_HIDE);
                AoEsconder?.Invoke();
                return IntPtr.Zero;

            // Sem PostQuitMessage, ao contrário da Fase 2: quem encerra o laço é
            // a janela invisível da bandeja, pelo "Sair" do menu.
            case Win32.WM_DESTROY:
                return IntPtr.Zero;
        }
        return Win32.DefWindowProcW(hwnd, msg, wParam, lParam);
    }

    public void Dispose()
    {
        _controlador?.Close();
        if (Hwnd != IntPtr.Zero) Win32.DestroyWindow(Hwnd);
    }
}
