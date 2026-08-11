using System.Globalization;
using System.IO.Compression;
using System.Text;

namespace MeetingApp.Nucleo;

/// <summary>
/// A transcrição nos formatos que saem do app.
/// </summary>
/// <remarks>
/// Os quatro do app Python (G1 do FEATURES). TXT e as duas legendas são texto
/// puro; o DOCX é um zip com XML dentro, escrito à mão — trazer uma biblioteca
/// de Office custaria alguns megabytes e um monte de superfície para gerar
/// dois parágrafos por trecho.
/// </remarks>
public static class Exportacao
{
    /// <summary>Um instante no formato de legenda: <c>00:12:34,567</c>.</summary>
    private static string Marca(double s, char separadorDeMilissegundos)
    {
        var t = TimeSpan.FromSeconds(s);
        return $"{(int)t.TotalHours:00}:{t.Minutes:00}:{t.Seconds:00}"
               + separadorDeMilissegundos + $"{t.Milliseconds:000}";
    }

    /// <summary>Texto corrido com marca de tempo e quem falou.</summary>
    public static string Txt(ResultadoDaTranscricao r, bool comFalantes = true)
    {
        var sb = new StringBuilder();
        foreach (var s in r.Segments)
        {
            var t = TimeSpan.FromSeconds(s.Start);
            // Hora só quando existe: a maioria das reuniões cabe em minutos, e
            // "00:07:12" numa reunião de 20 min é ruído de leitura.
            string tempo = t.TotalHours >= 1
                ? $"{(int)t.TotalHours:00}:{t.Minutes:00}:{t.Seconds:00}"
                : $"{t.Minutes:00}:{t.Seconds:00}";

            sb.Append('[').Append(tempo).Append(']');
            if (comFalantes && s.Speaker is { Length: > 0 }) sb.Append(' ').Append(s.Speaker).Append(':');
            sb.Append(' ').AppendLine(s.Text.Trim());
        }
        return sb.ToString();
    }

