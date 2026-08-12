using MeetingApp.Sidecar;

namespace MeetingApp.Nucleo;

/// <summary>Onde o pipeline está, para quem espera.</summary>
/// <param name="Etapa">"mix", "asr", "diarizacao" ou "montagem".</param>
/// <param name="Fracao">0 a 1 dentro da etapa, ou -1 quando não há como saber.</param>
public readonly record struct Progresso(string Etapa, double Fracao, string Texto);

/// <summary>Como achar os motores Python.</summary>
/// <remarks>
/// Caminhos, e não descoberta automática: quando o empacotamento da Fase 2
/// chegar, os motores virão numa pasta conhecida ao lado do executável, e até
/// lá dá para apontar para um ambiente de desenvolvimento sem mudar código.
/// </remarks>
public sealed record Motores(string Python, string ScriptAsr, string ScriptDiarizacao,
                             string ScriptModelos)
{
    /// <summary>O arranjo esperado do app instalado: <c>motores/</c> ao lado do .exe.</summary>
    public static Motores AoLadoDoExecutavel()
    {
        string raiz = Path.Combine(AppContext.BaseDirectory, "motores");
        return new Motores(
            Path.Combine(raiz, "python", "python.exe"),
            Path.Combine(raiz, "asr", "motor.py"),
            Path.Combine(raiz, "diarizacao", "motor.py"),
            Path.Combine(raiz, "modelos", "motor.py"));
    }

    /// <summary>
    /// O token do HuggingFace, que o pyannote exige para baixar o modelo.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Três fontes, nesta ordem: a variável de ambiente, o arquivo
    /// <c>%USERPROFILE%\.meeting-recorder\.env</c>, e o <b>token embutido no
    /// executável</b>. As duas primeiras existem para quem desenvolve poder
    /// sobrepor; a terceira é a que faz o app funcionar na máquina de quem só
    /// quer transcrever uma reunião.
    /// </para>
    /// <para>
    /// <b>Por que embutir.</b> Criar conta no HuggingFace, aceitar os termos do
    /// modelo e gerar um token é trabalho de desenvolvedor, e o app não pode
    /// exigir isso de quem grava reunião — foi a decisão do dono do produto,
    /// pelo mesmo caminho que as credenciais do Google já tinham seguido. O
    /// token fica no binário publicado, e nunca no repositório: o
    /// <c>.csproj</c> só o embute se o arquivo existir na máquina de quem
    /// publica.
    /// </para>
    /// <para>
    /// Diferente do segredo OAuth do Google, <b>este token é secreto de
    /// verdade</b> — ele dá acesso à conta HuggingFace de quem publica. Deve
    /// ser um token de leitura, criado só para isto, e revogável sem afetar
    /// mais nada. Só é usado na primeira execução de cada máquina, para baixar
    /// o modelo; depois ele fica no cache local.
    /// </para>
    /// </remarks>
    public static string? TokenDoHuggingFace()
    {
        if (Environment.GetEnvironmentVariable("HF_TOKEN") is { Length: > 0 } doAmbiente)
            return doAmbiente;

        string env = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".meeting-recorder", ".env");
        try
        {
            if (File.Exists(env))
                foreach (string linha in File.ReadAllLines(env))
                {
                    var partes = linha.Split('=', 2);
                    if (partes.Length == 2 && partes[0].Trim() == "HF_TOKEN")
                        return partes[1].Trim().Trim('"', '\'');
                }
        }
        catch (IOException)
        {
            // Arquivo ilegível não pode derrubar a transcrição: cai no embutido.
        }
        return Embutido();
    }

    internal const string RecursoDoToken = "MeetingApp.hf_token.txt";

    private static string? Embutido()
    {
        using var fluxo = typeof(Motores).Assembly.GetManifestResourceStream(RecursoDoToken);
        if (fluxo is null) return null;

        using var leitor = new StreamReader(fluxo);
        string token = leitor.ReadToEnd().Trim();
        return token.Length > 0 ? token : null;
    }

    /// <summary>Diz o que falta, ou <c>null</c> se está tudo no lugar.</summary>
    /// <remarks>
    /// Checar antes de spawnar é o que transforma "o motor morreu" — mensagem
    /// que não ajuda ninguém — em "faltou este arquivo aqui".
    /// </remarks>
    public string? OQueFalta()
    {
        if (!File.Exists(Python)) return $"o Python dos motores não está em {Python}";
        if (!File.Exists(ScriptAsr)) return $"o motor de transcrição não está em {ScriptAsr}";
        if (!File.Exists(ScriptDiarizacao))
            return $"o motor de diarização não está em {ScriptDiarizacao}";
        return null;
    }
}

