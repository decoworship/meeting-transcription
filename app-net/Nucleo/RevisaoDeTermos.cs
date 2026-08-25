using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace MeetingApp.Nucleo;

/// <summary>Uma troca proposta, antes de alguém decidir se ela vale.</summary>
/// <param name="De">Como está escrito na transcrição.</param>
/// <param name="Para">O termo conhecido que se propõe no lugar.</param>
/// <param name="Fonte">Quem propôs — <c>"regra"</c> ou <c>"modelo"</c>.</param>
public sealed record Proposta(string De, string Para, string Fonte);

/// <summary>
/// Nome próprio e sigla que o ASR trocou, recuperados contra o que o app já sabe.
/// </summary>
/// <remarks>
/// <para>
/// <b>A lacuna que isto fecha foi medida.</b> O <c>hotwords</c> saiu do caminho
/// do ASR em 19/08/2026 — decisão certa, o ganho de segmentação é real — e a
/// justificativa era que "o vocabulário continua inteiro na correção fonética".
/// Medido em 25/08 (<c>AuditoriaCorrecaoFonetica</c>): a
/// <see cref="CorrecaoFonetica"/> recupera o caso para o qual foi desenhada
/// ("Jimmy" quando o vocabulário tem "Dimi") e <b>nenhum</b> dos dez erros de
/// nome próprio catalogados nas gravações reais. Eles não são erro de grafia de
/// som parecido: são sigla corrompida (<c>G6CB</c> por <c>GCCB</c>), troca por
/// palavra mais comum (<c>Algarve</c> por <c>Algar</c>) e estrangeirismo
/// aportuguesado (<c>Cláudio</c> por <c>Claude</c>).
/// </para>
/// <para>
/// <b>Distância de edição, e não som.</b> É o que separa esta classe da
/// <see cref="CorrecaoFonetica"/>, que continua existindo e resolvendo o que
/// resolve. Um dígito no lugar de uma letra não tem fonética; duas letras
/// trocadas, sim, têm distância.
/// </para>
/// <para>
/// <b>Um aplicador, vários propositores.</b> <see cref="Propor"/> é a fonte
/// determinística. Um modelo pode produzir a mesma lista de
/// <see cref="Proposta"/> — medido em 25/08, o Gemma 4 acerta 8 dos 10 casos
/// contra 3 do Qwen3.5 —, e passa pela mesma <see cref="Validar"/> antes de
/// virar troca. O modelo é fonte a mais, nunca caminho paralelo: quem decide o
/// que entra no texto é sempre esta classe.
/// </para>
/// <para>
/// <b>Nada é silencioso.</b> Toda troca aplicada vira <see cref="TrocaFeita"/>
/// em <c>SegmentoFinal.Swaps</c>, que é o campo que a tela já sabe mostrar e
/// desfazer. É a mesma regra do verificador de ata, e pelo mesmo motivo:
/// reescrever a palavra que alguém disse, sem deixar rastro, é pior que deixar
/// o erro.
/// </para>
/// </remarks>
public static class RevisaoDeTermos
{
    /// <summary>Quantas edições separam o escrito do termo conhecido.</summary>
    /// <remarks>
    /// Duas, e não três. Com três, <c>Tulsa</c> vira <c>Tools</c> e <c>Tim</c>
    /// vira <c>Teams</c> — que por acaso estariam certos nas gravações medidas,
    /// e estariam errados em qualquer reunião que mencionasse a operadora ou uma
    /// pessoa chamada Tim. Duas letras é o ponto em que a troca ainda é
    /// explicável por erro de escuta.
    /// </remarks>
    public const int DistanciaMaxima = 2;

    /// <summary>Diferença de tamanho tolerada entre o escrito e o termo.</summary>
    public const int DiferencaDeTamanho = 2;

    /// <summary>
    /// O quanto uma proposta pode se afastar, venha de onde vier.
    /// </summary>
    /// <remarks>
    /// Mais frouxo que o da regra, porque o modelo enxerga o que ela não
    /// enxerga: <c>Tim → Teams</c> e <c>Tulsa → Tools</c> são três edições e
    /// estão certos. Mas não é sem teto — sem ele, "Cláudio → Andre Monlevade"
    /// passava.
    /// </remarks>
    public const int DistanciaDoModelo = 4;

