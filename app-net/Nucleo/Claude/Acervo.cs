using System.Globalization;
using System.Text;
using System.Text.Json;

namespace MeetingApp.Nucleo.Claude;

/// <summary>Uma reunião como o Claude a vê na lista: o bastante para escolher qual ler.</summary>
public sealed record ReuniaoNoAcervo(
    string Id, string Pasta, string? Titulo, DateTime Quando, double DuracaoS,
    string? Cliente, string? Projeto, IReadOnlyList<string> Convidados,
    bool Transcrita, bool TemAta, bool AtaVelha, bool TemNotas)
{
    /// <summary>"22/10/2026 09:00 · Semanal do Portal" — como a reunião é chamada numa linha.</summary>
    public string Rotulo => $"{Quando:dd/MM/yyyy HH:mm} · {Titulo ?? Id}";
}

/// <summary>Um cliente e os projetos dele, com o período em que houve reunião.</summary>
public sealed record ClienteNoAcervo(
    string Nome, int Reunioes, DateTime Primeira, DateTime Ultima,
    IReadOnlyList<(string Projeto, int Reunioes)> Projetos);

/// <summary>Uma fala: os segmentos seguidos de um mesmo falante, juntos.</summary>
public sealed record Fala(double Inicio, string? Falante, string Texto);

/// <summary>Um lugar onde a busca achou o que se pediu.</summary>
public sealed record Achado(ReuniaoNoAcervo Reuniao, string Fonte, double? Instante,
                            string? Falante, string Trecho);

/// <summary>O que filtra a lista: tudo opcional, e tudo combinado com "e".</summary>
public sealed record FiltroDoAcervo(
    string? Cliente = null, string? Projeto = null, DateOnly? Desde = null, DateOnly? Ate = null);

/// <summary>
/// As reuniões gravadas, lidas do disco para quem está do outro lado do MCP.
/// </summary>
/// <remarks>
/// <para>
/// <b>Só lê.</b> O acervo é a coisa mais valiosa do app, e um caminho novo até
/// ele que também escreve é um caminho novo para estragá-lo. Ver
/// docs/INTEGRACAO-CLAUDE.md.
/// </para>
/// <para>
/// <b>A ata é o índice, a transcrição é a prova.</b> As atas de um cliente com
/// cinquenta reuniões cabem numa conversa; as transcrições não — medido em
/// 22/10/2026, 224 KB contra 4,8 MB. Por isso a transcrição sai como texto
/// corrido por falante, e por faixa de tempo, e a busca devolve trechos.
/// </para>
/// <para>
/// Lê os mesmos arquivos e pelas mesmas funções que a tela: o vínculo pelo
/// <see cref="DadosDaReuniao.Ler"/> (com o recuo para a transcrição nas
/// gravações antigas), as notas sem as imagens, o estado da ata pelo
/// <see cref="EstadoDaAta"/>. Uma segunda leitura dos formatos é como uma
/// delas fica para trás.
/// </para>
/// </remarks>
public sealed class Acervo(string pastaDasGravacoes)
{
    public string Raiz { get; } = pastaDasGravacoes;

    /// <summary>
    /// Uma fala não passa disto, para o instante dela continuar dizendo onde ela está.
    /// </summary>
    /// <remarks>
    /// Quem fala sozinho por dez minutos viraria uma linha só com um instante
    /// só, e "aos 12 min" não ajudaria ninguém a achar a frase no áudio.
    /// </remarks>
    public const double FalaMaximaS = 60;

    /// <summary>Quanto de texto um achado mostra em volta do que foi buscado.</summary>
    public const int TamanhoDoTrecho = 360;

    /// <summary>As reuniões, da mais antiga para a mais nova.</summary>
    /// <remarks>
    /// Em ordem de data, e não ao contrário como na lista do app: quem lê é um
    /// modelo montando uma história, e a história anda para a frente.
    /// </remarks>
    public List<ReuniaoNoAcervo> Listar(FiltroDoAcervo? filtro = null)
    {
        if (!Directory.Exists(Raiz)) return [];

        var lista = new List<ReuniaoNoAcervo>();
        foreach (string pasta in Directory.EnumerateDirectories(Raiz))
        {
            if (!File.Exists(Path.Combine(pasta, "meta.json"))) continue;
            try
            {
                var r = LerReuniao(pasta);
                if (filtro is null || Passa(r, filtro)) lista.Add(r);
            }
            catch (Exception)
            {
                // Uma gravação ilegível não pode esconder as outras — a mesma
                // regra da lista do app (Ponte.Listar).
            }
        }
        return [.. lista.OrderBy(r => r.Quando).ThenBy(r => r.Id, StringComparer.Ordinal)];
    }

