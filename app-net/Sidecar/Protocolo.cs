using System.Text.Json;
using System.Text.Json.Serialization;

namespace MeetingApp.Sidecar;

/// <summary>Um trecho de fala atribuído a um falante.</summary>
/// <remarks>
/// O rótulo vem cru do motor (<c>SPEAKER_00</c>). Traduzir para "Falante 1" é
/// decisão de apresentação e vive no núcleo. Ver docs/SIDECAR.md.
/// </remarks>
public sealed record Segmento(double Inicio, double Fim, string Falante);

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

    // "erro"
    [JsonPropertyName("mensagem")] public string? MensagemDeErro { get; init; }
}

internal sealed class SegmentoJson
{
    [JsonPropertyName("inicio")] public double Inicio { get; init; }
    [JsonPropertyName("fim")] public double Fim { get; init; }
    [JsonPropertyName("falante")] public string Falante { get; init; } = "";
}

internal sealed class Requisicao
{
    [JsonPropertyName("id")] public required int Id { get; init; }
    [JsonPropertyName("op")] public required string Op { get; init; }
    [JsonPropertyName("audio")] public required string Audio { get; init; }
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
