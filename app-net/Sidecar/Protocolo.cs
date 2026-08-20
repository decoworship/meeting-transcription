using System.Text.Json;
using System.Text.Json.Serialization;

namespace MeetingApp.Sidecar;

/// <summary>Um trecho de fala atribuído a um falante, vindo da diarização.</summary>
/// <remarks>
/// O rótulo vem cru do motor (<c>SPEAKER_00</c>). Traduzir para "Falante 1" é
/// decisão de apresentação e vive no núcleo. Ver docs/SIDECAR.md.
/// </remarks>
public sealed record SegmentoDeFalante(double Inicio, double Fim, string Falante);

/// <summary>Uma palavra com o tempo em que foi dita, vinda do alinhamento do ASR.</summary>
public sealed record Palavra(double Inicio, double Fim, string Texto);

/// <summary>Um trecho transcrito, vindo do ASR. Sem falante: quem atribui é o núcleo.</summary>
/// <param name="Palavras">
/// O alinhamento por palavra, quando o motor o mandou.
/// </param>
/// <remarks>
/// <b>As palavras existem para poder cortar o segmento.</b> O motor sempre
/// calculou o alinhamento (<c>word_timestamps=True</c>) e o núcleo sempre o
/// descartou — pagava-se o custo e perdia-se o uso. Um segmento de 43 s com
/// três pessoas dentro recebe <b>um</b> rótulo de falante, e duas somem; com as
/// palavras dá para cortá-lo onde a diarização diz que o falante mudou. Ver
/// docs/FASE6.md §4.1 e §4.5.
///
/// Vem vazia de motor antigo, e o núcleo trata isso como "não dá para cortar",
/// nunca como erro.
/// </remarks>
public sealed record SegmentoDeTexto(double Inicio, double Fim, string Texto,
                                     IReadOnlyList<Palavra>? Palavras = null);

/// <summary>O que o motor de ASR devolve por gravação.</summary>
/// <param name="Dispositivo">
/// "cuda" ou "cpu" — o que o motor <b>de fato</b> usou.
/// </param>
/// <param name="MotivoDaCpu">
/// Quando caiu para CPU, por quê. Nulo quando rodou na placa.
/// </param>
/// <remarks>
/// Os dois últimos campos existem desde 18/08/2026, quando a transcrição de um
/// usuário rodou na CPU numa máquina com RTX 4050 e <b>derrubou o Windows</b>
/// por falta de RAM. O motor já mandava o dispositivo; o núcleo o descartava.
/// </remarks>
public sealed record Transcricao(
    IReadOnlyList<SegmentoDeTexto> Segmentos, string? Idioma, double Duracao,
    string? Dispositivo = null, string? MotivoDaCpu = null);

/// <summary>O que o motor enxerga da placa, antes de carregar modelo nenhum.</summary>
/// <param name="Cuda">O torch achou uma placa utilizável.</param>
/// <param name="Nome">O modelo da placa, quando achou.</param>
/// <param name="CudaDoTorch">A versão de CUDA do torch. Nula num build de CPU.</param>
/// <param name="Motivo">Por que não achou, quando não achou.</param>
public sealed record DispositivoDoMotor(
    bool Cuda, string? Nome, string? CudaDoTorch, string? Motivo);

/// <summary>
/// Uma linha vinda do motor. Ver docs/SIDECAR.md para o contrato.
/// </summary>
/// <remarks>
/// Um único tipo para todas as mensagens, com os campos opcionais nulos quando
/// não se aplicam: são quatro formas de mensagem e uma hierarquia polimórfica
/// custaria mais em cerimônia do que economizaria em clareza.
/// </remarks>
internal sealed class Mensagem
{
    [JsonPropertyName("tipo")] public string? Tipo { get; init; }
    [JsonPropertyName("id")] public int? Id { get; init; }

    // "pronto"
    [JsonPropertyName("motor")] public string? Motor { get; init; }
    [JsonPropertyName("versao")] public string? Versao { get; init; }

    // "progresso"
    [JsonPropertyName("pct")] public double? Pct { get; init; }
    [JsonPropertyName("texto")] public string? Texto { get; init; }

    // "resultado"
    [JsonPropertyName("segmentos")] public List<SegmentoJson>? Segmentos { get; init; }
    [JsonPropertyName("idioma")] public string? Idioma { get; init; }
    [JsonPropertyName("duracao")] public double? Duracao { get; init; }

    /// <summary>"cuda" ou "cpu": onde o motor rodou de verdade.</summary>
    [JsonPropertyName("dispositivo")] public string? Dispositivo { get; init; }

    /// <summary>Por que não foi para a placa, quando não foi.</summary>
    [JsonPropertyName("motivo")] public string? Motivo { get; init; }

    /// <summary>Da operação <c>dispositivo</c>: o torch achou CUDA?</summary>
    [JsonPropertyName("cuda")] public bool? Cuda { get; init; }