    /// <summary>Os clientes com reunião, do que tem mais para o que tem menos.</summary>
    public List<ClienteNoAcervo> Clientes()
    {
        return [.. Listar()
            .Where(r => r.Cliente is { Length: > 0 })
            .GroupBy(r => r.Cliente!, StringComparer.OrdinalIgnoreCase)
            .Select(g => new ClienteNoAcervo(
                g.First().Cliente!, g.Count(), g.Min(r => r.Quando), g.Max(r => r.Quando),
                [.. g.Where(r => r.Projeto is { Length: > 0 })
                     .GroupBy(r => r.Projeto!, StringComparer.OrdinalIgnoreCase)
                     .Select(p => (p.First().Projeto!, p.Count()))
                     .OrderByDescending(p => p.Item2)]))
            .OrderByDescending(c => c.Reunioes)
            .ThenBy(c => c.Nome, StringComparer.CurrentCultureIgnoreCase)];
    }

    /// <summary>
    /// A reunião pelo id — o nome da pasta —, ou nulo.
    /// </summary>
    /// <remarks>
    /// O id vem de fora, escrito por um modelo, e vira caminho no disco. Só
    /// vale o nome de uma pasta que está direto dentro do acervo: barra,
    /// <c>..</c> e caminho absoluto não chegam nem a ser procurados.
    /// </remarks>
    public ReuniaoNoAcervo? Achar(string? id)
    {
        if (id is not { Length: > 0 }) return null;
        id = id.Trim();
        if (id is "." or ".." || id.IndexOfAny(['/', '\\', ':']) >= 0
            || id.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            return null;

        string pasta = Path.Combine(Raiz, id);
        if (!File.Exists(Path.Combine(pasta, "meta.json"))) return null;
        try { return LerReuniao(pasta); }
        catch (Exception) { return null; }
    }

    /// <summary>
    /// A ata como ela está no disco, com as observações e as notas de quem gravou.
    /// </summary>
    /// <returns>Nulo quando a reunião não tem ata.</returns>
    public static string? TextoDaAta(ReuniaoNoAcervo r)
    {
        string caminho = Path.Combine(r.Pasta, "ata.md");
        if (!File.Exists(caminho)) return null;

        var sb = new StringBuilder();
        if (r.AtaVelha)
            sb.AppendLine("> A transcrição foi corrigida depois que esta ata foi escrita: "
                          + "confira na transcrição o que parecer errado.").AppendLine();
        sb.AppendLine(File.ReadAllText(caminho).TrimEnd());

        // No formato por tema o recado do verificador mora fora do ata.md; no
        // clássico ele está dentro, e o arquivo à parte nem existe.
        if (Atas.GeracaoDeAta.LerObservacoes(r.Pasta) is { Length: > 0 } obs)
            sb.AppendLine().AppendLine(obs.TrimEnd());

        AnexarNotas(sb, r);
        return sb.ToString();
    }

    /// <summary>As notas sem as imagens, sob um título que diz de quem elas são.</summary>
    public static void AnexarNotas(StringBuilder sb, ReuniaoNoAcervo r)
    {
        string notas = Notas.SemImagens(Notas.Ler(r.Pasta)).Trim();
        if (notas.Length == 0) return;
        sb.AppendLine().AppendLine("## Notas de quem gravou").AppendLine().AppendLine(notas);
    }

    /// <summary>A transcrição lida do disco, ou nula.</summary>
    public static ResultadoDaTranscricao? Transcricao(ReuniaoNoAcervo r)
    {
        string caminho = Path.Combine(r.Pasta, "transcricao.json");
        if (!File.Exists(caminho)) return null;
        try { return ResultadoDaTranscricao.DeJson(File.ReadAllText(caminho)); }
        catch (Exception) { return null; }
    }

    /// <summary>
    /// Os segmentos juntados por falante.
    /// </summary>
    /// <remarks>
    /// O ASR corta a fala em pedaços de um ou dois segundos — 362 numa reunião
    /// de 20 minutos — e uma linha por pedaço gasta mais em marca de tempo e
    /// nome do que em texto.
    /// </remarks>
    public static List<Fala> Falas(IEnumerable<SegmentoFinal> segmentos)
    {
        var falas = new List<Fala>();
        string? quem = null;
        double inicio = 0;
        var texto = new StringBuilder();

        void Fechar()
        {
            if (texto.Length > 0) falas.Add(new Fala(inicio, quem, texto.ToString()));
            texto.Clear();
        }

        foreach (var s in segmentos)
        {
            string t = s.Text.Trim();
            if (t.Length == 0) continue;

            string? falante = s.Speaker is { Length: > 0 } f ? f : null;
            if (texto.Length == 0 || falante != quem || s.Start - inicio >= FalaMaximaS)
            {
                Fechar();
                quem = falante;
                inicio = s.Start;
            }
            if (texto.Length > 0) texto.Append(' ');
            texto.Append(t);
        }
        Fechar();
        return falas;
    }

    /// <summary>
    /// Até onde a transcrição sai de uma vez, em caracteres.
    /// </summary>
    /// <remarks>
    /// Uma reunião de duas horas dá uns 100 mil caracteres, e cabe. O teto é
    /// para a de quatro horas: ela sai até aqui, com o instante de onde
    /// continuar, em vez de lotar a conversa de quem só queria o começo.
    /// </remarks>
    public const int TranscricaoMaxima = 150_000;

    /// <summary>
    /// A transcrição como texto corrido, de <paramref name="deS"/> até <paramref name="ateS"/>.
    /// </summary>
    /// <returns>Nulo quando a reunião não foi transcrita.</returns>
    public static string? TextoDaTranscricao(ReuniaoNoAcervo r, double? deS = null,
                                             double? ateS = null, int limite = TranscricaoMaxima)
    {
        var dados = Transcricao(r);
        if (dados is null) return null;

        var falas = Falas(dados.Segments)
            .Where(f => (deS is null || f.Inicio >= deS) && (ateS is null || f.Inicio < ateS))
            .ToList();

        var sb = new StringBuilder(Cabecalho(r, dados)).AppendLine();
        if (deS is not null || ateS is not null)
            sb.Append("Trecho de ").Append(Relogio(deS ?? 0)).Append(" a ")
              .Append(ateS is { } a ? Relogio(a) : "fim").AppendLine(".").AppendLine();
        if (falas.Count == 0) sb.AppendLine("Nada foi dito nesse intervalo.");

        foreach (var f in falas)
        {
            string linha = Linha(f);
            if (sb.Length + linha.Length > limite)
            {
                sb.AppendLine().Append("[a transcrição continua: peça de ")
                  .Append(Relogio(f.Inicio)).AppendLine(" em diante]");
                break;
            }
            sb.AppendLine(linha);
        }
        return sb.ToString();
    }

    /// <summary>Título, quando, quanto durou, de quem é e quem falou — em três linhas.</summary>
    public static string Cabecalho(ReuniaoNoAcervo r, ResultadoDaTranscricao? dados = null)
    {
        var sb = new StringBuilder();
        sb.Append("# ").AppendLine(r.Titulo ?? r.Id);
        sb.Append("id: ").Append(r.Id).Append(" · ").Append(r.Quando.ToString("dd/MM/yyyy HH:mm"));
        if (r.DuracaoS > 0) sb.Append(" · ").Append(MeetingApp.Nucleo.Cabecalho.Duracao(r.DuracaoS));
        if (Vinculo(r) is { } v) sb.Append(" · ").Append(v);
        sb.AppendLine();
        if (dados is not null && Falantes(dados) is { Count: > 0 } quem)
            sb.Append("Falaram: ").AppendLine(string.Join(", ", quem));
        else if (r.Convidados.Count > 0)
            sb.Append("Convidados: ").AppendLine(string.Join(", ", r.Convidados));
        return sb.ToString();
    }

    /// <summary>"Cliente › Projeto", só o que existir, ou nulo.</summary>
    public static string? Vinculo(ReuniaoNoAcervo r) =>
        (r.Cliente, r.Projeto) switch
        {
            ({ Length: > 0 } c, { Length: > 0 } p) => $"{c} › {p}",
            ({ Length: > 0 } c, _) => c,
            (_, { Length: > 0 } p) => p,
            _ => null,
        };

    /// <summary><c>[12:03] Carla Nunes: texto</c> — o formato do TXT exportado.</summary>
    public static string Linha(Fala f) =>
        f.Falante is { } quem ? $"[{Relogio(f.Inicio)}] {quem}: {f.Texto}"
                              : $"[{Relogio(f.Inicio)}] {f.Texto}";

    /// <summary>Hora só quando existe, como no <see cref="Exportacao.Txt"/>.</summary>
    public static string Relogio(double segundos)
    {
        var t = TimeSpan.FromSeconds(Math.Max(0, segundos));
        return t.TotalHours >= 1
            ? $"{(int)t.TotalHours:00}:{t.Minutes:00}:{t.Seconds:00}"
            : $"{t.Minutes:00}:{t.Seconds:00}";
    }

    /// <summary>Quem falou, na ordem em que apareceu.</summary>
    public static List<string> Falantes(ResultadoDaTranscricao dados) =>
        [.. dados.Segments
             .Where(s => s.Speaker is { Length: > 0 } && s.Speaker != "Unknown")
             .Select(s => s.Speaker!)
             .Distinct()];

    /// <summary>
    /// Procura nas atas, nas notas e nas transcrições.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Todas as palavras, na mesma fala ou na mesma linha</b>, sem acento e
    /// sem caixa. A frase exata perde o que um modelo costuma mandar ("prazo
    /// faturamento B2B"), e qualquer palavra devolveria a reunião inteira.
    /// </para>
    /// <para>
    /// Da reunião mais nova para a mais antiga, e com teto: quem busca quer
    /// primeiro o estado de agora, e o resto vem contado para a pessoa saber
    /// que precisa estreitar.
    /// </para>
    /// </remarks>
    public (List<Achado> Achados, int Total) Buscar(string termo, FiltroDoAcervo? filtro = null,
                                                   string? soNaReuniao = null, int limite = 30)
    {
        string[] palavras = Normalizar(termo).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (palavras.Length == 0) return ([], 0);

        IEnumerable<ReuniaoNoAcervo> onde = soNaReuniao is { Length: > 0 }
            ? Achar(soNaReuniao) is { } uma ? [uma] : []
            : Listar(filtro).AsEnumerable().Reverse();

        var achados = new List<Achado>();
        int total = 0;
        void Anotar(Achado a)
        {
            total++;
            if (achados.Count < limite) achados.Add(a);
        }

        foreach (var r in onde)
        {
            foreach (var (fonte, linha) in LinhasDeTexto(r))
                if (Contem(linha, palavras, out int pos))
                    Anotar(new Achado(r, fonte, null, null, Recorte(linha, pos)));

            if (!r.Transcrita || Transcricao(r) is not { } dados) continue;
            foreach (var f in Falas(dados.Segments))
                if (Contem(f.Texto, palavras, out int pos))
                    Anotar(new Achado(r, "transcrição", f.Inicio, f.Falante, Recorte(f.Texto, pos)));
        }
        return (achados, total);
    }

    /// <summary>As linhas da ata e das notas que têm texto, com a fonte de cada uma.</summary>
    /// <remarks>
    /// A ata entra da primeira seção em diante. O que vem antes é o cabeçalho
    /// — data, cliente, projeto, convidados —, e ele casaria com toda busca
    /// pelo nome do projeto, em toda reunião do projeto.
    /// </remarks>
    private static IEnumerable<(string Fonte, string Linha)> LinhasDeTexto(ReuniaoNoAcervo r)
    {
        if (r.TemAta)
        {
            string ata;
            try { ata = File.ReadAllText(Path.Combine(r.Pasta, "ata.md")); }
            catch (Exception) { ata = ""; }
            bool corpo = false;
            foreach (string l in ata.Split('\n'))
            {
                string t = l.Trim();
                if (t.StartsWith("## ", StringComparison.Ordinal)) corpo = true;
                if (corpo && t.Length > 0 && !t.StartsWith('#')) yield return ("ata", t);
            }
        }
        foreach (string l in Notas.SemImagens(Notas.Ler(r.Pasta)).Split('\n'))
            if (l.Trim() is { Length: > 0 } t) yield return ("notas", t);
    }

    private static bool Contem(string texto, string[] palavras, out int primeira)
    {
        string n = Normalizar(texto);
        primeira = -1;
        foreach (string p in palavras)
        {
            int i = n.IndexOf(p, StringComparison.Ordinal);
            if (i < 0) return false;
            if (primeira < 0 || i < primeira) primeira = i;
        }
        return true;
    }

    /// <summary>Um pedaço de <see cref="TamanhoDoTrecho"/> em volta de <paramref name="pos"/>.</summary>
    private static string Recorte(string texto, int pos)
    {
        if (texto.Length <= TamanhoDoTrecho) return texto;
        int inicio = Math.Clamp(pos - TamanhoDoTrecho / 3, 0, texto.Length - TamanhoDoTrecho);
        int fim = inicio + TamanhoDoTrecho;

        // Na palavra inteira: cortar no meio dela inventa uma palavra que ninguém disse.
        if (inicio > 0 && texto.IndexOf(' ', inicio) is int a and >= 0 && a < pos) inicio = a + 1;
        if (fim < texto.Length && texto.LastIndexOf(' ', fim) is int b && b > pos) fim = b;

        return (inicio > 0 ? "…" : "") + texto[inicio..fim] + (fim < texto.Length ? "…" : "");
    }

    /// <summary>
    /// Sem acento e em minúscula, <b>um caractere por caractere</b>.
    /// </summary>
    /// <remarks>
    /// O mesmo comprimento do texto original é o que deixa a posição achada no
    /// texto normalizado servir para recortar o original.
    /// </remarks>
    public static string Normalizar(string texto)
    {
        var sb = new StringBuilder(texto.Length);
        foreach (char c in texto)
        {
            string d = c.ToString().Normalize(NormalizationForm.FormD);
            char letra = d.Length > 0 && CharUnicodeInfo.GetUnicodeCategory(d[0])
                         != UnicodeCategory.NonSpacingMark ? d[0] : c;
            sb.Append(char.IsWhiteSpace(letra) ? ' ' : char.ToLowerInvariant(letra));
        }
        return sb.ToString();
    }

    /// <summary>
    /// Cliente e projeto casam por pedaço, sem acento: "coca" acha as três
    /// unidades de um cliente, e é o Claude quem decide se junta ou pergunta.
    /// </summary>
    private static bool Passa(ReuniaoNoAcervo r, FiltroDoAcervo f)
    {
        static bool Casa(string? valor, string? procurado) =>
            procurado is not { Length: > 0 }
            || (valor is { Length: > 0 } && Normalizar(valor).Contains(Normalizar(procurado.Trim())));

        var dia = DateOnly.FromDateTime(r.Quando);
        return Casa(r.Cliente, f.Cliente) && Casa(r.Projeto, f.Projeto)
               && (f.Desde is null || dia >= f.Desde) && (f.Ate is null || dia <= f.Ate);
    }

    private static ReuniaoNoAcervo LerReuniao(string pasta)
    {
        string id = Path.GetFileName(pasta);
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(pasta, "meta.json")));
        var raiz = doc.RootElement;