    /// <summary>Abaixo disto a palavra é curta demais para duas edições.</summary>
    /// <remarks>
    /// Em três letras, duas edições transformam qualquer coisa em qualquer
    /// coisa. Siglas de três letras existem e são justamente as mais fáceis de
    /// confundir — por isso elas entram, mas com uma edição só
    /// (<see cref="DistanciaPara"/>).
    /// </remarks>
    public const int TamanhoMinimo = 3;

    /// <summary>
    /// Palavra que parece nome próprio ou sigla, que é onde o ASR erra assim.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>O recorte é o que torna a regra segura.</b> Palavra minúscula não
    /// entra: <c>sexta</c> por <c>cesta</c> e <c>pooling</c> por <c>polling</c>
    /// ficam de fora, e ficam de propósito — para decidir aquelas é preciso
    /// entender a frase, e é exatamente o que o propositor de modelo faz melhor.
    /// Uma regra que tentasse pegá-las reescreveria português correto.
    /// </para>
    /// <para>
    /// <b>Maiúscula só vale no meio da frase</b>, e essa lição já estava escrita
    /// no <see cref="CorrecaoFonetica"/> — eu a ignorei, e a primeira versão
    /// desta classe rodou sobre o acervo propondo <c>Falar→Algar</c>,
    /// <c>Jogar→Algar</c>, <c>Pegar→Algar</c>, <c>Gente→Agentes</c> e
    /// <c>Felipe→Felipeof</c>. Início de frase também é maiúsculo, então sem
    /// esse recorte a regra reescreve verbo comum como se fosse nome de cliente.
    /// Doze das 37 gravações teriam sido corrompidas.
    /// </para>
    /// <para>
    /// Sigla toda em maiúscula é exceção: <c>PDB</c> no começo da frase continua
    /// sendo sigla, porque português não começa frase com três maiúsculas.
    /// </para>
    /// </remarks>
    private static readonly Regex ParecemNome = new(
        @"^(?:\p{Lu}[\p{L}\p{Nd}.'-]*|\p{Lu}[\p{Lu}\p{Nd}]+)$", RegexOptions.Compiled);

    /// <summary>Só maiúsculas e dígitos: sigla, e a posição na frase não importa.</summary>
    private static readonly Regex Sigla = new(
        @"^[\p{Lu}\p{Nd}][\p{Lu}\p{Nd}]+$", RegexOptions.Compiled);

    /// <summary>
    /// As trocas que a regra sustenta, lidas da transcrição.
    /// </summary>
    /// <param name="entidades">
    /// O que o app já sabe: cliente, projeto, convidados e o vocabulário do
    /// projeto. Sem elas não há proposta — a regra não inventa alvo.
    /// </param>
    public static IReadOnlyList<Proposta> Propor(
        IEnumerable<string> textos, IEnumerable<string> entidades)
    {
        var alvos = Alvos(entidades);
        if (alvos.Count == 0) return [];

        var conhecidas = alvos.Select(a => Chave(a)).ToHashSet(StringComparer.Ordinal);
        var vistas = new Dictionary<string, Proposta>(StringComparer.Ordinal);

        foreach (string texto in textos)
        foreach (Match m in Regex.Matches(texto ?? "", @"[\p{L}\p{Nd}][\p{L}\p{Nd}.'-]*"))
        {
            string escrita = m.Value.Trim('.', '\'', '-');
            if (escrita.Length < TamanhoMinimo || !ParecemNome.IsMatch(escrita)) continue;

            // Início de frase é maiúsculo por gramática, não por ser nome. Sem
            // esta linha a regra reescreve "Falar" como "Algar". Ver ParecemNome.
            if (!Sigla.IsMatch(escrita) && !MeioDeFrase(texto!, m.Index)) continue;

            string chave = Chave(escrita);
            // Já é um termo conhecido: nada a propor, e é o caso comum.
            if (conhecidas.Contains(chave) || vistas.ContainsKey(chave)) continue;

            var alvo = MaisProximo(escrita, alvos);
            if (alvo is not null) vistas[chave] = new Proposta(escrita, alvo, "regra");
        }

        return [.. vistas.Values];
    }

