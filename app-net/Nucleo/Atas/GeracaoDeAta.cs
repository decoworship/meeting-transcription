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
    /// <summary>O que o motor faz: prompt, esquema e duração entram, a ata sai.</summary>
    /// <remarks>
    /// O esquema virou parâmetro com a ata por tema (ATA-5): ele é parte do
    /// prompt — as descrições dos campos instruem o modelo —, e muda com o formato.
    /// </remarks>
    public delegate Task<AtaGerada> Motor(
        string prompt, string esquema, double duracaoS,
        Action<ProgressoDaAta>? progresso, CancellationToken ct);

    /// <summary>O recado do verificador, fora do <c>ata.md</c> no formato por tema.</summary>
    public const string ArquivoDeObservacoes = "ata.observacoes.md";

    /// <summary>
    /// O <c>ata.md</c> de antes de gerar de novo: comparar formatos é trocar a
    /// chave e refazer.
    /// </summary>
    public const string ArquivoAnterior = "ata.anterior.md";

    public static string? LerObservacoes(string pasta)
    {
        string caminho = Path.Combine(pasta, ArquivoDeObservacoes);
        try { return File.Exists(caminho) ? File.ReadAllText(caminho) : null; }
        catch (IOException) { return null; }
    }

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
            Notas = Notas.SemImagens(Notas.Ler(pasta)),
            Vocabulario = projetos?.Preferencias(cliente ?? "", projeto ?? "")?.InitialPrompt ?? "",
        };
    }

    /// <summary>
    /// Gera, confere e grava <c>ata.md</c> e <c>ata.json</c> na pasta.
    /// </summary>
    /// <returns>A ata já conferida, para quem quiser relatar números.</returns>
    public static async Task<AtaGerada> GerarAsync(
        string pasta, ModeloDeAta tipo, ResultadoDaTranscricao dados, ContextoDaReuniao ctx,
        Motor motor, Action<ProgressoDaAta>? progresso, CancellationToken ct,
        FormatoDaAta formato = FormatoDaAta.Classico, AbrirSessao? abrir = null)
    {
        // O tipo de formalização tem caminho próprio, e não muda com o formato:
        // ele é o formato do e-mail ao cliente.
        if (tipo.Id == AtaDeFormalizacao.Id && abrir is not null)
            return await GerarFormalizacaoAsync(pasta, dados, ctx, abrir, progresso, ct);

        var roteiro = RoteiroDeFatos.De(dados.Segments);
        string prompt = PromptDeAta.Montar(tipo, ctx, dados.Segments, roteiro, formato);
        string esquema = AtaGerada.EsquemaPara(PromptDeAta.Agrupa(tipo, formato));

        var ata = await motor(prompt, esquema, ctx.DuracaoS, progresso, ct);

        VerificadorDeAta.Conferir(ata, dados.Segments,
            [.. ctx.Convidados.Concat(ctx.Falantes)], roteiro, ctx.Pessoas);

        string md = Path.Combine(pasta, "ata.md");
        if (File.Exists(md))
            File.Copy(md, Path.Combine(pasta, ArquivoAnterior), overwrite: true);
        await File.WriteAllTextAsync(md, RedatorDeAta.Escrever(ata, tipo, ctx, formato), ct);

        // No clássico as observações estão dentro do ata.md; um arquivo à parte
        // de uma geração anterior por tema contradiria a ata nova.
        string obs = Path.Combine(pasta, ArquivoDeObservacoes);
        if (formato == FormatoDaAta.PorTema && RedatorDeAta.Observacoes(ata) is { } texto)
            await File.WriteAllTextAsync(obs, texto, ct);
        else if (File.Exists(obs))
            File.Delete(obs);

        await File.WriteAllTextAsync(Path.Combine(pasta, "ata.json"), ata.ParaJson(), ct);
        return ata;
    }

    /// <summary>
    /// Sobe o motor preso a um sistema e a um esquema — o
    /// <see cref="MotorDeAta.AbrirSessaoAsync"/>, ou um falso nos testes.
    /// </summary>
    public delegate Task<ISessaoDoMotor> AbrirSessao(
        string sistema, string nomeDoEsquema, string esquema,
        int caracteresMaximos, int tokensDeSaida, CancellationToken ct);

    /// <summary>
    /// A ata de formalização: extrair com citação, conferir sem modelo,
    /// escrever no estilo. Ver <see cref="AtaDeFormalizacao"/>.
    /// </summary>
    /// <remarks>
    /// Duas subidas do motor, uma por etapa, porque cada sessão é presa a um
    /// sistema e a um esquema. Custa os 6 a 9 s de carregar o modelo uma vez a
    /// mais, e não exige mexer no motor.
    /// </remarks>
    public static async Task<AtaGerada> GerarFormalizacaoAsync(
        string pasta, ResultadoDaTranscricao dados, ContextoDaReuniao ctx,
        AbrirSessao abrir, Action<ProgressoDaAta>? progresso, CancellationToken ct)
    {
        var turnos = AtaDeFormalizacao.Turnos(dados.Segments);
        string pedido = AtaDeFormalizacao.PedidoDaExtracao(ctx, turnos);

        progresso?.Invoke(new ProgressoDaAta("modelo", 0.05, "carregando o modelo"));
        AtaDeFormalizacao.Extracao extraida;
        using (var sessao = await abrir(AtaDeFormalizacao.SistemaDaExtracao, "extracao",
                   AtaDeFormalizacao.EsquemaDaExtracao,
                   pedido.Length + AtaDeFormalizacao.SistemaDaExtracao.Length, 6144, ct))
        {
            progresso?.Invoke(new ProgressoDaAta("lendo", 0.2, "lendo a reunião"));
            extraida = AtaDeFormalizacao.LerExtracao(await sessao.PerguntarAsync(pedido, ct));
        }

        progresso?.Invoke(new ProgressoDaAta("conferindo", 0.6, "conferindo cada item com a fala"));
        var (conferida, obs) = AtaDeFormalizacao.Conferir(extraida, turnos, ctx);

        progresso?.Invoke(new ProgressoDaAta("escrevendo", 0.7, "escrevendo a ata"));
        string pedidoDaEscrita = AtaDeFormalizacao.PedidoDaEscrita(conferida);
        AtaDeFormalizacao.Escrita escrita;
        using (var sessao = await abrir(AtaDeFormalizacao.SistemaDaEscrita, "escrita",
                   AtaDeFormalizacao.EsquemaDaEscrita,
                   pedidoDaEscrita.Length + AtaDeFormalizacao.SistemaDaEscrita.Length, 2048, ct))
        {
            escrita = AtaDeFormalizacao.LerEscrita(await sessao.PerguntarAsync(pedidoDaEscrita, ct));
        }

        var ata = AtaDeFormalizacao.Montar(conferida, escrita, obs, ctx);

        progresso?.Invoke(new ProgressoDaAta("montagem", 0.95, "montando a ata"));
        string md = Path.Combine(pasta, "ata.md");
        if (File.Exists(md))
            File.Copy(md, Path.Combine(pasta, ArquivoAnterior), overwrite: true);
        await File.WriteAllTextAsync(md, AtaDeFormalizacao.Markdown(ata), ct);

        // A ata vai por e-mail ao cliente: a conferência fica fora dela, sempre.
        string arquivoObs = Path.Combine(pasta, ArquivoDeObservacoes);
        if (AtaDeFormalizacao.Observacoes(ata) is { } texto)
            await File.WriteAllTextAsync(arquivoObs, texto, ct);
        else if (File.Exists(arquivoObs))
            File.Delete(arquivoObs);

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
