using System.Text;

namespace MeetingApp.Nucleo.Atas;

/// <summary>
/// O JSON conferido virando o <c>ata.md</c> que a pessoa lê.
/// </summary>
/// <remarks>
/// <para>
/// <b>Quem escreve o arquivo é o C#, não o modelo</b> (ATA.md §3). Com isso o
/// formato é garantido em vez de pedido: o item de ação sai
/// <c>- [ ] Ação — **Responsável** — prazo</c> porque é assim que este arquivo
/// o escreve, e não porque o modelo lembrou. Medido: sem isto, o mesmo modelo
/// escreveu <c>**Responsável: Fulano**</c>.
/// </para>
/// <para>
/// <b>Seção vazia não é escrita.</b> É regra da skill — "seção vazia treina o
/// leitor a ignorar" — e aqui ela é aplicada por construção, em vez de depender
/// de o modelo se lembrar de omitir.
/// </para>
/// </remarks>
public static class RedatorDeAta
{
    public static string Escrever(AtaGerada ata, ModeloDeAta tipo, ContextoDaReuniao ctx,
                                  FormatoDaAta formato = FormatoDaAta.Classico)
    {
        var sb = new StringBuilder();
        // Por tema, com ou sem a marca do tipo: sem prazo vazio, sem o dono
        // repetido no texto e sem as observações, que vão para um arquivo à
        // parte (ATA-5). O clássico é o de antes, byte a byte.
        bool limpo = formato == FormatoDaAta.PorTema;

        string titulo = ctx.Titulo is { Length: > 0 } ? ctx.Titulo
            : string.Join(" · ", new[] { ctx.Cliente, ctx.Projeto }
                .Where(x => x is { Length: > 0 }));
        sb.AppendLine($"# Ata — {(titulo.Length > 0 ? titulo : tipo.Nome)}");
        sb.AppendLine();

        var cabecalho = new List<string>();
        if (ctx.Data is { Length: > 0 })
            cabecalho.Add($"**Data:** {Cabecalho.DataLegivel(ctx.Data)}");
        if (ctx.DuracaoS > 0)
            cabecalho.Add($"**Duração:** {Cabecalho.Duracao(ctx.DuracaoS)}");
        if (ctx.Cliente is { Length: > 0 }) cabecalho.Add($"**Cliente:** {ctx.Cliente}");
        if (ctx.Projeto is { Length: > 0 }) cabecalho.Add($"**Projeto:** {ctx.Projeto}");
        if (cabecalho.Count > 0) sb.AppendLine(string.Join(" · ", cabecalho));

        // Só gente: "Speaker 3" na lista de participantes faz a ata parecer
        // escrita por quem não estava lá — e foi o que aconteceu na medição.
        sb.AppendLine(Participantes(ctx));

        sb.AppendLine();

        if (ata.Resumo.Trim().Length > 0)
        {
            sb.AppendLine("## Resumo");
            sb.AppendLine();
            sb.AppendLine(ata.Resumo.Trim());
            sb.AppendLine();
        }

        if (PromptDeAta.Agrupa(tipo, formato))
        {
            PorTema(sb, ata, tipo);
            return sb.ToString().TrimEnd() + "\n";
        }

        // A ORDEM é a do esqueleto do tipo, e não a que o modelo devolveu.
        //
        // É para isto que o esqueleto da referência virou lista de seções: numa
        // ata de update, "Próxima reunião" é a última linha, e o modelo a
        // devolvia no meio. Seguir a ordem do arquivo do tipo é o que faz
        // customizar um tipo mudar de verdade a cara da ata.
        var pendentes = ata.Secoes.Where(s => s.Texto.Trim().Length > 0).ToList();
        var escritas = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (string doEsqueleto in tipo.Secoes)
        {
            if (EhCanonica(doEsqueleto))
            {
                if (limpo && Canonizar(doEsqueleto) == "Observações sobre a transcrição") continue;
                Canonica(sb, doEsqueleto, ata, escritas, limpo);
                continue;
            }

            var achada = pendentes.FirstOrDefault(s => Parecido(s.Titulo, doEsqueleto));
            if (achada is null) continue;
            pendentes.Remove(achada);
            Secao(sb, achada);
        }

        // O que o modelo inventou de seção e não estava no esqueleto entra
        // depois: pode ser útil, e jogar fora conteúdo é pior que desarrumá-lo.
        foreach (var s in pendentes)
        {
            if (EhCanonica(s.Titulo)) continue;
            Secao(sb, s);
        }

        // E o que o esqueleto não mencionou, mas existe, fecha a ata. As
        // observações são sempre as últimas: são rodapé, não conteúdo.
        foreach (string canonica in new[]
                 { "Decisões", "Pendências", "Pontos em aberto", "Riscos e alertas" })
            Canonica(sb, canonica, ata, escritas, limpo);
        if (!limpo)
            Canonica(sb, "Observações sobre a transcrição", ata, escritas, limpo);

        return sb.ToString().TrimEnd() + "\n";
    }

