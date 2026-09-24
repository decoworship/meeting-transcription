namespace MeetingApp.Nucleo;

/// <summary>Cortar um texto para caber numa linha de lista.</summary>
internal static class Corte
{
    /// <summary>
    /// Até <paramref name="limite"/> caracteres, sem partir palavra, com "…" no
    /// fim quando cortou.
    /// </summary>
    /// <remarks>
    /// Na palavra, e não no caractere: "o tom da comunica…" lê como erro; "o tom
    /// da…" lê como continuação.
    /// </remarks>
    public static string NumaPalavra(string texto, int limite)
    {
        if (texto.Length <= limite) return texto;
        int corte = texto.LastIndexOf(' ', limite);
        if (corte <= 0) corte = limite;
        return texto[..corte].TrimEnd(' ', ',', ';', ':') + "…";
    }
}