        double duracao = raiz.TryGetProperty("duration_s", out var d) && d.ValueKind == JsonValueKind.Number
            ? d.GetDouble() : 0;

        string? titulo = null, inicio = null;
        var convidados = new List<string>();
        if (raiz.TryGetProperty("meeting", out var m) && m.ValueKind == JsonValueKind.Object)
        {
            titulo = Texto(m, "title");
            inicio = Texto(m, "start");
            if (m.TryGetProperty("attendees", out var a) && a.ValueKind == JsonValueKind.Array)
                foreach (var n in a.EnumerateArray())
                    if (n.ValueKind == JsonValueKind.String && n.GetString() is { Length: > 0 } nome)
                        convidados.Add(nome);
        }

        // Só se a ata existe e se é velha, e não o EstadoDaAta inteiro: ele lê
        // o ata.md para contar pendências, e a lista passa por todas as
        // reuniões a cada pedido.
        var vinculo = DadosDaReuniao.Ler(pasta);
        return new ReuniaoNoAcervo(
            id, pasta, titulo, Quando(id, inicio, Texto(raiz, "recorded_at")), duracao,
            vinculo.Cliente, vinculo.Projeto, convidados,
            File.Exists(Path.Combine(pasta, "transcricao.json")),
            File.Exists(Path.Combine(pasta, "ata.md")), EstadoDaAta.EstaVelha(pasta),
            Notas.Existem(pasta));
    }

    /// <summary>
    /// A hora da reunião no relógio de quem gravou.
    /// </summary>
    /// <remarks>
    /// O início da agenda primeiro, porque é a hora que as pessoas usam para
    /// falar da reunião ("a das 9h"); depois o nome da pasta, que o gravador
    /// escreve na hora local; o <c>recorded_at</c> é UTC e fica por último.
    /// </remarks>
    private static DateTime Quando(string id, string? inicioDaAgenda, string? gravadoEm)
    {
        if (DateTimeOffset.TryParse(inicioDaAgenda, CultureInfo.InvariantCulture,
                                    DateTimeStyles.None, out var agenda))
            return agenda.DateTime;
        if (DateTime.TryParseExact(id, "yyyy-MM-dd_HH-mm-ss", CultureInfo.InvariantCulture,
                                   DateTimeStyles.None, out var daPasta))
            return daPasta;
        if (DateTimeOffset.TryParse(gravadoEm, CultureInfo.InvariantCulture,
                                    DateTimeStyles.None, out var gravado))
            return gravado.LocalDateTime;
        return DateTime.MinValue;
    }

    private static string? Texto(JsonElement e, string campo) =>
        e.TryGetProperty(campo, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}
