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

    /// <summary>
    /// As regras que só existem porque a saída deste app é JSON, e não conversa.
    /// </summary>
    /// <remarks>
    /// <para>
    /// O <c>SKILL.md</c> é escrito para o Claude num chat e é fonte única — a
    /// mesma cópia serve à skill de verdade. Então o que é específico deste
    /// motor mora aqui, e não lá.
    /// </para>
    /// <para>
    /// Cada regra abaixo nasceu de um defeito visto nas 30 atas medidas em
    /// 17/08/2026, e não de suposição sobre o que um modelo faria.
    /// </para>
    /// </remarks>
    public const string ContratoDoJson = """
        # O que você escreve, e o que o app escreve

        Você preenche campos de um JSON. O app monta o Markdown a partir deles.

        **O app já escreve, e você não deve repetir em lugar nenhum:**

        - o título da ata e o cabeçalho com data, duração, cliente e participantes;
        - as seções **Resumo**, **Decisões**, **Pendências**, **Pontos em aberto**,
          **Riscos e alertas** e **Observações sobre a transcrição** — elas saem
          dos campos `resumo`, `decisoes`, `acoes`, `pontos_em_aberto`, `riscos` e
          `observacoes`.

        Em `secoes` vai **só o corpo específico deste tipo de ata**. Nunca escreva
        um título começando com `#`, e nunca repita, dentro de uma seção, uma
        decisão, ação ou pendência que já está no campo próprio dela.

        # Quem é o dono de cada ação

        Antes de fechar o campo `acoes`, percorra a lista de participantes **um por
        um** e pergunte: *o que ficou para esta pessoa?*

        Quem fala mais não é dono de tudo. Quem coordena a reunião normalmente
        também sai com tarefas — pedir a alguém que envie algo é tarefa de quem
        pede, quando ele disse que faria algo com o que receber. "Me manda o número
        do chamado que eu falo com o fulano" são **duas** ações, uma de cada lado.

        Se ninguém assumiu, use `[responsável a definir]`. Inventar dono é pior que
        admitir que não houve.

        # Restrição também é decisão

        Vai em `decisoes` não só o que o grupo escolheu fazer, mas o que ele
        combinou **não** fazer, ou fazer só sob condição:

        - "não corrija antes de falar comigo";
        - "só seguimos depois que o cliente confirmar";
        - "vamos assumir X como verdade até alguém provar o contrário".

        São as que mais custam quando somem da ata, porque alguém age sem elas.

        # Números

        Registre os números que **dimensionam** algo: volume, valor, percentual,
        prazo, contagem de casos. Ignore aritmética falada em voz alta durante a
        análise — hipótese ("não sei se seria 50, 70 ou 80"), conta em andamento
        ("6299 mais 32 dá 194") e leitura de tela não são fatos a acompanhar.
        """;

    public static string Montar(ModeloDeAta tipo, ContextoDaReuniao ctx,
                                IReadOnlyList<SegmentoFinal> segmentos,
                                IReadOnlyList<Fato> roteiro)
    {
        var sb = new StringBuilder();

        sb.AppendLine(ModelosDeAta.RegrasComuns());
        sb.AppendLine("\n---\n");

        sb.AppendLine(ContratoDoJson);
        sb.AppendLine("\n---\n");

        sb.AppendLine("# Estrutura desta ata\n");
        sb.AppendLine($"O tipo desta reunião **já foi definido**: {tipo.Nome}. "
                      + "Produza as seções da estrutura abaixo, na ordem dela, no campo "
                      + "`secoes` do JSON. Não classifique a reunião de novo.\n");
        sb.AppendLine("O modelo em Markdown abaixo serve para dizer **quais seções "
                      + "existem e o que vai em cada uma**. Ele não é o formato da sua "
                      + "resposta: você responde em JSON, e o app monta o Markdown.\n");
        sb.AppendLine(tipo.TextoParaPrompt());
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
