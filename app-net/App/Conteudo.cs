using System.Reflection;

namespace MeetingApp.App;

/// <summary>
/// Serve a interface a partir dos recursos embutidos no executável.
/// </summary>
/// <remarks>
/// <para>
/// Nada vem de disco nem da rede: o WebView2 pede
/// <c>https://app.local/…</c> e recebe bytes que já estavam dentro do binário.
/// Isso faz o Content-Security-Policy poder ser o mais fechado possível, mantém
/// o app inteiro num arquivo só — o instalador da Fase 4 agradece — e elimina a
/// classe de bug em que a interface e o executável saem de versões diferentes.
/// </para>
/// <para>
/// O nome do host é inventado e nunca resolve em DNS; quem intercepta é o
/// próprio WebView2, antes de qualquer rede.
/// </para>
/// </remarks>
internal static class Conteudo
{
    public const string Host = "app.local";
    public const string Raiz = $"https://{Host}/";

    private static readonly Assembly Assembly = typeof(Conteudo).Assembly;

    /// <summary>
    /// Pasta de onde servir a interface em vez dos recursos embutidos.
    /// </summary>
    /// <remarks>
    /// Existe só para encurtar o ciclo de desenho: com ela, mudar um CSS e
    /// apertar F5 leva segundos, contra os dois minutos de recompilar,
    /// republicar e recopiar o executável. O app publicado nunca a define, e aí
    /// tudo vem de dentro do binário — que é o que garante que a interface e o
    /// código não saiam de versões diferentes.
    /// </remarks>
    public static string? PastaDeDesenvolvimento { get; set; }

    /// <summary>
    /// O tema com que o <c>index.html</c> sai daqui: <c>claro</c>, <c>escuro</c>
    /// ou <c>auto</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// O tema é aplicado <b>aqui</b>, reescrevendo o atributo no HTML enquanto
    /// ele é servido, e não por JavaScript depois que a página abre. O motivo é
    /// o brilho: a configuração vive no processo e chega à página pela ponte,
    /// que é assíncrona — quem escolheu escuro veria a interface clara por uma
    /// fração de segundo a cada abertura. Um script embutido resolveria, mas a
    /// CSP desta página não tem <c>unsafe-inline</c>, e afrouxá-la para pintar
    /// um fundo seria caro pelo preço errado.
    /// </para>
    /// <para>
    /// Trocar de tema com o app aberto continua sendo trabalho do JavaScript —
    /// é só mexer no mesmo atributo. Isto aqui é só o estado inicial.
    /// </para>
    /// <para>
    /// <c>null</c> — o normal — significa perguntar à configuração a cada
    /// navegação, que é o que impede o tema de ficar velho depois de trocado
    /// com o app aberto. Os testes escrevem aqui para não dependerem do
    /// <c>app.json</c> do usuário.
    /// </para>
    /// </remarks>
    public static string? Tema { get; set; }

    private static string TemaAtual => Nucleo.ConfiguracoesDoApp.TemaAceito(
        Tema ?? Nucleo.ConfiguracoesDoApp.Carregar().Tema);

    /// <summary>Bytes de um caminho de URL, ou <c>null</c> se não existe.</summary>
    public static (byte[] Bytes, string Tipo)? Buscar(string caminho)
    {
        caminho = caminho.Trim('/');
        if (caminho.Length == 0) caminho = "index.html";

        if (PastaDeDesenvolvimento is { Length: > 0 } pasta)
        {
            // O design system continua vindo do repositório, não da pasta de
            // trabalho: é a mesma cópia que o app Gradio usa.
            string doDisco = Path.Combine(pasta, caminho.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(doDisco))
                return (ComTema(caminho, File.ReadAllBytes(doDisco)), TipoDe(caminho));
        }

        // "ds/tokens.css" -> "MeetingApp.web.ds.tokens.css". Os recursos
        // embutidos já são nomeados com ponto pelo próprio MSBuild.
        string recurso = "MeetingApp.web." + caminho.Replace('/', '.');

        using var fluxo = Assembly.GetManifestResourceStream(recurso);
        if (fluxo is null) return null;

        using var memoria = new MemoryStream();
        fluxo.CopyTo(memoria);
        return (ComTema(caminho, memoria.ToArray()), TipoDe(caminho));
    }

    /// <summary>
    /// Troca o <c>data-tema</c> do <c>index.html</c> pelo tema configurado.
    /// </summary>
    /// <remarks>
    /// Só o <c>index.html</c>, e só o atributo do <c>&lt;html&gt;</c>: é o
    /// único lugar em que ele aparece, e a troca é feita por texto exato para
    /// que uma mudança no HTML apareça como tema que parou de funcionar, e não
    /// como marcação corrompida.
    /// </remarks>
    private static byte[] ComTema(string caminho, byte[] bytes)
    {
        if (caminho != "index.html") return bytes;

        // O que entra no atributo passou por TemaAceito, que só devolve uma das
        // três constantes: um app.json editado à mão não escreve marcação aqui.
        string tema = TemaAtual;
        if (tema == Nucleo.ConfiguracoesDoApp.TemaPadrao) return bytes;

        string html = System.Text.Encoding.UTF8.GetString(bytes);
        return System.Text.Encoding.UTF8.GetBytes(
            html.Replace("data-tema=\"claro\"", $"data-tema=\"{tema}\""));
    }

    private static string TipoDe(string caminho) => Path.GetExtension(caminho) switch
    {
        ".html" => "text/html; charset=utf-8",
        ".css" => "text/css; charset=utf-8",
        ".js" => "text/javascript; charset=utf-8",
        ".ttf" => "font/ttf",
        ".svg" => "image/svg+xml",
        ".png" => "image/png",
        ".json" => "application/json; charset=utf-8",
        _ => "application/octet-stream",
    };
}
