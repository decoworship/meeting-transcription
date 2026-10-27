using System.Text.Json;
using System.Text.Json.Nodes;

namespace MeetingRecorder.Core;

/// <summary>Uma mensagem do Teams, com só o que a sincronia usa.</summary>
public sealed record MensagemDoTeams(EstadoDaChamada? Estado, string? TokenNovo, string? Erro);

/// <summary>
/// A API local do Teams — a mesma dos plugins de Stream Deck.
/// </summary>
/// <remarks>
/// <para>
/// <b>Não documentada pela Microsoft.</b> O que está aqui é o que a comunidade
/// mediu no Teams novo (protocolo 2.0.0): <c>svrooij/teams-monitor</c> e
/// <c>ferenyl/Teams.ThirdPartyAppApi</c>. Pode mudar entre versões do Teams, e
/// por isso tudo que não se entende é ignorado em vez de derrubar a ponte.
/// </para>
/// <para>
/// Só existe no Teams desktop, com Configurações › Privacidade › Gerenciar API
/// ligado — e uma política da empresa pode desligar isso. Ver PLANO.md §2.1.
/// </para>
/// </remarks>
public static class ProtocoloDoTeams
{
    public const int Porta = 8124;

    public const string AlternarMudo = "toggle-mute";

    /// <summary>
    /// Pede o estado, e não muda nada na reunião.
    /// </summary>
    /// <remarks>
    /// É com ele que se pede o pareamento: o Teams só mostra o "Permitir?" quando
    /// recebe um comando, e os clientes que existem mandam uma reação de
    /// "curtir" — que aparece para a reunião inteira.
    /// </remarks>
    public const string PedirEstado = "query-state";

    public static Uri Endereco(string? token, string nome, string versao, int porta = Porta)
    {
        static string E(string s) => Uri.EscapeDataString(s);
        string comToken = string.IsNullOrEmpty(token) ? "" : $"token={E(token)}&";
        return new Uri($"ws://localhost:{porta}/?{comToken}protocol-version=2.0.0"
                       + $"&manufacturer={E(nome)}&device={E(nome)}&app={E(nome)}&app-version={E(versao)}");
    }

    public static string Comando(string acao, int pedido) =>
        new JsonObject
        {
            ["action"] = acao,
            ["parameters"] = new JsonObject(),
            ["requestId"] = pedido,
        }.ToJsonString();

    /// <summary>A mensagem, ou nulo se ela não diz nada que a sincronia use.</summary>
    public static MensagemDoTeams? Ler(string json)
    {
        JsonObject? raiz;
        try { raiz = JsonNode.Parse(json) as JsonObject; }
        catch (JsonException) { return null; }
        if (raiz is null) return null;

        EstadoDaChamada? estado = null;
        if (raiz["meetingUpdate"] is JsonObject atualizacao)
        {
            var reuniao = atualizacao["meetingState"] as JsonObject;
            var permissoes = atualizacao["meetingPermissions"] as JsonObject;
            bool podeMutar = Bool(permissoes, "canToggleMute");
            bool podeParear = Bool(permissoes, "canPair");
            estado = new EstadoDaChamada(
                // Antes de parear não vem o isInMeeting; poder mutar ou parear
                // só acontece dentro de uma reunião.
                EmReuniao: reuniao is not null ? Bool(reuniao, "isInMeeting") : podeMutar || podeParear,
                Mudo: reuniao is not null ? Bool(reuniao, "isMuted") : null,
                PodeMutar: podeMutar,
                PodeParear: podeParear);
        }

        string? token = Texto(raiz, "tokenRefresh");
        string? erro = Texto(raiz, "errorMsg");
        return estado is null && token is null && erro is null
            ? null : new MensagemDoTeams(estado, token, erro);
    }

    private static bool Bool(JsonObject? o, string chave) =>
        o?[chave] is JsonValue v && v.TryGetValue(out bool b) && b;

    private static string? Texto(JsonObject o, string chave) =>
        o[chave] is JsonValue v && v.TryGetValue(out string? s) && s is { Length: > 0 } ? s : null;
}
