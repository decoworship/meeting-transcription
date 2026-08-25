using System.Text.RegularExpressions;

namespace MeetingApp.Nucleo.Atas;

/// <summary>
/// Quem se comprometeu, lido da transcrição em vez de deduzido pelo modelo.
/// </summary>
/// <remarks>
/// <para>
/// <b>O defeito que isto conserta é o mais caro que uma ata pode ter.</b> Na
/// reunião medida em 14/08/2026, a entrega de uma base de 27.529 registros
/// saiu com <c>"lado": "cliente"</c>. Quem se compromete na fala é o André, do
/// nosso lado: <i>"eu vou te mandar esse arquivo, tá bom?"</i>. Uma pendência
/// nossa arquivada como do cliente <b>não é cobrada de ninguém</b> — some das
/// duas listas ao mesmo tempo (FASE6 §1.6, defeito 1).
/// </para>
/// <para>
/// <b>Por que o verificador não pegava.</b> Ele fez o seu trabalho: viu que o
/// responsável "Vivo" não era participante e trocou por
/// <c>[responsável a definir]</c>. Só que ele confere o <i>dono</i>, e o lado
/// vem do dono — sem dono, <see cref="VerificadorDeAta"/> não tinha por onde
/// decidir e o palpite do modelo ficava de pé. O buraco não era a regra: era a
/// ordem em que as coisas se sabem.
/// </para>
/// <para>
/// <b>O sinal está na transcrição e é forte.</b> Quem diz "eu vou te mandar" é
/// o dono, e o lado dele já é conhecido pelo classificador de participantes da
/// Fase 3. Regra determinística embaixo, modelo por cima — o mesmo desenho da
/// correção fonética e do roteiro de fatos.
/// </para>
/// <para>
/// <b>Conservador por construção.</b> Atribuir dono errado é exatamente o erro
/// que o verificador existe para impedir, então aqui só se afirma com três
/// coisas ao mesmo tempo: primeira pessoa, compromisso explícito, e eco forte
/// do conteúdo da ação naquele trecho. Faltando qualquer uma, a ação continua
/// sem dono — que é o estado honesto.
/// </para>
/// </remarks>
public static class DonoPelaFala
{
    /// <summary>
    /// Quanto do conteúdo da ação precisa ecoar no trecho para valer.
    /// </summary>
    /// <remarks>
    /// Alto de propósito. Numa reunião de uma hora há dezenas de "eu te mando",
    /// e casar a ação com o errado troca uma pendência sem dono — que alguém lê
    /// e resolve — por uma pendência com o dono errado, que ninguém confere.
    /// </remarks>
    public const double EcoMinimo = 0.6;