/// <summary>
/// O pipeline completo de uma gravação: mix → ASR → diarização → resultado.
/// </summary>
/// <remarks>
/// Vive no núcleo, e não no CLI, porque tem dois consumidores — a linha de
/// comando, que provou o caminho antes de existir UI, e o app. Duplicar a
/// ordem das etapas nos dois seria garantir que um dia divergissem.
/// </remarks>
public sealed class Transcritor(Motores motores)
{
    /// <param name="progresso">Chamado na thread do pipeline, não na da UI.</param>
    /// <summary>
    /// Quando a reunião foi, em ISO com hora.
    /// </summary>
    /// <remarks>
    /// Prefere o horário da agenda ao do arquivo: a reunião marcada para as 9h
    /// é o que as pessoas lembram, mesmo que a gravação tenha começado 9h03. Só
    /// cai no nome da pasta quando não houve evento.
    /// </remarks>
    public static string? DataDaReuniao(string pastaDaGravacao)
    {
        try
        {
            string meta = Path.Combine(pastaDaGravacao, "meta.json");
            if (File.Exists(meta))
            {
                using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(meta));
                if (doc.RootElement.TryGetProperty("meeting", out var reuniao)
                    && reuniao.TryGetProperty("start", out var inicio)
                    && inicio.ValueKind == System.Text.Json.JsonValueKind.String
                    && inicio.GetString() is { Length: > 0 } quando)
                    return quando;
            }
        }
        catch (Exception)
        {
            // meta.json ilegível não pode impedir a exportação.
        }

