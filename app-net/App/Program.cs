using MeetingApp.App.Bandeja;
using MeetingApp.App.Nativo;

namespace MeetingApp.App;

/// <summary>
/// Um executável, um ícone na bandeja, uma janela.
/// </summary>
/// <remarks>
/// Sem WinForms nem WPF pelo motivo que a Fase 1 mediu doendo — o
/// <c>Microsoft.WindowsDesktop.App</c> recusa trimming (<c>NETSDK1175</c>) e
/// custou 140 MB na bandeja. A janela que este app precisa é um retângulo que
/// hospeda um controle.
/// <para>
/// Classe com <c>Main</c> em vez de instruções de nível superior porque o
/// <c>[STAThread]</c> é obrigatório — o WebView2 é COM, e o diálogo de pasta
/// também.
/// </para>
/// </remarks>
internal static class Programa
{
    /// <summary>
    /// Uma instância por máquina.
    /// </summary>
    /// <remarks>
    /// Nome novo, e não o <c>Global\MeetingRecorder.Tray</c> da Fase 1, de
    /// propósito: enquanto o app fundido não for aprovado, o gravador antigo
    /// continua em uso — e o critério A da Fase 2.5 é justamente gravar com os
    /// dois ao mesmo tempo para comparar as faixas. Um mutex compartilhado
    /// impediria a única prova de que o porte não perdeu nada.
    /// </remarks>
    private const string NomeDoMutex = @"Global\MeetingApp";

    /// <param name="args">
    /// <c>--bandeja</c> sobe sem janela, para iniciar junto com o Windows.
    /// <para>
    /// <c>--gravacoes &lt;pasta&gt;</c> sobrepõe a pasta configurada. Existe para
    /// abrir um acervo que não é o da máquina — teste, suporte, ou uma pasta de
    /// rede — sem mexer no <c>settings.json</c> de quem grava.
    /// </para>
    /// <para>
    /// <c>--web &lt;pasta&gt;</c> serve a interface do disco em vez dos recursos
    /// embutidos, para desenhar sem recompilar. Ver <see cref="Conteudo"/>.
    /// </para>
    /// </param>
    [STAThread]
    private static int Main(string[] args)
    {
        string? Opcao(string nome)
        {
            int i = Array.IndexOf(args, nome);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }

        // Segunda instância não abre um segundo ícone na bandeja nem disputa os
        // dispositivos: pede a janela à que já está de pé e sai. É o que faz
        // clicar no atalho de novo parecer "trazer o app para a frente".
        using var unica = new Mutex(true, NomeDoMutex, out bool sozinho);
        if (!sozinho)
        {
            PedirAJanelaDaOutraInstancia();
            return 0;
        }

        try
        {
            Conteudo.PastaDeDesenvolvimento = Opcao("--web");

            // --tela abre direto numa tela, sem clicar. Serve para desenhar e
            // para fotografar cada estado; o app normal sempre abre na lista.
            using var app = new Aplicacao(Opcao("--gravacoes"), Opcao("--tela"));
            app.Rodar(comJanela: !args.Contains("--bandeja"));
            return 0;
        }
        catch (Exception e)
        {
            // Falhar aqui é falhar em ter interface; sem esta mensagem o
            // executável simplesmente não apareceria. Mesma decisão da bandeja.
            Win32.MessageBox(IntPtr.Zero,
                $"O aplicativo não conseguiu iniciar:\n\n{e.Message}\n\n"
                + "Se a mensagem falar em WebView2, instale o runtime:\n"
                + "https://developer.microsoft.com/microsoft-edge/webview2/",
                "Reuniões", Win32.MB_OK | Win32.MB_ICONERROR);
            return 1;
        }
    }

    /// <remarks>
    /// Pela classe da janela e não pelo título: o título é texto de interface e
    /// muda; a classe é registrada em código e é o que identifica o processo.
    /// </remarks>
    private static void PedirAJanelaDaOutraInstancia()
    {
        var hwnd = Win32.FindWindow(JanelaDeMensagens.NomeDaClasse, null);
        if (hwnd == IntPtr.Zero) return;

        Win32.PostMessageW(hwnd, Win32.RegisterWindowMessage(Win32.MSG_MOSTRAR_JANELA),
                           IntPtr.Zero, IntPtr.Zero);
    }
}
