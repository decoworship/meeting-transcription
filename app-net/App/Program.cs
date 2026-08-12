using MeetingApp.App.Nativo;

namespace MeetingApp.App;

/// <summary>
/// A janela do app: WebView2 dentro de uma janela Win32 crua.
/// </summary>
/// <remarks>
/// Sem WinForms nem WPF pelo motivo que a Fase 1 mediu doendo — o
/// <c>Microsoft.WindowsDesktop.App</c> recusa trimming (<c>NETSDK1175</c>) e
/// custou 140 MB na bandeja. A janela que este app precisa é um retângulo que
/// hospeda um controle.
/// <para>
/// Classe com <c>Main</c> em vez de instruções de nível superior porque o
/// <c>[STAThread]</c> é obrigatório — o WebView2 é COM.
/// </para>
/// </remarks>
internal static class Programa
{
    /// <param name="args">
    /// <c>--gravacoes &lt;pasta&gt;</c> sobrepõe a pasta configurada. Existe para
    /// abrir um acervo que não é o da máquina — teste, suporte, ou uma pasta de
    /// rede — sem mexer no <c>settings.json</c> de quem grava.
    /// <para>
    /// <c>--web &lt;pasta&gt;</c> serve a interface do disco em vez dos recursos
    /// embutidos, para desenhar sem recompilar. Ver <see cref="Conteudo"/>.
    /// </para>
    /// </param>
    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            string? Opcao(string nome)
            {
                int i = Array.IndexOf(args, nome);
                return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
            }

            Conteudo.PastaDeDesenvolvimento = Opcao("--web");

            // --tela abre direto numa tela, sem clicar. Serve para desenhar e
            // para fotografar cada estado; o app normal sempre abre na lista.
            using var janela = new JanelaDoApp("Reuniões", Opcao("--gravacoes"), Opcao("--tela"));
            janela.Rodar();
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
}
