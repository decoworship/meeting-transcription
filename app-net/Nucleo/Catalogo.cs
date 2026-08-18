using System.Text.Json.Serialization;

namespace MeetingApp.Nucleo;

/// <summary>Um pacote de modelo que a tela oferece.</summary>
/// <remarks>
/// <para>
/// O nome, o tamanho e a descrição ficam <b>num só lugar</b>, e não espalhados
/// entre um <c>&lt;option&gt;</c> no JavaScript e um <c>--modelo</c> na linha de
/// comando do motor. É a "tabela de modelos" que a análise do Meetily
/// recomendou copiar: nome → repositório, com o tamanho esperado ao lado.
/// </para>
/// <para>
/// O tamanho não é enfeite. Ele é o que permite dizer ao usuário quanto vai
/// custar antes de custar, e é o que detecta download interrompido — arquivo
/// menor que o esperado é pacote corrompido, que é a verificação barata que o
/// Meetily faz e que funciona.
/// </para>
/// </remarks>
public sealed class PacoteDeModelo
{
    /// <summary>O que o motor recebe em <c>--modelo</c>.</summary>
    [JsonPropertyName("id")] public required string Id { get; init; }

    [JsonPropertyName("nome")] public required string Nome { get; init; }

    /// <summary>"asr" ou "ata" — a aba agrupa por isto.</summary>
    /// <remarks>
    /// Havia uma terceira, <c>"diarizacao"</c>, e ela saiu na Fase 4 junto com o
    /// último pacote dela: a diarização deixou de ser um download.
    /// </remarks>
    [JsonPropertyName("familia")] public required string Familia { get; init; }

    /// <summary>Uma linha, dita em português comum.</summary>
    [JsonPropertyName("descricao")] public required string Descricao { get; init; }

    /// <summary>O repositório no HuggingFace, que é o que define a pasta no cache.</summary>
    [JsonPropertyName("repositorio")] public required string Repositorio { get; init; }

    [JsonPropertyName("tamanho_esperado_bytes")] public required long TamanhoEsperadoBytes { get; init; }

    /// <summary>
    /// Se o tamanho ao lado foi medido nesta máquina ou veio do repositório.
    /// </summary>
    /// <remarks>
    /// A distinção vai para a tela de propósito. Número medido e número
    /// publicado não valem o mesmo, e o dia em que a verificação de corrupção
    /// existir, ela só pode ser estrita sobre os medidos.
    /// </remarks>
    [JsonPropertyName("tamanho_medido")] public bool TamanhoMedido { get; init; }

    /// <summary>O que sabemos de custo, medido aqui. Vazio quando não medimos.</summary>
    [JsonPropertyName("nota")] public string? Nota { get; init; }

    /// <summary>
    /// O arquivo único a baixar, nos pacotes que não são repositório inteiro.
    /// </summary>
    /// <remarks>
    /// Os GGUF de ata moram em repositórios com uma dezena de quantizações, e
    /// baixar tudo traria 20 GB para usar 2,5. Preenchido só na família "ata";
    /// nulo nos outros, que continuam vindo por <c>snapshot_download</c>.
    /// </remarks>
    [JsonPropertyName("arquivo")] public string? Arquivo { get; init; }

    /// <summary>
    /// Onde o arquivo fica, quando não é no cache do HuggingFace.
    /// </summary>
    /// <remarks>
    /// O llama.cpp abre o <c>.gguf</c> por caminho, e não pela biblioteca do
    /// HF — então o modelo de ata mora ao lado do <c>llama-server</c>, em
    /// <c>motores/ata/modelos</c>, e não no cache.
    /// </remarks>
    [JsonPropertyName("nome_local")] public string? NomeLocal { get; init; }
}

/// <summary>Um pacote com o estado dele nesta máquina.</summary>
public sealed class PacoteComEstado
{
    [JsonPropertyName("pacote")] public required PacoteDeModelo Pacote { get; init; }

