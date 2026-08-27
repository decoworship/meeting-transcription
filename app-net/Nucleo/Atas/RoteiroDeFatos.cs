using System.Text.RegularExpressions;

namespace MeetingApp.Nucleo.Atas;

/// <summary>Um fato achado na transcrição, com o pedaço de fala em volta.</summary>
/// <param name="Trecho">O que foi dito, cortado no tamanho que cabe num prompt.</param>
/// <param name="Quando">Segundos desde o início. Serve para ordenar e para citar.</param>
/// <param name="Tipo">
/// "numero" ou "compromisso". A conferência de omissão só vale para números: um
/// compromisso tem como chave a palavra do prazo ("hoje", "amanhã"), e cobrar
/// que a ata repita a palavra "hoje" é cobrar coisa nenhuma.
/// </param>
/// <param name="Unidade">
/// O substantivo que acompanha o número na fala — "registros", "produtos",
/// "reais". Vazio quando não dá para dizer.
/// </param>
public sealed record Fato(string Chave, string Trecho, double Quando, string Tipo = "numero",
                          string Unidade = "");

/// <summary>
/// O que a reunião disse de concreto: números, compromissos, nomes, datas.
/// </summary>
/// <remarks>
/// <para>
/// <b>Existe porque o modelo pequeno não inventa — ele omite.</b> Na comparação
/// do ATA.md §8, o Qwen3-4B não escreveu um único fato falso e ainda assim
/// deixou de fora metade dos números, incluindo o impacto financeiro da reunião,
/// que era a linha mais importante dela. O verificador do §4 foi desenhado
/// contra invenção e não faz nada contra esquecimento.
/// </para>
/// <para>
/// A resposta é não pedir ao modelo que <em>procure</em>: quem procura é isto
/// aqui, com expressão regular, e o modelo recebe a lista pronta para usar o que
/// for relevante. Regra determinística embaixo, modelo por cima — o mesmo
/// desenho da correção fonética.
/// </para>
/// <para>
/// <b>Errar para mais é barato.</b> Um fato irrelevante na lista custa alguns
/// tokens; um fato ausente custa a linha que faltou na ata.
/// </para>
/// </remarks>
public static class RoteiroDeFatos
{
    /// <summary>Números com três dígitos ou mais, dinheiro, percentual e multiplicador.</summary>
    /// <remarks>
    /// Números de um e dois dígitos ficam de fora: "às 2 horas", "os 3 casos" e
    /// "a v2" enchem a lista sem carregar informação. O que interessa é
    /// <c>27.529</c>, <c>R$ 180 mil</c>, <c>95%</c>, <c>2 milhões</c>.
    /// </remarks>
    private static readonly Regex Numeros = new(
        // A ORDEM importa, e ela custou um defeito. A alternância do regex é
        // ordenada: com `\b\d{3,}\b` antes da forma com magnitude, "129 mil"
        // casava como **"129"**, e a palavra seguinte — que o roteiro passou a
        // ler como unidade — virava "mil". O número saía do roteiro menor do que
        // foi dito. A magnitude vem antes por isso.
        @"(R\$\s?[\d.,]+(\s*(mil|milh(ão|ões|oes)))?"
        + @"|\b[\d.,]+\s*(mil|milh(ão|ões|oes)|bilh(ão|ões|oes))\b"
        + @"|[\d.,]+\s*(%|por\s?cento)"
        + @"|\b\d{1,3}(\.\d{3})+([,.]\d+)?\b|\b\d{3,}\b)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Compromisso verbal: o que parece casual na fala e é o que a ata precisa fixar.
    /// </summary>
    /// <remarks>
    /// A frase da própria skill: "'Vamos ver isso até sexta', 'eu te mando os
    /// dados amanhã' — parecem casuais na fala e são exatamente o que a ata
    /// precisa fixar, com nome e data".
    /// </remarks>
    private static readonly Regex Compromissos = new(
        @"\b(eu\s+)?(te\s+)?(mando|envio|passo|gero|fa(ç|c)o|vejo|confirmo|verifico|olho"
        + @"|dou uma olhada|fico de|vou (mandar|enviar|passar|ver|fazer|confirmar|olhar|verificar)"
        + @"|pode (deixar|contar)|combinado|fica(mos)? de"
        // Acrescentados em 20/08/2026: a comparação com o Notion mostrou que a
        // ata perdeu "a apresentação para a Carla no dia seguinte" — que era o
        // **propósito** da reunião — e ficou só com "a próxima reunião será
        // marcada para amanhã", sem dizer para quem nem para quê. O compromisso
        // estava dito; o roteiro é que não tinha o verbo. FASE6 §1.6, defeito 3.
        + @"|apresent(o|a|amos|ar)|mostr(o|a|amos|ar)|marc(o|a|amos|ar)"
        + @"|agend(o|a|amos|ar)|entreg(o|a|amos|ar)|sub(o|e|imos|ir)"
        + @"|revis(o|a|amos|ar)|levant(o|a|amos|ar))\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Preocupação dita: dependência, bloqueio, incidente, prazo apertado.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>O verificador derruba risco inventado e ninguém fornecia risco real.</b>
    /// Na reunião medida, a seção <c>riscos</c> saiu <b>vazia</b> — e a reunião
    /// tinha um: o time de TI do cliente foi orientado a fazer o levantamento em
    /// vez de receber a base, com um incidente já aberto com prioridade. É risco
    /// de cronograma, estava na transcrição, e não chegou à ata
    /// (FASE6 §1.6, defeito 3).
    /// </para>
    /// <para>
    /// A régua da skill é "alguém sinalizou preocupação com prazo, dado,
    /// capacidade ou dependência externa". Estas são as palavras com que isso é
    /// dito em reunião — e, como no resto do roteiro, <b>errar para mais é
    /// barato</b>: o modelo ignora o que não for risco, e o verificador derruba
    /// o que ele escrever sem eco na fala.
    /// </para>
    /// </remarks>
    private static readonly Regex Riscos = new(
        @"\b(risco|arriscado|preocup(a|ado|ação|acao)|receio|problema|bloquei(o|ado|ando)"
        + @"|travad(o|a)|trav(ou|ando)|impedimento|incidente|chamado aberto"
        + @"|depende d(e|o|a)|dependência|dependencia|na m(ã|a)o d(e|o|a)"
        + @"|esperando (o|a|pelo|pela)|no aguardo|em aberto"
        + @"|n(ã|a)o vai dar tempo|prazo apertado|estour(a|ar|ou) o prazo"
        + @"|corre o risco|se n(ã|a)o|caso contr(á|a)rio)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>Prazos ditos de forma relativa, que somem se ninguém fixar.</summary>
    private static readonly Regex Prazos = new(
        @"\b(hoje|amanh(ã|a)|depois de amanh(ã|a)|ainda hoje|essa semana|esta semana"
        + @"|semana que vem|pr(ó|o)xima semana|segunda|ter(ç|c)a|quarta|quinta|sexta"
        + @"|s(á|a)bado|domingo|at(é|e) o fim do (dia|m(ê|e)s)|no pr(ó|o)ximo ciclo"
        + @"|m(ê|e)s que vem)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Monta o roteiro a partir dos segmentos, sem repetir o mesmo achado.
    /// </summary>
    /// <param name="limite">
    /// Teto de fatos por categoria. Existe porque o roteiro divide o contexto
    /// com a transcrição: numa reunião de duas horas, 400 números empurrariam a
    /// fala para fora da janela — e a fala é a fonte, o roteiro é o índice.
    /// </param>
    public static IReadOnlyList<Fato> De(IEnumerable<SegmentoFinal> segmentos, int limite = 40)
    {
        var numeros = new List<Fato>();
        var compromissos = new List<Fato>();
        var riscos = new List<Fato>();
        var vistos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var s in segmentos)
        {
            string texto = s.Text.Trim();
            if (texto.Length == 0) continue;

            foreach (Match m in Numeros.Matches(texto))
            {
                string chave = Normalizar(m.Value);
                if (chave.Length == 0 || !vistos.Add($"n:{chave}")) continue;
                numeros.Add(new Fato(m.Value.Trim(), Recorte(texto, m.Index), s.Start,
                                     "numero", UnidadeDe(texto, m)));
            }

            // Um trecho conta como compromisso quando alguém se compromete E há
            // um prazo dito: "eu te mando" sozinho é conversa; "eu te mando
            // amanhã" é item de ação com data.
            if (Compromissos.IsMatch(texto) && Prazos.Match(texto) is { Success: true } prazo)
            {
                // O corte é sobre o texto JÁ normalizado, e não sobre o
                // original: normalizar tira pontuação e espaço, então o
                // resultado é mais curto — cortar pelo tamanho do original
                // estoura o índice.
                string normalizado = Normalizar(texto);
                string chave = normalizado[..Math.Min(60, normalizado.Length)];
                if (vistos.Add($"c:{chave}"))
                        compromissos.Add(new Fato(prazo.Value, Encurtar(texto), s.Start,
                                              "compromisso"));
            }

            // Risco não precisa de prazo junto: "o TI deles está fazendo o
            // levantamento" é dependência externa sem data nenhuma, e some da
            // ata exatamente por parecer conversa.
            if (Riscos.Match(texto) is { Success: true } risco)
            {
                string normalizado = Normalizar(texto);
                string chave = normalizado[..Math.Min(60, normalizado.Length)];
                if (vistos.Add($"r:{chave}"))
                    riscos.Add(new Fato(risco.Value, Encurtar(texto), s.Start, "risco"));
            }
        }

        // Metade do teto para os riscos, e não o teto inteiro: eles são a
        // categoria mais frouxa das três — "problema" e "depende de" aparecem em
        // conversa o tempo todo —, e o roteiro divide a janela de contexto com a
        // transcrição, que é a fonte. Errar para mais é barato até o ponto em que
        // empurra a fala para fora do contexto.
        return [.. numeros.Take(limite), .. compromissos.Take(limite),
                .. riscos.Take(limite / 2)];
    }

    /// <summary>O roteiro como o prompt o recebe.</summary>
    public static string ParaPrompt(IReadOnlyList<Fato> fatos)
    {
        if (fatos.Count == 0) return "";

        var linhas = new List<string>
        {
            "# Fatos citados na reunião",
            "",
            "Lista levantada automaticamente da transcrição, para conferência. "
            + "**Use o que for relevante para a ata e ignore o resto** — números "
            + "de contexto e valores que não pertencem a nenhuma seção não "
            + "precisam entrar. Não invente nada que não esteja aqui ou na "
            + "transcrição.",
            "",
            // A unidade vai marcada porque o modelo a reconstrói sozinho quando
            // ela não está à mão — e reconstrói errado. Medido: "106 produtos
            // com registros zerados (R$ 129 mil)", onde os 129 mil eram
            // contagem de registros. Num documento cujo valor é ser citável,
            // R$ colado num número que não é dinheiro é pior que a omissão.
            "O que está *entre parênteses e em itálico* é a **unidade dita na "
            + "reunião**. Use exatamente ela. Se um número não tiver unidade "
            + "marcada, escreva-o sem unidade nenhuma — nunca acrescente R$, "
            + "%, \"mil\" ou qualquer medida que não esteja na lista.",
            "",
        };

        if (fatos.Any(f => f.Tipo == "risco"))
            linhas.Add("Os itens marcados **[risco]** são preocupações ditas por "
                       + "alguém — dependência, bloqueio, incidente, prazo. Se "
                       + "forem da reunião e não conversa solta, eles pertencem à "
                       + "seção de riscos. **Não invente risco que não esteja "
                       + "aqui**; o que não foi levantado por ninguém não entra.");

        foreach (var f in fatos)
            linhas.Add($"- [{Relogio(f.Quando)}] {(f.Tipo == "risco" ? "**[risco]** " : "")}**{f.Chave}**"
                       + (f.Unidade.Length > 0 ? $" *({f.Unidade})*" : "")
                       + $" — {f.Trecho}");

        return string.Join("\n", linhas);
    }

    /// <summary>
    /// Os números citados que não entraram na ata.
    /// </summary>
    /// <remarks>
    /// A rede contra omissão, do outro lado: não é o modelo que julga o que
    /// faltou, é uma lista que a pessoa bate o olho. Vai para "Observações",
    /// junto do que o verificador mexeu.
    /// </remarks>
    public static IReadOnlyList<string> NaoIncorporados(
        IReadOnlyList<Fato> roteiro, string ata)
    {
        var faltando = new List<string>();
        foreach (var f in roteiro)
        {
            if (f.Tipo != "numero") continue;
            string chave = Normalizar(f.Chave);
            if (chave.Length < 3) continue;
            // Compara sem pontuação: a transcrição diz "27.529" e a ata pode
            // dizer "27529" ou "27,529" sem que nenhum dos dois esteja errado.
            if (!Normalizar(ata).Contains(chave, StringComparison.OrdinalIgnoreCase))
                faltando.Add(f.Chave);
        }
        return faltando;
    }

    /// <summary>
    /// Compromissos ditos na reunião que não viraram pendência nenhuma.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>A rede que faltava.</b> O roteiro já extraía compromisso com prazo —
    /// "eu te mando amanhã" — e os mandava ao modelo, mas a conferência de
    /// omissão só olhava números: havia um aviso alto quando um número sumia e
    /// nenhum quando sumia um item de ação. Medido em 25/08 numa sessão de
    /// trabalho com nove compromissos ditos em voz alta: um modelo devolveu
    /// <b>zero</b> pendências e a ata saiu sem um único aviso.
    /// </para>
    /// <para>
    /// <b>Compara por eco de conteúdo, e não por texto igual.</b> A pendência é
    /// a reescrita do compromisso, não a cópia: a fala diz "eu vou rodar de
    /// novo que aí eu rodo pra esse mês" e a ata escreve "Rodar a aderência do
    /// mês". O que sobrevive à reescrita são as palavras de conteúdo.
    /// </para>
    /// <para>
    /// <b>Não corrige nada — lista.</b> Como a conferência de números, o que
    /// ela produz é uma linha em Observações, e quem decide se faltou é quem
    /// leu a reunião. Inventar pendência a partir de regex seria pior que
    /// omitir.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<Fato> CompromissosForaDaAta(
        IReadOnlyList<Fato> roteiro, IEnumerable<string> pendencias)
    {
        var ditas = pendencias.Select(p => Conteudo(p).ToHashSet(StringComparer.OrdinalIgnoreCase))
                              .Where(c => c.Count > 0).ToList();

        var fora = new List<Fato>();
        foreach (var f in roteiro)
        {
            if (f.Tipo != "compromisso") continue;

            var doTrecho = Conteudo(f.Trecho).ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (doTrecho.Count == 0) continue;

            // Metade das palavras de conteúdo é o bastante: o trecho traz a
            // frase inteira ("deixa eu ver aqui como é que eu faço para ter o
            // máximo de informação a tempo") e a pendência traz só o miolo.
            bool ecoa = ditas.Any(d => (double)doTrecho.Count(d.Contains) / doTrecho.Count >= 0.4);
            if (!ecoa) fora.Add(f);
        }
        return fora;
    }

    /// <summary>Palavras de conteúdo, pela mesma régua do verificador.</summary>
    private static IEnumerable<string> Conteudo(string texto) =>
        Regex.Matches((texto ?? "").ToLowerInvariant(), @"[\p{L}\p{Nd}]{4,}")
             .Select(m => m.Value)
             .Where(p => !Comuns.Contains(p));

    private static readonly HashSet<string> Comuns = new(StringComparer.OrdinalIgnoreCase)
    {
        "para", "como", "isso", "esse", "essa", "está", "estão", "sobre", "quando",
        "porque", "então", "também", "ainda", "todos", "todas", "deve", "pode",
        "fazer", "sendo", "cada", "mais", "menos", "muito", "após", "entre",
        "aqui", "onde", "qual", "quais", "seja", "gente", "coisa", "aqui",
        "vamos", "vou", "acho", "assim", "tipo", "cara", "beleza", "certo",
    };

    /// <summary>
    /// O substantivo que vem logo depois do número na fala.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Por que o roteiro precisa disto.</b> Ele entregava o número com o
    /// trecho em volta, e o modelo recuperava o número; a <b>unidade</b> ele
    /// reconstruía sozinho, às vezes errado. A ata escreveu "106 produtos com
    /// registros zerados (R$ 129 mil)" — os 129 mil eram contagem de registros,
    /// e o resumo do Notion, que não tem verificador nenhum, acertou
    /// (FASE6 §1.6, defeito 2).
    /// </para>
    /// <para>
    /// <b>Deliberadamente burro.</b> Pula os conectores e pega a primeira
    /// palavra de conteúdo depois do número: "129 mil registros" → registros;
    /// "27.529 de produtos" → produtos. Não é análise sintática, e erra em
    /// frases tortas — mas errar aqui custa uma unidade estranha na lista, que
    /// o modelo pode ignorar, enquanto <b>não</b> ter a unidade custou um valor
    /// em reais que não existia.
    /// </para>
    /// <para>
    /// Número com <c>R$</c> ou <c>%</c> já carrega a unidade no próprio texto e
    /// não recebe outra — marcá-lo com o substantivo seguinte produziria
    /// "R$ 180 mil *(por)*".
    /// </para>
    /// </remarks>
    private static string UnidadeDe(string texto, Match numero)
    {
        if (numero.Value.Contains("R$") || numero.Value.Contains('%')
            || numero.Value.Contains("cento", StringComparison.OrdinalIgnoreCase))
            return "";

        string depois = texto[(numero.Index + numero.Length)..];

        // A unidade, quando existe, vem colada ao número na mesma oração. Uma
        // vírgula ou um ponto no meio já significa que o que vem depois é outra
        // coisa — sem este corte, "são 27.529 , e a gente conversou sobre X"
        // devolvia "gente" como unidade.
        int pontuacao = depois.IndexOfAny([',', ';', '.', '!', '?', ':']);
        if (pontuacao >= 0) depois = depois[..pontuacao];

        foreach (Match palavra in Regex.Matches(depois, @"[\p{L}]+"))
        {
            string p = palavra.Value;
            if (Conectores.Contains(p)) continue;
            // Só as primeiras palavras: se a de conteúdo estiver longe, o
            // número não está sendo medido por ela.
            if (palavra.Index > 24) return "";
            return p.ToLowerInvariant();
        }
        return "";
    }

    /// <summary>Palavras que ligam o número ao substantivo, e não são a unidade.</summary>
    private static readonly HashSet<string> Conectores =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "de", "do", "da", "dos", "das", "em", "no", "na", "nos", "nas",
            "e", "ou", "que", "com", "a", "o", "os", "as", "ao", "à", "aos",
            "para", "por", "um", "uma", "uns", "umas", "mais", "menos",
            "cerca", "aproximadamente", "quase", "só", "apenas", "tem", "são",
            "é", "foi", "está", "estão", "ficou", "ficaram", "deu", "dá",
            // A magnitude é parte do número, não a coisa medida. Ela já entra
            // na chave pelo regex; aqui ela só não pode virar a unidade.
            "mil", "milhão", "milhões", "milhao", "milhoes", "bilhão", "bilhões",
            // "a gente" é sujeito, e cai bem no lugar onde a unidade estaria.
            "gente",
        };

    private static string Normalizar(string t) =>
        string.Concat(t.Where(char.IsLetterOrDigit)).ToLowerInvariant();

    private static string Recorte(string texto, int posicao, int janela = 90)
    {
        int inicio = Math.Max(0, posicao - janela / 2);
        int fim = Math.Min(texto.Length, posicao + janela);
        string t = texto[inicio..fim].Trim();
        return (inicio > 0 ? "…" : "") + t + (fim < texto.Length ? "…" : "");
    }

    private static string Encurtar(string texto, int teto = 160) =>
        texto.Length <= teto ? texto : texto[..teto].TrimEnd() + "…";

    private static string Relogio(double segundos)
    {
        int s = (int)Math.Round(segundos);
        return $"{s / 60:00}:{s % 60:00}";
    }
}