    /// <summary>
    /// Os participantes, agrupados por organização quando dá para saber.
    /// </summary>
    /// <remarks>
    /// "Participantes: [agrupados por organização]" é o que o esqueleto da ata
    /// de cliente pede, e agora dá para cumprir: o domínio do e-mail da agenda
    /// diz de que lado cada um está.
    /// </remarks>
    private static string Participantes(ContextoDaReuniao ctx)
    {
        var quadro = Presenca.Cruzar(ctx.Pessoas, ctx.Falantes);

        // Sem como separar, o cabeçalho é o de antes: listar todo mundo junto é
        // menos errado que dizer que ninguém falou.
        if (!quadro.DaParaSeparar)
            return PorOrganizacao("Participantes", ctx.Pessoas, ctx) is { Length: > 0 } linha
                ? linha
                : Todos(ctx);

        // Duas linhas, e não três: a lista inteira em cima, quem falou embaixo,
        // e quem não falou o leitor tira da diferença. Uma terceira linha
        // dizendo "não falaram" gasta espaço para repetir o que a subtração já
        // diz — e obriga a ata a afirmar ausência, que é mais do que a
        // transcrição sabe.
        var linhas = new List<string> { PorOrganizacao("Convidados", ctx.Pessoas, ctx) };

        // Na ordem do convite, e não na ordem em que falaram: é o que faz a
        // diferença entre as duas linhas ser vista de relance.
        var falaram = ctx.Pessoas.Where(quadro.Falaram.Contains).Select(p => p.Nome).ToList();
        string segunda = falaram.Count > 0 ? $"**Falaram:** {string.Join(", ", falaram)}" : "";

        if (quadro.NaoIdentificados > 0)
            segunda += (segunda.Length > 0 ? " · " : "")
                       + $"**+{quadro.NaoIdentificados}** "
                       + (quadro.NaoIdentificados == 1
                           ? "falante não identificado" : "falantes não identificados");
        linhas.Add(segunda);

        return string.Join("\n", linhas.Where(l => l.Length > 0));
    }

    /// <summary>Uma linha de gente, separada por lado quando os dois existem.</summary>
    private static string PorOrganizacao(string rotulo, IReadOnlyList<Pessoa> gente,
                                         ContextoDaReuniao ctx)
    {
        var casa = gente.Where(p => p.DaCasa == true).Select(p => p.Nome).ToList();
        var cliente = gente.Where(p => p.DaCasa == false).Select(p => p.Nome).ToList();

        if (casa.Count > 0 && cliente.Count > 0)
        {
            string nomeDoCliente = ctx.Cliente is { Length: > 0 } c ? c : "cliente";
            return $"**{rotulo}:** {string.Join(", ", casa)} · "
                   + $"**{nomeDoCliente}:** {string.Join(", ", cliente)}";
        }

        var todos = gente.Select(p => p.Nome).ToList();
        return todos.Count > 0 ? $"**{rotulo}:** {string.Join(", ", todos)}" : "";
    }

