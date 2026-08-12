using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace MeetingApp.Nucleo;

/// <summary>Uma substituição aplicada, para a UI poder mostrar e desfazer.</summary>
public sealed record Troca(string De, string Para, int Posicao);

/// <summary>
/// Recupera termos do vocabulário grafados errado, por similaridade fonética.
/// </summary>
/// <remarks>
/// <para>
/// Porte de <c>tools/correcao_fonetica.py</c>, que continua sendo a referência.
/// Motivação medida (FASE0, resultado 5): "Dimi" sai como <b>"Jimmy" 10 vezes</b>
/// nos dois motores sem vocabulário injetado. São erros de <i>grafia de som
/// parecido</i> — o modelo ouviu certo e escreveu errado.
/// </para>
/// <para>
/// Por ser conserto a jusante, no núcleo, vale igual para faster-whisper e
/// whisper.cpp, e liberta o vocabulário do teto de 224 tokens do prompt.
/// </para>
/// <para>
/// <b>O casamento é conservador de propósito.</b> Um falso positivo aqui
/// reescreve uma palavra que a pessoa disse, e quem lê a ata não tem como
/// desconfiar — por isso exige código fonético igual <i>e</i> distância de
/// edição pequena <i>e</i> capitalização no meio da frase.
/// </para>
/// </remarks>
public static class CorrecaoFonetica
{
    /// <remarks>
    /// Ordem importa: dígrafos antes de letras isoladas. Uma regra de "remover
    /// vogal final" foi testada e <b>rejeitada</b> — fazia "fixo" e "Fixa"
    /// colidirem, e o corretor reescrevia "Do IP fixo" (português legítimo numa
    /// reunião de telecom) como "Fixa". Os casos verdadeiros não precisam dela:
    /// "Dimi", "Dimmy" e "Jimmy" já convergem para "jimi".
    /// </remarks>
    private static readonly (Regex Padrao, string Troca)[] Regras =
    [
        (new("ph"), "f"), (new("ch"), "x"), (new("lh"), "l"), (new("nh"), "n"),
        (new("qu"), "k"), (new("gu"), "g"), (new("ss"), "s"), (new("sc"), "s"),
        (new("ç"), "s"),
        (new("rr"), "r"), (new("mm"), "m"), (new("nn"), "n"), (new("tt"), "t"),
        (new("dd"), "d"),
        (new("[cq]"), "k"), (new("z"), "s"), (new("[jg](?=[ei])"), "j"),
        (new("y"), "i"), (new("w"), "v"), (new("h"), ""),
        // /d/ e /dʒ/ antes de i colapsam na fala carioca/paulista ("Dimi"~"Jimi"),
        // que é exatamente o caso "Dimi" → "Jimmy".
        (new("d(?=i)"), "j"),
    ];

    private static readonly Regex LetrasRepetidas = new(@"(.)\1+");

    /// <remarks>
    /// A regra <c>(r"g", "g")</c> do Python é identidade e foi omitida: ela não
    /// muda nada, e mantê-la só faria alguém procurar o que ela faz.
    /// </remarks>
    public static string Foneticar(string palavra)
    {
        string p = Desacentuar(palavra);
        foreach (var (padrao, troca) in Regras) p = padrao.Replace(p, troca);
        return LetrasRepetidas.Replace(p, "$1");
    }

    public static string Desacentuar(string s)
    {
        var sb = new StringBuilder();
        foreach (char c in s.ToLowerInvariant().Normalize(NormalizationForm.FormKD))
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                sb.Append(c);
        return sb.ToString();
    }

    /// <summary>
    /// Teto de distância de edição na forma escrita.
    /// </summary>
    /// <remarks>
    /// <b>Escalar pelo tamanho do termo foi testado e rejeitado.</b> Parecia que
    /// distância 3 era folgada para nome de 4 letras; medido, derrubava o caso
    /// central — <c>Levenshtein("Jimmy", "Dimi") == 3</c> num termo de 4 letras,
    /// e as 10 correções desapareciam.
    /// <para>
    /// A lição: quando o código fonético já é idêntico, a distância de superfície
    /// é um guarda ruim — grafias de som igual divergem muito na escrita, e é
    /// isso que se quer capturar. Quem filtra de verdade é a exigência de
    /// capitalização.
    /// </para>
    /// </remarks>
    public static int DistanciaMaxima(string termo) =>
        Desacentuar(termo).Length <= 10 ? 3 : 4;

    public static int Levenshtein(string a, string b)
    {
        if (a.Length < b.Length) (a, b) = (b, a);

        var anterior = new int[b.Length + 1];
        var atual = new int[b.Length + 1];
        for (int j = 0; j <= b.Length; j++) anterior[j] = j;

        for (int i = 1; i <= a.Length; i++)
        {
            atual[0] = i;
            for (int j = 1; j <= b.Length; j++)
                atual[j] = Math.Min(Math.Min(anterior[j] + 1, atual[j - 1] + 1),
                                    anterior[j - 1] + (a[i - 1] != b[j - 1] ? 1 : 0));
            (anterior, atual) = (atual, anterior);
        }
        return anterior[b.Length];
    }

    /// <summary>Decide se <paramref name="candidato"/> é grafia errada de <paramref name="termo"/>.</summary>
    public static bool Casa(string candidato, string termo, int? distanciaMaxima = null)
    {
        if (string.Equals(candidato, termo, StringComparison.OrdinalIgnoreCase))
            return false;                                    // já está certo
        if (Foneticar(candidato) != Foneticar(termo))
            return false;                                    // não soam igual

        int limite = distanciaMaxima ?? DistanciaMaxima(termo);
        return Levenshtein(Desacentuar(candidato), Desacentuar(termo)) <= limite;
    }

    private static readonly Regex Palavra = new(@"\b[A-Za-zÀ-ÿ][A-Za-zÀ-ÿ']{2,}\b");

    /// <summary>
    /// Substitui grafias erradas pelos termos do vocabulário.
    /// </summary>
    /// <param name="soCapitalizadas">
    /// O guarda que faltava na primeira versão do protótipo: candidatar qualquer
    /// palavra foi a porta por onde "fixo" → "Fixa" quase passou. Exigir
    /// maiúscula no meio da frase aproveita um sinal que já está no texto — o
    /// Whisper capitaliza nome próprio.
    /// </param>
    /// <returns>O texto corrigido e as trocas, com posição, para a UI marcar.</returns>
    public static (string Texto, List<Troca> Trocas) Corrigir(
        string texto, IReadOnlyList<string> termos, int? distanciaMaxima = null,
        IReadOnlySet<string>? excecoes = null, bool soCapitalizadas = true)
    {
        var trocas = new List<Troca>();

        string resultado = Palavra.Replace(texto, m =>
        {
            string pal = m.Value;
            if (excecoes is not null && excecoes.Contains(pal.ToLowerInvariant()))
                return pal;

            // Início de frase também é maiúsculo, então maiúscula só vale como
            // sinal quando há texto antes. Sem isto, "Fixo" abrindo frase seria
            // candidato.
            string anterior = texto[..m.Index].TrimEnd();
            bool meioDeFrase = anterior.Length > 0 && !".!?".Contains(anterior[^1]);
            if (soCapitalizadas && !(char.IsUpper(pal[0]) && meioDeFrase))
                return pal;

            foreach (string t in termos)
            {
                if (Casa(pal, t, distanciaMaxima))
                {
                    trocas.Add(new Troca(pal, t, m.Index));
                    return t;
                }
            }
            return pal;
        });

        return (resultado, trocas);
    }
}
