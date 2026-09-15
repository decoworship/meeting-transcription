using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace MeetingApp.Nucleo;

/// <summary>Uma troca proposta, antes de alguém decidir se ela vale.</summary>
/// <param name="De">Como está escrito na transcrição.</param>
/// <param name="Para">O termo conhecido que se propõe no lugar.</param>
/// <param name="Fonte">
/// Quem propôs — <c>"regra"</c>, <c>"grafia"</c> ou <c>"modelo"</c>. A
/// <c>"grafia"</c> é a variante de espaço e hífen do §12 da
/// <c>docs/FASE7-RESULTADOS.md</c>, e é a única que não muda letra nenhuma.
/// </param>
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
/// <b>E há um terceiro caso, que não é distância nem som: a grafia.</b> Medido
/// em 03/09/2026 (<c>docs/FASE7-RESULTADOS.md</c> §12, com o
/// <c>tools/medir_vocabulario_moss.py</c>): parte do vocabulário que se dá por
/// perdido não está perdido — está escrito com o espaço no lugar errado.
/// O MOSS escreve <c>next best</c> onde o projeto escreve <c>NextBest</c>, e
/// <c>lifecycle</c> onde o projeto escreve <c>life cycle</c>. Contando a forma
/// canônica, a distância entre o app com <c>hotwords</c> e o MOSS sem eles era
/// de 26,4 pontos; contando o termo como dito, é de <b>15,8</b> — a diferença
/// são as variantes de grafia. <see cref="PropostaDeGrafia"/> é o que as cola.
/// </para>
/// <para>
/// <b>Ela é segura por construção, e por isso não usa o recorte de nome
/// próprio.</b> A regra de distância pode trocar letra, e é por isso que ela só
/// olha palavra com maiúscula no meio da frase — sem esse corte ela reescreveria
/// <c>Falar</c> como <c>Algar</c>. A regra de grafia exige as <b>mesmas letras,
/// na mesma ordem</b>: ela não consegue produzir uma palavra que não seja, letra
/// por letra, um termo que alguém digitou no vocabulário. <c>next best</c> é
/// minúsculo e entra; <c>sexta</c> nunca vira <c>cesta</c> por este caminho,
/// porque as letras são outras.
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
    /// <para>
    /// Mais frouxo que o da regra, porque o modelo enxerga o que ela não
    /// enxerga: <c>Tim → Teams</c> e <c>Kino → Kenan</c> são três edições e
    /// estão certos. Mas não é sem teto — sem ele, "Cláudio → Andre Monlevade"
    /// passava.
    /// </para>
    /// <para>
    /// <b>Era quatro, e quatro foi medido demais.</b> Numa reunião real de 33
    /// minutos com 53 entidades no vocabulário, o modelo propôs
    /// <c>Emdux → CMF</c> (era Amdocs) e <c>Endit → Kenan</c> (é uma pessoa) —
    /// as duas a exatamente quatro edições, as duas erradas. Em três, elas caem
    /// e <c>Kino → Kenan</c> fica. O que se perde é <c>Tulsa → Tools</c>, que
    /// era acerto num caso sintético; dado real ganha de caso sintético.
    /// </para>
    /// </remarks>
    public const int DistanciaDoModelo = 3;

    /// <summary>Abaixo disto a palavra é curta demais para duas edições.</summary>
    /// <remarks>
    /// Em três letras, duas edições transformam qualquer coisa em qualquer
    /// coisa. Siglas de três letras existem e são justamente as mais fáceis de
    /// confundir — por isso elas entram, mas com uma edição só
    /// (<see cref="DistanciaPara"/>).
    /// </remarks>
    public const int TamanhoMinimo = 3;

    /// <summary>Até quantas palavras vizinhas podem ser coladas num termo só.</summary>
    /// <remarks>
    /// Quatro cobre o que existe no vocabulário deste projeto —
    /// <c>next best action</c> é o mais longo — e não custa nada: o casamento é
    /// por chave exata, então uma janela maior não afrouxa a regra, só varre
    /// mais.
    /// </remarks>
    public const int MaximoDePalavrasColadas = 4;

    /// <summary>
    /// Palavra de uma letra não entra numa colagem.
    /// </summary>
    /// <remarks>
    /// Em português a palavra de uma letra é artigo ou preposição, e colá-la à
    /// seguinte é o único jeito de esta regra inventar um termo: <c>a PI</c>
    /// viraria <c>API</c> num projeto que tenha <c>API</c> no vocabulário, e a
    /// frase "isso vai para a PI do cliente" é português correto.
    /// </remarks>
    private const int MinimoDaParte = 2;

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
    /// As variantes de espaço e hífen: mesmas letras, separador no lugar errado.
    /// </summary>
    /// <param name="entidades">As mesmas de <see cref="Propor"/>.</param>
    /// <remarks>
    /// <para>
    /// <b>Ela pega o que a regra de distância nunca poderia pegar</b>, porque
    /// para ela as duas formas <b>já são a mesma palavra</b>: a
    /// <see cref="Chave"/> descarta separador e acento, então
    /// <c>next best</c> e <c>NextBest</c> têm a mesma chave. É por isso que o
    /// <see cref="Propor"/> passa por elas sem ver nada — o termo consta como
    /// conhecido, e está mesmo, só não na forma que o cliente escreve.
    /// </para>
    /// <para>
    /// <b>Nos dois sentidos.</b> Colar (<c>next best</c> → <c>NextBest</c>) e
    /// separar (<c>lifecycle</c> → <c>life cycle</c>) são o mesmo caso visto de
    /// dois lados, e o MOSS produziu os dois no acervo
    /// (<c>docs/FASE7-RESULTADOS.md</c> §12.2). Quem manda é sempre o
    /// vocabulário: a forma canônica é a que a pessoa digitou.
    /// </para>
    /// <para>
    /// <b>Um dos dois lados tem de ter separador</b>, e este é o corte que
    /// mantém a regra estreita. Sem ele, <c>cloud</c> viraria <c>Cloud</c> em
    /// toda frase de um projeto que tenha <c>Cloud</c> no vocabulário — trocar
    /// maiúscula não é o defeito que foi medido, e reescrever palavra comum é
    /// como esta classe já errou uma vez (ver <see cref="ParecemNome"/>).
    /// </para>
    /// <para>
    /// <b>Ela ajuda os dois motores</b>, e não só o MOSS: o faster-whisper com
    /// <c>hotwords</c> também escreve o termo separado quando ouve a pausa.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<Proposta> ProporGrafia(
        IEnumerable<string> textos, IEnumerable<string> entidades)
    {
        var alvos = Alvos(entidades);
        if (alvos.Count == 0) return [];

        // Chave → forma canônica. Só entram alvos com letra ou dígito bastante;
        // o piso é o mesmo do resto da classe.
        var porChave = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string a in alvos)
            if (Chave(a).Length >= TamanhoMinimo) porChave.TryAdd(Chave(a), a);
        if (porChave.Count == 0) return [];

        var vistas = new Dictionary<string, Proposta>(StringComparer.Ordinal);

        foreach (string bruto in textos)
        {
            string texto = bruto ?? "";
            var palavras = Regex.Matches(texto, @"[\p{L}\p{Nd}][\p{L}\p{Nd}.']*")
                                .Cast<Match>().ToList();

            for (int i = 0; i < palavras.Count; i++)
            for (int n = 1; n <= MaximoDePalavrasColadas && i + n <= palavras.Count; n++)
            {
                // A janela só cresce enquanto o que separa as palavras for
                // espaço ou hífen. Vírgula, ponto e parêntese são fronteira de
                // sentido: colar através deles inventaria termo de pedaços de
                // duas orações.
                if (n > 1 && !SoEspacoOuHifen(
                        texto, palavras[i + n - 2].Index + palavras[i + n - 2].Length,
                        palavras[i + n - 1].Index))
                    break;

                var primeira = palavras[i];
                var ultima = palavras[i + n - 1];
                if (n > 1 && palavras.Skip(i).Take(n)
                        .Any(m => m.Value.Trim('.', '\'').Length < MinimoDaParte))
                    continue;

                int fim = ultima.Index + ultima.Length;
                string escrita = texto[primeira.Index..fim].Trim('.', '\'');
                string chave = Chave(escrita);
                if (chave.Length < TamanhoMinimo) continue;
                if (!porChave.TryGetValue(chave, out string? canonico)) continue;
                if (string.Equals(escrita, canonico, StringComparison.Ordinal)) continue;

                // O corte que mantém a regra estreita: um dos dois lados tem de
                // trazer separador. Ver as observações do método.
                if (!TemSeparador(escrita) && !TemSeparador(canonico)) continue;

                vistas.TryAdd(escrita, new Proposta(escrita, canonico, "grafia"));
            }
        }

        return [.. vistas.Values];
    }

    /// <summary>Entre o fim de uma palavra e o começo da outra só há espaço ou hífen.</summary>
    private static bool SoEspacoOuHifen(string texto, int de, int ate)
    {
        if (ate <= de) return false;
        for (int i = de; i < ate; i++)
            if (texto[i] is not (' ' or '-' or '\u00a0')) return false;
        return true;
    }

    /// <summary>A forma escrita carrega espaço ou hífen entre as letras.</summary>
    private static bool TemSeparador(string t) =>
        t.Any(c => c is ' ' or '-' or '\u00a0');

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
        var porChave = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string a in alvos) porChave.TryAdd(Chave(a), a);

        var boas = new List<Proposta>();
        foreach (var p in propostas)
        {
            string de = (p.De ?? "").Trim();
            string para = (p.Para ?? "").Trim();
            if (de.Length < TamanhoMinimo || para.Length == 0) continue;

            // **A grafia entra por outra porta, e tem de entrar.** As duas
            // formas têm a mesma chave por definição — é isso que ela conserta
            // —, então as três guardas de baixo a matariam: a de identidade
            // primeiro, e a de "expandir nome não é corrigir" logo depois. O que
            // resta é o que importa: o alvo continua tendo de ser entidade
            // conhecida, e a troca continua sendo registrada e desfazível.
            if (p.Fonte == "grafia")
            {
                if (!porChave.TryGetValue(Chave(para), out string? daGrafia)) continue;
                if (string.Equals(de, daGrafia, StringComparison.Ordinal)) continue;
                boas.Add(p with { Para = daGrafia });
                continue;
            }

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
        // **Deduplicado pela mesma chave que compara, e não pelo texto.** A
        // agenda traz "Andre Monlevade" e o vocabulário traz "André Monlevade":
        // são diferentes como texto, iguais como alvo. Sem isto o dicionário de
        // Validar estourava com "An item with the same key has already been
        // added" — e derrubava a transcrição inteira, que já estava pronta.
        // Apareceu ao ligar o vocabulário de verdade de um projeto.
        //
        // Vence a primeira grafia: a lista chega com o vocabulário na frente, e
        // é ele que a pessoa escreveu à mão.
        var porChave = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string alvo in saida) porChave.TryAdd(Chave(alvo), alvo);
        return [.. porChave.Values];
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

        bool escritaEhSigla = Sigla.IsMatch(escrita);
        foreach (string alvo in alvos)
        {
            string b = Chave(alvo);
            if (Math.Abs(a.Length - b.Length) > DiferencaDeTamanho) continue;

            // **Sigla só é trocada por sigla.** Medido em 25/08 no acervo com o
            // vocabulário de verdade: "São" ficava a uma edição de "SAS" e a
            // regra propunha trocar — e "São" é português comum, aparece em
            // "São Paulo" e no verbo. A forma da palavra é o sinal que separa
            // as duas coisas, e ele é de graça.
            if (Sigla.IsMatch(alvo) && !escritaEhSigla) continue;

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
