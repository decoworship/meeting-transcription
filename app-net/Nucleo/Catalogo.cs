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

    /// <summary>"asr" ou "diarizacao" — a aba agrupa por isto.</summary>
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
            Id = "community-1",
            Nome = "Community 1",
            Familia = "diarizacao",
            Descricao = "Separa quem falou. É o que o app usa por padrão.",
            Repositorio = "pyannote/speaker-diarization-community-1",
            TamanhoEsperadoBytes = 32_821_829,
            TamanhoMedido = true,
        },
        new PacoteDeModelo
        {
            Id = "3.1",
            Nome = "Pyannote 3.1",
            Familia = "diarizacao",
            Descricao = "A geração anterior. Fica como saída se o Community 1 regredir.",
            Repositorio = "pyannote/speaker-diarization-3.1",
            TamanhoEsperadoBytes = 26_000_000,
            TamanhoMedido = false,
        },
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
        Path.Combine(PastaDoCache(), "models--" + pacote.Repositorio.Replace("/", "--"));

    /// <summary>Os pacotes com o estado de cada um nesta máquina.</summary>
    /// <param name="config">Para marcar quais estão em uso hoje.</param>
    public static List<PacoteComEstado> Listar(ConfiguracoesDoApp config)
    {
        var lista = new List<PacoteComEstado>();
        foreach (var pacote in Pacotes)
        {
            long bytes = TamanhoEmDisco(PastaDoPacote(pacote));

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
                EmUso = pacote.Familia == "asr"
                    ? pacote.Id == config.ModeloPadrao
                    : pacote.Id == config.DiarizacaoPadrao,
            });
        }
        return lista;
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
