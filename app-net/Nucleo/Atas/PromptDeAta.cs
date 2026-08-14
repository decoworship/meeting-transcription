using System.Text;

namespace MeetingApp.Nucleo.Atas;

/// <summary>O que se sabe da reunião antes de escrever a ata dela.</summary>
public sealed record ContextoDaReuniao
{
    public string? Titulo { get; init; }
    public string? Cliente { get; init; }
    public string? Projeto { get; init; }
    public string? Data { get; init; }
    public double DuracaoS { get; init; }

    /// <summary>Quem a agenda listou. São os nomes que um dono de ação pode ter.</summary>
    public IReadOnlyList<string> Convidados { get; init; } = [];

    /// <summary>
    /// Os convidados com o lado da mesa, quando o e-mail permitiu saber.
    /// </summary>
    /// <remarks>
    /// Vem do domínio do e-mail da agenda contra os domínios da casa, e não de
    /// dedução do modelo — ver <see cref="Organizacoes"/>.
    /// </remarks>
    public IReadOnlyList<Pessoa> Pessoas { get; init; } = [];

    /// <summary>Quem a diarização separou, já nomeado quando reconhecido.</summary>
    public IReadOnlyList<string> Falantes { get; init; } = [];

    /// <summary>O que a pessoa escreveu durante a reunião. Tem precedência.</summary>
    public string Notas { get; init; } = "";

    /// <summary>Os termos do projeto, que são as grafias certas.</summary>
    public string Vocabulario { get; init; } = "";
}

/// <summary>
/// Monta o que vai ao modelo, na ordem em que ele precisa ler.
/// </summary>
/// <remarks>
/// A ordem não é estética. Instrução antes de dado: o modelo precisa saber o que
/// fazer antes de receber vinte mil tokens de conversa. E a transcrição por
/// último, porque é o pedaço que empurra tudo para longe do fim do prompt — e o
/// que fica perto do fim é o que mais pesa na hora de gerar.
/// </remarks>
public static class PromptDeAta
{
    public const string Sistema =
        "Você redige atas de reunião em português do Brasil a partir de transcrições "
        + "automáticas. Siga as regras e a estrutura dadas. Responda APENAS com o objeto "
        + "JSON pedido, preenchido com o conteúdo real da reunião.";

    public static string Montar(ModeloDeAta tipo, ContextoDaReuniao ctx,
                                IReadOnlyList<SegmentoFinal> segmentos,
                                IReadOnlyList<Fato> roteiro)
    {
        var sb = new StringBuilder();

        sb.AppendLine(ModelosDeAta.RegrasComuns());
        sb.AppendLine("\n---\n");

        sb.AppendLine("# Estrutura desta ata\n");
        sb.AppendLine($"O tipo desta reunião **já foi definido**: {tipo.Nome}. "
                      + "Produza as seções da estrutura abaixo, na ordem dela, no campo "
                      + "`secoes` do JSON. Não classifique a reunião de novo.\n");
        sb.AppendLine(tipo.Texto);
        sb.AppendLine("\n---\n");

        sb.AppendLine("# Dados da reunião\n");
        if (ctx.Titulo is { Length: > 0 }) sb.AppendLine($"- Reunião: {ctx.Titulo}");
        if (ctx.Cliente is { Length: > 0 }) sb.AppendLine($"- Cliente: {ctx.Cliente}");
        if (ctx.Projeto is { Length: > 0 }) sb.AppendLine($"- Projeto: {ctx.Projeto}");
        if (ctx.Data is { Length: > 0 }) sb.AppendLine($"- Data: {Cabecalho.DataLegivel(ctx.Data)}");
        if (ctx.DuracaoS > 0) sb.AppendLine($"- Duração: {Cabecalho.Duracao(ctx.DuracaoS)}");
        // Agrupado por organização quando dá para saber: é o que a skill pede no
        // cabeçalho ("participantes agrupados por organização") e é o que impede
        // o modelo de deduzir de que lado alguém está pelo assunto da conversa.
        if (Organizacoes.ParaPrompt(ctx.Pessoas, ctx.Cliente) is { Length: > 0 } grupos)
            sb.AppendLine(grupos);
        else if (ctx.Convidados.Count > 0)
            sb.AppendLine($"- Convidados pela agenda: {string.Join(", ", ctx.Convidados)}");

        if (ctx.Falantes.Count > 0)
            sb.AppendLine($"- Quem falou: {string.Join(", ", ctx.Falantes)}");

        // A lista de nomes válidos é dita como restrição, e não só como dado: é
        // a instrução que o verificador depois cobra.
        var conhecidos = ctx.Convidados.Concat(ctx.Falantes)
            .Where(n => n is { Length: > 0 } && !EhRotuloGenerico(n))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (conhecidos.Count > 0)
            sb.AppendLine($"\n**Responsável de uma ação só pode ser um destes nomes**: "
                          + $"{string.Join(", ", conhecidos)} — ou "
                          + "`[responsável a definir]`. Não invente nomes.");

        if (ctx.Vocabulario is { Length: > 0 })
            sb.AppendLine($"\n- Grafias corretas dos termos do projeto: {ctx.Vocabulario}");

        if (ctx.Notas.Trim().Length > 0)
        {
            sb.AppendLine("\n# Notas escritas por quem estava na reunião\n");
            sb.AppendLine("**Estas notas têm precedência sobre a transcrição.** Foram "
                          + "escritas por uma pessoa presente; a transcrição é a melhor "
                          + "tentativa de uma máquina. Onde divergirem, siga as notas e "
                          + "registre a divergência em `observacoes`.\n");
            sb.AppendLine(ctx.Notas);
        }

        if (roteiro.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine(RoteiroDeFatos.ParaPrompt(roteiro));
        }

        sb.AppendLine("\n# Transcrição\n");
        foreach (var s in segmentos)
        {
            int t = (int)s.Start;
            string quem = s.Speaker is { Length: > 0 } ? s.Speaker : "?";
            sb.AppendLine($"[{t / 60:00}:{t % 60:00}] {quem}: {s.Text.Trim()}");
        }

        sb.AppendLine("\n---\n");
        sb.AppendLine("Escreva agora a ata desta reunião, no formato JSON pedido.");
        return sb.ToString();
    }

    /// <summary>
    /// "Speaker 3" não é gente.
    /// </summary>
    /// <remarks>
    /// Medido: na ata da reunião de 2 h, os falantes não nomeados entraram na
    /// lista de participantes como se fossem pessoas (ATA.md §4).
    /// </remarks>
    public static bool EhRotuloGenerico(string nome) =>
        nome.StartsWith("Speaker", StringComparison.OrdinalIgnoreCase)
        || nome.StartsWith("SPEAKER_", StringComparison.OrdinalIgnoreCase)
        || nome == "?" || nome == "Unknown";
}
