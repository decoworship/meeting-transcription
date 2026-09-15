using System.Text.RegularExpressions;
using Xunit;

namespace MeetingApp.Tests;

/// <summary>
/// A cor do falante está escrita em dois arquivos que não se falam.
/// </summary>
/// <remarks>
/// O <c>web/pecas.js</c> tem a <c>PALETA</c>, que é uma lista de
/// <c>var(--falante-N)</c>; o <c>web/app.css</c> tem o valor de cada token, três
/// vezes — no tema claro, no <c>[data-tema="escuro"]</c> e no
/// <c>[data-tema="auto"]</c> de dentro do <c>@media</c>.
/// <para>
/// Nada avisa quando os dois discordam: um <c>var()</c> sem definição não é
/// erro de CSS, é valor inválido — a declaração inteira é descartada e o nome do
/// falante sai na cor herdada. Fica igual ao texto da transcrição, que é
/// exatamente a coisa de que a cor existe para separar (docs/FASE7-FRONTEND.md
/// §12.3), e não aparece em teste nenhum nem no console.
/// </para>
/// <para>
/// Esquecer <b>um</b> dos blocos é pior ainda: o app fica certo no tema em que
/// quem mexeu estava e errado no outro. Daí conferir os três, e não só a
/// existência do token em algum lugar do arquivo.
/// </para>
/// <para>
/// Mesmo raciocínio do <see cref="MarcaTests"/>: dois arquivos que só um humano
/// mantinha sincronizados passam a ter quem confira.
/// </para>
/// </remarks>
public sealed class PaletaDeFalanteTests
{
    [Fact]
    public void TodoTokenDaPaletaTemValorNosTresBlocosDoCss()
    {
        string? js = Achar(Path.Combine("app-net", "App", "web", "pecas.js"));
        string? css = Achar(Path.Combine("app-net", "App", "web", "app.css"));
        // Fora do repositório não há o que comparar. Não é falha: a suíte é
        // net8.0 portátil e roda também de um diretório publicado.
        if (js is null || css is null) return;

        string[] tokens = TokensDaPaleta(File.ReadAllText(js));

        Assert.True(tokens.Length > 0,
            $"{js} perdeu a PALETA em var(--falante-N) — a cor do falante voltou a ser "
            + "hexadecimal fixo, e hexadecimal fixo não tem tema escuro.");

        string folha = File.ReadAllText(css);

        // Os três blocos, na forma que o assets/ds/colors_and_type.css usa.
        // O ":root" aparece mais de uma vez no app.css (a altura da barra mora
        // noutro), por isso a busca é por bloco que contenha o primeiro token, e
        // não pelo primeiro bloco com aquele seletor.
        foreach (string seletor in new[] { ":root", "[data-tema=\"escuro\"]", "[data-tema=\"auto\"]" })
        {
            string? bloco = BlocoQueDefine(folha, seletor, tokens[0]);
            Assert.True(bloco is not null,
                $"{css} não tem um bloco {seletor} definindo {tokens[0]}. "
                + "Sem os três, a cor do falante some num dos temas em silêncio.");

            foreach (string token in tokens)
                Assert.True(Regex.IsMatch(bloco!, $@"{Regex.Escape(token)}\s*:\s*\S"),
                    $"{css}: o bloco {seletor} não define {token}, que a PALETA de {js} usa.");
        }
    }

    /// <summary>Os "--falante-N" que a PALETA do pecas.js referencia, em ordem.</summary>
    private static string[] TokensDaPaleta(string js)
    {
        var lista = Regex.Match(js, @"const\s+PALETA\s*=\s*\[(.*?)\]\s*;", RegexOptions.Singleline);
        if (!lista.Success) return [];

        return Regex.Matches(lista.Groups[1].Value, @"var\(\s*(--falante-\d+)\s*\)")
                    .Select(m => m.Groups[1].Value)
                    .Distinct()
                    .ToArray();
    }

    /// <summary>
    /// O corpo do primeiro bloco daquele seletor que menciona <paramref name="pista"/>.
    /// </summary>
    /// <remarks>
    /// Conta chaves em vez de casar com regex porque o bloco do tema automático
    /// mora dentro de um <c>@media</c>, e o seletor de dentro é o que interessa —
    /// procurar pelo <c>@media</c> traria o aninhamento junto.
    /// </remarks>
    private static string? BlocoQueDefine(string css, string seletor, string pista)
    {
        int de = 0;
        while (true)
        {
            int i = css.IndexOf(seletor, de, StringComparison.Ordinal);
            if (i < 0) return null;

            int abre = css.IndexOf('{', i + seletor.Length);
            // Só aceita o que vier logo depois do seletor: um ":root" citado num
            // comentário não abre bloco nenhum antes da próxima chave de outro.
            if (abre < 0) return null;
            if (css.AsSpan(i + seletor.Length, abre - i - seletor.Length).Trim().Length > 0)
            {
                de = i + seletor.Length;
                continue;
            }

            int nivel = 1, j = abre + 1;
            while (j < css.Length && nivel > 0)
            {
                if (css[j] == '{') nivel++;
                else if (css[j] == '}') nivel--;
                j++;
            }

            string corpo = css[(abre + 1)..(j - 1)];
            if (corpo.Contains(pista, StringComparison.Ordinal)) return corpo;
            de = j;
        }
    }

    /// <summary>Sobe do binário de teste até achar o arquivo, ou nulo.</summary>
    private static string? Achar(string relativo)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            string tentativa = Path.Combine(dir.FullName, relativo);
            if (File.Exists(tentativa)) return tentativa;
            dir = dir.Parent;
        }
        return null;
    }
}