    /// <summary>
    /// A porta pela qual toda proposta passa, venha da regra ou do modelo.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>O modelo propõe trocas identidade</b> — <c>Sorocaba → Sorocaba</c> —,
    /// e propõe alvos que ninguém conhece. Medido em 25/08: das dezessete
    /// propostas do Gemma sobre os casos com erro, oito eram identidade. Elas
    /// não fazem mal e não podem virar troca.
    /// </para>
    /// <para>
    /// <b>O alvo tem que ser entidade conhecida.</b> É o que impede um
    /// propositor de escrever no texto uma palavra que ninguém no projeto usa —
    /// e é a mesma exigência que o verificador de ata faz do dono de uma
    /// pendência.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<Proposta> Validar(
        IEnumerable<Proposta> propostas, IEnumerable<string> entidades)
    {
        var alvos = Alvos(entidades);
        var porChave = alvos.ToDictionary(Chave, a => a, StringComparer.Ordinal);

        var boas = new List<Proposta>();
        foreach (var p in propostas)
        {
            string de = (p.De ?? "").Trim();
            string para = (p.Para ?? "").Trim();
            if (de.Length < TamanhoMinimo || para.Length == 0) continue;
            if (Chave(de) == Chave(para)) continue;                  // identidade
            if (!porChave.TryGetValue(Chave(para), out string? canonico)) continue;

            // **Expandir nome não é corrigir.** O modelo propôs "Yuri → Andre
            // Yuri", "Diego → Diego Lacerda" e "Andre → Andre Monlevade" —
            // medido em 25/08 sobre uma gravação real. Nenhuma é erro de
            // escuta: a pessoa disse o primeiro nome, e disse certo. Com dois
            // Andrés na reunião, a terceira ainda escolhia o errado.
            if (Chave(canonico).Contains(Chave(de), StringComparison.Ordinal)
                || Chave(de).Contains(Chave(canonico), StringComparison.Ordinal))
                continue;

            // **O alvo tem que ser alcançável por erro de escuta.** Sem isto o
            // modelo propôs "Cláudio → Andre Monlevade", que nenhum ASR
            // produziria. O teto é frouxo de propósito — o modelo acerta
            // "Tim → Teams", que são três edições, e é para isso que ele existe.
            if (Distancia(Chave(de), Chave(canonico), DistanciaDoModelo) > DistanciaDoModelo)
                continue;

            // A grafia que vale é a do vocabulário, e não a que o propositor
            // digitou: o modelo escreve "gccb" e o projeto escreve "GCCB".
            boas.Add(p with { Para = canonico });
        }
        return boas;
    }

    /// <summary>
    /// Aplica as trocas ao texto, devolvendo o que mudou.
    /// </summary>
    /// <remarks>
    /// Troca palavra inteira, com fronteira: sem isso, <c>PDB</c> dentro de
    /// <c>PDBX</c> seria reescrito, e a troca de uma sigla curta espalharia por
    /// palavras que a contêm.
    /// </remarks>
    public static (string Texto, List<Troca> Trocas) Aplicar(
        string texto, IReadOnlyList<Proposta> validadas)
    {
        var feitas = new List<Troca>();
        string saida = texto ?? "";

        foreach (var p in validadas)
        {
            string padrao = $@"(?<![\p{{L}}\p{{Nd}}]){Regex.Escape(p.De)}(?![\p{{L}}\p{{Nd}}])";
            saida = Regex.Replace(saida, padrao, _ =>
            {
                feitas.Add(new Troca(p.De, p.Para, 0));
                return p.Para;
            });
        }
        return (saida, feitas);
    }

    // ─────────────────────────────────────────────────────────── interno

    /// <summary>Há texto antes, e a frase anterior não terminou.</summary>
    private static bool MeioDeFrase(string texto, int posicao)
    {
        string antes = texto[..posicao].TrimEnd();
        return antes.Length > 0 && !".!?…".Contains(antes[^1]);
    }

    /// <summary>
    /// Os alvos possíveis, com as entidades compostas abertas.
    /// </summary>
    /// <remarks>
    /// <b>"Coca Cola - GCCB" precisa render "GCCB".</b> O cliente é digitado
    /// como um rótulo humano, e o pedaço que o ASR erra é a sigla dentro dele —
    /// medido em 25/08: com a entidade inteira, <c>G6CB</c> não tinha alvo e
    /// nem a regra nem o modelo o alcançavam.
    ///
    /// Nome de pessoa <b>não</b> é aberto: "Andre Yuri" render "Andre" faria a
    /// regra tratar como erro o primeiro nome de quem tem sobrenome — e com dois
    /// Andrés na reunião, escolheria o errado.
    /// </remarks>
    public static List<string> Alvos(IEnumerable<string> entidades)
    {
        var saida = new List<string>();
        foreach (string bruto in entidades)
        foreach (string e in (bruto ?? "").Split([',', ';', '\n'],
                                                 StringSplitOptions.TrimEntries
                                                 | StringSplitOptions.RemoveEmptyEntries))
        {
            if (e.Length < TamanhoMinimo) continue;
            saida.Add(e);

            // Só o que tem separador não-espaço: "Coca Cola - GCCB" abre,
            // "Andre Yuri" não.
            if (e.Contains(" - ") || e.Contains('/') || e.Contains('('))
                saida.AddRange(e.Split([" - ", "/", "(", ")"], StringSplitOptions.TrimEntries
                                                               | StringSplitOptions.RemoveEmptyEntries)
                                .Where(x => x.Length >= TamanhoMinimo));
        }
        return [.. saida.Distinct(StringComparer.OrdinalIgnoreCase)];
    }

    /// <summary>O termo mais próximo, ou nulo se nenhum está perto o bastante.</summary>
    /// <remarks>
    /// Empate devolve nulo em vez de escolher: duas entidades igualmente
    /// próximas significam que o contexto é quem decide, e contexto é o que esta
    /// regra não tem.
    /// </remarks>
    private static string? MaisProximo(string escrita, List<string> alvos)
    {
        string a = Chave(escrita);
        string? melhor = null;
        int menor = int.MaxValue;
        bool empate = false;

        foreach (string alvo in alvos)
        {
            string b = Chave(alvo);
            if (Math.Abs(a.Length - b.Length) > DiferencaDeTamanho) continue;

            int d = Distancia(a, b, DistanciaPara(a, b));
            if (d > DistanciaPara(a, b)) continue;

            if (d < menor) { menor = d; melhor = alvo; empate = false; }
            else if (d == menor && !string.Equals(melhor, alvo, StringComparison.Ordinal))
                empate = true;
        }
        return empate ? null : melhor;
    }

    /// <summary>
    /// Uma edição até seis letras; duas só em palavra mais longa.
    /// </summary>
    /// <remarks>
    /// <b>Medido sobre as 37 gravações do acervo em 25/08.</b> Com duas edições
    /// a partir de cinco letras a regra propunha seis trocas: cinco certas
    /// (<c>Algarve→Algar</c> duas vezes, <c>Algarum→Algar</c>,
    /// <c>Dalgar→Algar</c>, <c>VIVA→Vivo</c>) e uma errada — <c>Edgar→Algar</c>,
    /// numa frase que diz "o que o Edgar tinha indicado". Nome de pessoa fica a
    /// duas edições do nome do cliente, e a regra não tem como saber a
    /// diferença.
    ///
    /// Com uma edição sobram duas propostas em 37 gravações, as duas certas. O
    /// que se perde — <c>Algarve→Algar</c> — é exatamente o que o propositor de
    /// modelo acerta, e é essa a divisão de trabalho entre os dois.
    /// </remarks>
    private static int DistanciaPara(string a, string b) =>
        Math.Min(a.Length, b.Length) <= 6 ? 1 : DistanciaMaxima;

    /// <summary>Sem acento e sem caixa — a comparação é de letras, não de estilo.</summary>
    private static string Chave(string t)
    {
        var sb = new StringBuilder();
        foreach (char c in (t ?? "").Normalize(NormalizationForm.FormD))
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark
                && char.IsLetterOrDigit(c))
                sb.Append(char.ToLowerInvariant(c));
        return sb.ToString();
    }

    /// <summary>Levenshtein com corte: acima do teto não interessa o valor exato.</summary>
    private static int Distancia(string a, string b, int teto)
    {
        var anterior = new int[b.Length + 1];
        var atual = new int[b.Length + 1];
        for (int j = 0; j <= b.Length; j++) anterior[j] = j;

        for (int i = 1; i <= a.Length; i++)
        {
            atual[0] = i;
            int melhorDaLinha = atual[0];
            for (int j = 1; j <= b.Length; j++)
            {
                atual[j] = Math.Min(Math.Min(anterior[j] + 1, atual[j - 1] + 1),
                                    anterior[j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
                melhorDaLinha = Math.Min(melhorDaLinha, atual[j]);
            }
            if (melhorDaLinha > teto) return teto + 1;
            (anterior, atual) = (atual, anterior);
        }
        return anterior[b.Length];
    }
}
