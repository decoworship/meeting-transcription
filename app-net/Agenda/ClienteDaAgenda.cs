using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;

namespace MeetingRecorder.Agenda;

/// <summary>
/// Por que status e não só "achou / não achou".
/// </summary>
/// <remarks>
/// Uma agenda nunca configurada e um token que morreu produzem o mesmo
/// resultado — sem evento — mas exigem reações opostas: silêncio no primeiro
/// caso, aviso no segundo. Sem essa distinção o gravador pararia de identificar
/// reuniões sem ninguém perceber, que é exatamente o tipo de falha silenciosa
/// que já custou uma gravação a este projeto.
/// </remarks>
public enum StatusDaAgenda
{
    Ok,
    SemEvento,
    NaoConfigurado,
    NaoAutorizado,
    TokenExpirado,
    Erro,
}

/// <summary>Resultado de uma consulta: o evento (talvez) e por que não veio.</summary>
public sealed record Consulta(Evento? Evento, StatusDaAgenda Status, string Detalhe = "")
{
    /// <summary>Houve autorização e ela quebrou — vale interromper o usuário.</summary>
    public bool ExigeAtencao => Status is StatusDaAgenda.TokenExpirado or StatusDaAgenda.Erro;
}

/// <summary>
/// Associa uma gravação ao evento do Google Calendar que está acontecendo.
/// </summary>
/// <remarks>
/// <b>Regra de ouro: nada aqui pode atrasar ou impedir uma gravação.</b> Rede
/// caindo, token expirado ou nenhum evento encontrado devolvem uma
/// <see cref="Consulta"/> sem evento, e a gravação segue. Por isso a consulta
/// roda depois que a captura já começou, e nunca no caminho de início.
/// </remarks>
public sealed class ClienteDaAgenda : IDisposable
{
    /// <summary>Uma reunião raramente começa no minuto exato.</summary>
    public const int JanelaMinutos = 15;

    /// <summary>Somente leitura: o gravador não tem motivo para escrever na agenda.</summary>
    public const string Escopo = "https://www.googleapis.com/auth/calendar.readonly";

    private readonly HttpClient _http;
    private readonly Credenciais _cred;

    public ClienteDaAgenda(HttpClient? http = null)
    {
        // Timeout curto: isto roda em paralelo com uma gravação e ninguém está
        // esperando a resposta. Travar dois minutos num socket morto seria pior
        // que desistir em dez segundos.
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        _cred = new Credenciais(_http);
    }

    /// <summary>
    /// Há credencial do Google — do arquivo do usuário ou embutida no
    /// executável. Ver <see cref="FonteDoSegredo"/>.
    /// </summary>
    public static bool EstaConfigurado() => FonteDoSegredo.Existe();
    public static bool EstaAutorizado() => File.Exists(Caminhos.Token);

    /// <summary>E-mail da conta conectada, ou string vazia. Nunca faz rede.</summary>
    public static string EmailDaConta()
    {
        try
        {
            if (!File.Exists(Caminhos.Conta)) return "";
            var c = JsonSerializer.Deserialize(File.ReadAllText(Caminhos.Conta),
                                               AgendaJson.Default.ContaSalva);
            return c?.Email ?? "";
        }
        catch (Exception)
        {
            return "";
        }
    }

    /// <summary>Esquece a conta atual. A próxima autorização começa do zero.</summary>
    public static void Desconectar()
    {
        foreach (var p in new[] { Caminhos.Token, Caminhos.Conta })
        {
            try { File.Delete(p); }
            catch (IOException) { /* não impede o resto */ }
            catch (UnauthorizedAccessException) { }
        }
    }