    /// <summary>"instalado", "parcial" ou "ausente".</summary>
    [JsonPropertyName("estado")] public required string Estado { get; init; }

    /// <summary>Quanto ocupa no cache agora. Zero quando ausente.</summary>
    [JsonPropertyName("bytes_em_disco")] public long BytesEmDisco { get; init; }

    /// <summary>Se é o que o app usa hoje, por configuração.</summary>
    [JsonPropertyName("em_uso")] public bool EmUso { get; init; }
}

/// <summary>
/// O que existe para baixar, e o que já está nesta máquina.
/// </summary>
/// <remarks>
/// <para>
/// <b>Por que esta classe existe antes de existir download.</b> Hoje o modelo
/// já é baixado em tempo de execução — o <c>faster_whisper</c> o puxa do
/// HuggingFace na primeira transcrição, sem barra de progresso, sem anunciar os
/// 3 GB e sem verificar o que chegou. A tela ficava sem ter o que mostrar porque
/// ninguém no lado C# sabia responder "o modelo está aí?".
/// </para>
/// <para>
/// Esta é a metade que responde. Ela só <b>lê</b>: nada aqui baixa nem apaga
/// nada. Quando o download entrar, ele entra atrás deste mesmo contrato, e a
/// tela não muda — que é a razão de o contrato vir primeiro.
/// </para>
/// </remarks>
public static class Catalogo
{
    /// <summary>
    /// Os pacotes oferecidos.
    /// </summary>
    /// <remarks>
    /// Deliberadamente menor que a lista que o <c>faster-whisper</c> aceita. As
    /// variantes <c>.en</c> não servem a um app de reuniões em português, e
    /// oferecer o que não serve é gastar a atenção de quem escolhe.
    /// </remarks>
    public static readonly IReadOnlyList<PacoteDeModelo> Pacotes =
    [
        new PacoteDeModelo
        {
            Id = "large-v3",
            Nome = "Large v3",
            Familia = "asr",
            Descricao = "O mais exato. É o que o app usa por padrão.",
            Repositorio = "Systran/faster-whisper-large-v3",
            TamanhoEsperadoBytes = 3_090_836_026,
            TamanhoMedido = true,
            Nota = "~4,5× o tempo real na RTX 2060, com a diarização junto.",
        },
        new PacoteDeModelo
        {
            Id = "large-v3-turbo",
            Nome = "Large v3 Turbo",
            Familia = "asr",
            Descricao = "Bem mais rápido que o Large v3, e menor. Perde um pouco fora do inglês.",
            Repositorio = "mobiuslabsgmbh/faster-whisper-large-v3-turbo",
            TamanhoEsperadoBytes = 1_620_000_000,
            TamanhoMedido = false,
            Nota = "Ainda não medido em português aqui — a comparação está por fazer.",
        },
        new PacoteDeModelo
        {
            Id = "medium",
            Nome = "Medium",
            Familia = "asr",
            Descricao = "Meio-termo, para quando a placa está ocupada.",
            Repositorio = "Systran/faster-whisper-medium",
            TamanhoEsperadoBytes = 1_530_000_000,
            TamanhoMedido = false,
        },
        new PacoteDeModelo
        {
            Id = "small",
            Nome = "Small",
            Familia = "asr",
            Descricao = "Rápido e impreciso. Serve para conferir se o áudio presta.",
            Repositorio = "Systran/faster-whisper-small",
            TamanhoEsperadoBytes = 484_000_000,
            TamanhoMedido = false,
        },
        new PacoteDeModelo
        {
            Id = "base",
            Nome = "Base",
            Familia = "asr",
            Descricao = "O menor que ainda produz texto legível.",
            Repositorio = "Systran/faster-whisper-base",
            TamanhoEsperadoBytes = 147_883_213,
            TamanhoMedido = true,
        },
        new PacoteDeModelo
        {
            Id = "qwen3-4b-instruct",
            Nome = "Qwen3 4B Instruct",
            Familia = "ata",
            Descricao = "Escreve as atas. É o que o app usa por padrão.",
            Repositorio = "unsloth/Qwen3-4B-Instruct-2507-GGUF",
            Arquivo = "Qwen3-4B-Instruct-2507-Q4_K_M.gguf",
            NomeLocal = "qwen3-4b-instruct-q4km.gguf",
            TamanhoEsperadoBytes = 2_497_281_120,
            TamanhoMedido = true,
            Nota = "Ata de reunião de 30 min em ~1 min; de 2 h em ~4 min, na RTX 2060.",
        },
        // Os dois candidatos medidos em 17/08/2026, em 5 reuniões e 2 rodadas
        // cada — ver docs/MODELOS-DE-ATA.md. Entram como **opção**, e o padrão
        // segue o Qwen3: com os quatro defeitos de prompt e redator corrigidos,
        // os três empataram em qualidade, e trocar o padrão atinge todo mundo.
        new PacoteDeModelo
        {
            Id = "gemma-4-e4b",
            Nome = "Gemma 4 E4B",
            Familia = "ata",
            Descricao = "O mais confiável nos testes, e o mais rápido. Pede placa folgada.",
            Repositorio = "unsloth/gemma-4-E4B-it-GGUF",
            Arquivo = "gemma-4-E4B-it-Q4_K_M.gguf",
            NomeLocal = "gemma-4-e4b-q4km.gguf",
            TamanhoEsperadoBytes = 4_977_171_584,
            TamanhoMedido = true,
            Nota = "61 s por ata e nenhuma falha em 10 rodadas, contra 122 s do padrão. "
                 + "Mas são 5 GB: numa placa de 6 GB fica apertado.",
        },
        new PacoteDeModelo
        {
            Id = "qwen3.5-4b",
            Nome = "Qwen3.5 4B",
            Familia = "ata",
            Descricao = "Rápido como o Gemma e metade do tamanho. Registra menos pendências.",
            Repositorio = "unsloth/Qwen3.5-4B-GGUF",
            Arquivo = "Qwen3.5-4B-Q4_K_M.gguf",
            NomeLocal = "qwen3.5-4b-q4km.gguf",
            TamanhoEsperadoBytes = 2_740_937_888,
            TamanhoMedido = true,
            Nota = "64 s por ata, mas 4,3 pendências por ata contra 5,0 e 6,3 dos outros "
                 + "— e pendência é o que a ata existe para não deixar cair.",
        },
        new PacoteDeModelo
        {
            Id = "qwen3-1.7b-instruct",
            Nome = "Qwen3 1.7B Instruct",
            Familia = "ata",
            Descricao = "Menor e mais rápido, para placa apertada. Ata mais pobre.",
            Repositorio = "unsloth/Qwen3-1.7B-GGUF",
            Arquivo = "Qwen3-1.7B-Q4_K_M.gguf",
            NomeLocal = "qwen3-1.7b-q4km.gguf",
            TamanhoEsperadoBytes = 1_100_000_000,
            TamanhoMedido = false,
            Nota = "Ainda não medido aqui — o 4B é o que passou no critério de qualidade.",
        },
        // ── A família "diarizacao" saiu do catálogo na Fase 4 ───────────────
        //
        // Não porque a diarização tenha mudado, mas porque ela **deixou de ser
        // um download**: os 57 MB de pesos viajam dentro do instalador e o motor
        // os carrega da pasta ao lado (docs/FASE4.md §4). O catálogo mede o
        // cache do HuggingFace, que o pipeline não lê mais — um cartão aqui
        // diria "ausente" sobre uma diarização que funciona, ou ofereceria
        // "Remover" sobre arquivos do instalador.
        //
        // A tela de Modelos continua dizendo qual modelo separa os falantes; o
        // que ela não faz mais é fingir que há o que escolher ou baixar.

        // O "Pyannote 3.1" saiu da lista na Fase 4, e o motivo não é de
        // empacotamento: **nada nunca o carregou.** O pipeline pede o
        // community-1 pelo nome (motores/diarizacao/motor.py), e a escolha de
        // modelo de diarização — `diarizacao_padrao` no app.json e `diar_model`
        // nas preferências do projeto — é colhida na tela, salva, e ignorada.
        // Oferecer o download de 26 MB de um modelo que o app não sabe usar é
        // gastar a atenção de quem escolhe com uma escolha que não existe.
        //
        // Ele também tem portão no HuggingFace, então desde que o token saiu do
        // binário esse download falharia com 401 — o que tornou visível um
        // defeito que já estava lá, calado.
        //
        // Ligar o seletor de verdade é trabalho de pipeline, e está na Fase 6.
        // Quando ele existir, esta entrada volta — junto de um caminho local
        // para os pesos, como o community-1 tem hoje.
    ];

