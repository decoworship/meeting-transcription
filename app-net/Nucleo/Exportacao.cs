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
    public static string Txt(ResultadoDaTranscricao r, bool comFalantes = true,
                             Cabecalho? cabecalho = null)
    {
        var sb = new StringBuilder();

        if (cabecalho is not null)
        {
            if (cabecalho.Titulo is { Length: > 0 } t)
                sb.AppendLine(t).AppendLine(new string('=', Math.Min(t.Length, 60)));
            foreach (var (rotulo, valor) in cabecalho.Linhas())
                sb.Append(rotulo).Append(": ").AppendLine(valor);
            sb.AppendLine(new string('-', 60)).AppendLine();
        }

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
    public static string Srt(ResultadoDaTranscricao r, bool comFalantes = true,
                             Cabecalho? cabecalho = null)
    {
        var sb = new StringBuilder();

        // Legenda não tem campo de metadado, então o cabeçalho entra como uma
        // "legenda zero" que aparece antes do primeiro segundo. Players a
        // exibem; quem abre o arquivo em editor lê direto.
        if (cabecalho is not null && Resumo(cabecalho) is { Length: > 0 } resumo)
        {
            sb.Append("0\n00:00:00,000 --> 00:00:00,001\n").Append(resumo).Append("\n\n");
        }

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
    public static string Vtt(ResultadoDaTranscricao r, bool comFalantes = true,
                             Cabecalho? cabecalho = null)
    {
        var sb = new StringBuilder("WEBVTT\n\n");

        // NOTE é o bloco de comentário do WebVTT: fica no arquivo, não vai para
        // a tela, e é exatamente onde metadado deve morar.
        if (cabecalho is not null && Resumo(cabecalho) is { Length: > 0 } resumo)
            sb.Append("NOTE\n").Append(resumo).Append("\n\n");
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
                            string titulo, bool comFalantes = true,
                            Cabecalho? cabecalho = null)
    {
        var cores = CoresPorFalante(r);

        var corpo = new StringBuilder();
        corpo.Append(Paragrafo(titulo, negrito: true, cor: null, tamanho: 32));

        if (cabecalho is not null)
        {
            foreach (var (rotulo, valor) in cabecalho.Linhas())
            {
                corpo.Append("<w:p><w:r><w:rPr><w:b/><w:sz w:val=\"20\"/></w:rPr>")
                     .Append("<w:t xml:space=\"preserve\">").Append(Escapar(rotulo))
                     .Append(": </w:t></w:r>");
                corpo.Append("<w:r><w:rPr><w:sz w:val=\"20\"/></w:rPr><w:t xml:space=\"preserve\">")
                     .Append(Escapar(valor)).Append("</w:t></w:r></w:p>");
            }
            corpo.Append(Paragrafo(new string('_', 60), negrito: false, cor: "AAAAAA", tamanho: 16));
        }

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

    /// <summary>
    /// A ata em Word, a partir do <c>ata.md</c> (ATA-1 do docs/BACKLOG.md).
    /// </summary>
    /// <remarks>
    /// Converte só o Markdown que o redator escreve: títulos <c>#</c> a
    /// <c>###</c>, listas com <c>-</c> ou <c>*</c> (e <c>- [ ]</c>, que vira ☐),
    /// listas numeradas, citação e <c>**negrito**</c> dentro da linha. Uma linha
    /// é um parágrafo — no <c>ata.md</c>, linhas seguidas são campos distintos
    /// ("**Data:** …" e "**Participantes:** …"), e juntá-las como o Markdown faz
    /// colaria os dois. O que não for reconhecido sai como texto, sem perder
    /// nada.
    /// </remarks>
    public static void DocxDaAta(string markdown, string destino)
    {
        var corpo = new StringBuilder();
        foreach (var bruta in markdown.Replace("\r\n", "\n").Split('\n'))
        {
            string linha = bruta.TrimEnd();
            if (linha.Trim().Length == 0) continue;

            var titulo = System.Text.RegularExpressions.Regex.Match(linha, @"^(#{1,6})\s+(.*)$");
            if (titulo.Success)
            {
                int nivel = titulo.Groups[1].Value.Length;
                int tamanho = nivel switch { 1 => 36, 2 => 28, 3 => 24, _ => 22 };
                corpo.Append("<w:p><w:pPr><w:spacing w:before=\"240\" w:after=\"80\"/></w:pPr>")
                     .Append(Trechos(titulo.Groups[2].Value, negritoBase: true, tamanho))
                     .Append("</w:p>");
                continue;
            }

            string recuo = "";
            string marcador = "";
            var item = System.Text.RegularExpressions.Regex.Match(
                linha, @"^(\s*)(?:([-*+])\s+(\[[ xX]\]\s+)?|(\d+[.)])\s+|>\s?)(.*)$");
            if (item.Success && linha.TrimStart().Length > 0 && !linha.TrimStart().StartsWith("**"))
            {
                int nivel = 1 + item.Groups[1].Value.Replace("\t", "  ").Length / 2;
                bool citacao = !item.Groups[2].Success && !item.Groups[4].Success;
                recuo = citacao
                    ? $"<w:pPr><w:ind w:left=\"{360 * nivel}\"/></w:pPr>"
                    : $"<w:pPr><w:ind w:left=\"{360 * nivel}\" w:hanging=\"280\"/></w:pPr>";
                marcador = item.Groups[3].Success
                    ? (item.Groups[3].Value.Trim() == "[ ]" ? "☐ " : "☑ ")
                    : item.Groups[4].Success ? item.Groups[4].Value + " "
                    : citacao ? "" : "• ";
                linha = item.Groups[5].Value;
            }

            corpo.Append("<w:p>").Append(recuo);
            if (marcador.Length > 0)
                corpo.Append("<w:r><w:t xml:space=\"preserve\">").Append(Escapar(marcador)).Append("</w:t></w:r>");
            corpo.Append(Trechos(linha, negritoBase: false, 22)).Append("</w:p>");
        }

        using var zip = new ZipArchive(File.Create(destino), ZipArchiveMode.Create);
        Escrever(zip, "[Content_Types].xml", TiposDeConteudo);
        Escrever(zip, "_rels/.rels", Relacoes);
        Escrever(zip, "word/document.xml",
            "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>"
            + "<w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\">"
            + "<w:body>" + corpo + "</w:body></w:document>");
    }

    /// <summary>Uma linha em runs, alternando negrito a cada <c>**</c>.</summary>
    private static string Trechos(string texto, bool negritoBase, int tamanho)
    {
        var sb = new StringBuilder();
        var partes = texto.Split("**");
        // Número par de partes é um ** sem par: ele volta para o texto, e o
        // negrito não vaza até o fim da linha.
        if (partes.Length % 2 == 0)
            partes = [.. partes[..^2], partes[^2] + "**" + partes[^1]];
        for (int i = 0; i < partes.Length; i++)
        {
            if (partes[i].Length == 0) continue;
            bool negrito = negritoBase || i % 2 == 1;
            sb.Append("<w:r><w:rPr>");
            if (negrito) sb.Append("<w:b/>");
            sb.Append("<w:sz w:val=\"").Append(tamanho).Append("\"/></w:rPr><w:t xml:space=\"preserve\">")
              .Append(Escapar(partes[i])).Append("</w:t></w:r>");
        }
        return sb.ToString();
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

    /// <summary>O cabeçalho em uma linha por campo, para formatos sem metadado.</summary>
    private static string Resumo(Cabecalho c)
    {
        var sb = new StringBuilder();
        if (c.Titulo is { Length: > 0 }) sb.AppendLine(c.Titulo);
        foreach (var (rotulo, valor) in c.Linhas()) sb.Append(rotulo).Append(": ").AppendLine(valor);
        return sb.ToString().TrimEnd();
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

    /// <summary>
    /// Nome de arquivo seguro, com a data da reunião na frente.
    /// </summary>
    /// <remarks>
    /// A data primeiro porque é o que ordena: numa pasta com um ano de
    /// exportações, "2026-08-11 BI-Weekly" cai no lugar certo sozinho, e três
    /// reuniões do mesmo projeto deixam de ter o mesmo nome.
    /// </remarks>
    public static string NomeDeArquivo(string titulo, string extensao, string? dataIso = null)
    {
        var limpo = new StringBuilder();
        foreach (char c in titulo)
            limpo.Append(Path.GetInvalidFileNameChars().Contains(c) ? '-' : c);

        string nome = limpo.ToString().Trim();
        if (nome.Length == 0) nome = "transcricao";

        string data = Cabecalho.DataCurta(dataIso) is { Length: > 0 } d ? d + " " : "";
        return $"{data}{nome}.{extensao}";
    }
}
