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
    /// O token do HuggingFace, quando esta máquina tem um. Normalmente não tem.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Duas fontes, nesta ordem: a variável de ambiente e o arquivo
    /// <c>%USERPROFILE%\.meeting-recorder\.env</c>. Havia uma terceira — o token
    /// embutido no executável —, e ela <b>saiu na Fase 4</b>.
    /// </para>
    /// <para>
    /// <b>Por que ela existia, e por que deixou de precisar existir.</b> Criar
    /// conta no HuggingFace, aceitar os termos do modelo e gerar um token é
    /// trabalho de desenvolvedor, e o app não pode exigir isso de quem grava
    /// reunião — decisão do dono do produto, e ela continua valendo. O que mudou
    /// foi o custo de cumpri-la: dos quatro modelos que o app baixa, só o
    /// <c>speaker-diarization-community-1</c> tinha portão, ele pesa 32 MB e é
    /// CC-BY-4.0. Redistribuí-lo dentro do instalador cumpre a mesma decisão sem
    /// carregar um segredo, e ainda tira a rede do caminho da primeira
    /// diarização. Ver <c>docs/FASE4.md</c> §4.
    /// </para>
    /// <para>
    /// O que sobrou aqui serve a duas situações, as duas de quem desenvolve:
    /// baixar um modelo de ASR sob demanda, e rodar numa árvore onde
    /// <c>tools/empacotar_modelos_de_diarizacao.sh</c> ainda não passou. Na
    /// máquina de quem só usa o app, este método devolve <c>null</c> e nada
    /// depende disso.
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
            // Arquivo ilegível não pode derrubar a transcrição: segue sem token,
            // que desde a Fase 4 é o caso normal.
        }
        return null;
    }

    /// <summary>
    /// O ambiente com que todo sidecar Python é iniciado.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Existe porque eram três lugares montando o mesmo dicionário à mão — o
    /// pipeline, o aprendizado de vozes e o download de modelos. Três cópias de
    /// uma decisão é a forma mais confiável de garantir que um dia elas
    /// discordem, e a discordância aqui seria invisível: um motor com telemetria
    /// ligada e outro não.
    /// </para>
    /// <para>
    /// <b>A telemetria do pyannote fica desligada.</b> A partir da 4.x ele
    /// exporta um span para <c>otel.pyannote.ai</c> a cada carga de pipeline e a
    /// cada aplicação, com origem, versão e um id de sessão. Não vai áudio nem
    /// texto junto — mas a promessa deste app é que a reunião não sai da
    /// máquina, e um app instalado na máquina de outra pessoa não pede a ela uma
    /// conexão que ela não sabe que existe. <c>PYANNOTE_METRICS_ENABLED</c> é a
    /// chave que a própria biblioteca lê (<c>telemetry/metrics.py</c>), e ela
    /// <b>não tem valor padrão no código</b>: sem a variável, o
    /// <c>is_metrics_enabled</c> levanta exceção. Defini-la aqui é obrigatório,
    /// não opcional.
    /// </para>
    /// <para>
    /// <b>O token do HuggingFace é opcional desde a Fase 4.</b> Os pesos de
    /// diarização viajam dentro do app (docs/FASE4.md §4), então o caso normal é
    /// não haver token nenhum. Ele continua sendo passado quando existe, para a
    /// máquina de quem desenvolve — que pode não ter rodado o empacotador — e
    /// para o download de modelos de ASR sob demanda.
    /// </para>
    /// </remarks>
    public static Dictionary<string, string> Ambiente()
    {
        var ambiente = new Dictionary<string, string>
        {
            ["PYANNOTE_METRICS_ENABLED"] = "false",
        };

        if (TokenDoHuggingFace() is { Length: > 0 } token) ambiente["HF_TOKEN"] = token;

        return ambiente;
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

    /// <summary>
    /// O que dizer quando o motor não achou a placa.
    /// </summary>
    /// <remarks>
    /// Dois casos, e a saída de cada um é diferente — juntá-los numa frase só
    /// mandaria metade das pessoas fazer a coisa errada. Se o Windows enxerga
    /// uma placa que o motor não enxerga, isso é <b>defeito</b>, e o caminho é
    /// mandar o diagnóstico. Se não há placa nenhuma, é escolha informada, e o
    /// caminho é a chave nos ajustes.
    /// </remarks>
    public static string SemPlaca(DispositivoDoMotor placa)
    {
        string comum =
            " Transcrever pela CPU leva horas e consome muita memória — numa máquina"
            + " já apertada, o suficiente para derrubá-la. Se quiser mesmo assim,"
            + " ligue \"Transcrever sem placa\" em Ajustes › Transcrição.";

        if (Diagnostico.PlacaNvidia() is { Length: > 0 } doWindows)
            return $"o Windows enxerga a placa ({doWindows}), mas o motor de transcrição "
                 + $"não: {placa.Motivo ?? "sem detalhe"}. Isso é um defeito — mande o "
                 + "bloco de diagnóstico de Ajustes › Sobre." + comum;

        return "não há placa NVIDIA disponível para a transcrição"
             + (placa.Motivo is { Length: > 0 } m ? $" ({m})" : "") + "." + comum;
    }

    /// <param name="modelo">
    /// Tamanho do modelo de ASR. Vem da tela, que por sua vez o carrega das
    /// preferências do projeto — modelo menor é a saída para quem precisa de
    /// rapidez mais que de exatidão.
    /// </param>
    /// <param name="diarizar">
    /// Separar quem falou. Desligar pula a etapa inteira — o pyannote é o trecho
    /// mais lento do pipeline depois do ASR, e em reunião onde só importa o que
    /// foi dito ele é tempo de GPU gasto à toa. Vem das preferências do projeto,
    /// e até 13/08/2026 a escolha era colhida na tela e ignorada aqui.
    /// </param>
    public async Task<ResultadoDaTranscricao> ExecutarAsync(
        string pastaDaGravacao, string? vocabulario = null, string? idioma = null,
        bool filtrarSilencio = false, Action<Progresso>? progresso = null,
        string? modelo = null, string? cliente = null, string? projeto = null,
        bool diarizar = true, bool corrigirFonetica = true,
        CancellationToken ct = default)
    {
        if (motores.OQueFalta() is { } falta) throw new MotorException(falta);

        string mic = Path.Combine(pastaDaGravacao, "mic.wav");
        string sistema = Path.Combine(pastaDaGravacao, "system.wav");
        foreach (string f in new[] { mic, sistema })
            if (!File.Exists(f))
                throw new MotorException($"a gravação não tem {Path.GetFileName(f)}");

        // O modelo, depois das faixas e antes do mix. Depois das faixas porque
        // gravação faltando é problema maior e mais específico; antes do mix
        // porque somar as duas faixas é trabalho de verdade, e numa instalação
        // nova o modelo não está lá — fazer o usuário esperar por um trabalho
        // que vai ser jogado fora é o que esta ordem evita. Ver
        // Catalogo.OQueImpede.
        string escolhido = modelo is { Length: > 0 } ? modelo
                                                     : ConfiguracoesDoApp.Carregar().ModeloPadrao;
        if (Catalogo.OQueImpede(escolhido) is { } semModelo)
            throw new MotorException(semModelo);

        progresso?.Invoke(new Progresso("mix", 0, "somando as duas faixas"));
        var faixas = Faixas.Ler(mic, sistema);

        // O mix vai para junto da gravação: é derivado e refazível, mas enquanto
        // o pipeline roda ele precisa existir num caminho que o motor abra.
        string caminhoDoMix = Path.Combine(pastaDaGravacao, "mix.wav");
        faixas.EscreverMix(caminhoDoMix);

        // ASR primeiro, diarização depois, cada um no seu processo: numa placa
        // de 6 GB os dois modelos não cabem juntos, e processos separados fazem
        // a VRAM do primeiro voltar antes de o segundo subir.
        // O mesmo ambiente para os dois motores, montado num lugar só — inclusive
        // o desligamento da telemetria do pyannote. Ver Motores.Ambiente().
        var ambiente = Motores.Ambiente();

        Transcricao transcricao;
        string[] argsAsr = modelo is { Length: > 0 }
            ? [motores.ScriptAsr, "--modelo", modelo]
            : [motores.ScriptAsr];

        using (var asr = await MotorSidecar.IniciarAsync(
                   motores.Python, argsAsr, ct, ambiente))
        {
            // A placa, perguntada ao motor ANTES de carregar o modelo.
            //
            // Relatado em 18/08/2026: a transcrição caiu para CPU numa máquina
            // com RTX 4050 e o large-v3 comeu RAM por horas até derrubar o
            // Windows. Rodar em CPU não é um modo do app — é o que acontece
            // quando o motor não acha a placa, e a diferença entre as duas
            // coisas precisa ser dita antes, não descoberta depois.
            var placa = await asr.DispositivoAsync(ct);
            if (!placa.Cuda && !ConfiguracoesDoApp.Carregar().PermitirCpu)
                throw new MotorException(SemPlaca(placa));

            progresso?.Invoke(new Progresso(
                "asr", 0, placa.Cuda ? $"transcrevendo em {placa.Nome}" : "transcrevendo em CPU"));

            transcricao = await asr.TranscreverAsync(caminhoDoMix, vocabulario, idioma,
                (pct, texto) => progresso?.Invoke(new Progresso("asr", pct, texto)), ct);
        }

        // A diarização roda só no system.wav: o que o microfone captou já se sabe
        // de quem é, e dar o mix ao pyannote o faria tentar separar você de você.
        IReadOnlyList<SegmentoDeFalante> diarizacao = [];
        if (diarizar)
        {
            using var diar = await MotorSidecar.IniciarAsync(
                motores.Python, [motores.ScriptDiarizacao], ct, ambiente);
            diarizacao = await diar.DiarizarAsync(sistema,
                (pct, texto) => progresso?.Invoke(new Progresso("diarizacao", pct, texto)), ct);
        }
        else
        {
            // Sem falantes, mas ainda com o dono: a faixa do microfone diz o que
            // é seu com certeza, e isso não custa GPU nenhuma. Desligar a
            // separação não é motivo para perder a única atribuição que o
            // desenho de duas faixas dá de graça.
            progresso?.Invoke(new Progresso("diarizacao", 1, "sem separar falantes"));
        }

        progresso?.Invoke(new Progresso("montagem", 0, "juntando texto e falantes"));

        var segmentos = transcricao.Segmentos
            .Select(s => new SegmentoFinal { Start = s.Inicio, End = s.Fim, Text = s.Texto })
            .ToList();

        if (filtrarSilencio) FiltroDeSilencio.Filtrar(segmentos, faixas.Mix());

        if (vocabulario is { Length: > 0 } && corrigirFonetica)
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
