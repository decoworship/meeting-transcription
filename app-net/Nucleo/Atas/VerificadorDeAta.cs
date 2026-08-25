using System.Text.RegularExpressions;

namespace MeetingApp.Nucleo.Atas;

/// <summary>
/// Confere a ata contra a reunião, antes de ela virar arquivo.
/// </summary>
/// <remarks>
/// <para>
/// É a peça que torna um modelo de 4B aceitável, e ela é <b>determinística</b>:
/// não pergunta ao modelo se ele acertou. Uma ata que inventa decisão é pior que
/// nenhuma ata, porque cria memória falsa — e a memória falsa só aparece meses
/// depois, quando alguém cobra o que nunca foi combinado.
/// </para>
/// <para>
/// <b>Nada aqui é silencioso.</b> Tudo o que o verificador muda vira uma linha
/// em "Observações", que é seção que a skill já prevê. Corrigir escondido seria
/// trocar um erro do modelo por um erro nosso, invisível.
/// </para>
/// </remarks>
public static class VerificadorDeAta
{
    /// <param name="conhecidos">
    /// Nomes que podem ser dono de ação: convidados da agenda e falantes
    /// reconhecidos. Rótulos genéricos ("Speaker 3") não entram.
    /// </param>
    public static AtaGerada Conferir(AtaGerada ata, IReadOnlyList<SegmentoFinal> segmentos,
                                     IReadOnlyList<string> conhecidos,
                                     IReadOnlyList<Fato> roteiro,
                                     IReadOnlyList<Pessoa>? pessoas = null)
    {
        var notas = new List<string>();

        // Primeiro de todos: o que o modelo mandou como seção em vez de campo
        // volta para o campo. Antes das outras conferências porque é isso que
        // põe o conteúdo recuperado debaixo delas — dono, lado e eco valem para
        // a decisão que chegou pelo caminho errado igual à que chegou certo.
        // Ver Nucleo/Atas/SecaoDobrada.cs.
        notas.AddRange(SecaoDobrada.Dobrar(ata));

        ConferirDonos(ata, conhecidos, notas);
        // Entre os dois de propósito: o de cima acabou de esvaziar os donos que
        // o modelo inventou, e é justamente aí que a fala tem o que dizer. O de
        // baixo confere o lado a partir do dono — e agora encontra dono onde
        // antes só havia "[responsável a definir]". Ver DonoPelaFala.
        notas.AddRange(DonoPelaFala.Atribuir(ata, segmentos, pessoas ?? []));
        ConferirLados(ata, pessoas ?? [], notas);
        ConferirDecisoes(ata, segmentos, notas);
        ConferirRiscos(ata, segmentos, notas);
        ConferirOmissoes(ata, roteiro, notas);

        // ── As observações do modelo NÃO ficam ──────────────────────────────
        //
        // Ficavam, e a seção se contradizia dentro de si mesma. Medido em
        // 14/08/2026 (FASE6 §1.6, defeito 4): a ata afirmava "todos foram
        // registrados com precisão conforme o contexto" e, quatro linhas
        // abaixo, listava onze números que não apareciam nela. E inventava
        // leitura — "o termo 'SVA' foi corrigido para 'suspensão temporária'",
        // quando na reunião são duas coisas distintas que alguém pede para
        // relembrar.
        //
        // A conferência de cobertura é determinística e boa; o problema era
        // deixar o **modelo narrar** o que ela achou. Um documento cujo valor é
        // ser auditável não pode ter, na mesma seção, uma medição e uma opinião
        // sobre a medição — quem lê não tem como saber qual das duas é a fonte.
        //
        // O que se perde: as observações do modelo às vezes eram úteis ("o áudio
        // some entre 12:03 e 12:07"). Perder isso é aceitável porque não havia
        // como separar o útil do inventado, e o inventado tinha a mesma cara de
        // certeza. Se voltar a fazer falta, o caminho é um campo próprio para
        // problemas de áudio, verificável contra o silêncio das faixas — não
        // prosa livre.
        ata.Observacoes = notas;
        return ata;
    }