    private static string Todos(ContextoDaReuniao ctx)
    {
        var todos = ctx.Pessoas.Select(p => p.Nome)
            .Concat(ctx.Convidados).Concat(ctx.Falantes)
            .Where(n => n is { Length: > 0 } && !PromptDeAta.EhRotuloGenerico(n))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        return todos.Count > 0 ? $"**Participantes:** {string.Join(", ", todos)}" : "";
    }

    private static void Secao(StringBuilder sb, SecaoDaAta secao)
    {
        sb.AppendLine($"## {secao.Titulo.Trim()}");
        sb.AppendLine();
        if (secao.Situacao is { Length: > 0 } situacao)
        {
            sb.AppendLine($"**Situação:** {situacao.Trim()}");
            sb.AppendLine();
        }
        sb.AppendLine(secao.Texto.Trim());
        sb.AppendLine();
    }

    /// <summary>Escreve uma seção de campo próprio, uma vez só.</summary>
    /// <remarks>
    /// <b>A chave do "já escrevi" é o nome de saída, não o de entrada.</b> Era o
    /// de entrada, e o efeito aparecia em toda ata de sessão de trabalho: o
    /// esqueleto pede "Ações", que o redator escreve como "## Pendências" e
    /// registra sob a chave <c>acoes</c>; no fim, a lista fixa pedia
    /// "Pendências", que não estava registrada — e a mesma lista saía duas vezes
    /// no documento. Medido em 17/08/2026, e por um bom tempo confundido com
    /// mania do modelo.
    /// </remarks>
    private static void Canonica(StringBuilder sb, string titulo, AtaGerada ata,
                                 HashSet<string> escritas, bool limpo)
    {
        if (Canonizar(titulo) is not { } canonica) return;
        if (!escritas.Add(canonica)) return;

        switch (canonica)
        {
            case "Decisões": Lista(sb, "Decisões", Textos(ata.Decisoes)); break;
            case "Pendências": Acoes(sb, ata.Acoes, limpo); break;
            case "Pontos em aberto": Lista(sb, "Pontos em aberto", Textos(ata.PontosEmAberto)); break;
            case "Riscos e alertas": Lista(sb, "Riscos e alertas", Textos(ata.Riscos)); break;
            case "Observações sobre a transcrição":
                Lista(sb, "Observações sobre a transcrição", ata.Observacoes); break;
        }
    }

    private static bool Parecido(string a, string b) =>
        Normalizar(a) == Normalizar(b)
        || Normalizar(a).StartsWith(Normalizar(b), StringComparison.Ordinal)
        || Normalizar(b).StartsWith(Normalizar(a), StringComparison.Ordinal);

    private static string Normalizar(string t)
    {
        string semAcento = t.ToLowerInvariant()
            .Replace('á', 'a').Replace('ã', 'a').Replace('â', 'a')
            .Replace('é', 'e').Replace('ê', 'e').Replace('í', 'i')
            .Replace('ó', 'o').Replace('õ', 'o').Replace('ô', 'o')
            .Replace('ú', 'u').Replace('ç', 'c');
        return string.Concat(semAcento.Where(c => char.IsLetterOrDigit(c) || c == ' ')).Trim();
    }