    /// <summary>
    /// Evento acontecendo agora, ou o mais próximo dentro da janela.
    /// </summary>
    /// <remarks>
    /// Nunca lança: qualquer falha vira uma <see cref="Consulta"/> sem evento e
    /// a gravação segue sem rótulo.
    /// </remarks>
    public async Task<Consulta> EventoAtualAsync(DateTimeOffset? quando = null,
                                                 CancellationToken ct = default)
    {
        try
        {
            if (!EstaConfigurado()) return new Consulta(null, StatusDaAgenda.NaoConfigurado);
            if (!EstaAutorizado()) return new Consulta(null, StatusDaAgenda.NaoAutorizado);

            string? token;
            try
            {
                token = await _cred.AccessTokenAsync(ct);
            }
            catch (TokenMortoException e)
            {
                return new Consulta(null, StatusDaAgenda.TokenExpirado, e.Message);
            }
            if (token is null) return new Consulta(null, StatusDaAgenda.NaoAutorizado);

            var agora = quando ?? DateTimeOffset.Now;
            var margem = TimeSpan.FromMinutes(JanelaMinutos);

            string url = "https://www.googleapis.com/calendar/v3/calendars/primary/events"
                + "?singleEvents=true&orderBy=startTime&maxResults=20"
                + $"&timeMin={Uri.EscapeDataString(Iso(agora - margem))}"
                + $"&timeMax={Uri.EscapeDataString(Iso(agora + margem))}";

            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var resp = await _http.SendAsync(req, ct);

            if (!resp.IsSuccessStatusCode)
                return new Consulta(null, StatusDaAgenda.Erro, $"HTTP {(int)resp.StatusCode}");

            var dados = JsonSerializer.Deserialize(
                await resp.Content.ReadAsStringAsync(ct), AgendaJson.Default.RespostaDeEventos);

            var candidatos = (dados?.Items ?? [])
                .Where(i => i.Status != "cancelled" && !string.IsNullOrWhiteSpace(i.Summary))
                .Select(Converter)
                .ToList();

            if (candidatos.Count == 0) return new Consulta(null, StatusDaAgenda.SemEvento);

            var escolhido = EscolhaDeEvento.Escolher(candidatos, agora);
            return escolhido is null
                ? new Consulta(null, StatusDaAgenda.SemEvento)
                : new Consulta(escolhido, StatusDaAgenda.Ok);
        }
        catch (Exception e)
        {
            // Deliberadamente amplo: nenhuma falha de calendário pode contaminar
            // uma gravação em andamento.
            return new Consulta(null, StatusDaAgenda.Erro, e.Message);
        }
    }

    /// <summary>
    /// Descobre e guarda o e-mail da conta conectada.
    /// </summary>
    /// <remarks>
    /// O id do calendário "primary" é o próprio endereço, então dá para saber a
    /// conta sem pedir nenhum escopo de identidade além do que já temos.
    /// </remarks>
    public async Task GuardarContaAsync(string accessToken, CancellationToken ct = default)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get,
                "https://www.googleapis.com/calendar/v3/calendars/primary");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            using var resp = await _http.SendAsync(req, ct);
            if (!resp.IsSuccessStatusCode) return;

            var cal = JsonSerializer.Deserialize(
                await resp.Content.ReadAsStringAsync(ct), AgendaJson.Default.RespostaDeCalendario);
            if (cal?.Id is not { Length: > 0 } email) return;

            Directory.CreateDirectory(Caminhos.Base);
            await File.WriteAllTextAsync(Caminhos.Conta,
                JsonSerializer.Serialize(new ContaSalva { Email = email },
                                         AgendaJson.Default.ContaSalva), ct);
        }
        catch (Exception)
        {
            // Saber a conta é conveniência; não vale derrubar a autorização.
        }
    }

    private static string Iso(DateTimeOffset q) =>
        q.ToString("yyyy-MM-ddTHH:mm:sszzz", CultureInfo.InvariantCulture);

    private static Evento Converter(ItemDeEvento i) => new(
        Id: i.Id ?? "",
        Titulo: (i.Summary ?? "").Trim(),
        Inicio: Instante(i.Start),
        Fim: Instante(i.End),
        Participantes: (i.Attendees ?? [])
            .Select(a => new Participante(a.DisplayName, a.Email, a.Resource)).ToList(),
        Organizador: i.Organizer?.DisplayName);

    /// <remarks>
    /// Só <c>dateTime</c>: evento de dia inteiro traz <c>date</c> e não
    /// identifica uma reunião. Ver <see cref="EscolhaDeEvento"/>.
    /// </remarks>
    private static DateTimeOffset? Instante(MomentoDoEvento? m) =>
        m?.DateTime is { Length: > 0 } s &&
        DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture,
                                DateTimeStyles.None, out var q) ? q : null;

    public void Dispose() => _http.Dispose();
}
