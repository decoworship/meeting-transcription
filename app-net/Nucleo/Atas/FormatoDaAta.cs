namespace MeetingApp.Nucleo.Atas;

/// <summary>Como a ata é escrita: a de antes, ou com os itens dentro de cada tema.</summary>
/// <remarks>
/// O clássico é o app de antes da ATA-5, byte a byte — o
/// <c>ReferenciasDaAtaTests</c> cobra isso. Existe para comparar, e para voltar
/// se o modelo não acompanhar o formato novo numa reunião.
/// </remarks>
public enum FormatoDaAta { Classico, PorTema }

public static class FormatosDaAta
{
    /// <summary>
    /// Por tema. A validação com o modelo de verdade, em 08/10/2026, deu a ata
    /// 19% menor na mediana contra a meta de 25% (spec §7.2), e o dono aprovou
    /// assim — só o update com cliente agrupa, porque a sessão de trabalho
    /// perdia o "por quê" e as alternativas.
    /// </summary>
    public const string Padrao = "por-tema";

    /// <summary>
    /// Ausente é o padrão; desconhecido é o clássico, que é o caminho conhecido.
    /// </summary>
    public static FormatoDaAta Ler(string? valor) => valor switch
    {
        null or "" or Padrao => FormatoDaAta.PorTema,
        _ => FormatoDaAta.Classico,
    };

    /// <summary>
    /// A leitura estrita, para a linha de comando: só os dois nomes valem.
    /// </summary>
    /// <remarks>
    /// O <see cref="Ler"/> é tolerante porque lê um app.json que pode ser velho;
    /// quem digita <c>--formato portema</c> quer saber que errou, e não medir o
    /// clássico achando que mediu o novo.
    /// </remarks>
    public static bool TentarLer(string valor, out FormatoDaAta formato)
    {
        formato = valor == Padrao ? FormatoDaAta.PorTema : FormatoDaAta.Classico;
        return valor is Padrao or "classico";
    }
}