    /// <summary>
    /// Títulos que já têm campo próprio e não podem vir por <c>secoes</c>.
    /// </summary>
    /// <remarks>
    /// Não é lista de proibições ao modelo — é desempate na hora de escrever.
    /// Ele pode continuar preenchendo os dois; o arquivo sai com um só, e com o
    /// que é verificável (o campo), não com a prosa.
    /// </remarks>
    /// <summary>
    /// O nome canônico da seção que o redator escreve, ou <c>null</c> se o
    /// título é corpo do tipo e vem do modelo.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Casamento exato, e não por prefixo.</b> A versão anterior aceitava
    /// qualquer título começando com <c>decis</c>, e com isso engolia
    /// <b>"Decisões técnicas"</b> — que não é a lista de decisões, é o raciocínio
    /// por trás delas, com alternativas descartadas e implicações. Ou seja: a
    /// seção mais valiosa da ata de sessão de trabalho era substituída pela lista
    /// simples, em silêncio. Medido em 17/08/2026.
    /// </para>
    /// <para>
    /// Os sinônimos entram na lista um a um, de propósito. É mais chato de manter
    /// e não tem como pegar por engano o título que só se parece.
    /// </para>
    /// </remarks>
    private static string? Canonizar(string titulo) => Normalizar(titulo) switch
    {
        "resumo" => "Resumo",
        "decisoes" => "Decisões",
        "pendencias" or "acoes" or "acao" or "acoes imediatas"
            or "action items" or "action item" => "Pendências",
        "pontos em aberto" => "Pontos em aberto",
        "riscos" or "riscos e alertas" or "riscos identificados" => "Riscos e alertas",
        "observacoes" or "observacoes sobre a transcricao" =>
            "Observações sobre a transcrição",
        _ => null,
    };

    private static bool EhCanonica(string titulo) => Canonizar(titulo) is not null;

    /// <summary>
    /// O nome canônico de um título, ou <c>null</c> quando ele é corpo do tipo.
    /// </summary>
    /// <remarks>
    /// Público para o <see cref="SecaoDobrada"/>: quem decide o que é seção de
    /// campo próprio é este arquivo, e ter a régua em dois lugares faria as duas
    /// divergirem no dia em que um sinônimo novo aparecesse.
    /// </remarks>
    public static string? NomeCanonico(string titulo) => Canonizar(titulo);

    private static List<string> Textos(IEnumerable<ItemDaAta> itens) => [.. itens.Select(i => i.Texto)];

    private static void Lista(StringBuilder sb, string titulo, IReadOnlyList<string> itens)
    {
        var uteis = itens.Where(i => i.Trim().Length > 0).ToList();
        if (uteis.Count == 0) return;

        sb.AppendLine($"## {titulo}");
        sb.AppendLine();
        foreach (string i in uteis) sb.AppendLine($"- {i.Trim()}");
        sb.AppendLine();
    }

    /// <summary>
    /// As pendências, separadas por lado quando houver os dois.
    /// </summary>
    /// <remarks>
    /// "Um item que depende do cliente misturado na lista geral desaparece;
    /// separado, ele é cobrável na próxima reunião" — a skill chama isso de a
    /// razão de existir da ata de update. Quando só há um lado, a divisão vira
    /// ruído e some.
    /// </remarks>
    private static void Acoes(StringBuilder sb, IReadOnlyList<AcaoDaAta> acoes, bool limpo)
    {
        var uteis = acoes.Where(a => a.Acao.Trim().Length > 0).ToList();
        if (uteis.Count == 0) return;

        sb.AppendLine("## Pendências");
        sb.AppendLine();

        var nossas = uteis.Where(a => !EhDoCliente(a)).ToList();
        var doCliente = uteis.Where(EhDoCliente).ToList();

        if (nossas.Count > 0 && doCliente.Count > 0)
        {
            sb.AppendLine("### Do nosso lado");
            sb.AppendLine();
            foreach (var a in nossas) sb.AppendLine(Linha(a, limpo));
            sb.AppendLine();
            sb.AppendLine("### Do lado do cliente");
            sb.AppendLine();
            foreach (var a in doCliente) sb.AppendLine(Linha(a, limpo));
        }
        else
        {
            foreach (var a in uteis) sb.AppendLine(Linha(a, limpo));
        }
        sb.AppendLine();
    }

    private static bool EhDoCliente(AcaoDaAta a) =>
        string.Equals(a.Lado, "cliente", StringComparison.OrdinalIgnoreCase);