    /// <summary>
    /// Onde o HuggingFace guarda o que baixou.
    /// </summary>
    /// <remarks>
    /// As duas variáveis são respeitadas na ordem que a própria biblioteca usa.
    /// Sem isso, quem move o cache para outro disco — coisa comum com 3 GB por
    /// modelo — veria a tela dizer "ausente" sobre um modelo que está lá.
    /// </remarks>
    public static string PastaDoCache()
    {
        if (Environment.GetEnvironmentVariable("HF_HUB_CACHE") is { Length: > 0 } direto)
            return direto;

        if (Environment.GetEnvironmentVariable("HF_HOME") is { Length: > 0 } casa)
            return Path.Combine(casa, "hub");

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".cache", "huggingface", "hub");
    }

    /// <summary>A pasta que um repositório ocupa no cache: <c>models--org--nome</c>.</summary>
    /// <remarks>
    /// Só a barra vira <c>--</c>. Os hifens que já existem no nome do
    /// repositório ficam como estão — <c>faster-whisper-large-v3</c> continua
    /// com um hífen em cada junta.
    /// </remarks>
    public static string PastaDoPacote(PacoteDeModelo pacote) =>
        pacote.Familia == "ata"
            ? PastaDosModelosDeAta()
            : Path.Combine(PastaDoCache(), "models--" + pacote.Repositorio.Replace("/", "--"));

    /// <summary>Ao lado do llama-server, que é quem abre o arquivo.</summary>
    public static string PastaDosModelosDeAta() =>
        Environment.GetEnvironmentVariable("MEETINGAPP_MOTOR_ATA") is { Length: > 0 } fora
            ? Path.Combine(fora, "modelos")
            : Path.Combine(AppContext.BaseDirectory, "motores", "ata", "modelos");

    /// <summary>O caminho final do arquivo de um pacote de ata.</summary>
    public static string ArquivoDoPacote(PacoteDeModelo pacote) =>
        Path.Combine(PastaDosModelosDeAta(), pacote.NomeLocal ?? pacote.Arquivo ?? pacote.Id);

    /// <summary>Os pacotes com o estado de cada um nesta máquina.</summary>
    /// <param name="config">Para marcar quais estão em uso hoje.</param>
    public static List<PacoteComEstado> Listar(ConfiguracoesDoApp config)
    {
        var lista = new List<PacoteComEstado>();
        foreach (var pacote in Pacotes)
        {
            // Na família "ata" o pacote é um arquivo, e não uma pasta de cache:
            // medir a pasta contaria os outros modelos de ata junto.
            long bytes = pacote.Familia == "ata"
                ? (File.Exists(ArquivoDoPacote(pacote))
                    ? new FileInfo(ArquivoDoPacote(pacote)).Length : 0)
                : TamanhoEmDisco(PastaDoPacote(pacote));

            // A margem de 5% existe porque o tamanho publicado não é o tamanho
            // em disco: o cache guarda blobs mais links, e o sistema de arquivos
            // arredonda. Estrito demais marcaria "parcial" o que está inteiro.
            string estado = bytes == 0 ? "ausente"
                          : bytes >= pacote.TamanhoEsperadoBytes * 0.95 ? "instalado"
                          : "parcial";

            lista.Add(new PacoteComEstado
            {
                Pacote = pacote,
                Estado = estado,
                BytesEmDisco = bytes,
                EmUso = pacote.Familia switch
                {
                    "ata" => pacote.NomeLocal == config.ModeloDeAta,
                    // "asr" e o que vier depois: o id é o que o motor recebe.
                    _ => pacote.Id == config.ModeloPadrao,
                },
            });
        }
        return lista;
    }

    /// <summary>
    /// O que impede este modelo de ser usado agora, ou <c>null</c> se nada impede.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Nasceu na Fase 4, com o instalador: até então o modelo estava sempre lá,
    /// porque a máquina era a de quem o baixou à mão. Numa instalação nova ele
    /// não está, e sem esta checagem a primeira transcrição morre lá dentro —
    /// o <c>faster_whisper</c> tenta baixar 3 GB sem barra de progresso, ou
    /// falha com uma mensagem de biblioteca que não diz o que fazer.
    /// </para>
    /// <para>
    /// <b>Só reprova o que sabe estar ausente.</b> Um id fora do catálogo passa:
    /// quem digitou um modelo que não conhecemos pode ter um cache montado à mão,
    /// e barrar por desconhecimento seria transformar ignorância em veto.
    /// </para>
    /// </remarks>
    public static string? OQueImpede(string? id)
    {
        if (id is not { Length: > 0 }) return null;

        var pacote = Pacotes.FirstOrDefault(p => p.Id == id);
        if (pacote is null) return null;

        long bytes = TamanhoEmDisco(PastaDoPacote(pacote));
        if (bytes == 0)
            return $"o modelo {pacote.Nome} ainda não foi baixado. "
                 + "Abra Ajustes → Modelos e baixe-o antes de transcrever.";

        if (bytes < pacote.TamanhoEsperadoBytes * 0.95)
            return $"o download do modelo {pacote.Nome} está pela metade "
                 + $"({bytes / 1_000_000} MB de {pacote.TamanhoEsperadoBytes / 1_000_000} MB). "
                 + "Abra Ajustes → Modelos e baixe-o de novo.";

        return null;
    }

    /// <summary>
    /// Espaço livre no disco de destino, em bytes. -1 quando não deu para ler.
    /// </summary>
    /// <remarks>
    /// Perguntar antes de baixar 3 GB é o que evita o pior desfecho do
    /// download: acabar o disco no meio, deixar um pacote parcial, e a próxima
    /// transcrição falhar por um motivo que não tem nada a ver com o que se
    /// estava fazendo.
    /// </remarks>
    public static long LivreNoDestino(PacoteDeModelo pacote)
    {
        try
        {
            string destino = pacote.Familia == "ata" ? PastaDosModelosDeAta() : PastaDoCache();
            // A pasta pode ainda não existir numa instalação nova; o que importa
            // é o volume, e ele existe.
            string? raiz = Path.GetPathRoot(Path.GetFullPath(destino));
            if (raiz is null or "") return -1;
            return new DriveInfo(raiz).AvailableFreeSpace;
        }
        catch (Exception e) when (e is IOException or ArgumentException
                                       or UnauthorizedAccessException)
        {
            return -1;
        }
    }

    private static long TamanhoEmDisco(string pasta)
    {
        try
        {
            if (!Directory.Exists(pasta)) return 0;

            long total = 0;
            foreach (string arquivo in Directory.EnumerateFiles(pasta, "*",
                                                               SearchOption.AllDirectories))
            {
                var info = new FileInfo(arquivo);
                // Os links simbólicos do cache apontam para os blobs, que já
                // foram contados. Somar os dois dobraria o tamanho.
                if (info.LinkTarget is null) total += info.Length;
            }
            return total;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Cache ilegível não pode derrubar a tela de configurações: some
            // como "ausente", que é o pior caso honesto.
            return 0;
        }
    }
}
