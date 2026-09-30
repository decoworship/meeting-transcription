using System.Text.Json;

namespace MeetingApp.Nucleo.Atas;

/// <summary>
/// O caminho da ata de uma gravação transcrita: contexto, prompt, motor,
/// verificador e os dois arquivos em disco.
/// </summary>
/// <remarks>
/// <para>
/// Morava duas vezes — no <c>GerarAta</c> da <c>App/Ponte.cs</c> e no
/// <c>Cli/GeradorDeAta.cs</c> — e as duas já tinham divergido: a do app punha o
/// título e o vocabulário do projeto no prompt, e a da linha de comando, que é
/// a que as ferramentas de medição usam, não. Media-se uma ata que ninguém via.
/// </para>
/// <para>
/// E a da ponte não tinha teste, porque a suíte não alcança o executável
/// (DEB-1). Aqui ela alcança: o motor entra como função, e o teste passa um
/// falso.
/// </para>
/// </remarks>
public static class GeracaoDeAta
{
    /// <summary>O que o motor faz: prompt e duração entram, a ata sai.</summary>
    public delegate Task<AtaGerada> Motor(
        string prompt, double duracaoS, Action<ProgressoDaAta>? progresso, CancellationToken ct);

    /// <summary>Tudo o que o prompt sabe da reunião, lido da pasta.</summary>
    /// <param name="projetos">Para o vocabulário do projeto; nulo, fica sem.</param>
    public static ContextoDaReuniao Contexto(
        string pasta, ResultadoDaTranscricao dados, IEnumerable<string> dominiosDaCasa,
        Projetos? projetos)
    {
        var vinculo = DadosDaReuniao.Ler(pasta);
        var (convidados, emails) = ConvidadosDaAgenda.Ler(pasta);
        // Quem é da casa e quem é do cliente sai do domínio do e-mail, e
        // não de dedução do modelo: ver Nucleo/Atas/Organizacoes.cs.
        // Nome de exibição e e-mail juntos: o e-mail diz o lado, o nome
        // diz como a pessoa é chamada. Ver Organizacoes.Classificar.
        var pessoas = Organizacoes.Classificar(convidados, emails, dominiosDaCasa);
        // A partir daqui vale o nome canônico, e não o cru do meta.json.
        // Ele mistura nome próprio com local-part de e-mail na mesma
        // lista ("Andre Yuri" ao lado de "dimi.randel"), e o modelo copia
        // o que vê: numa ata gerada de ponta a ponta em 25/08 três
        // responsáveis saíram como "dimi.randel", "andre.monlevade" e
        // "thiago.souza". Ver Organizacoes.Classificar.
        if (pessoas.Count > 0) convidados = [.. pessoas.Select(p => p.Nome)];

        // O par resolvido, e não só o vínculo: sem vinculo.json o cliente vem
        // da transcrição, e o vocabulário tem que vir do mesmo projeto.
        string? cliente = vinculo.Cliente ?? dados.Client;
        string? projeto = vinculo.Projeto ?? dados.Project;

        return new ContextoDaReuniao
        {
            Titulo = Titulo(pasta),
            Convidados = convidados,
            Pessoas = pessoas,
            Cliente = cliente,
            Projeto = projeto,
            Data = dados.Date ?? Transcritor.DataDaReuniao(pasta),
            DuracaoS = dados.Duration ?? 0,
            Falantes = [.. dados.Segments.Select(s => s.Speaker)
                .Where(s => s is { Length: > 0 }).Distinct()!],
            Notas = Notas.Ler(pasta),
            Vocabulario = projetos?.Preferencias(cliente ?? "", projeto ?? "")?.InitialPrompt ?? "",
        };
    }

    /// <summary>
    /// Gera, confere e grava <c>ata.md</c> e <c>ata.json</c> na pasta.
    /// </summary>
    /// <returns>A ata já conferida, para quem quiser relatar números.</returns>
    public static async Task<AtaGerada> GerarAsync(
        string pasta, ModeloDeAta tipo, ResultadoDaTranscricao dados, ContextoDaReuniao ctx,
        Motor motor, Action<ProgressoDaAta>? progresso, CancellationToken ct)
    {
        var roteiro = RoteiroDeFatos.De(dados.Segments);
        string prompt = PromptDeAta.Montar(tipo, ctx, dados.Segments, roteiro);

        var ata = await motor(prompt, ctx.DuracaoS, progresso, ct);

        VerificadorDeAta.Conferir(ata, dados.Segments,
            [.. ctx.Convidados.Concat(ctx.Falantes)], roteiro, ctx.Pessoas);

        await File.WriteAllTextAsync(Path.Combine(pasta, "ata.md"),
                                     RedatorDeAta.Escrever(ata, tipo, ctx), ct);
        await File.WriteAllTextAsync(Path.Combine(pasta, "ata.json"), ata.ParaJson(), ct);
        return ata;
    }

    /// <summary><c>meeting.title</c> do <c>meta.json</c> — o mesmo que a lista mostra.</summary>
    private static string? Titulo(string pasta)
    {
        try
        {
            string meta = Path.Combine(pasta, "meta.json");
            if (!File.Exists(meta)) return null;
            using var doc = JsonDocument.Parse(File.ReadAllText(meta));
            return doc.RootElement.TryGetProperty("meeting", out var r)
                && r.TryGetProperty("title", out var t) && t.ValueKind == JsonValueKind.String
                ? t.GetString() : null;
        }
        catch (Exception)
        {
            return null;   // meta.json ilegível não impede a ata
        }
    }
}
