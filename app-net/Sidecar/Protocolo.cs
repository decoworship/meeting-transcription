using System.Text.Json;
using System.Text.Json.Serialization;

namespace MeetingApp.Sidecar;

/// <summary>Um trecho de fala atribuído a um falante, vindo da diarização.</summary>
/// <remarks>
/// O rótulo vem cru do motor (<c>SPEAKER_00</c>). Traduzir para "Falante 1" é
/// decisão de apresentação e vive no núcleo. Ver docs/SIDECAR.md.
/// </remarks>
public sealed record SegmentoDeFalante(double Inicio, double Fim, string Falante);

/// <summary>Um trecho transcrito, vindo do ASR. Sem falante: quem atribui é o núcleo.</summary>
public sealed record SegmentoDeTexto(double Inicio, double Fim, string Texto);

/// <summary>O que o motor de ASR devolve por gravação.</summary>
public sealed record Transcricao(
    IReadOnlyList<SegmentoDeTexto> Segmentos, string? Idioma, double Duracao);

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
