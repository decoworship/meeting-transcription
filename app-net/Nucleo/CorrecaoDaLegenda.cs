using System.Text.RegularExpressions;

namespace MeetingApp.Nucleo;

/// <summary>
/// A correção de termos da passada final, aplicada à legenda ao vivo.
/// </summary>
/// <remarks>
/// <para>
/// <b>É o <c>VIVO-3</c>.</b> O Nemotron não tem gancho de vocabulário na
/// entrada, então o que dá para fazer é corrigir depois — e roda no fim da
/// reunião, junto da separação de falantes. Medido em 23/09/2026: "Wifi" sai
/// de 2 para 11 ocorrências (a passada final tem 14).
/// </para>
/// <para>
/// <b>Corrige por fala e aplica por trecho.</b> O trecho tem 1,1 s — a
/// granularidade do commit do motor —, e três das quatro propostas medidas
/// eram de duas palavras. Corrigir trecho a trecho perderia justamente essas:
/// "Wi" num, "Fi" no seguinte. A fala (trechos seguidos do mesmo falante) é
/// onde a troca se enxerga; o trecho é onde ela se grava.
/// </para>
/// <para>
/// <b>Funde só o que a troca atravessa.</b> Os trechos carregam o tempo que a
/// diarização usa; fundir tudo apagaria esse tempo. Dois trechos viram um
/// quando, e só quando, uma troca cobre os dois.
/// </para>
/// </remarks>
public static class CorrecaoDaLegenda
{
    public sealed record Resultado(List<TrechoDaLegenda> Trechos, int Trocas, int Fundidos);

    public static Resultado Corrigir(
        IReadOnlyList<TrechoDaLegenda> trechos, string? vocabulario,
        IReadOnlyList<string> entidades)
    {
        var falas = Falas(trechos);
        var textos = falas.Select(f => Juntar(f)).ToList();

        var foneticos = CorrecaoDeTermos.Fonetica(textos, vocabulario);
        var propostas = CorrecaoDeTermos.Propostas(
            [.. foneticos.Select(f => f.Texto)], entidades);

        var saida = new List<TrechoDaLegenda>(trechos.Count);
        int trocas = 0, fundidos = 0;
        for (int i = 0; i < falas.Count; i++)
        {
            var alvos = foneticos[i].Trocas.Select(t => t.De)
                .Concat(propostas.Select(p => p.De))
                .Distinct().ToList();
            var fundida = Fundir(falas[i], alvos, ref fundidos);

            foreach (var t in fundida)
            {
                var (texto, dela) = CorrecaoDeTermos.Fonetica([t.Texto], vocabulario)[0];
                var (final, daRegra) = RevisaoDeTermos.Aplicar(texto, propostas);
                var todas = dela.Concat(daRegra).ToList();
                if (todas.Count == 0) { saida.Add(t); continue; }

                trocas += todas.Count;
                saida.Add(new TrechoDaLegenda
                {
                    InicioMs = t.InicioMs, FimMs = t.FimMs, Dono = t.Dono,
                    Falante = t.Falante, Colado = t.Colado, Texto = final,
                    Swaps = [.. (t.Swaps ?? []),
                             .. todas.Select(x => new TrocaFeita { De = x.De, Para = x.Para })],
                });
            }
        }
        return new Resultado(saida, trocas, fundidos);
    }

    /// <summary>Trechos seguidos do mesmo falante e do mesmo dono.</summary>
    private static List<List<TrechoDaLegenda>> Falas(IReadOnlyList<TrechoDaLegenda> trechos)
    {
        var falas = new List<List<TrechoDaLegenda>>();
        foreach (var t in trechos)
        {
            if (falas.Count > 0 && falas[^1][^1].Falante == t.Falante
                                && falas[^1][^1].Dono == t.Dono)
                falas[^1].Add(t);
            else
                falas.Add([t]);
        }
        return falas;
    }

    /// <summary>A regra do <c>Ponte.FalasDaLegenda</c>: sem espaço quando a palavra continua.</summary>
    private static string Juntar(IEnumerable<TrechoDaLegenda> fala)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var t in fala)
        {
            if (sb.Length > 0 && !t.Colado) sb.Append(' ');
            sb.Append(t.Texto);
        }
        return sb.ToString();
    }

    /// <summary>
    /// Os trechos da fala, com os que uma ocorrência de <paramref name="alvos"/>
    /// atravessa fundidos num só.
    /// </summary>
    private static List<TrechoDaLegenda> Fundir(
        List<TrechoDaLegenda> fala, IReadOnlyList<string> alvos, ref int fundidos)
    {
        if (fala.Count < 2 || alvos.Count == 0) return fala;

        // Onde cada trecho começa no texto da fala.
        var inicios = new int[fala.Count];
        int pos = 0;
        for (int i = 0; i < fala.Count; i++)
        {
            if (i > 0 && !fala[i].Colado) pos++;
            inicios[i] = pos;
            pos += fala[i].Texto.Length;
        }
        int TrechoDe(int c) => Array.FindLastIndex(inicios, x => x <= c);

        // grupo[i] = índice do primeiro trecho do bloco fundido a que i pertence
        var grupo = Enumerable.Range(0, fala.Count).ToArray();
        string texto = Juntar(fala);
        foreach (var alvo in alvos)
        {
            var re = new Regex($@"(?<!\w){Regex.Escape(alvo)}(?!\w)", RegexOptions.IgnoreCase);
            foreach (Match m in re.Matches(texto))
            {
                int a = TrechoDe(m.Index), b = TrechoDe(m.Index + m.Length - 1);
                for (int k = a + 1; k <= b; k++) grupo[k] = grupo[a];
            }
        }

        var saida = new List<TrechoDaLegenda>();
        for (int i = 0; i < fala.Count; i++)
        {
            // grupo[k] sempre aponta para o primeiro de um bloco contíguo, então
            // "diferente de si" é exatamente "continua o trecho anterior".
            if (grupo[i] != i)
            {
                var antes = saida[^1];
                saida[^1] = new TrechoDaLegenda
                {
                    InicioMs = antes.InicioMs, FimMs = fala[i].FimMs, Dono = antes.Dono,
                    Falante = antes.Falante, Colado = antes.Colado,
                    Texto = antes.Texto + (fala[i].Colado ? "" : " ") + fala[i].Texto,
                    Swaps = antes.Swaps is null && fala[i].Swaps is null ? null
                        : [.. antes.Swaps ?? [], .. fala[i].Swaps ?? []],
                };
                fundidos++;
            }
            else saida.Add(fala[i]);
        }
        return saida;
    }
}
