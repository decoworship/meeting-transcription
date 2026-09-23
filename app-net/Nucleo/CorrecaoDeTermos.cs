using MeetingApp.Nucleo.Atas;

namespace MeetingApp.Nucleo;

/// <summary>
/// A correção de termos: fonética, regra e grafia, sobre texto já transcrito.
/// </summary>
/// <remarks>
/// <b>Uma cadeia só para os dois caminhos.</b> Morava dentro do
/// <see cref="Transcritor"/>; saiu em 23/09/2026 para a legenda ao vivo usar a
/// mesma regra no fim da reunião (<c>VIVO-3</c>). Duas cópias divergiriam na
/// primeira mudança de regra, e a divergência só apareceria como "a legenda
/// corrige diferente da ata".
/// <para>
/// <b>A ordem importa e é a do Transcritor:</b> fonética primeiro, depois as
/// propostas sobre o texto já corrigido — o que a fonética consertou vira termo
/// conhecido e a regra nem olha. Ver o comentário em <see cref="RevisaoDeTermos"/>.
/// </para>
/// </remarks>
public static class CorrecaoDeTermos
{
    /// <summary>
    /// Os termos que esta reunião conhece: vocabulário, cliente, projeto e quem
    /// a agenda convidou.
    /// </summary>
    /// <remarks>
    /// <b>Os nomes da agenda são de graça e são os que mais aparecem.</b> Numa
    /// ata medida, o nome do cliente saiu errado no corpo enquanto o cabeçalho,
    /// três linhas acima, o escrevia certo — porque o cabeçalho lê o meta.json e
    /// o corpo lia o que o ASR ouviu. Aqui as duas fontes passam a ser a mesma.
    /// </remarks>
    public static IReadOnlyList<string> Entidades(
        string pastaDaGravacao, string? vocabulario, string? cliente, string? projeto)
    {
        var (nomes, emails) = ConvidadosDaAgenda.Ler(pastaDaGravacao);
        var pessoas = Organizacoes.Classificar(nomes, emails, []);

        // **Nome de uma palavra só não vira alvo.** Quando a agenda não traz o
        // nome de exibição, sobra o local-part do e-mail — e ele entra na lista
        // como se fosse gente: "Felipeof", "Emalina", "Tomole",
        // "Johnmartinez01". Medido em 25/08 sobre as 37 gravações: com eles
        // dentro, a regra propunha reescrever "Felipe" (pessoa real, dita na
        // reunião) para "Felipeof" (lixo da agenda), e "Emilia" para "Emalina".
        //
        // O corte custa pouco: alvo de uma palavra só quase nunca é o que a
        // regra precisa, porque ela compara token contra token e nome de
        // verdade vem com sobrenome. E o que se perde é uma correção; o que se
        // evita é reescrever o nome certo de alguém.
        var comNomeDeVerdade = pessoas
            .Select(p => p.Nome)
            .Where(n => n.Contains(' '));

        return [.. new[] { vocabulario, cliente, projeto }
            .Where(x => x is { Length: > 0 })
            .Concat(comNomeDeVerdade)!];
    }

    /// <summary>A fonética, texto a texto. Vocabulário vazio devolve tudo intacto.</summary>
    public static List<(string Texto, List<Troca> Trocas)> Fonetica(
        IReadOnlyList<string> textos, string? vocabulario)
    {
        if (vocabulario is not { Length: > 0 })
            return [.. textos.Select(t => (t, new List<Troca>()))];

        var termos = vocabulario.Split(',', StringSplitOptions.TrimEntries
                                            | StringSplitOptions.RemoveEmptyEntries);
        return [.. textos.Select(t => CorrecaoFonetica.Corrigir(t, termos))];
    }

    /// <summary>As propostas validadas: regra + grafia + as extras (as do modelo, na passada final).</summary>
    public static IReadOnlyList<Proposta> Propostas(
        IReadOnlyList<string> textos, IReadOnlyList<string> entidades,
        IEnumerable<Proposta>? extras = null)
    {
        // Depois da fonética, e não no lugar dela: as duas pegam coisas
        // diferentes. A fonética recupera grafia de som parecido ("Jimmy" por
        // "Dimi"); esta recupera sigla e nome próprio por distância de edição
        // ("G6CB" por "GCCB"), que é onde a fonética acerta zero — medido nos
        // dez casos de AuditoriaCorrecaoFonetica. Ver Nucleo/RevisaoDeTermos.cs.
        //
        // Rodar depois evita que as duas disputem a mesma palavra: o que a
        // fonética já consertou vira termo conhecido e esta nem olha.
        var cruas = new List<Proposta>(RevisaoDeTermos.Propor(textos, entidades));

        // A grafia entra junto, e não no lugar: as duas pegam coisas
        // diferentes. A de cima recupera o termo que o motor ouviu errado
        // ("G6CB" por "GCCB"); esta recupera o que ele ouviu certo e
        // escreveu com o espaço no lugar errado ("next best" por
        // "NextBest"). É o que fecha boa parte da distância medida no §12
        // da docs/FASE7-RESULTADOS.md, e vale para os dois motores.
        cruas.AddRange(RevisaoDeTermos.ProporGrafia(textos, entidades));

        if (extras is not null) cruas.AddRange(extras);
        return RevisaoDeTermos.Validar(cruas, entidades);
    }
}
