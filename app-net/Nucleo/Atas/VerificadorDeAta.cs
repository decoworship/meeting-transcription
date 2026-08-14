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
                                     IReadOnlyList<Fato> roteiro)
    {
        var notas = new List<string>();
        string transcricao = string.Join(" ", segmentos.Select(s => s.Text));

        ConferirDonos(ata, conhecidos, notas);
        ConferirDecisoes(ata, segmentos, notas);
        ConferirRiscos(ata, segmentos, notas);
        ConferirOmissoes(ata, roteiro, notas);

        // As observações do modelo ficam; as do verificador entram depois, para
        // quem lê saber o que é leitura da máquina e o que é conferência.
        ata.Observacoes.AddRange(notas);
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
        var primeiros = validos
            .Select(n => n.Split(' ', StringSplitOptions.RemoveEmptyEntries)[0])
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var acao in ata.Acoes)
        {
            // O modelo repete o rótulo dentro do valor — "Responsável: Fulano",
            // "prazo: amanhã" — e o redator, que já escreve o rótulo, produzia
            // "**Responsável: Fulano**". Medido na primeira geração de ponta a
            // ponta. Limpar aqui é mais barato que pedir ao modelo que não faça.
            acao.Responsavel = SemRotulo(acao.Responsavel, "responsável", "responsavel");
            acao.Prazo = SemRotulo(acao.Prazo, "prazo");

            string dono = acao.Responsavel.Trim();
            if (dono.Length == 0 || dono.StartsWith('['))
            {
                acao.Responsavel = "[responsável a definir]";
                continue;
            }

            bool conhecido = validos.Any(
                v => v.Contains(dono, StringComparison.OrdinalIgnoreCase)
                     || dono.Contains(v, StringComparison.OrdinalIgnoreCase))
                || dono.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                       .Any(parte => primeiros.Contains(parte));

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

            int eco = palavras.Count(faladas.Contains);
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

            if ((double)palavras.Count(faladas.Contains) / palavras.Count >= 0.5)
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