    /// <summary>O formato que a skill pede, escrito por nós e não pelo modelo.</summary>
    /// <param name="limpo">
    /// Formato por tema: sem "[prazo a definir]" — sete vezes na ata de 22/09 —
    /// e sem o dono repetido no começo do texto.
    /// </param>
    private static string Linha(AcaoDaAta a, bool limpo)
    {
        string dono = a.Responsavel.Trim() is { Length: > 0 } d ? d : "[responsável a definir]";
        string prazo = a.Prazo.Trim() is { Length: > 0 } p ? p : "[prazo a definir]";
        string acao = a.Acao.Trim();
        if (!limpo) return $"- [ ] {acao} — **{dono}** — {prazo}";

        acao = SemDonoNoComeco(acao, dono).TrimEnd('.');
        bool semPrazo = prazo.StartsWith('[');
        return $"- [ ] {acao} — **{dono}**" + (semPrazo ? "" : $" — {prazo}");
    }

    /// <summary>"Andre Yuri investigar…" → "Investigar…", quando o dono já está no campo.</summary>
    /// <remarks>
    /// <para>
    /// Corta o primeiro nome do dono e, depois dele, só as palavras que também
    /// são do nome — o modelo às vezes abrevia ("Marina Lopes" para "Marina
    /// Costa Lopes"). Uma sigla logo depois do nome não é nome, e fica. Sem
    /// acento dos dois lados: "Andre" e "André" são a mesma pessoa.
    /// </para>
    /// <para>
    /// Exige a palavra inteira ("Ana" não corta "Analisar"), e não corta nada
    /// quando o nome vem seguido de "e", "com", "junto"… — "Ana e Bruno
    /// revisarem" é uma ação de duas pessoas, e tirar a primeira a desmente.
    /// Se não sobra nada depois do nome, o texto fica.
    /// </para>
    /// </remarks>
    public static string SemDonoNoComeco(string acao, string dono)
    {
        var nome = dono.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (nome.Length == 0 || nome[0].StartsWith('[')) return acao;
        var partes = nome.Select(Normalizar).ToHashSet(StringComparer.Ordinal);

        var palavras = acao.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (palavras.Length < 2 || Normalizar(palavras[0]) != Normalizar(nome[0]))
            return acao;

        int i = 1;
        while (i < palavras.Length && partes.Contains(Normalizar(palavras[i]))) i++;
        if (i >= palavras.Length || Conectivos.Contains(Normalizar(palavras[i]))) return acao;

        string resto = string.Join(' ', palavras[i..]);
        return char.ToUpper(resto[0]) + resto[1..];
    }

    private static readonly HashSet<string> Conectivos = new(StringComparer.Ordinal)
    {
        "e", "com", "junto", "mais", "ou", "",
    };

    /// <summary>
    /// O <c>ata.observacoes.md</c>: o recado do verificador para quem gerou a ata.
    /// </summary>
    /// <remarks>
    /// No formato por tema ele sai do <c>ata.md</c>, porque o <c>ata.md</c> é o
    /// que se exporta e vai para o cliente — e a de 22/09 foi com "confira se
    /// algum deveria estar aqui" dentro. A tela da ata o mostra à parte.
    /// </remarks>
    public static string? Observacoes(AtaGerada ata)
    {
        var sb = new StringBuilder();
        Lista(sb, "Observações sobre a transcrição", ata.Observacoes);
        return sb.Length > 0 ? sb.ToString().TrimEnd() + "\n" : null;
    }

    // ─────────────────────────────────────────────── a ata por tema (ATA-5)