    /// <summary>
    /// Dono que não é ninguém da reunião vira <c>[responsável a definir]</c>.
    /// </summary>
    /// <remarks>
    /// Um dono inventado é pior que nenhum dono: a tarefa fica com cara de
    /// atribuída e não é cobrada de ninguém. Comparação por nome próprio solto —
    /// "Vanessa" casa com "Vanessa Levorato" —, porque a fala usa o primeiro
    /// nome e a agenda usa o completo.
    /// </remarks>
    private static void ConferirDonos(AtaGerada ata, IReadOnlyList<string> conhecidos,
                                      List<string> notas)
    {
        var validos = conhecidos
            .Where(n => n is { Length: > 0 } && !PromptDeAta.EhRotuloGenerico(n))
            .ToList();
        // O ponto vale por espaço. O modelo mistura os dois estilos que a
        // agenda lhe mostrou e escreve "vanessa.levorato sao bernado"; sem esta
        // tolerância o verificador não a reconhece e apaga um dono CERTO —
        // medido numa ata gerada de ponta a ponta em 25/08.
        var primeiros = validos.Select(n => Pedacos(n)[0])
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var acao in ata.Acoes)
        {
            // O modelo repete o rótulo dentro do valor — "Responsável: Fulano",
            // "prazo: amanhã" — e o redator, que já escreve o rótulo, produzia
            // "**Responsável: Fulano**". Medido na primeira geração de ponta a
            // ponta. Limpar aqui é mais barato que pedir ao modelo que não faça.
            acao.Responsavel = SemRotulo(acao.Responsavel, "responsável", "responsavel");
            acao.Prazo = ComColchetes(SemRotulo(acao.Prazo, "prazo"));

            string dono = acao.Responsavel.Trim();
            if (dono.Length == 0 || dono.StartsWith('['))
            {
                acao.Responsavel = "[responsável a definir]";
                continue;
            }

            bool conhecido = validos.Any(
                v => v.Contains(dono, StringComparison.OrdinalIgnoreCase)
                     || dono.Contains(v, StringComparison.OrdinalIgnoreCase))
                || Pedacos(dono).Any(primeiros.Contains);

            // "Andre Monlevade (Vivo)" quando Andre é da nossa equipe: o modelo
            // deduz organização do contexto e erra. O nome fica; a organização
            // inventada sai, porque a única fonte confiável dela é a agenda —
            // que traz e-mail, e domínio de e-mail diz de que lado a pessoa é.
            if (conhecido && dono.Contains('(') && dono.Contains(')'))
            {
                string semOrg = dono[..dono.IndexOf('(')].Trim();
                if (semOrg.Length > 0 && validos.Any(
                        v => v.Contains(semOrg, StringComparison.OrdinalIgnoreCase)
                             || semOrg.Contains(v, StringComparison.OrdinalIgnoreCase)))
                    acao.Responsavel = semOrg;
            }

            if (!conhecido)
            {
                notas.Add($"A ação \"{Encurtar(acao.Acao)}\" vinha atribuída a "
                          + $"\"{dono}\", que não é participante desta reunião. "
                          + "Trocado por [responsável a definir].");
                acao.Responsavel = "[responsável a definir]";
            }
        }
    }

    /// <summary>
    /// De que lado a pendência está, pelo domínio do e-mail e não por dedução.
    /// </summary>
    /// <remarks>
    /// O modelo põe do lado do cliente quem ele acha que é do cliente, e o que
    /// ele acha vem do assunto da conversa: numa reunião que fala de Vivo o
    /// tempo todo, alguém da equipe vira "Andre Monlevade (Vivo)". Quando a
    /// agenda deu o e-mail, quem decide é o domínio.
    /// </remarks>
    private static void ConferirLados(AtaGerada ata, IReadOnlyList<Pessoa> pessoas,
                                      List<string> notas)
    {
        var sabidos = pessoas.Where(p => p.DaCasa is not null).ToList();
        if (sabidos.Count == 0) return;      // sem e-mail não se afirma nada

        int trocados = 0;
        foreach (var acao in ata.Acoes)
        {
            string dono = acao.Responsavel.Trim();
            if (dono.StartsWith('[')) continue;

            var quem = sabidos.FirstOrDefault(
                p => p.Nome.Contains(dono, StringComparison.OrdinalIgnoreCase)
                     || dono.Contains(p.Nome, StringComparison.OrdinalIgnoreCase)
                     || PrimeiroNome(p.Nome).Equals(PrimeiroNome(dono),
                                                    StringComparison.OrdinalIgnoreCase));
            if (quem is null) continue;

            if (!string.Equals(acao.Lado, quem.Lado, StringComparison.OrdinalIgnoreCase))
            {
                acao.Lado = quem.Lado;
                trocados++;
            }
        }

        if (trocados > 0)
            notas.Add($"{trocados} pendência(s) mudaram de lado: quem é da casa e quem é do "
                      + "cliente foi decidido pelo domínio do e-mail da agenda, e não pelo "
                      + "assunto da conversa.");
    }

    /// <summary>As partes de um nome, com ponto e sublinhado valendo espaço.</summary>
    private static string[] Pedacos(string n) =>
        (n ?? "").Replace('.', ' ').Replace('_', ' ')
            .Split(' ', StringSplitOptions.RemoveEmptyEntries) is { Length: > 0 } p
            ? p : [""];

    private static string PrimeiroNome(string n) =>
        n.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? n;

    /// <summary>
    /// Decisão sem eco na transcrição desce para "Pontos em aberto".
    /// </summary>
    /// <remarks>
    /// <para>
    /// A regra é grosseira de propósito: exige que as palavras de conteúdo da
    /// decisão apareçam de fato na conversa. Não é entendimento — é uma rede, e
    /// uma rede grosseira que pega metade dos casos vale mais que nenhuma.
    /// </para>
    /// <para>
    /// Promover hipótese a decisão é o erro que estraga ata, e a própria skill o
    /// chama de "o erro mais frequente em ata automática".
    /// </para>
    /// </remarks>
    private static void ConferirDecisoes(AtaGerada ata, IReadOnlyList<SegmentoFinal> segmentos,
                                         List<string> notas)
    {
        var faladas = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var s in segmentos)
            foreach (string p in Palavras(s.Text))
                faladas.Add(p);

        var sobreviventes = new List<string>();
        foreach (string decisao in ata.Decisoes)
        {
            var palavras = Palavras(decisao).ToList();
            if (palavras.Count == 0) continue;

            int eco = palavras.Count(p => Ecoa(p, faladas));
            double proporcao = (double)eco / palavras.Count;

            if (proporcao < 0.5)
            {
                notas.Add($"\"{Encurtar(decisao)}\" foi registrada como decisão, mas quase "
                          + "nada dela aparece na transcrição. Movida para pontos em aberto "
                          + "— confira antes de tratar como combinada.");
                ata.PontosEmAberto.Add(decisao);
            }
            else
            {
                sobreviventes.Add(decisao);
            }
        }
        ata.Decisoes = sobreviventes;
    }

    /// <summary>
    /// Risco que ninguém levantou não é risco.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A skill é explícita: riscos entram "quando alguém sinalizou preocupação
    /// com prazo, dado, capacidade ou dependência externa. Se ninguém levantou
    /// risco, omita a seção — seção vazia treina o leitor a ignorar".
    /// </para>
    /// <para>
    /// Medido: o modelo preenche a seção de qualquer jeito, com riscos
    /// plausíveis e genéricos ("risco de inconsistência nos dados se os filtros
    /// não forem aplicados corretamente") que ninguém disse. É invenção com cara
    /// de zelo, e é pior que omissão porque parece conteúdo. A mesma régua das
    /// decisões: sem eco na fala, cai.
    /// </para>
    /// </remarks>
    private static void ConferirRiscos(AtaGerada ata, IReadOnlyList<SegmentoFinal> segmentos,
                                       List<string> notas)
    {
        if (ata.Riscos.Count == 0) return;

        var faladas = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var s in segmentos)
            foreach (string p in Palavras(s.Text))
                faladas.Add(p);

        var sobreviventes = new List<string>();
        int caidos = 0;
        foreach (string risco in ata.Riscos)
        {
            var palavras = Palavras(risco).ToList();
            if (palavras.Count == 0) continue;

            if ((double)palavras.Count(p => Ecoa(p, faladas)) / palavras.Count >= 0.5)
                sobreviventes.Add(risco);
            else
                caidos++;
        }

        if (caidos > 0)
            notas.Add($"{caidos} risco(s) foram retirados por não terem sido levantados "
                      + "por ninguém na reunião — a ata registra preocupação dita, "
                      + "não preocupação possível.");
        ata.Riscos = sobreviventes;
    }

    /// <summary>
    /// O que a reunião disse e a ata não repetiu.
    /// </summary>
    /// <remarks>
    /// A rede contra o modo de falha real do modelo pequeno: ele não inventa,
    /// ele esquece. Metade dos números de uma reunião medida ficou de fora, e o
    /// impacto financeiro estava entre eles (ATA.md §8). Aqui não se corrige
    /// nada — lista-se, e quem decide se faltou é quem leu a reunião.
    /// </remarks>
    private static void ConferirOmissoes(AtaGerada ata, IReadOnlyList<Fato> roteiro,
                                         List<string> notas)
    {
        if (roteiro.Count == 0) return;

        string tudo = string.Join("\n", new[] { ata.Resumo }
            .Concat(ata.Secoes.Select(s => $"{s.Titulo} {s.Texto}"))
            .Concat(ata.Decisoes)
            .Concat(ata.Acoes.Select(a => a.Acao))
            .Concat(ata.PontosEmAberto)
            .Concat(ata.Riscos));

        var faltando = RoteiroDeFatos.NaoIncorporados(roteiro, tudo);
        if (faltando.Count == 0) return;

        // Teto na listagem: numa reunião longa, metade dos números é contexto de
        // fala ("o 500 mega"), e uma observação com 40 itens não é lida.
        var mostrar = faltando.Take(12).ToList();

        notas.Add("Números citados na reunião que não aparecem nesta ata: "
                  + string.Join(", ", mostrar)
                  + (faltando.Count > mostrar.Count ? $" (e mais {faltando.Count - mostrar.Count})" : "")
                  + ". Confira se algum deveria estar aqui.");
    }

    /// <summary>
    /// "prazo a definir" vira "[prazo a definir]".
    /// </summary>
    /// <remarks>
    /// Os colchetes não são enfeite: eles marcam a lacuna como lacuna, e é o que
    /// diferencia "ninguém deu prazo" de um prazo chamado "a definir". A skill
    /// pede a forma exata; o modelo escreve sem, às vezes.
    /// </remarks>
    private static string ComColchetes(string prazo)
    {
        string t = prazo.Trim();
        if (t.StartsWith('[')) return t;
        return t.Equals("prazo a definir", StringComparison.OrdinalIgnoreCase)
               || t.Equals("a definir", StringComparison.OrdinalIgnoreCase)
               || t.Equals("sem prazo", StringComparison.OrdinalIgnoreCase)
            ? "[prazo a definir]" : t;
    }

    /// <summary>Tira o rótulo que o modelo repetiu dentro do valor.</summary>
    private static string SemRotulo(string valor, params string[] rotulos)
    {
        string t = valor.Trim();
        foreach (string r in rotulos)
        {
            foreach (string forma in new[] { $"{r}:", $"**{r}:**", $"**{r}**:" })
                if (t.StartsWith(forma, StringComparison.OrdinalIgnoreCase))
                    return t[forma.Length..].Trim();
        }
        return t;
    }

    /// <summary>Palavras de conteúdo: sem as vazias, que casam com qualquer coisa.</summary>
    private static IEnumerable<string> Palavras(string texto) =>
        Regex.Matches(texto.ToLowerInvariant(), @"[\p{L}\p{Nd}]{4,}")
             .Select(m => m.Value)
             .Where(p => !Vazias.Contains(p));

    /// <summary>
    /// A palavra da ata ecoa alguma da fala, tolerando flexão.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Nasceu de três falsos positivos medidos</b>, em três atas diferentes.
    /// A régua comparava palavra inteira, e o modelo reescreve o que ouviu:
    /// a fala diz "tem que ser <i>casado</i>" e a ata escreve "tratadas de forma
    /// <i>casada</i>"; a fala diz "<i>desconto</i>" e "<i>diferença</i>", a ata
    /// escreve "<i>descontos</i>" e "<i>diferenças</i>". Nenhuma casava, e
    /// decisões reais eram rebaixadas para "pontos em aberto" com uma frase que
    /// soa autoritativa: <i>"quase nada dela aparece na transcrição"</i>.
    /// </para>
    /// <para>
    /// <b>Prefixo comum, e não radical de verdade.</b> Um stemmer português
    /// completo erraria mais do que ganharia aqui — e esta rede é grosseira de
    /// propósito. Duas palavras ecoam quando compartilham pelo menos quatro
    /// letras iniciais <b>e</b> 60% da mais longa: <c>casada</c>/<c>casado</c>
    /// (5 de 6), <c>desconto</c>/<c>descontos</c> (8 de 9),
    /// <c>igual</c>/<c>iguais</c> (4 de 6).
    /// </para>
    /// <para>
    /// <b>Os 60% são o que impede a rede de virar peneira.</b> Sem eles,
    /// <c>conta</c> casaria com <c>contrato</c> e <c>financeiro</c> com
    /// <c>finalizar</c> — e uma decisão inventada passaria por qualquer
    /// transcrição que falasse do mesmo assunto. Medido: com o corte,
    /// decisão de uma reunião continua sendo rejeitada contra a transcrição
    /// de outra.
    /// </para>
    /// </remarks>
    private static bool Ecoa(string palavra, IReadOnlyCollection<string> faladas)
    {
        if (faladas.Contains(palavra)) return true;

        foreach (string dita in faladas)
        {
            int comum = 0;
            int teto = Math.Min(palavra.Length, dita.Length);
            while (comum < teto && palavra[comum] == dita[comum]) comum++;

            if (comum >= 4 && comum >= 0.6 * Math.Max(palavra.Length, dita.Length))
                return true;
        }
        return false;
    }

    private static readonly HashSet<string> Vazias = new(StringComparer.OrdinalIgnoreCase)
    {
        "para", "como", "pelo", "pela", "isso", "esse", "essa", "está", "estão",
        "sobre", "quando", "porque", "então", "também", "ainda", "todos", "todas",
        "deve", "pode", "fazer", "sendo", "cada", "mais", "menos", "muito", "após",
        "entre", "durante", "aqui", "onde", "qual", "quais", "seja", "sejam",
    };

    private static string Encurtar(string t, int teto = 70) =>
        t.Length <= teto ? t : t[..teto].TrimEnd() + "…";
}
