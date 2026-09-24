namespace MeetingApp.Nucleo;

/// <summary>
/// O que a lista de reuniões precisa saber da ata de uma gravação, sem abri-la.
/// </summary>
/// <remarks>
/// <para>
/// <b>Lido do <c>ata.md</c>, e não guardado à parte.</b> O arquivo é escrito
/// pelo <c>RedatorDeAta</c>, que garante o formato: a pendência sai
/// <c>- [ ] Ação — **Responsável** — prazo</c>, e o resumo sai sob
/// <c>## Resumo</c>. Um segundo arquivo com os mesmos números envelheceria no
/// dia em que a ata fosse refeita por outro caminho.
/// </para>
/// <para>
/// <b>Nunca lança.</b> A lista monta um resumo por gravação dentro de um
/// <c>try</c> (<c>Ponte.Listar</c>), e uma exceção aqui esconderia a gravação
/// inteira — um <c>ata.md</c> travado custaria a reunião na lista, e não só a
/// ata.
/// </para>
/// </remarks>
public sealed record EstadoDaAta(bool Existe, bool Velha, int Pendencias,
                                 IReadOnlyList<string> PrimeirasPendencias, string? Resumo)
{
    /// <summary>Quanto do resumo a lista mostra. O resto está na ata.</summary>
    public const int TamanhoDoResumo = 280;

    /// <summary>Quantas pendências o painel da lista mostra.</summary>
    public const int PendenciasNoPainel = 3;

    public static readonly EstadoDaAta Nenhuma = new(false, false, 0, [], null);

    public static EstadoDaAta Ler(string pasta)
    {
        string caminho = Path.Combine(pasta, "ata.md");
        if (!File.Exists(caminho)) return Nenhuma;

        string texto;
        try
        {
            texto = File.ReadAllText(caminho);
        }
        catch (Exception)
        {
            // Existe e não se lê agora. A lista diz que há ata; abrir mostra o
            // erro de verdade, na hora em que ele importa.
            return new(true, EstaVelha(pasta), 0, [], null);
        }

        return new(true, EstaVelha(pasta), ContarPendencias(texto),
                   PrimeirasAbertas(texto), ExtrairResumo(texto));
    }

    /// <summary>
    /// A ata é mais velha que a transcrição: alguém corrigiu o texto depois que
    /// ela foi escrita, e ela não viu a correção.
    /// </summary>
    public static bool EstaVelha(string pasta)
    {
        try
        {
            string ata = Path.Combine(pasta, "ata.md");
            string transcricao = Path.Combine(pasta, "transcricao.json");
            return File.Exists(ata) && File.Exists(transcricao)
                   && File.GetLastWriteTimeUtc(ata) < File.GetLastWriteTimeUtc(transcricao);
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>Os itens abertos: <c>- [ ]</c> no começo da linha.</summary>
    /// <remarks>
    /// A mesma regra de <c>atas.js</c> (<c>/^- \[ \]/gm</c>), para a lista e a
    /// tela de Atas não discordarem do número.
    /// </remarks>
    private static int ContarPendencias(string markdown) =>
        markdown.Split('\n').Count(l => l.StartsWith("- [ ]", StringComparison.Ordinal));

    private static List<string> PrimeirasAbertas(string markdown) =>
        [.. markdown.Split('\n')
            .Where(l => l.StartsWith("- [ ]", StringComparison.Ordinal))
            .Select(l => l[5..].Replace("**", "").Trim())
            .Where(l => l.Length > 0)
            .Take(PendenciasNoPainel)];

    /// <summary>O primeiro parágrafo sob <c>## Resumo</c>, numa linha só e cortado.</summary>
    private static string? ExtrairResumo(string markdown)
    {
        string[] linhas = markdown.Replace("\r", "").Split('\n');
        int inicio = Array.FindIndex(linhas,
            l => l.Trim().Equals("## Resumo", StringComparison.OrdinalIgnoreCase));
        if (inicio < 0) return null;

        var paragrafo = new List<string>();
        for (int i = inicio + 1; i < linhas.Length; i++)
        {
            string l = linhas[i].Trim();
            if (l.StartsWith('#')) break;
            if (l.Length == 0)
            {
                if (paragrafo.Count > 0) break;
                continue;
            }
            paragrafo.Add(l);
        }

        return paragrafo.Count == 0
            ? null
            : Corte.NumaPalavra(string.Join(" ", paragrafo), TamanhoDoResumo);
    }
}
