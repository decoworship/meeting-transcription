using System.ComponentModel;
using System.Runtime.InteropServices;
using MeetingApp.App.Nativo;
using MeetingApp.Nucleo;
using Microsoft.Web.WebView2.Core;

namespace MeetingApp.App;

/// <summary>
/// A janela do aplicativo: um retângulo Win32 hospedando o WebView2.
/// </summary>
internal sealed class JanelaDoApp : IDisposable
{
    private const string NomeDaClasse = "MeetingApp.Janela";

    // Campo, não local: se o GC coletar o delegate, o processo morre na próxima
    // callback do Windows. Mesma armadilha da bandeja.
    private readonly Win32.WndProc _wndProc;
    private Ponte? _ponte;
    private readonly string _pastaDasGravacoes;

    private CoreWebView2Controller? _controlador;
    private CoreWebView2? _web;

    /// <remarks>
    /// Fila e não <see cref="SynchronizationContext"/>: numa janela Win32 crua
    /// não há contexto instalado — quem instala é o WinForms, que este projeto
    /// não usa de propósito. O par fila + <c>PostMessage</c> é o mesmo padrão já
    /// provado na bandeja.
    /// </remarks>
    private readonly System.Collections.Concurrent.ConcurrentQueue<Action> _acoes = new();

    public IntPtr Hwnd { get; }

    public JanelaDoApp(string titulo, string? pastaDasGravacoes = null)
    {
        Win32.SetProcessDpiAwarenessContext(Win32.DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);

        _pastaDasGravacoes = pastaDasGravacoes ?? PastaPadraoDasGravacoes();
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
    }

    /// <summary>Sobe o WebView2 e entra no laço de mensagens.</summary>
    public void Rodar()
    {
        // O WebView2 é assíncrono e precisa de um laço rodando para completar.
        // Iniciar aqui e bombear as mensagens é o que substitui o
        // Application.Run de um app WinForms.
        _ = IniciarWebAsync();

        while (Win32.GetMessageW(out var msg, IntPtr.Zero, 0, 0) > 0)
        {
            Win32.TranslateMessage(ref msg);
            Win32.DispatchMessageW(ref msg);
        }
    }

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
        _web.AddWebResourceRequestedFilter($"{Conteudo.Raiz}*",
                                           CoreWebView2WebResourceContext.All);
        _web.WebResourceRequested += (_, e) => Servir(ambiente, e);

        // A ponte: a página manda JSON, o núcleo responde JSON. O PostWebMessage
        // só pode ser chamado na thread da UI, e o pipeline responde de uma
        // thread de trabalho — daí o salto de volta pelo laço de mensagens.
        _ponte = new Ponte(_pastaDasGravacoes, NaUi);
        _web.WebMessageReceived += (_, e) =>
        {
            string pedido = e.TryGetWebMessageAsString();
            _ = _ponte!.AtenderAsync(pedido);
        };

        _web.Navigate(Conteudo.Raiz);
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

            case Win32.WM_DESTROY:
                Win32.PostQuitMessage(0);
                return IntPtr.Zero;
        }
        return Win32.DefWindowProcW(hwnd, msg, wParam, lParam);
    }

    /// <summary>Onde o gravador deixa as gravações.</summary>
    /// <remarks>
    /// Lê o mesmo <c>settings.json</c> do gravador em vez de referenciar o
    /// assembly dele: os dois executáveis são separados por desenho (FASE2.md,
    /// princípio 1) e se encontram pelos arquivos. Uma chave lida à mão custa
    /// menos que acoplar os binários.
    /// </remarks>
    private static string PastaPadraoDasGravacoes()
    {
        string padrao = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "MeetingRecordings");

        string cfg = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".meeting-recorder", "settings.json");

        try
        {
            if (!File.Exists(cfg)) return padrao;
            using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(cfg));
            return doc.RootElement.TryGetProperty("output_dir", out var dir)
                   && dir.ValueKind == System.Text.Json.JsonValueKind.String
                   && dir.GetString() is { Length: > 0 } valor
                ? valor
                : padrao;
        }
        catch (Exception)
        {
            // settings.json ilegível não pode impedir o app de abrir — mesma
            // postura do gravador, que cai nos padrões.
            return padrao;
        }
    }

    public void Dispose()
    {
        _controlador?.Close();
        if (Hwnd != IntPtr.Zero) Win32.DestroyWindow(Hwnd);
    }
}