    /// <summary>Legenda SRT: numerada, com vírgula nos milissegundos.</summary>
    public static string Srt(ResultadoDaTranscricao r, bool comFalantes = true)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < r.Segments.Count; i++)
        {
            var s = r.Segments[i];
            sb.Append(i + 1).Append('\n');
            sb.Append(Marca(s.Start, ',')).Append(" --> ").Append(Marca(s.End, ',')).Append('\n');
            if (comFalantes && s.Speaker is { Length: > 0 }) sb.Append(s.Speaker).Append(": ");
            sb.Append(s.Text.Trim()).Append("\n\n");
        }
        return sb.ToString();
    }

    /// <summary>Legenda WebVTT: cabeçalho obrigatório e ponto nos milissegundos.</summary>
    public static string Vtt(ResultadoDaTranscricao r, bool comFalantes = true)
    {
        var sb = new StringBuilder("WEBVTT\n\n");
        foreach (var s in r.Segments)
        {
            sb.Append(Marca(s.Start, '.')).Append(" --> ").Append(Marca(s.End, '.')).Append('\n');
            if (comFalantes && s.Speaker is { Length: > 0 }) sb.Append(s.Speaker).Append(": ");
            sb.Append(s.Text.Trim()).Append("\n\n");
        }
        return sb.ToString();
    }

    /// <summary>
    /// Documento do Word, com o nome de cada falante em negrito e colorido.
    /// </summary>
    /// <remarks>
    /// Um <c>.docx</c> é um zip com três arquivos obrigatórios. Escrevê-los à
    /// mão é mais código que chamar uma biblioteca, mas evita alguns megabytes
    /// no instalador e uma dependência inteira para produzir um parágrafo por
    /// trecho.
    /// </remarks>
    public static void Docx(ResultadoDaTranscricao r, string destino,
                            string titulo, bool comFalantes = true)
    {
        var cores = CoresPorFalante(r);

        var corpo = new StringBuilder();
        corpo.Append(Paragrafo(titulo, negrito: true, cor: null, tamanho: 32));

        foreach (var s in r.Segments)
        {
            var t = TimeSpan.FromSeconds(s.Start);
            string tempo = t.TotalHours >= 1
                ? $"{(int)t.TotalHours:00}:{t.Minutes:00}:{t.Seconds:00}"
                : $"{t.Minutes:00}:{t.Seconds:00}";

            corpo.Append("<w:p><w:r><w:rPr><w:color w:val=\"808080\"/><w:sz w:val=\"18\"/></w:rPr>");
            corpo.Append("<w:t xml:space=\"preserve\">[").Append(Escapar(tempo)).Append("] </w:t></w:r>");

            if (comFalantes && s.Speaker is { Length: > 0 } quem)
            {
                corpo.Append("<w:r><w:rPr><w:b/><w:color w:val=\"")
                     .Append(cores.GetValueOrDefault(quem, "333333"))
                     .Append("\"/></w:rPr><w:t xml:space=\"preserve\">")
                     .Append(Escapar(quem)).Append(": </w:t></w:r>");
            }

            corpo.Append("<w:r><w:t xml:space=\"preserve\">")
                 .Append(Escapar(s.Text.Trim())).Append("</w:t></w:r></w:p>");
        }

        using var zip = new ZipArchive(File.Create(destino), ZipArchiveMode.Create);
        Escrever(zip, "[Content_Types].xml", TiposDeConteudo);
        Escrever(zip, "_rels/.rels", Relacoes);
        Escrever(zip, "word/document.xml",
            "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>"
            + "<w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\">"
            + "<w:body>" + corpo + "</w:body></w:document>");
    }

    private static string Paragrafo(string texto, bool negrito, string? cor, int tamanho)
    {
        var sb = new StringBuilder("<w:p><w:r><w:rPr>");
        if (negrito) sb.Append("<w:b/>");
        if (cor is not null) sb.Append("<w:color w:val=\"").Append(cor).Append("\"/>");
        sb.Append("<w:sz w:val=\"").Append(tamanho).Append("\"/></w:rPr><w:t xml:space=\"preserve\">");
        sb.Append(Escapar(texto)).Append("</w:t></w:r></w:p>");
        return sb.ToString();
    }

    /// <summary>As mesmas cores da tela, para o documento parecer com o que se leu.</summary>
    private static Dictionary<string, string> CoresPorFalante(ResultadoDaTranscricao r)
    {
        string[] paleta = ["2E5E8A", "8A6D3B", "4A7C59", "8C4A5F", "3D6D8A", "7A5C9E"];
        var mapa = new Dictionary<string, string>();

        foreach (var s in r.Segments)
        {
            if (s.Speaker is not { Length: > 0 } quem || mapa.ContainsKey(quem)) continue;
            mapa[quem] = quem == "You" ? paleta[0] : paleta[1 + (mapa.Count % (paleta.Length - 1))];
        }
        return mapa;
    }

    private static string Escapar(string s) =>
        s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");

    private static void Escrever(ZipArchive zip, string nome, string conteudo)
    {
        using var fluxo = new StreamWriter(zip.CreateEntry(nome).Open(), new UTF8Encoding(false));
        fluxo.Write(conteudo);
    }

    private const string TiposDeConteudo =
        "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>"
        + "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">"
        + "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>"
        + "<Default Extension=\"xml\" ContentType=\"application/xml\"/>"
        + "<Override PartName=\"/word/document.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml\"/>"
        + "</Types>";

    private const string Relacoes =
        "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>"
        + "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">"
        + "<Relationship Id=\"rId1\" Target=\"word/document.xml\" "
        + "Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\"/>"
        + "</Relationships>";

    /// <summary>Nome de arquivo seguro a partir do título da reunião.</summary>
    public static string NomeDeArquivo(string titulo, string extensao)
    {
        var limpo = new StringBuilder();
        foreach (char c in titulo)
            limpo.Append(Path.GetInvalidFileNameChars().Contains(c) ? '-' : c);

        string nome = limpo.ToString().Trim();
        if (nome.Length == 0) nome = "transcricao";
        return $"{nome}.{extensao}";
    }
}
