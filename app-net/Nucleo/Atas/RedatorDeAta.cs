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
    public static string Escrever(AtaGerada ata, ModeloDeAta tipo, ContextoDaReuniao ctx)
    {
        var sb = new StringBuilder();

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
        var pessoas = ctx.Convidados.Concat(ctx.Falantes)
            .Where(n => n is { Length: > 0 } && !PromptDeAta.EhRotuloGenerico(n))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (pessoas.Count > 0)
            sb.AppendLine($"**Participantes:** {string.Join(", ", pessoas)}");

        sb.AppendLine();

        if (ata.Resumo.Trim().Length > 0)
        {
            sb.AppendLine("## Resumo");
            sb.AppendLine();
            sb.AppendLine(ata.Resumo.Trim());
            sb.AppendLine();
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
                Canonica(sb, doEsqueleto, ata, escritas);
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
            Canonica(sb, canonica, ata, escritas);
        Canonica(sb, "Observações sobre a transcrição", ata, escritas);

        return sb.ToString().TrimEnd() + "\n";
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
    private static void Canonica(StringBuilder sb, string titulo, AtaGerada ata,
                                 HashSet<string> escritas)
    {
        string chave = Normalizar(titulo);
        if (!escritas.Add(chave)) return;

        if (chave.StartsWith("decis", StringComparison.Ordinal))
            Lista(sb, "Decisões", ata.Decisoes);
        else if (chave.StartsWith("pend", StringComparison.Ordinal)
                 || chave.StartsWith("acoes", StringComparison.Ordinal)
                 || chave.StartsWith("acao", StringComparison.Ordinal))
            Acoes(sb, ata.Acoes);
        else if (chave.StartsWith("pontos", StringComparison.Ordinal))
            Lista(sb, "Pontos em aberto", ata.PontosEmAberto);
        else if (chave.StartsWith("riscos", StringComparison.Ordinal))
            Lista(sb, "Riscos e alertas", ata.Riscos);
        else if (chave.StartsWith("observ", StringComparison.Ordinal))
            Lista(sb, "Observações sobre a transcrição", ata.Observacoes);
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
    private static bool EhCanonica(string titulo)
    {
        string t = Normalizar(titulo);
        return t == "resumo"
            || t.StartsWith("decis", StringComparison.Ordinal)
            || t.StartsWith("pend", StringComparison.Ordinal)
            || t.StartsWith("acoes", StringComparison.Ordinal)
            || t.StartsWith("acao", StringComparison.Ordinal)
            || t.StartsWith("action item", StringComparison.Ordinal)
            || t.StartsWith("pontos em aberto", StringComparison.Ordinal)
            || t.StartsWith("riscos", StringComparison.Ordinal)
            || t.StartsWith("observ", StringComparison.Ordinal);
    }

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
    private static void Acoes(StringBuilder sb, IReadOnlyList<AcaoDaAta> acoes)
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
            foreach (var a in nossas) sb.AppendLine(Linha(a));
            sb.AppendLine();
            sb.AppendLine("### Do lado do cliente");
            sb.AppendLine();
            foreach (var a in doCliente) sb.AppendLine(Linha(a));
        }
        else
        {
            foreach (var a in uteis) sb.AppendLine(Linha(a));
        }
        sb.AppendLine();
    }

    private static bool EhDoCliente(AcaoDaAta a) =>
        string.Equals(a.Lado, "cliente", StringComparison.OrdinalIgnoreCase);

    /// <summary>O formato que a skill pede, escrito por nós e não pelo modelo.</summary>
    private static string Linha(AcaoDaAta a)
    {
        string dono = a.Responsavel.Trim() is { Length: > 0 } d ? d : "[responsável a definir]";
        string prazo = a.Prazo.Trim() is { Length: > 0 } p ? p : "[prazo a definir]";
        return $"- [ ] {a.Acao.Trim()} — **{dono}** — {prazo}";
    }
}
