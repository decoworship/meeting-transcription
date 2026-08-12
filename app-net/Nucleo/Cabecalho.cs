namespace MeetingApp.Nucleo;

/// <summary>
/// Os dados da reunião que abrem um arquivo exportado.
/// </summary>
/// <remarks>
/// O que o app Python põe no topo do DOCX, e que aqui vale para os quatro
/// formatos: um TXT que chega por e-mail sem dizer de que reunião é obriga
/// quem recebe a perguntar. Nas legendas vira comentário, porque SRT e VTT não
/// têm lugar para metadado — e o player ignora o que não entende.
/// </remarks>
public sealed class Cabecalho
{
    public string? Titulo { get; init; }
    public string? Cliente { get; init; }
    public string? Projeto { get; init; }
    public string? Data { get; init; }
    public double? DuracaoS { get; init; }
    public string? Idioma { get; init; }

    /// <summary>Quem falou, na ordem em que aparece.</summary>
    public List<string> Falantes { get; init; } = [];

    /// <summary>Os pares rótulo/valor, na ordem de leitura.</summary>
    public IEnumerable<(string Rotulo, string Valor)> Linhas()
    {
        if (Cliente is { Length: > 0 }) yield return ("Cliente", Cliente);
        if (Projeto is { Length: > 0 }) yield return ("Projeto", Projeto);
        if (Data is { Length: > 0 }) yield return ("Data", DataLegivel(Data));
        if (DuracaoS is > 0) yield return ("Duração", Duracao(DuracaoS.Value));
        if (Idioma is { Length: > 0 }) yield return ("Idioma", Idioma);
        if (Falantes.Count > 0)
            yield return ("Falantes", $"{Falantes.Count} — {string.Join(", ", Falantes)}");
    }

    /// <summary>
    /// "2026-08-11T08:02:40" vira "11/08/2026 às 08:02".
    /// </summary>
    /// <remarks>
    /// Com hora, e não só a data: numa semana com três reuniões do mesmo
    /// projeto, a data sozinha não diz qual delas é.
    /// </remarks>
    public static string DataLegivel(string iso)
    {
        return DateTimeOffset.TryParse(iso, System.Globalization.CultureInfo.InvariantCulture,
                   System.Globalization.DateTimeStyles.None, out var quando)
            ? quando.ToString("dd/MM/yyyy 'às' HH:mm")
            : iso;
    }

    /// <summary>Só a data, para nome de arquivo: "2026-08-11".</summary>
    public static string? DataCurta(string? iso)
    {
        if (iso is not { Length: > 0 }) return null;
        return DateTimeOffset.TryParse(iso, System.Globalization.CultureInfo.InvariantCulture,
                   System.Globalization.DateTimeStyles.None, out var quando)
            ? quando.ToString("yyyy-MM-dd")
            : iso[..Math.Min(10, iso.Length)];
    }

    public static string Duracao(double segundos)
    {
        var t = TimeSpan.FromSeconds(segundos);
        return t.TotalHours >= 1
            ? $"{(int)t.TotalHours}h {t.Minutes:00}min"
            : $"{t.Minutes}min {t.Seconds:00}s";
    }

    /// <summary>Monta o cabeçalho a partir da transcrição e do que a UI sabe.</summary>
    public static Cabecalho De(ResultadoDaTranscricao r, string? titulo,
                               string? cliente, string? projeto, string? data)
    {
        var falantes = new List<string>();
        foreach (var s in r.Segments)
        {
            if (s.Speaker is { Length: > 0 } quem && quem != "Unknown" && !falantes.Contains(quem))
                falantes.Add(quem);
        }

        return new Cabecalho
        {
            Titulo = titulo,
            Cliente = cliente,
            Projeto = projeto,
            Data = data,
            DuracaoS = r.Duration,
            Idioma = r.Language,
            Falantes = falantes,
        };
    }
}
