using System.Text.RegularExpressions;

namespace MeetingApp.Nucleo.Atas;

/// <summary>
/// A seção que o modelo mandou pelo lugar errado, recuperada em vez de descartada.
/// </summary>
/// <remarks>
/// <para>
/// <b>O defeito, medido no acervo inteiro em 21/08/2026:</b> o modelo às vezes
/// devolve decisões, ações e riscos como <b>seções</b> em
/// <c>secoes</c> em vez de nos campos próprios. O redator descartava a seção —
/// "essa eu escrevo a partir do campo" — e o campo estava vazio. <b>59 itens de
/// ata gerados e jogados fora, em 5 das 28 gravações</b>, sem uma linha em
/// Observações dizendo que sumiram.
/// </para>
/// <para>
/// A pior delas: uma daily de 38 minutos saiu com 2 KB de <c>ata.md</c>, só
/// Resumo e a rodada, enquanto o <c>ata.json</c> ao lado tinha quatro pendências
/// com dono e prazo. Uma ata que aparenta não ter gerado ação nenhuma é pior que
/// uma ata feia: ninguém desconfia dela.
/// </para>
/// <para>
/// <b>Por que dobrar em vez de renderizar a seção.</b> O campo é o que o
/// <see cref="VerificadorDeAta"/> conferre — eco de decisão, dono conhecido,
/// lado pelo domínio do e-mail. Texto solto em <c>secoes</c> passa por fora de
/// tudo isso. Recuperar para o campo devolve o conteúdo <b>e</b> o põe debaixo
/// das mesmas redes; renderizar a seção devolveria só o conteúdo.
/// </para>
/// <para>
/// <b>Nada é silencioso</b>, que é a regra do verificador: toda dobra vira linha
/// em Observações. O modelo escreveu prosa e o app a desmontou em itens — quem
/// lê precisa saber que houve remontagem, porque é onde um erro de análise
/// entraria.
/// </para>
/// </remarks>
public static class SecaoDobrada
{
    /// <summary>
    /// Move para os campos próprios o que veio como seção canônica.
    /// </summary>
    /// <returns>Uma linha por seção recuperada, para as observações.</returns>
    public static IReadOnlyList<string> Dobrar(AtaGerada ata)
    {
        var notas = new List<string>();
        if (ata.Secoes.Count == 0) return notas;

        var sobrevivem = new List<SecaoDaAta>(ata.Secoes.Count);
        foreach (var secao in ata.Secoes)
        {
            string? canonica = RedatorDeAta.NomeCanonico(secao.Titulo);
            var itens = canonica is null ? [] : Itens(secao.Texto);

            // Só dobra o que se perderia. Seção canônica com o campo já cheio é
            // duplicata: o redator a ignora, e ignorar é o certo.
            if (canonica is null || itens.Count == 0 || !CampoVazio(ata, canonica))
            {
                sobrevivem.Add(secao);
                continue;
            }

            Preencher(ata, canonica, itens);
            notas.Add(
                $"A seção \"{secao.Titulo.Trim()}\" veio como texto, e não no campo "
                + $"próprio. Os {itens.Count} itens dela foram recuperados para "
                + $"{canonica} — antes desta versão eles eram descartados em silêncio. "
                + "Confira se a divisão em itens ficou certa.");
        }

        ata.Secoes = sobrevivem;
        return notas;
    }

    private static bool CampoVazio(AtaGerada ata, string canonica) => canonica switch
    {
        "Decisões" => ata.Decisoes.Count == 0,
        "Pendências" => ata.Acoes.Count == 0,
        "Pontos em aberto" => ata.PontosEmAberto.Count == 0,
        "Riscos e alertas" => ata.Riscos.Count == 0,
        // "Observações sobre a transcrição" fica de fora de propósito: o
        // VerificadorDeAta descarta a prosa do modelo e reescreve a seção com o
        // que ele mesmo mediu (FASE6 §1.6, defeito 4). Recuperar para lá seria
        // devolver o texto para quem vai apagá-lo — e, pior, contrariar a
        // decisão de não deixar o modelo narrar a própria conferência.
        _ => false,
    };

    private static void Preencher(AtaGerada ata, string canonica, List<string> itens)
    {
        switch (canonica)
        {
            case "Decisões": ata.Decisoes = itens; break;
            case "Pontos em aberto": ata.PontosEmAberto = itens; break;
            case "Riscos e alertas": ata.Riscos = itens; break;
            case "Pendências": ata.Acoes = [.. itens.Select(Acao)]; break;
        }
    }

    /// <summary>
    /// <c>Ação — Responsável — prazo</c> de volta a campos.
    /// </summary>
    /// <remarks>
    /// O modelo escreve a linha no mesmo formato que o redator escreveria,
    /// porque é o formato que o esqueleto do tipo mostra a ele. Quando não
    /// escreve, a linha inteira vira a ação e o dono fica em aberto — que é o
    /// estado honesto, e é o que o <see cref="DonoPelaFala"/> depois tenta
    /// resolver pela transcrição.
    /// </remarks>
    private static AcaoDaAta Acao(string linha)
    {
        var partes = linha.Split('—', StringSplitOptions.TrimEntries);
        if (partes.Length < 2) return new AcaoDaAta { Acao = linha };

        return new AcaoDaAta
        {
            Acao = partes[0],
            Responsavel = Limpo(partes[1]),
            Prazo = partes.Length > 2 ? Limpo(partes[2]) : "",
        };
    }

    /// <summary>Tira o negrito e os colchetes que o Markdown do modelo traz.</summary>
    private static string Limpo(string t) =>
        t.Replace("**", "").Trim();

    /// <summary>
    /// O texto da seção como itens.
    /// </summary>
    /// <remarks>
    /// Marcador, caixa de tarefa e numeração saem; o resto da linha fica. Texto
    /// sem marcador nenhum vira <b>um</b> item com tudo — quebrar prosa em
    /// frases inventaria divisão que o modelo não fez, e a observação já avisa
    /// que houve remontagem.
    /// </remarks>
    private static List<string> Itens(string texto)
    {
        var linhas = (texto ?? "").Split('\n', StringSplitOptions.TrimEntries
                                              | StringSplitOptions.RemoveEmptyEntries);

        var comMarcador = linhas
            .Where(l => TemMarcador.IsMatch(l))
            .Select(l => TemMarcador.Replace(l, "").Trim())
            .Where(l => l.Length > 0)
            .ToList();
        if (comMarcador.Count > 0) return comMarcador;

        // Título interno ("### Frente X") sozinho não é item de lista: seção
        // assim é corpo de verdade, e devolvê-la como decisão seria pior.
        var soltas = linhas.Where(l => !l.StartsWith('#')).ToList();
        return soltas.Count > 0 ? [string.Join(" ", soltas)] : [];
    }

    /// <summary>Marcador de lista, com a caixa de tarefa opcional depois dele.</summary>
    private static readonly Regex TemMarcador = new(
        @"^\s*(?:[-*•]|\d+[.)])\s+(?:\[\s*[xX]?\s*\]\s*)?",
        RegexOptions.Compiled);
}
