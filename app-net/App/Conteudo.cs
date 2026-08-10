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

    /// <summary>Bytes de um caminho de URL, ou <c>null</c> se não existe.</summary>
    public static (byte[] Bytes, string Tipo)? Buscar(string caminho)
    {
        caminho = caminho.Trim('/');
        if (caminho.Length == 0) caminho = "index.html";

        // "ds/tokens.css" -> "MeetingApp.web.ds.tokens.css". Os recursos
        // embutidos já são nomeados com ponto pelo próprio MSBuild.
        string recurso = "MeetingApp.web." + caminho.Replace('/', '.');

        using var fluxo = Assembly.GetManifestResourceStream(recurso);
        if (fluxo is null) return null;

        using var memoria = new MemoryStream();
        fluxo.CopyTo(memoria);
        return (memoria.ToArray(), TipoDe(caminho));
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