        // "2026-08-11_08-02-40" -> "2026-08-11T08:02:40"
        string nome = Path.GetFileName(pastaDaGravacao);
        var m = System.Text.RegularExpressions.Regex.Match(
            nome, @"^(\d{4}-\d{2}-\d{2})_(\d{2})-(\d{2})-(\d{2})");
        return m.Success ? $"{m.Groups[1].Value}T{m.Groups[2].Value}:{m.Groups[3].Value}:{m.Groups[4].Value}"
                         : null;
    }

    /// <param name="modelo">
    /// Tamanho do modelo de ASR. Vem da tela, que por sua vez o carrega das
    /// preferências do projeto — modelo menor é a saída para quem precisa de
    /// rapidez mais que de exatidão.
    /// </param>
    public async Task<ResultadoDaTranscricao> ExecutarAsync(
        string pastaDaGravacao, string? vocabulario = null, string? idioma = null,
        bool filtrarSilencio = false, Action<Progresso>? progresso = null,
        string? modelo = null, string? cliente = null, string? projeto = null,
        CancellationToken ct = default)
    {
        if (motores.OQueFalta() is { } falta) throw new MotorException(falta);

        string mic = Path.Combine(pastaDaGravacao, "mic.wav");
        string sistema = Path.Combine(pastaDaGravacao, "system.wav");
        foreach (string f in new[] { mic, sistema })
            if (!File.Exists(f))
                throw new MotorException($"a gravação não tem {Path.GetFileName(f)}");

        progresso?.Invoke(new Progresso("mix", 0, "somando as duas faixas"));
        var faixas = Faixas.Ler(mic, sistema);

        // O mix vai para junto da gravação: é derivado e refazível, mas enquanto
        // o pipeline roda ele precisa existir num caminho que o motor abra.
        string caminhoDoMix = Path.Combine(pastaDaGravacao, "mix.wav");
        faixas.EscreverMix(caminhoDoMix);

        // ASR primeiro, diarização depois, cada um no seu processo: numa placa
        // de 6 GB os dois modelos não cabem juntos, e processos separados fazem
        // a VRAM do primeiro voltar antes de o segundo subir.
        // O token vai para os dois motores: hoje só a diarização o usa, mas o
        // faster-whisper também baixa do HuggingFace e um dia pode precisar.
        var ambiente = new Dictionary<string, string>();
        if (Motores.TokenDoHuggingFace() is { Length: > 0 } token) ambiente["HF_TOKEN"] = token;

        Transcricao transcricao;
        string[] argsAsr = modelo is { Length: > 0 }
            ? [motores.ScriptAsr, "--modelo", modelo]
            : [motores.ScriptAsr];

        using (var asr = await MotorSidecar.IniciarAsync(
                   motores.Python, argsAsr, ct, ambiente))
        {
            transcricao = await asr.TranscreverAsync(caminhoDoMix, vocabulario, idioma,
                (pct, texto) => progresso?.Invoke(new Progresso("asr", pct, texto)), ct);
        }

        // A diarização roda só no system.wav: o que o microfone captou já se sabe
        // de quem é, e dar o mix ao pyannote o faria tentar separar você de você.
        IReadOnlyList<SegmentoDeFalante> diarizacao;
        using (var diar = await MotorSidecar.IniciarAsync(
                   motores.Python, [motores.ScriptDiarizacao], ct, ambiente))
        {
            diarizacao = await diar.DiarizarAsync(sistema,
                (pct, texto) => progresso?.Invoke(new Progresso("diarizacao", pct, texto)), ct);
        }

        progresso?.Invoke(new Progresso("montagem", 0, "juntando texto e falantes"));

        var segmentos = transcricao.Segmentos
            .Select(s => new SegmentoFinal { Start = s.Inicio, End = s.Fim, Text = s.Texto })
            .ToList();

        if (filtrarSilencio) FiltroDeSilencio.Filtrar(segmentos, faixas.Mix());

        if (vocabulario is { Length: > 0 })
        {
            var termos = vocabulario.Split(',', StringSplitOptions.TrimEntries
                                                | StringSplitOptions.RemoveEmptyEntries);
            foreach (var seg in segmentos)
            {
                var (texto, trocas) = CorrecaoFonetica.Corrigir(seg.Text, termos);
                if (trocas.Count > 0)
                {
                    seg.Text = texto;
                    // A lista vai junto para o arquivo: é o que permite à tela
                    // mostrar o que foi trocado e desfazer o que estiver errado.
                    // Antes ela era descartada aqui, e a correção acontecia sem
                    // deixar rastro.
                    seg.Swaps = [.. trocas.Select(t => new TrocaFeita { De = t.De, Para = t.Para })];
                }
            }
        }

        Montagem.AtribuirFalantes(segmentos, diarizacao);
        Montagem.AtribuirDono(segmentos, faixas);

        // Quem já foi nomeado antes chega nomeado. Roda depois de tudo porque
        // precisa dos falantes montados, e nunca derruba a transcrição: não
        // reconhecer é o estado normal de quem nunca foi apresentado.
        try
        {
            progresso?.Invoke(new Progresso("montagem", 0.5, "procurando vozes conhecidas"));
            var conhecidos = await new AprendizadoDeVozes(motores, new Vozes())
                .ReconhecerAsync(pastaDaGravacao, segmentos, ct);

            foreach (var seg in segmentos)
                if (seg.Speaker is { } r && conhecidos.TryGetValue(r, out string? nome))
                    seg.Speaker = nome;
        }
        catch (Exception)
        {
            // Reconhecer voz é um extra sobre a transcrição, não um requisito.
        }

        var resultado = new ResultadoDaTranscricao
        {
            Language = transcricao.Idioma,
            Duration = transcricao.Duracao,
            Client = cliente,
            Project = projeto,
            Date = DataDaReuniao(pastaDaGravacao),
            Segments = segmentos,
        };

        await File.WriteAllTextAsync(
            Path.Combine(pastaDaGravacao, "transcricao.json"), resultado.ParaJson(), ct);

        progresso?.Invoke(new Progresso("montagem", 1, "pronto"));
        return resultado;
    }
}