    /// <summary>Fala em primeira pessoa: sem isto, "ele vai mandar" viraria compromisso de quem falou.</summary>
    private static readonly Regex PrimeiraPessoa = new(
        @"\b(eu|vou|posso|consigo|fa(ç|c)o|mando|envio|passo|gero|verifico|confirmo"
        + @"|deixa\s+comigo|pode\s+deixar|fico\s+de|me\s+encarrego)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>O verbo do compromisso propriamente dito.</summary>
    private static readonly Regex Compromisso = new(
        @"\b(mando|envio|passo|gero|fa(ç|c)o|confirmo|verifico|olho|preparo|monto"
        // O pronome no meio — "vou **te** mandar" — é a forma mais comum da
        // frase, e sem ele o casamento falhava justamente na fala que originou
        // este conserto: "eu vou te mandar esse arquivo, tá bom?".
        + @"|vou\s+((te|lhe|me|se|nos)\s+)?"
        + @"(mandar|enviar|passar|fazer|gerar|confirmar|verificar|olhar|preparar|montar)"
        + @"|deixa\s+comigo|pode\s+deixar|fico\s+de|me\s+encarrego)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Preenche o dono — e, com ele, o lado — das ações que ficaram sem.
    /// </summary>
    /// <param name="pessoas">
    /// Quem é da casa e quem é do cliente, pelo domínio do e-mail. Sem isto o
    /// lado não se afirma: é a mesma regra do <see cref="VerificadorDeAta"/>.
    /// </param>
    /// <param name="rotuloDoDono">
    /// Como o dono do microfone aparece nos segmentos antes de ser nomeado.
    /// </param>
    /// <returns>Uma linha por ação atribuída, para as observações.</returns>
    public static IReadOnlyList<string> Atribuir(
        AtaGerada ata, IReadOnlyList<SegmentoFinal> segmentos,
        IReadOnlyList<Pessoa> pessoas, string rotuloDoDono = "You")
    {
        var notas = new List<string>();
        if (segmentos.Count == 0 || pessoas.Count == 0) return notas;

        // Só os trechos em que alguém se compromete em primeira pessoa. São
        // poucos numa reunião, e reduzi-los antes é o que permite exigir eco
        // alto sem varrer a transcrição inteira por ação.
        var promessas = segmentos
            .Where(s => s.Speaker is { Length: > 0 }
                        && PrimeiraPessoa.IsMatch(s.Text)
                        && Compromisso.IsMatch(s.Text))
            .ToList();
        if (promessas.Count == 0) return notas;

        foreach (var acao in ata.Acoes)
        {
            string dono = acao.Responsavel.Trim();
            if (dono.Length > 0 && !dono.StartsWith('[')) continue;   // já tem dono

            var conteudo = Palavras(acao.Acao).ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (conteudo.Count == 0) continue;

            SegmentoFinal? melhor = null;
            double melhorEco = 0;
            foreach (var p in promessas)
            {
                var ditas = Palavras(p.Text).ToHashSet(StringComparer.OrdinalIgnoreCase);
                double eco = (double)conteudo.Count(ditas.Contains) / conteudo.Count;
                if (eco > melhorEco) { melhorEco = eco; melhor = p; }
            }

            if (melhor is null || melhorEco < EcoMinimo) continue;

            // O rótulo do falante vira pessoa. "You" é o dono do microfone, e
            // ele é da casa por construção — é a máquina dele que grava.
            var quem = QuemE(melhor.Speaker!, pessoas, rotuloDoDono);
            if (quem is null) continue;

            acao.Responsavel = quem.Nome;
            string ladoAntes = acao.Lado;
            acao.Lado = quem.Lado;

            notas.Add($"A pendência \"{Encurtar(acao.Acao)}\" estava sem responsável"
                      + (string.Equals(ladoAntes, quem.Lado, StringComparison.OrdinalIgnoreCase)
                          ? "" : $" e do lado \"{ladoAntes}\"")
                      + $". Na transcrição quem se compromete é {quem.Nome} — "
                      + $"\"{Encurtar(melhor.Text.Trim(), 60)}\". "
                      + $"Atribuída a {quem.Nome}, lado \"{quem.Lado}\".");
        }

        return notas;
    }

    /// <summary>O participante por trás de um rótulo de falante.</summary>
    /// <remarks>
    /// Casamento por primeiro nome, como no verificador: a fala usa "Vanessa" e
    /// a agenda traz "Vanessa Levorato". Falante ainda não nomeado
    /// ("Speaker 3") não casa com ninguém, e a ação segue sem dono.
    /// </remarks>
    private static Pessoa? QuemE(string falante, IReadOnlyList<Pessoa> pessoas,
                                 string rotuloDoDono)
    {
        if (PromptDeAta.EhRotuloGenerico(falante)) return null;

        if (string.Equals(falante, rotuloDoDono, StringComparison.OrdinalIgnoreCase))
            // A faixa do microfone é de quem gravou, e quem gravou é da casa.
            // Só vale quando a lista diz quem é: inventar um nome aqui seria o
            // mesmo erro que o verificador desfaz.
            return pessoas.FirstOrDefault(p => p.DaCasa == true);

        return pessoas.FirstOrDefault(
            p => p.Nome.Equals(falante, StringComparison.OrdinalIgnoreCase)
                 || PrimeiroNome(p.Nome).Equals(PrimeiroNome(falante),
                                                StringComparison.OrdinalIgnoreCase));
    }

    private static string PrimeiroNome(string n) =>
        n.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? n;

    /// <summary>Palavras de conteúdo, pela mesma régua do verificador.</summary>
    private static IEnumerable<string> Palavras(string texto) =>
        Regex.Matches(texto.ToLowerInvariant(), @"[\p{L}\p{Nd}]{4,}")
             .Select(m => m.Value)
             .Where(p => !Vazias.Contains(p));

    private static readonly HashSet<string> Vazias = new(StringComparer.OrdinalIgnoreCase)
    {
        "para", "como", "pelo", "pela", "isso", "esse", "essa", "está", "estão",
        "sobre", "quando", "porque", "então", "também", "ainda", "todos", "todas",
        "deve", "pode", "fazer", "sendo", "cada", "mais", "menos", "muito", "após",
        "entre", "durante", "aqui", "onde", "qual", "quais", "seja", "sejam",
        "vamos", "vou", "mandar", "enviar", "gente", "coisa", "certo",
    };

    private static string Encurtar(string t, int teto = 70) =>
        t.Length <= teto ? t : t[..teto].TrimEnd() + "…";
}