    /// <summary>
    /// O corpo da ata com cada item dentro do tema dele.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A ata clássica é organizada pelo tipo de informação, e cada tema
    /// reaparece em cada seção — o pedido mínimo, em sete lugares na de
    /// 22/09/2026. Aqui o tema é a unidade, e a decisão, a ação e o ponto em
    /// aberto moram dentro dele.
    /// </para>
    /// <para>
    /// <b>O item que não casa com nenhum tema não some:</b> vai para a lista
    /// geral, sob os títulos de sempre. Fora do lugar é defeito visível; perdido
    /// é o pior defeito de uma ata (ver <see cref="SecaoDobrada"/>).
    /// </para>
    /// </remarks>
    private static void PorTema(StringBuilder sb, AtaGerada ata, ModeloDeAta tipo)
    {
        var rodape = Rodape(tipo);
        // Título vazio não é rodapé: o vazio é prefixo de tudo, e casaria com o
        // primeiro título do esqueleto — tirando do corpo o que é corpo.
        bool NoRodape(SecaoDaAta s, string r) =>
            Normalizar(s.Titulo).Length > 0 && Parecido(s.Titulo, r);
        var corpo = ata.Secoes
            .Where(s => !EhCanonica(s.Titulo) && !rodape.Any(r => NoRodape(s, r)))
            .ToList();

        SecaoDaAta? Casa(string? secao) => TemaDoItem(corpo, secao);

        var decisoes = ata.Decisoes.ToLookup(i => Casa(i.Secao));
        var riscos = ata.Riscos.ToLookup(i => Casa(i.Secao));
        var acoes = ata.Acoes.Where(a => a.Acao.Trim().Length > 0).ToLookup(a => Casa(a.Secao));
        var abertos = ata.PontosEmAberto.ToLookup(i => Casa(i.Secao));

        // Grupos juntos, na posição do primeiro membro.
        var vistos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var s in corpo)
        {
            if (s.Grupo is not { Length: > 0 } grupo)
            {
                TemaComItens(sb, s, 2, decisoes[s], riscos[s], acoes[s], abertos[s]);
                continue;
            }
            if (!vistos.Add(grupo.Trim())) continue;

            var membros = corpo.Where(m => string.Equals(m.Grupo?.Trim(), grupo.Trim(),
                                                         StringComparison.OrdinalIgnoreCase)).ToList();
            if (!membros.Any(m => TemConteudo(m, decisoes[m], riscos[m], acoes[m], abertos[m])))
                continue;
            // Grupo de um tema só não é grupo: na validação saiu "## Carteira
            // da Mariana / ### Carteira da Mariana", o mesmo título duas vezes.
            if (membros.Count == 1)
            {
                var unico = membros[0];
                TemaComItens(sb, unico, 2, decisoes[unico], riscos[unico], acoes[unico], abertos[unico]);
                continue;
            }
            sb.AppendLine($"## {grupo.Trim()}");
            sb.AppendLine();
            foreach (var m in membros)
                TemaComItens(sb, m, 3, decisoes[m], riscos[m], acoes[m], abertos[m]);
        }

        // A sobra, sob os títulos de sempre.
        Lista(sb, "Decisões", Textos(decisoes[null]));
        bool algumaAninhada = acoes.Any(g => g.Key is not null);
        var soltas = acoes[null].ToList();
        if (soltas.Count > 0 && algumaAninhada)
        {
            sb.AppendLine("## Outras pendências");
            sb.AppendLine();
            foreach (var a in soltas) sb.AppendLine(Linha(a, limpo: true));
            sb.AppendLine();
        }
        // Nenhuma ação achou tema — o modelo deixou "secao" vazio, ou errou todos
        // os títulos: a lista de sempre, com a divisão por lado, que é "a razão
        // de existir" da ata de update. Sem ela, nada diria de quem cobrar.
        else if (soltas.Count > 0)
        {
            Acoes(sb, soltas, limpo: true);
        }
        Lista(sb, "Pontos em aberto", Textos(abertos[null]));
        Lista(sb, "Riscos e alertas", Textos(riscos[null]));

        if (algumaAninhada) QuemFicouComOQue(sb, ata.Acoes, Casa);

