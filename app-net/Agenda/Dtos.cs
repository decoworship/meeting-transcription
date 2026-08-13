using System.Text.Json;
using System.Text.Json.Serialization;

namespace MeetingRecorder.Agenda;

// Só os campos que o gravador usa. A API devolve muito mais; o resto é
// ignorado na desserialização, então acrescentar campos aqui é seguro.

internal sealed class RespostaDeEventos
{
    [JsonPropertyName("items")] public List<ItemDeEvento>? Items { get; set; }
}

internal sealed class ItemDeEvento
{
    [JsonPropertyName("id")] public string? Id { get; set; }
    [JsonPropertyName("status")] public string? Status { get; set; }
    [JsonPropertyName("summary")] public string? Summary { get; set; }
    [JsonPropertyName("start")] public MomentoDoEvento? Start { get; set; }
    [JsonPropertyName("end")] public MomentoDoEvento? End { get; set; }
    [JsonPropertyName("attendees")] public List<ParticipanteJson>? Attendees { get; set; }
    [JsonPropertyName("organizer")] public OrganizadorJson? Organizer { get; set; }
}

/// <remarks>
/// <c>dateTime</c> vem só em eventos com horário; um evento de dia inteiro traz
/// <c>date</c>. A distinção importa: ver <see cref="EscolhaDeEvento"/>.
/// </remarks>
internal sealed class MomentoDoEvento
{
    [JsonPropertyName("dateTime")] public string? DateTime { get; set; }
    [JsonPropertyName("date")] public string? Date { get; set; }
}

internal sealed class ParticipanteJson
{
    [JsonPropertyName("email")] public string? Email { get; set; }
    [JsonPropertyName("displayName")] public string? DisplayName { get; set; }
    [JsonPropertyName("resource")] public bool Resource { get; set; }
}

internal sealed class OrganizadorJson
{
    [JsonPropertyName("displayName")] public string? DisplayName { get; set; }
}

internal sealed class RespostaDeCalendario
{
    [JsonPropertyName("id")] public string? Id { get; set; }
}

internal sealed class RespostaDeToken
{
    [JsonPropertyName("access_token")] public string? AccessToken { get; set; }
    [JsonPropertyName("refresh_token")] public string? RefreshToken { get; set; }
    [JsonPropertyName("expires_in")] public int ExpiresIn { get; set; }
    [JsonPropertyName("error")] public string? Error { get; set; }
    [JsonPropertyName("error_description")] public string? ErrorDescription { get; set; }
}

internal sealed class SegredoDoCliente
{
    [JsonPropertyName("installed")] public DadosDoCliente? Installed { get; set; }
    [JsonPropertyName("web")] public DadosDoCliente? Web { get; set; }
}

internal sealed class DadosDoCliente
{
    [JsonPropertyName("client_id")] public string? ClientId { get; set; }
    [JsonPropertyName("client_secret")] public string? ClientSecret { get; set; }
    [JsonPropertyName("auth_uri")] public string? AuthUri { get; set; }
    [JsonPropertyName("token_uri")] public string? TokenUri { get; set; }
}

internal sealed class ContaSalva
{
    [JsonPropertyName("email")] public string? Email { get; set; }
}

/// <summary>
/// Contexto gerado em tempo de compilação — o mesmo motivo do <c>MetaJson</c>:
/// serialização por reflexão não sobrevive ao <c>PublishTrimmed</c>.
/// </summary>
[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = false)]
[JsonSerializable(typeof(RespostaDeEventos))]
[JsonSerializable(typeof(RespostaDeCalendario))]
[JsonSerializable(typeof(RespostaDeToken))]
[JsonSerializable(typeof(SegredoDoCliente))]
[JsonSerializable(typeof(ContaSalva))]
internal sealed partial class AgendaJson : JsonSerializerContext;
