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
/// As próximas reuniões, e qual delas seria gravada agora.
/// </summary>
/// <remarks>
/// <see cref="PreDefinido"/> sai da <see cref="EscolhaDeEvento.SeGravasseAgora"/>,
/// que é a mesma regra da gravação de verdade, recortada na mesma janela. A tela
/// não decide isso: se decidisse, passariam a existir duas respostas para "qual
/// reunião é esta" e a que aparece não seria a que grava.
/// </remarks>
public sealed record Proximas(
    IReadOnlyList<Evento> Eventos,
    string? PreDefinido,
    StatusDaAgenda Status,
    string Detalhe = "");

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

    /// <summary>Quanto do dia a tela mostra adiante.</summary>
    /// <remarks>
    /// Doze horas cobre o dia de trabalho inteiro visto de qualquer hora dele, e
    /// no fim da tarde já mostra a primeira reunião de amanhã — que é a pergunta
    /// que se faz às 18h. Mais que isso vira agenda, e agenda o Google já tem.
    /// </remarks>
    public static readonly TimeSpan Horizonte = TimeSpan.FromHours(12);

    /// <summary>Quanto do passado a tela ainda mostra.</summary>
    /// <remarks>
    /// <para>
    /// Muito mais que a <see cref="JanelaMinutos"/> de propósito. Reunião atrasa:
    /// a de 14:00 que só arranca às 14:40 já terminou <b>no papel</b>, e com o
    /// retrospecto de quinze minutos ela sumiria da tela na hora em que alguém
    /// finalmente aperta o gravar. Três horas cobrem o atraso mais teimoso sem
    /// listar a daily das 9h às 18h.
    /// </para>
    /// <para>
    /// <b>Isto não afrouxa o rótulo automático.</b> A marca de "seria esta"
    /// continua saindo da janela de ±15 min — ver
    /// <see cref="EscolhaDeEvento.SeGravasseAgora"/>. Uma reunião que terminou há
    /// uma hora entra na lista para <b>ser escolhida à mão</b>, e nunca para ser
    /// adivinhada: adivinhá-la carimbaria uma gravação com a reunião errada, que
    /// é a falha que esta tela existe para tirar do app.
    /// </para>
    /// </remarks>
    public static readonly TimeSpan Retrospecto = TimeSpan.FromHours(3);

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
        var agora = quando ?? DateTimeOffset.Now;
        var margem = TimeSpan.FromMinutes(JanelaMinutos);

        var (candidatos, status, detalhe) = await BuscarAsync(agora - margem, agora + margem, ct);
        if (candidatos is null) return new Consulta(null, status, detalhe);
        if (candidatos.Count == 0) return new Consulta(null, StatusDaAgenda.SemEvento);

        var escolhido = EscolhaDeEvento.Escolher(candidatos, agora);
        return escolhido is null
            ? new Consulta(null, StatusDaAgenda.SemEvento)
            : new Consulta(escolhido, StatusDaAgenda.Ok);
    }

    /// <summary>
    /// As próximas reuniões, para escolher qual gravar antes de começar.
    /// </summary>
    /// <remarks>
    /// Diferente da <see cref="EventoAtualAsync"/>, esta responde a alguém
    /// olhando a tela — mas a regra de ouro continua valendo, porque nada aqui
    /// está no caminho de iniciar uma captura: falhar devolve lista vazia com o
    /// motivo, e o botão de gravar segue funcionando.
    /// </remarks>
    public async Task<Proximas> ProximasAsync(DateTimeOffset? quando = null,
                                              CancellationToken ct = default)
    {
        var agora = quando ?? DateTimeOffset.Now;
        var margem = TimeSpan.FromMinutes(JanelaMinutos);

        // Olha três horas para trás, e não os quinze minutos da gravação: quem
        // entra numa reunião atrasada precisa achá-la na tela depois de ela ter
        // terminado no papel. Ver Retrospecto — e a marca continua sendo ±15 min.
        // Mais eventos porque o intervalo é quinze vezes maior, e o corte do
        // Google é por ordem de início: com 20 as reuniões do fim do dia
        // sumiriam justamente nos dias cheios.
        var (candidatos, status, detalhe) =
            await BuscarAsync(agora - Retrospecto, agora + Horizonte, ct, maxResultados: 50);
        if (candidatos is null) return new Proximas([], null, status, detalhe);

        var ordenados = candidatos
            .Where(e => e.Inicio is not null)   // dia inteiro não identifica reunião
            .OrderBy(e => e.Inicio!.Value)
            .ToList();

        var preDefinido = EscolhaDeEvento.SeGravasseAgora(ordenados, agora, margem);
        return new Proximas(ordenados, preDefinido?.Id,
            ordenados.Count == 0 ? StatusDaAgenda.SemEvento : StatusDaAgenda.Ok);
    }

    /// <summary>
    /// Os eventos de um intervalo, ou o motivo de não ter vindo nenhum.
    /// </summary>
    /// <remarks>
    /// Lista nula é falha (o <c>status</c> diz qual); lista vazia é agenda vazia.
    /// Nunca lança, pelo mesmo motivo de sempre: uma gravação em andamento não
    /// pode ser contaminada por rede.
    /// </remarks>
    private async Task<(List<Evento>?, StatusDaAgenda, string)> BuscarAsync(
        DateTimeOffset de, DateTimeOffset ate, CancellationToken ct, int maxResultados = 20)
    {
        try
        {
            if (!EstaConfigurado()) return (null, StatusDaAgenda.NaoConfigurado, "");
            if (!EstaAutorizado()) return (null, StatusDaAgenda.NaoAutorizado, "");

            string? token;
            try
            {
                token = await _cred.AccessTokenAsync(ct);
            }
            catch (TokenMortoException e)
            {
                return (null, StatusDaAgenda.TokenExpirado, e.Message);
            }
            if (token is null) return (null, StatusDaAgenda.NaoAutorizado, "");

            string url = "https://www.googleapis.com/calendar/v3/calendars/primary/events"
                + $"?singleEvents=true&orderBy=startTime&maxResults={maxResultados}"
                + $"&timeMin={Uri.EscapeDataString(Iso(de))}"
                + $"&timeMax={Uri.EscapeDataString(Iso(ate))}";

            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var resp = await _http.SendAsync(req, ct);

            if (!resp.IsSuccessStatusCode)
                return (null, StatusDaAgenda.Erro, $"HTTP {(int)resp.StatusCode}");

            var dados = JsonSerializer.Deserialize(
                await resp.Content.ReadAsStringAsync(ct), AgendaJson.Default.RespostaDeEventos);

            var candidatos = (dados?.Items ?? [])
                .Where(i => i.Status != "cancelled" && !string.IsNullOrWhiteSpace(i.Summary))
                .Select(Converter)
                .ToList();

            return (candidatos, StatusDaAgenda.Ok, "");
        }
        catch (Exception e)
        {
            // Deliberadamente amplo: nenhuma falha de calendário pode contaminar
            // uma gravação em andamento.
            return (null, StatusDaAgenda.Erro, e.Message);
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