    /// <summary>Da operação <c>dispositivo</c>: a versão de CUDA do torch.</summary>
    [JsonPropertyName("cuda_do_torch")] public string? CudaDoTorch { get; init; }

    /// <summary>Da operação <c>dispositivo</c>: o nome da placa, se houver.</summary>
    [JsonPropertyName("nome")] public string? Nome { get; init; }

    /// <summary>O vetor que identifica uma voz (operação "voz").</summary>
    [JsonPropertyName("vetor")] public float[]? Vetor { get; init; }

    // "erro"
    [JsonPropertyName("mensagem")] public string? MensagemDeErro { get; init; }
}

/// <remarks>
/// Um só formato de segmento para os dois motores: a diarização preenche
/// <c>falante</c> e o ASR preenche <c>texto</c>. Separá-los em dois esquemas
/// obrigaria o cliente a saber de antemão qual motor respondeu, e o que o
/// protocolo ganha em precisão perderia em rigidez quando um terceiro motor
/// devolver os dois.
/// </remarks>
internal sealed class SegmentoJson
{
    [JsonPropertyName("inicio")] public double Inicio { get; init; }
    [JsonPropertyName("fim")] public double Fim { get; init; }
    [JsonPropertyName("falante")] public string? Falante { get; init; }
    [JsonPropertyName("texto")] public string? Texto { get; init; }

    /// <summary>Só o ASR preenche, e só desde 19/08/2026. Ver <see cref="Palavra"/>.</summary>
    [JsonPropertyName("palavras")] public List<PalavraJson>? Palavras { get; init; }
}

/// <summary>Uma palavra alinhada, como o motor a manda.</summary>
internal sealed class PalavraJson
{
    [JsonPropertyName("inicio")] public double Inicio { get; init; }
    [JsonPropertyName("fim")] public double Fim { get; init; }
    [JsonPropertyName("texto")] public string? Texto { get; init; }
}

internal sealed class Requisicao
{
    [JsonPropertyName("id")] public required int Id { get; init; }
    [JsonPropertyName("op")] public required string Op { get; init; }

    /// <summary>
    /// O arquivo a processar. Nulo nas operações que não olham áudio.
    /// </summary>
    /// <remarks>
    /// Era obrigatório enquanto todo motor recebia um WAV. O motor de modelos
    /// baixa um repositório e não abre áudio nenhum — manter o campo obrigatório
    /// obrigaria a inventar um caminho falso para satisfazer o tipo.
    /// </remarks>
    [JsonPropertyName("audio")] public string? Audio { get; init; }

    /// <summary>O repositório do HuggingFace, para a operação de baixar.</summary>
    [JsonPropertyName("repositorio")] public string? Repositorio { get; init; }

    /// <summary>
    /// A pasta do cache e o tamanho esperado, só para o motor medir o andamento.
    /// </summary>
    /// <remarks>
    /// Vão daqui em vez de o motor deduzi-los: o <c>Catalogo</c> é o dono do
    /// tamanho esperado — o mesmo número que detecta pacote corrompido — e ter
    /// os dois lados calculando o caminho do cache seria garantir que um dia
    /// discordassem.
    /// </remarks>
    [JsonPropertyName("pasta")] public string? Pasta { get; init; }
    [JsonPropertyName("tamanho_esperado")] public long? TamanhoEsperado { get; init; }

    /// <summary>Um arquivo só do repositório, quando não se quer o todo.</summary>
    [JsonPropertyName("arquivo")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Arquivo { get; init; }

    /// <summary>
    /// Vocabulário do projeto, repassado ao ASR como <c>hotwords</c>.
    /// </summary>
    /// <remarks>
    /// Sem o orçamento de 224 tokens do <c>initial_prompt</c>: a correção
    /// fonética a jusante (FASE0 5-A) libertou a lista, e o
    /// <c>hotwords</c> é reinjetado em toda janela de 30 s em vez de só na
    /// primeira.
    /// </remarks>
    [JsonPropertyName("vocabulario")] public string? Vocabulario { get; init; }

    [JsonPropertyName("idioma")] public string? Idioma { get; init; }

    /// <summary>Intervalos de fala, para a operação de extrair voz.</summary>
    [JsonPropertyName("trechos")] public List<TrechoJson>? Trechos { get; init; }
}

internal sealed class TrechoJson
{
    [JsonPropertyName("inicio")] public double Inicio { get; init; }
    [JsonPropertyName("fim")] public double Fim { get; init; }
}

/// <remarks>
/// Contexto gerado em tempo de compilação: a serialização por reflexão não
/// sobrevive ao <c>PublishTrimmed</c>, e o app inteiro é publicado trimado —
/// a mesma decisão do <c>Meta.cs</c> no gravador.
/// </remarks>
[JsonSourceGenerationOptions(DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(Mensagem))]
[JsonSerializable(typeof(Requisicao))]
internal sealed partial class ProtocoloJson : JsonSerializerContext;
