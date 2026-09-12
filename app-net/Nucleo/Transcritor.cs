using MeetingApp.Nucleo.Atas;
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
    /// <summary>
    /// O motor que faz texto <b>e</b> falante numa passada só.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Fora da lista posicional, e vazio por padrão</b>, porque ele é o único
    /// motor opcional: quem não ligou a chave <c>motor_de_transcricao</c> nunca
    /// o vê, e uma instalação sem ele transcreve normalmente. Um quinto
    /// parâmetro obrigatório obrigaria todo lugar que monta os motores a ter
    /// opinião sobre um caminho que a maioria deles não usa.
    /// </para>
    /// <para>
    /// Vazio é "esta instalação não tem o MOSS", e é o que
    /// <see cref="OQueFaltaParaMoss"/> reporta.
    /// </para>
    /// </remarks>
    public string ScriptMoss { get; init; } = "";

    /// <summary>O motor da legenda ao vivo, opcional como o do MOSS.</summary>
    public string ScriptLegenda { get; init; } = "";

    /// <summary>O arranjo esperado do app instalado: <c>motores/</c> ao lado do .exe.</summary>
    public static Motores AoLadoDoExecutavel()
    {
        string raiz = Path.Combine(AppContext.BaseDirectory, "motores");
        return new Motores(
            Path.Combine(raiz, "python", "python.exe"),
            Path.Combine(raiz, "asr", "motor.py"),
            Path.Combine(raiz, "diarizacao", "motor.py"),
            Path.Combine(raiz, "modelos", "motor.py"))
        {
            ScriptMoss = Path.Combine(raiz, "moss", "motor.py"),
            ScriptLegenda = Path.Combine(raiz, "legenda", "motor.py"),
        };
    }

    /// <summary>
    /// Os pipelines de diarização que existem em disco, pelo nome da pasta.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>A lista sai do disco e não de uma constante</b>, e é isso que a torna
    /// confiável: até 20/08/2026 a tela oferecia "Modelo de diarização" a partir
    /// do catálogo, que perdeu a família <c>diarizacao</c> na Fase 4 — de modo
    /// que o seletor tinha uma opção morta e o valor escolhido era ignorado
    /// (docs/FASE6.md §4.6). Ler a pasta faz o seletor e o motor não terem como
    /// discordar.
    /// </para>
    /// <para>
    /// O critério é o mesmo do motor — uma pasta com <c>config.yaml</c> dentro
    /// de <c>modelos/</c> —, e está escrito nos dois lugares de propósito: o
    /// motor precisa dele para carregar e o núcleo para oferecer, e um pedido
    /// pelo sidecar só para listar pastas custaria subir o Python.
    /// </para>
    /// <para>
    /// Vazia quando o empacotador ainda não rodou. Nesse caso o motor cai no
    /// HuggingFace, e a tela não oferece escolha nenhuma — que é a verdade.
    /// </para>
    /// </remarks>
    public IReadOnlyList<string> ModelosDeDiarizacao()
    {
        try
        {
            string raiz = Path.Combine(
                Path.GetDirectoryName(ScriptDiarizacao) ?? ".", "modelos");
            if (!Directory.Exists(raiz)) return [];

            return [.. Directory.EnumerateDirectories(raiz)
                .Where(d => File.Exists(Path.Combine(d, "config.yaml")))
                .Select(Path.GetFileName)
                .OfType<string>()
                .Order(StringComparer.Ordinal)];
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return [];
        }
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

    /// <summary>
    /// O mesmo, para o caminho do MOSS — separado porque ele é opcional.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>O MOSS não pode entrar no <see cref="OQueFalta"/></b>, e a razão é o
    /// que ele quebraria: aquele método é a porta de toda transcrição, e pôr ali
    /// um arquivo que só existe para quem ligou a chave faria o app inteiro
    /// recusar-se a transcrever numa instalação onde nada está errado. A escolha
    /// é por transcrição (<c>motor_de_transcricao</c>), então a conferência
    /// também é.
    /// </para>
    /// <para>
    /// <b>Só o script, e não o Python.</b> O <see cref="OQueFalta"/> já respondeu
    /// por ele antes de qualquer transcrição começar, e repetir a pergunta aqui
    /// só criaria um segundo lugar para ela ser respondida diferente.
    /// </para>
    /// </remarks>
    /// <summary>O que impede a legenda ao vivo, ou <c>null</c>.</summary>
    public string? OQueFaltaParaLegenda() =>
        ScriptLegenda.Length == 0 || !File.Exists(ScriptLegenda)
            ? $"o motor de legenda não está em {ScriptLegenda} — esta instalação não o tem"
            : null;

    public string? OQueFaltaParaMoss()
    {
        if (ScriptMoss.Length == 0 || !File.Exists(ScriptMoss))
            return $"o motor MOSS não está em {ScriptMoss} — esta instalação não o tem, "
                 + "e a transcrição volta ao motor clássico em Ajustes › Transcrição";
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
    /// <param name="modeloDeDiarizacao">
    /// Qual pipeline separa os falantes, pelo nome da pasta em
    /// <c>motores/diarizacao/modelos</c>. Nulo usa o padrão do motor.
    /// <b>Não</b> é o modelo de voz: aquele não se troca, porque vetores de
    /// modelos diferentes não são comparáveis. Ver FASE6 §4.6.
    /// </param>
    /// <param name="usarHotwords">
    /// Mandar o vocabulário do projeto ao ASR como <c>hotwords</c>.
    /// <b>Desligado por padrão desde 19/08/2026</b>, e o vocabulário continua
    /// servindo à correção fonética de qualquer jeito. Ver FASE6 §4.1.
    /// </param>
    /// <param name="motorDeTranscricao">
    /// <c>"classico"</c> — o pipeline de dois motores de sempre — ou
    /// <c>"moss"</c>, que faz texto e falante numa passada. Nulo é o clássico.
    /// <para>
    /// <b>A bifurcação é só entre o <see cref="Faixas.Ler"/> e o
    /// <see cref="VozDoDono.Trilha"/>, e isso não é conveniência de
    /// implementação.</b> É o que preserva as peças que as medições provaram
    /// necessárias: a <see cref="CorrecaoFonetica"/> e a
    /// <see cref="RevisaoDeTermos"/> são o que recupera a parte "de grafia" do
    /// vocabulário que o MOSS perde — ele não tem <c>hotwords</c>, e nenhum
    /// runtime ggml expõe um (docs/FASE7-RESULTADOS.md §12) —, e a
    /// <see cref="VozDoDono"/> é o que dá o dono de graça pela faixa do
    /// microfone, que o MOSS não sabe fazer porque só vê o mix.
    /// </para>
    /// <para>
    /// A régua desta fase é a suíte: com a chave em <c>"classico"</c> o caminho
    /// executado é o de antes, e os testes passam sem alteração nenhuma. Se
    /// algum precisar mudar, a bifurcação vazou para baixo.
    /// </para>
    /// </param>
    /// <remarks>
    /// O <c>try/finally</c> existe pelo marcador de etapa: sair por exceção
    /// **tratada** é o app sabendo que falhou, e aí o marcador tem de ir embora
    /// — quem o deixasse para trás faria o próximo início relatar um
    /// desligamento que não houve. O marcador só sobrevive ao que não passa por
    /// aqui: corte de energia, e o processo morto de fora.
    /// Ver <see cref="MarcaDeEtapa"/>.
    /// </remarks>
    public async Task<ResultadoDaTranscricao> ExecutarAsync(
        string pastaDaGravacao, string? vocabulario = null, string? idioma = null,
        bool filtrarSilencio = false, Action<Progresso>? progresso = null,
        string? modelo = null, string? cliente = null, string? projeto = null,
        bool diarizar = true, bool corrigirFonetica = true,
        bool usarHotwords = false, string? modeloDeDiarizacao = null,
        bool revisarComModelo = false, CaminhosDoMotorDeAta? motorDeAta = null,
        CancellationToken ct = default, string? motorDeTranscricao = null)
    {
        try
        {
            return await ExecutarInternoAsync(
                pastaDaGravacao, vocabulario, idioma, filtrarSilencio, progresso,
                modelo, cliente, projeto, diarizar, corrigirFonetica, usarHotwords,
                modeloDeDiarizacao, revisarComModelo, motorDeAta, ct, motorDeTranscricao);
        }
        finally
        {
            MarcaDeEtapa.Terminar();
        }
    }

    private async Task<ResultadoDaTranscricao> ExecutarInternoAsync(
        string pastaDaGravacao, string? vocabulario, string? idioma,
        bool filtrarSilencio, Action<Progresso>? progresso,
        string? modelo, string? cliente, string? projeto,
        bool diarizar, bool corrigirFonetica,
        bool usarHotwords, string? modeloDeDiarizacao,
        bool revisarComModelo, CaminhosDoMotorDeAta? motorDeAta,
        CancellationToken ct, string? motorDeTranscricao)
    {
        string motorEscolhido = ConfiguracoesDoApp.MotorAceito(
            motorDeTranscricao ?? ConfiguracoesDoApp.Carregar().MotorDeTranscricao);
        bool comMoss = motorEscolhido == Vozes.MotorMoss;
        // O vocabulário se divide em dois usos que sempre foram tratados como
        // um: enviesar o ASR (hotwords) e corrigir a grafia depois
        // (CorrecaoFonetica). A Fase 0 mediu que os dois recuperam nomes na
        // mesma medida — 36 contra 36 — e concluiu que o primeiro era
        // dispensável; a correção foi escrita, e o hotwords nunca foi desligado.
        // O app pagava os dois e recebia um, e o que ele pagava era a
        // segmentação: 207 segmentos contra 787 no mesmo áudio, com 15 deles
        // passando de 25 s. Ver FASE6 §4.1.
        //
        // Só esta variável vai ao motor. `vocabulario` segue inteiro para a
        // correção fonética adiante, que é o mecanismo que ficou.
        string? vocabularioDoAsr = usarHotwords ? vocabulario : null;

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

        // ASR primeiro, diarização depois, cada um no seu processo: numa placa
        // de 6 GB os dois modelos não cabem juntos, e processos separados fazem
        // a VRAM do primeiro voltar antes de o segundo subir.
        // O mesmo ambiente para os dois motores, montado num lugar só — inclusive
        // o desligamento da telemetria do pyannote. Ver Motores.Ambiente().
        var ambiente = Motores.Ambiente();

        Registro.Escrever("pipeline",
            $"transcrever {Path.GetFileName(pastaDaGravacao)} · motor {motorEscolhido} · "
            + $"modelo {escolhido} · "
            + $"diarizar={diarizar} ({modeloDeDiarizacao ?? "padrão"}) · "
            + $"hotwords={usarHotwords}");

        // O marcador de etapa, e a marca órfã que ele encontrou. Uma órfã prova
        // que a transcrição anterior não terminou — e diz em qual etapa —, que é
        // a pergunta que o SUP-2 fez duas vezes sem conseguir resposta.
        // Ver Nucleo/MarcaDeEtapa.cs.
        if (MarcaDeEtapa.Comecar(Path.GetFileName(pastaDaGravacao), motorEscolhido,
                                 escolhido) is { } orfa)
            Registro.Escrever("pipeline", MarcaDeEtapa.Descrever(orfa));

        // **A etapa se anota sozinha.** Envolver o progresso aqui, num lugar só,
        // em vez de chamar MarcaDeEtapa.Etapa nos dez pontos que já invocam o
        // Progresso: dez chamadas divergem, e a que divergir é a que ninguém
        // olha. O Etapa() ignora repetição, então a cadência do progresso não
        // vira cadência de escrita em disco.
        var doChamador = progresso;
        progresso = p => { MarcaDeEtapa.Etapa(p.Etapa); doChamador?.Invoke(p); };

        // O texto que já existe em disco, quando existe. Ver Retomada.
        // `vocabularioDoAsr` e não `vocabulario`: com o hotwords desligado o
        // vocabulário não decide mais a saída do ASR, e um parcial recusado por
        // uma vírgula mexida no vocabulário custaria uma transcrição inteira à
        // toa. Ligar ou desligar a chave, esse sim, invalida o parcial — porque
        // aí o texto sai mesmo diferente.
        var jaFeito = Retomada.Ler(pastaDaGravacao, escolhido, idioma, vocabularioDoAsr,
                                   motorEscolhido);

        // As faixas são lidas nos dois caminhos: mesmo retomando, AtribuirDono
        // precisa delas para dizer o que é do microfone.
        progresso?.Invoke(new Progresso(
            "mix", 0, jaFeito is null ? "somando as duas faixas" : "lendo as faixas"));
        var faixas = Faixas.Ler(mic, sistema);

        List<SegmentoFinal> segmentos;
        string? idiomaDetectado;
        double duracaoDoAudio;

        // O que a bifurcação produz: no caminho clássico ela fica vazia e os
        // falantes vêm do pyannote adiante; no caminho do MOSS ela já chega
        // pronta, porque o modelo carimbou falante junto com o texto.
        IReadOnlyList<SegmentoDeFalante> diarizacao = [];

        if (jaFeito is not null)
        {
            Registro.Escrever("pipeline",
                $"retomando: {jaFeito.Segmentos.Count} segmentos já transcritos em disco, "
                + "o ASR não roda de novo");
            progresso?.Invoke(new Progresso("asr", 1, "texto já transcrito, retomando"));

            segmentos = jaFeito.Segmentos;
            idiomaDetectado = jaFeito.Idioma;
            duracaoDoAudio = jaFeito.Duracao ?? 0;
        }
        else if (comMoss)
        {
            // ─────────────────────── o caminho do MOSS ───────────────────────
            //
            // Texto e falante numa passada, em blocos de 3 minutos. Substitui as
            // DUAS etapas do clássico — o RodarAsrAsync e a chamada ao pyannote
            // — e nada abaixo da bifurcação muda. Ver docs/FASE7-BACKEND.md.
            var doMoss = await MossEmBlocos.TranscreverAsync(
                pastaDaGravacao, faixas, motores, ambiente,
                ConfiguracoesDoApp.Carregar().PermitirCpu, progresso, ct);

            segmentos = doMoss.Segmentos;
            duracaoDoAudio = doMoss.Duracao;
            // **O idioma é o que foi PEDIDO, não o detectado.** O MOSS não
            // detecta idioma e recusa recebê-lo: o build GGUF declara
            // ('en','zh') e nega 'pt', embora transcreva português corretamente
            // quando ninguém pede nada (docs/FASE7-RESULTADOS.md §12.1).
            // Inventar "pt" aqui seria gravar como fato uma coisa não medida.
            idiomaDetectado = idioma;

            // **Sem AtribuirDono aqui, ao contrário do caminho clássico**, e a
            // diferença é o que torna a retomada possível. Lá o segmento chega
            // sem falante e marcar o dono não custa nada; aqui o `Speaker`
            // carrega o rótulo local do bloco (`b3_S1`), e sobrescrevê-lo com
            // "You" apagaria justamente o que a costura precisa ler. O dono
            // entra adiante, pelo VozDoDono, que é de onde ele sempre veio.
            Retomada.Escrever(pastaDaGravacao, new ResultadoDaTranscricao
            {
                Language = idiomaDetectado,
                Duration = duracaoDoAudio,
                Client = cliente,
                Project = projeto,
                Date = DataDaReuniao(pastaDaGravacao),
                Segments = segmentos,
                Engine = motorEscolhido,
            }, escolhido, idioma, vocabularioDoAsr, motorEscolhido);
        }
        else
        {
            (segmentos, idiomaDetectado, duracaoDoAudio) = await RodarAsrAsync(
                pastaDaGravacao, faixas, modelo, vocabularioDoAsr, idioma, ambiente,
                progresso, ct);

            // Só o que não depende da diarização, para o parcial já valer na
            // tela. O resto — filtro, correção, falantes — roda adiante, nos
            // dois caminhos, sobre o texto cru.
            Montagem.AtribuirDono(segmentos, faixas);

            Retomada.Escrever(pastaDaGravacao, new ResultadoDaTranscricao
            {
                Language = idiomaDetectado,
                Duration = duracaoDoAudio,
                Client = cliente,
                Project = projeto,
                Date = DataDaReuniao(pastaDaGravacao),
                Segments = segmentos,
            }, escolhido, idioma, vocabularioDoAsr, motorEscolhido);
        }
        // **A costura é, no caminho do MOSS, o que a diarização é no clássico:**
        // a etapa que o parcial deixa pendente. Por isso ela vive aqui e não
        // dentro do ramo acima — uma retomada precisa refazê-la, exatamente como
        // uma retomada clássica refaz o pyannote. Os rótulos do MOSS são locais
        // ao bloco, e transformá-los em identidade é trabalho de vetor de voz.
        if (comMoss && diarizar)
        {
            progresso?.Invoke(new Progresso(
                "diarizacao", 0, "juntando os falantes entre os blocos"));

            // Um motor quente para a costura inteira: são dezenas de rótulos, e
            // subir o pyannote por rótulo pagaria a carga do modelo dezenas de
            // vezes. É o mesmo modelo de voz do banco — carregar um próprio aqui
            // criaria um segundo espaço vetorial dentro do mesmo app.
            using var voz = await MotorSidecar.IniciarAsync(
                motores.Python, [motores.ScriptDiarizacao], ct, ambiente);
            voz.AoRegistrar += l => Registro.Escrever("costura", l);

            // O mix, e não o system.wav: o MOSS ouviu a conversa inteira, o dono
            // incluído, e os carimbos que ele devolveu são da linha do tempo do
            // mix. Pedir o vetor sobre outra faixa alinharia o trecho errado.
            string caminhoDoMix = Path.Combine(pastaDaGravacao, "mix.wav");
            if (!File.Exists(caminhoDoMix)) faixas.EscreverMix(caminhoDoMix);

            var costura = await CosturaDeFalantes.CosturarAsync(segmentos,
                async (trechos, c) =>
                {
                    try
                    {
                        return (await voz.VozAsync(caminhoDoMix, trechos, c)).Vetor;
                    }
                    catch (MotorException)
                    {
                        // Sem vetor, a costura trata o rótulo como gente nova —
                        // o erro barato, que quem lê conserta juntando duas
                        // linhas. Ver Nucleo/CosturaDeFalantes.cs.
                        return null;
                    }
                }, ct);

            diarizacao = costura.Falantes;
            Registro.Escrever("costura",
                $"{costura.RotulosLocais} rótulos locais → {costura.Identidades} "
                + $"identidades ({costura.SemVoz} sem fala limpa para ancorar)");
        }
        // A diarização roda só no system.wav: o que o microfone captou já se sabe
        // de quem é, e dar o mix ao pyannote o faria tentar separar você de você.
        else if (diarizar && !comMoss)
        {
            using var diar = await MotorSidecar.IniciarAsync(
                motores.Python, [motores.ScriptDiarizacao], ct, ambiente);
            diar.AoRegistrar += l => Registro.Escrever("diarizacao", l);
            diarizacao = await diar.DiarizarAsync(sistema,
                (pct, texto) => progresso?.Invoke(new Progresso("diarizacao", pct, texto)),
                modeloDeDiarizacao, ct);
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

        // O dono entra na linha do tempo junto com o pyannote, e não depois
        // dele. É o que faz um segmento em sobreposição ser CORTADO na fronteira
        // em vez de trocar de dono por inteiro — ver Nucleo/VozDoDono.cs. Sem
        // isto, toda vez que o dono falava junto com alguém, a fala dele saía
        // atribuída ao outro; era a causa de 30% dos erros de rótulo medidos.
        var trilhaDoDono = VozDoDono.Trilha(faixas);
        if (trilhaDoDono.Count > 0)
            Registro.Escrever("pipeline",
                $"a faixa do microfone rendeu {trilhaDoDono.Count} trechos do dono");
        else
            Registro.Escrever("pipeline",
                "sem trilha do dono: o microfone capta o alto-falante "
                + $"(vazamento {VozDoDono.Vazamento(faixas):F4}) ou está vazio");
        diarizacao = VozDoDono.Juntar(diarizacao, trilhaDoDono);

        // Antes de qualquer coisa que reescreva texto: o corte usa as palavras
        // para montar o texto de cada pedaço, e a correção fonética adiante
        // deixa as duas coisas desencontradas. Ver Montagem.RepartirPorFalante.
        int cortados = Montagem.RepartirPorFalante(segmentos, diarizacao);
        if (cortados > 0)
            Registro.Escrever("pipeline",
                $"{cortados} segmentos tinham mais de um falante dentro e foram cortados "
                + $"— {segmentos.Count} trechos no total");

        // As palavras já serviram. Não vão para o arquivo pronto: ele precisa
        // sair como sempre saiu. Ver SegmentoFinal.Words.
        foreach (var seg in segmentos) seg.Words = null;

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
                    Anotar(seg, trocas);
                }
            }
        }

        // Depois da fonética, e não no lugar dela: as duas pegam coisas
        // diferentes. A fonética recupera grafia de som parecido ("Jimmy" por
        // "Dimi"); esta recupera sigla e nome próprio por distância de edição
        // ("G6CB" por "GCCB"), que é onde a fonética acerta zero — medido nos
        // dez casos de AuditoriaCorrecaoFonetica. Ver Nucleo/RevisaoDeTermos.cs.
        //
        // Rodar depois evita que as duas disputem a mesma palavra: o que a
        // fonética já consertou vira termo conhecido e esta nem olha.
        if (corrigirFonetica)
        {
            var entidades = EntidadesConhecidas(pastaDaGravacao, vocabulario, cliente, projeto);
            var cruas = new List<Proposta>(
                RevisaoDeTermos.Propor(segmentos.Select(s => s.Text), entidades));

            // A grafia entra junto, e não no lugar: as duas pegam coisas
            // diferentes. A de cima recupera o termo que o motor ouviu errado
            // ("G6CB" por "GCCB"); esta recupera o que ele ouviu certo e
            // escreveu com o espaço no lugar errado ("next best" por
            // "NextBest"). É o que fecha boa parte da distância medida no §12
            // da docs/FASE7-RESULTADOS.md, e vale para os dois motores.
            cruas.AddRange(
                RevisaoDeTermos.ProporGrafia(segmentos.Select(s => s.Text), entidades));

            // O segundo propositor, quando ligado. Ele nunca substitui o
            // primeiro: as duas listas se somam e passam pela mesma porta.
            // Falhar aqui não pode derrubar a transcrição — a revisão é
            // acabamento, e o texto já está pronto.
            if (revisarComModelo && motorDeAta is not null && entidades.Count > 0)
            {
                try
                {
                    progresso?.Invoke(new Progresso("montagem", 0.7, "revisando os termos"));
                    cruas.AddRange(await PropositorDeModelo.ProporAsync(
                        new MotorDeAta(motorDeAta), segmentos.Select(s => s.Text),
                        entidades, ct: ct));
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception e)
                {
                    Registro.Escrever("pipeline",
                        $"a revisão pelo modelo falhou e foi ignorada: {e.Message}");
                }
            }

            var propostas = RevisaoDeTermos.Validar(cruas, entidades);

            if (propostas.Count > 0)
            {
                int mexidos = 0;
                foreach (var seg in segmentos)
                {
                    var (texto, trocas) = RevisaoDeTermos.Aplicar(seg.Text, propostas);
                    if (trocas.Count == 0) continue;
                    seg.Text = texto;
                    Anotar(seg, trocas);
                    mexidos++;
                }
                Registro.Escrever("pipeline",
                    $"revisão de termos: {propostas.Count} troca(s) em {mexidos} trecho(s) — "
                    + string.Join(", ", propostas.Take(6).Select(p => $"{p.De}→{p.Para}")));
            }
        }

        Montagem.AtribuirFalantes(segmentos, diarizacao);

        // Rede, e não regra principal: quando a trilha do dono não existe — sem
        // microfone, ou com vazamento de alto-falante — o teste por segmento é
        // o comportamento de antes de 21/08/2026, que rodou em campo. Quando ela
        // existe, o AtribuirFalantes já resolveu, e isto não muda nada.
        //
        // **No caminho do MOSS ela deixa de ser rede e vira a regra**, e isso é
        // consequência de uma coisa só: ele não carimba palavra. O desenho de
        // 21/08 dá a vitória ao dono por CORTE — o VozDoDono.Juntar põe os
        // trechos dele por cima sem recortar, e quem resolve a sobreposição é o
        // RepartirPorFalante, cortando o segmento na fronteira. Sem palavras não
        // há corte, e aí o AtribuirFalantes soma sobreposição sobre o segmento
        // inteiro: o intervalo da costura cobre 100% dele e o do dono cobre uma
        // fração, então a costura vence sempre e o dono não aparece nunca.
        //
        // Medido em 09/09/2026, na primeira transcrição de verdade com o MOSS:
        // a faixa do microfone rendeu 9 trechos do dono e a saída teve **zero**
        // trechos "You" — o dono saiu como mais um participante nomeado pelo
        // banco de vozes. Ver docs/FASE7-BACKEND.md, o princípio do §"o que
        // preserva as peças que as medições provaram necessárias".
        //
        // O que se perde aceitando isto: um segmento em que o dono e outra
        // pessoa falam junto passa a ser do dono por inteiro, em vez de cortado
        // na fronteira. O que se ganha é o dono existir. Enquanto o MOSS não
        // devolver palavras, essa é a troca — e ela é a favor de saber, porque
        // a faixa do microfone não estima: ela sabe.
        if (trilhaDoDono.Count == 0 || comMoss) Montagem.AtribuirDono(segmentos, faixas);

        // Quem já foi nomeado antes chega nomeado. Roda depois de tudo porque
        // precisa dos falantes montados, e nunca derruba a transcrição: não
        // reconhecer é o estado normal de quem nunca foi apresentado.
        try
        {
            progresso?.Invoke(new Progresso("montagem", 0.5, "procurando vozes conhecidas"));
            var conhecidos = await new AprendizadoDeVozes(motores, new Vozes())
                // As faixas já estão em memória aqui; a guarda de contaminação
                // sai de graça. Ver AprendizadoDeVozes.TrechosDe.
                .ReconhecerAsync(pastaDaGravacao, segmentos, faixas.Mic, ct);

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
            Language = idiomaDetectado,
            Duration = duracaoDoAudio,
            Client = cliente,
            Project = projeto,
            Date = DataDaReuniao(pastaDaGravacao),
            Segments = segmentos,
            // Nulo no caminho clássico, e é o que mantém o arquivo saindo byte a
            // byte como sempre saiu. Ver ResultadoDaTranscricao.Engine.
            Engine = comMoss ? motorEscolhido : null,
        };

        await File.WriteAllTextAsync(
            Path.Combine(pastaDaGravacao, "transcricao.json"), resultado.ParaJson(), ct);

        progresso?.Invoke(new Progresso("montagem", 1, "pronto"));
        return resultado;
    }

    /// <summary>Acrescenta trocas ao rastro do trecho, sem apagar as de antes.</summary>
    /// <remarks>
    /// Atribuir em vez de acrescentar apagaria o que a correção fonética
    /// registrou — e o rastro existe justamente para a pessoa poder desfazer.
    /// </remarks>
    private static void Anotar(SegmentoFinal seg, IEnumerable<Troca> trocas)
    {
        seg.Swaps ??= [];
        seg.Swaps.AddRange(trocas.Select(t => new TrocaFeita { De = t.De, Para = t.Para }));
    }

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
    private static IReadOnlyList<string> EntidadesConhecidas(
        string pasta, string? vocabulario, string? cliente, string? projeto)
    {
        var (nomes, emails) = ConvidadosDaAgenda.Ler(pasta);
        var pessoas = Atas.Organizacoes.Classificar(nomes, emails, []);

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

    /// <summary>
    /// O mix e o ASR: a etapa cara, isolada para que a retomada possa pulá-la.
    /// </summary>
    /// <remarks>
    /// Devolve o texto <b>cru</b>, sem filtro de silêncio, correção fonética nem
    /// falantes. É o que <see cref="Retomada"/> grava em disco, e o que os dois
    /// caminhos do pipeline tratam adiante do mesmo jeito.
    /// </remarks>
    private async Task<(List<SegmentoFinal> Segmentos, string? Idioma, double Duracao)>
        RodarAsrAsync(string pastaDaGravacao, Faixas faixas, string? modelo,
                      string? vocabulario, string? idioma,
                      Dictionary<string, string> ambiente,
                      Action<Progresso>? progresso, CancellationToken ct)
    {
        // O mix vai para junto da gravação: é derivado e refazível, mas enquanto
        // o pipeline roda ele precisa existir num caminho que o motor abra.
        string caminhoDoMix = Path.Combine(pastaDaGravacao, "mix.wav");
        faixas.EscreverMix(caminhoDoMix);

        Transcricao transcricao;
        string[] argsAsr = modelo is { Length: > 0 }
            ? [motores.ScriptAsr, "--modelo", modelo]
            : [motores.ScriptAsr];

        using (var asr = await MotorSidecar.IniciarAsync(
                   motores.Python, argsAsr, ct, ambiente))
        {
            // O que o motor diz de si — dispositivo, carga do modelo, avisos de
            // CUDA — passa a existir em disco. Era tudo o que faltava para
            // diagnosticar a máquina de quem instalou.
            asr.AoRegistrar += l => Registro.Escrever("asr", l);
            // A placa, perguntada ao motor ANTES de carregar o modelo.
            //
            // Relatado em 18/08/2026: a transcrição caiu para CPU numa máquina
            // com RTX 4050 e o large-v3 comeu RAM por horas até derrubar o
            // Windows. Rodar em CPU não é um modo do app — é o que acontece
            // quando o motor não acha a placa, e a diferença entre as duas
            // coisas precisa ser dita antes, não descoberta depois.
            var placa = await asr.DispositivoAsync(ct);
            Registro.Escrever("asr", placa.Cuda
                ? $"dispositivo: {placa.Nome} (CUDA {placa.CudaDoTorch})"
                : $"dispositivo: CPU — {placa.Motivo ?? "sem detalhe"}");

            if (!placa.Cuda && !ConfiguracoesDoApp.Carregar().PermitirCpu)
                throw new MotorException(SemPlaca(placa));

            progresso?.Invoke(new Progresso(
                "asr", 0, placa.Cuda ? $"transcrevendo em {placa.Nome}" : "transcrevendo em CPU"));

            transcricao = await asr.TranscreverAsync(caminhoDoMix, vocabulario, idioma,
                (pct, texto) => progresso?.Invoke(new Progresso("asr", pct, texto)), ct);
        }

        var segmentos = transcricao.Segmentos
            .Select(s => new SegmentoFinal
            {
                Start = s.Inicio,
                End = s.Fim,
                Text = s.Texto,
                // O alinhamento por palavra vai adiante em vez de ser descartado
                // aqui: é o que Montagem.RepartirPorFalante usa para cortar o
                // segmento na troca de falante. Ver FASE6 §4.5.
                Words = s.Palavras?.Count > 0
                    ? [.. s.Palavras.Select(p => new PalavraDita
                        { Start = p.Inicio, End = p.Fim, Text = p.Texto })]
                    : null,
            })
            .ToList();
        return (segmentos, transcricao.Idioma, transcricao.Duracao);
    }
}
