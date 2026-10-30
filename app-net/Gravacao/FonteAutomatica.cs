using System.Text.RegularExpressions;

namespace MeetingRecorder.Core;

/// <summary>De onde o quadro do vídeo vem, com a fonte "automático".</summary>
public enum EscolhaAutomatica
{
    Nenhuma,

    /// <summary>A janela do navegador com a reunião (Meet, Teams ou Zoom na web).</summary>
    Navegador,

    /// <summary>O Zoom desktop.</summary>
    Zoom,
}

/// <summary>
/// A fonte "automático": grava a reunião que estiver aberta, onde estiver.
/// </summary>
/// <remarks>
/// <para>
/// <b>O que está acontecendo vale mais que a agenda.</b> A extensão só diz
/// "em reunião" quando acha a reunião na página, e a janela do Zoom desktop só
/// existe durante uma: os dois são fato, e a agenda é plano. Por isso ela só
/// desempata — duas reuniões abertas ao mesmo tempo — e diz qual esperar
/// enquanto nenhuma abriu.
/// </para>
/// <para>
/// <b>A escolha gruda.</b> Com as duas abertas e a agenda sem dizer qual, fica a
/// que já estava sendo gravada: trocar de fonte a cada quadro faria um vídeo
/// que pula entre duas reuniões.
/// </para>
/// </remarks>
public static class FonteAutomatica
{
    /// <summary>O valor de <c>screen_monitor</c> que pede a fonte automática.</summary>
    public const string Fonte = "automatico";

    /// <param name="appNoNavegador">
    /// "meet", "teams" ou "zoom" quando a extensão diz uma reunião aberta no
    /// navegador; nulo quando não há.
    /// </param>
    /// <param name="zoomAberto">O Zoom desktop tem uma reunião, mesmo minimizada.</param>
    /// <param name="plataformaDaAgenda">A do evento sendo gravado (<see cref="Plataforma"/>), ou nulo.</param>
    /// <param name="atual">A escolha do quadro anterior.</param>
    public static EscolhaAutomatica Escolher(string? appNoNavegador, bool zoomAberto,
                                            string? plataformaDaAgenda, EscolhaAutomatica atual)
    {
        bool navegador = appNoNavegador is not null;
        if (navegador && zoomAberto)
        {
            if (plataformaDaAgenda is not null && plataformaDaAgenda == appNoNavegador)
                return EscolhaAutomatica.Navegador;
            if (plataformaDaAgenda == "zoom") return EscolhaAutomatica.Zoom;
            return atual == EscolhaAutomatica.Nenhuma ? EscolhaAutomatica.Navegador : atual;
        }
        if (navegador) return EscolhaAutomatica.Navegador;
        if (zoomAberto) return EscolhaAutomatica.Zoom;
        return EscolhaAutomatica.Nenhuma;
    }

    /// <summary>O aviso enquanto nenhuma reunião abriu: a da agenda, quando se sabe.</summary>
    public static string Espera(string? plataformaDaAgenda) => plataformaDaAgenda switch
    {
        "meet" => "Esperando a reunião do Meet abrir",
        "teams" => "Esperando a reunião do Teams abrir",
        "zoom" => "Esperando a reunião do Zoom abrir",
        _ => "Esperando uma reunião abrir — Meet, Teams ou Zoom",
    };

    /// <summary>
    /// "meet", "teams" ou "zoom" pelo link da reunião, no primeiro texto que
    /// tiver um; nulo quando nenhum tem.
    /// </summary>
    /// <remarks>
    /// Os textos são, nesta ordem, o <c>hangoutLink</c>, os links do
    /// <c>conferenceData</c>, o local e a descrição do evento. O Meet vem no
    /// primeiro; o Teams e o Zoom, pelos complementos do Google Agenda, no
    /// segundo — e quem cola o convite à mão, no local ou na descrição.
    /// </remarks>
    public static string? Plataforma(params string?[] textos)
    {
        foreach (var t in textos)
        {
            if (string.IsNullOrEmpty(t)) continue;
            var m = Link.Match(t);
            if (!m.Success) continue;
            return m.Groups["meet"].Success ? "meet" : m.Groups["teams"].Success ? "teams" : "zoom";
        }
        return null;
    }

    private static readonly Regex Link = new(
        @"(?<meet>meet\.google\.com/)|(?<teams>teams\.microsoft\.com/|teams\.live\.com/|teams\.cloud\.microsoft/)"
        + @"|(?<zoom>(?:[\w-]+\.)?zoom\.(?:us|com)/(?:j|w|wc|my|s)/)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
}