        // Todas as que casam com cada título do rodapé, e não só a primeira:
        // duas "Próxima reunião" são duas coisas ditas, e jogar fora conteúdo é
        // pior que desarrumá-lo.
        var escritas = new HashSet<SecaoDaAta>();
        foreach (string r in rodape)
            foreach (var achada in ata.Secoes.Where(s => NoRodape(s, r)))
                if (escritas.Add(achada) && achada.Texto.Trim().Length > 0)
                    Secao(sb, achada);
    }

    /// <summary>
    /// A seção do corpo onde o item mora: igual antes de parecida, e nunca o vazio.
    /// </summary>
    /// <remarks>
    /// <para>
    /// O <see cref="Parecido"/> aceita prefixo nos dois sentidos, e o primeiro
    /// tema que casasse levaria o item: com "Dashboard" e "Dashboard de
    /// aderência" no corpo, o item do segundo ia para o primeiro. Então a
    /// igualdade vem antes, e no prefixo ganha o título mais longo — o mais
    /// específico.
    /// </para>
    /// <para>
    /// Um "secao" que normalizado fica vazio ("—", "·") não casa com nada: o
    /// vazio é prefixo de qualquer título, e mandaria o item para o primeiro
    /// tema da ata. Fica na lista geral, que é o lugar do que não tem tema.
    /// </para>
    /// <para>
    /// O <see cref="Parecido"/> não muda porque o formato clássico também o usa,
    /// e o clássico é o de antes, byte a byte.
    /// </para>
    /// </remarks>
    private static SecaoDaAta? TemaDoItem(IReadOnlyList<SecaoDaAta> corpo, string? secao)
    {
        string alvo = Normalizar(secao ?? "");
        if (alvo.Length == 0) return null;

        return corpo.FirstOrDefault(s => Normalizar(s.Titulo) == alvo)
            ?? corpo
                .Select(s => (secao: s, titulo: Normalizar(s.Titulo)))
                .Where(x => x.titulo.Length > 0
                            && (x.titulo.StartsWith(alvo, StringComparison.Ordinal)
                                || alvo.StartsWith(x.titulo, StringComparison.Ordinal)))
                .OrderByDescending(x => x.titulo.Length)
                .Select(x => x.secao)
                .FirstOrDefault();
    }

    /// <summary>
    /// As seções do esqueleto que fecham a ata: não canônicas, depois da
    /// primeira canônica que não é o Resumo.
    /// </summary>
    /// <remarks>
    /// "Próxima reunião" no update, "Referências citadas" na sessão de trabalho,
    /// "Definições e glossário" no kickoff. Sem isto elas sairiam no meio dos
    /// temas, porque o corpo segue a ordem do modelo.
    /// </remarks>
    private static List<string> Rodape(ModeloDeAta tipo)
    {
        var secoes = tipo.Secoes;
        int primeira = -1;
        for (int i = 0; i < secoes.Count; i++)
            if (Canonizar(secoes[i]) is { } c && c != "Resumo") { primeira = i; break; }
        return primeira < 0 ? [] : [.. secoes.Skip(primeira).Where(s => !EhCanonica(s))];
    }

    private static bool TemConteudo(SecaoDaAta s, IEnumerable<ItemDaAta> d, IEnumerable<ItemDaAta> r,
                                    IEnumerable<AcaoDaAta> a, IEnumerable<ItemDaAta> o) =>
        s.Texto.Trim().Length > 0 || d.Any() || r.Any() || a.Any() || o.Any();

    private static void TemaComItens(StringBuilder sb, SecaoDaAta s, int nivel,
                                     IEnumerable<ItemDaAta> decisoes, IEnumerable<ItemDaAta> riscos,
                                     IEnumerable<AcaoDaAta> acoes, IEnumerable<ItemDaAta> abertos)
    {
        if (!TemConteudo(s, decisoes, riscos, acoes, abertos)) return;

        string dono = DonoDoTema(s) is { } r ? $" · {r}" : "";
        // Sem título, o texto entra sem cabeçalho: "## " vazio é um título que
        // a tela e o .docx desenham como uma linha em branco grande.
        if (s.Titulo.Trim().Length > 0)
        {
            sb.AppendLine($"{new string('#', nivel)} {s.Titulo.Trim()}{dono}");
            sb.AppendLine();
        }
        if (s.Situacao is { Length: > 0 } situacao)
        {
            sb.AppendLine($"**Situação:** {situacao.Trim()}");
            sb.AppendLine();
        }
        if (s.Texto.Trim().Length > 0)
        {
            sb.AppendLine(s.Texto.Trim());
            sb.AppendLine();
        }
        foreach (var d in decisoes) Rotulado(sb, "Decidido", d.Texto);
        foreach (var r2 in riscos) Rotulado(sb, "Atenção", r2.Texto);
        var lista = acoes.ToList();
        if (lista.Count > 0)
        {
            foreach (var a in lista) sb.AppendLine(Linha(a, limpo: true));
            sb.AppendLine();
        }
        foreach (var o in abertos) Rotulado(sb, "Em aberto", o.Texto);
    }

    /// <summary>O dono do tema, quando há um dono de verdade para mostrar.</summary>
    /// <remarks>
    /// Medido na validação de 08/10/2026: o modelo escreveu "nosso" e "cliente"
    /// no dono do tema — que é o lado da ação, e não uma área ou pessoa. Também
    /// some o vazio, o "[responsável a definir]" e o dono que o título já traz
    /// ("Pedido mínimo · Comercial").
    /// </remarks>
    private static string? DonoDoTema(SecaoDaAta s)
    {
        string r = (s.Responsavel ?? "").Trim();
        if (r.Length == 0 || r.StartsWith('[')) return null;
        string n = Normalizar(r);
        if (n is "nosso" or "cliente" or "nos" or "nossa" or "casa") return null;
        if (Normalizar(s.Titulo).EndsWith(n, StringComparison.Ordinal)) return null;
        return r;
    }

    /// <remarks>
    /// Negrito no começo do parágrafo, e não um título: é o que o
    /// <c>renderizar()</c> da tela e o <c>Exportacao.DocxDaAta</c> já entendem.
    /// </remarks>
    private static void Rotulado(StringBuilder sb, string rotulo, string texto)
    {
        if (texto.Trim().Length == 0) return;
        sb.AppendLine($"**{rotulo}:** {texto.Trim()}");
        sb.AppendLine();
    }

    /// <summary>
    /// Quem ficou com o quê, por lado, com os nomes dos temas.
    /// </summary>
    /// <remarks>
    /// Substitui as listas "Do nosso lado / Do lado do cliente" quando as ações
    /// já estão dentro dos temas. A skill chama a separação por lado de "a razão
    /// de existir" da ata de update — ela continua, sem repetir o texto de cada
    /// ação.
    /// </remarks>
    private static void QuemFicouComOQue(StringBuilder sb, IEnumerable<AcaoDaAta> acoes,
                                         Func<string?, SecaoDaAta?> casa)
    {
        sb.AppendLine("## Quem ficou com o quê");
        sb.AppendLine();
        var validas = acoes.Where(a => a.Acao.Trim().Length > 0).ToList();
        // Com um lado só, dizer o lado é ruído — a mesma regra das Pendências.
        bool doisLados = validas.Any(EhDoCliente) && validas.Any(a => !EhDoCliente(a));
        foreach (var (lado, rotulo) in new[] { ("nosso", "Do nosso lado"), ("cliente", "Do lado do cliente") })
        {
            var doLado = validas.Where(a => (lado == "cliente") == EhDoCliente(a)).ToList();
            if (doLado.Count == 0) continue;

            var pessoas = doLado
                .GroupBy(a => a.Responsavel.Trim() is { Length: > 0 } d ? d : "[responsável a definir]")
                .Select(g => $"**{g.Key}**: " + string.Join(", ", g
                    .GroupBy(a => casa(a.Secao)?.Titulo.Trim() ?? "Outras pendências")
                    .Select(t => t.Count() > 1 ? $"{t.Key} ({t.Count()})" : t.Key)));
            sb.AppendLine(doisLados ? $"{rotulo}: {string.Join(" · ", pessoas)}"
                                    : string.Join(" · ", pessoas));
            sb.AppendLine();
        }
    }
}
