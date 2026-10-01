using System.Text.Json;
using System.Text.Json.Nodes;

namespace MeetingRecorder.Agenda;

/// <summary>
/// Relê no Google os eventos das gravações que ficaram sem e-mails (GRA-1).
/// </summary>
/// <remarks>
/// <para>
/// O <c>calendar_event_id</c> está no <c>meta.json</c> desde sempre, e nada o
/// lia. As gravações de antes de 14/08/2026 guardaram os nomes e não os
/// e-mails — no acervo de 26/08, 14 de 44, e 13 delas com o id. É o domínio do
/// e-mail que separa a nossa equipe do cliente na ata.
/// </para>
/// <para>
/// <b>Só completa, nunca reescreve.</b> <c>attendees</c> e
/// <c>attendee_emails</c> são listas paralelas (GRA-2); se os nomes que o
/// evento dá hoje não forem exatamente os gravados — alguém entrou ou saiu do
/// convite depois —, os e-mails não entram, porque casariam com a pessoa
/// errada. A gravação fica como estava, que é o que sempre foi.
/// </para>
/// <para>
/// Cada id é perguntado uma vez só, com resposta definitiva: o que deu erro de
/// rede volta na próxima abertura, o que o Google disse que não existe não.
/// </para>
/// </remarks>
public static class ReleituraDaAgenda
{
    public sealed record Resultado(int Completadas, int Divergentes, int Sumidas, int Adiadas);

    /// <summary>O id a reler, ou nulo se a gravação não precisa.</summary>
    public static string? Falta(JsonNode? meta)
    {
        if (meta?["meeting"] is not JsonObject reuniao) return null;
        if (reuniao["attendee_emails"] is JsonArray { Count: > 0 }) return null;
        return reuniao["calendar_event_id"]?.GetValueKind() == JsonValueKind.String
            && reuniao["calendar_event_id"]!.GetValue<string>() is { Length: > 0 } id ? id : null;
    }

    /// <summary>
    /// Põe os e-mails do evento no <c>meeting</c>, se os nomes baterem.
    /// </summary>
    /// <returns>Verdadeiro se mudou algo.</returns>
    public static bool Completar(JsonNode meta, Evento evento)
    {
        if (meta["meeting"] is not JsonObject reuniao) return false;

        var gravados = (reuniao["attendees"] as JsonArray ?? [])
            .Select(n => n?.GetValue<string>() ?? "").ToList();
        var nomes = evento.NomesDosParticipantes();
        var emails = evento.EmailsDosParticipantes();

        // Mesma contagem também: um participante sem e-mail tira as listas de
        // posição (GRA-2), e aí é melhor não ter e-mail nenhum.
        if (emails.Count == 0 || emails.Count != nomes.Count) return false;
        if (gravados.Count > 0 && !gravados.SequenceEqual(nomes)) return false;

        if (gravados.Count == 0) reuniao["attendees"] = new JsonArray(nomes.Select(n => (JsonNode?)n).ToArray());
        reuniao["attendee_emails"] = new JsonArray(emails.Select(e => (JsonNode?)e).ToArray());
        return true;
    }

    /// <param name="pasta">A pasta das gravações, uma subpasta por reunião.</param>
    /// <param name="buscar">O <see cref="ClienteDaAgenda.EventoPorIdAsync"/>, ou um falso nos testes.</param>
    /// <param name="jaPerguntados">
    /// Os ids com resposta definitiva; a função acrescenta os novos.
    /// </param>
    public static async Task<Resultado> RelerAsync(
        string pasta,
        Func<string, Task<(Evento? Evento, StatusDaAgenda Status)>> buscar,
        ISet<string> jaPerguntados)
    {
        int completadas = 0, divergentes = 0, sumidas = 0, adiadas = 0;
        if (!Directory.Exists(pasta)) return new(0, 0, 0, 0);

        foreach (string g in Directory.GetDirectories(pasta))
        {
            string caminho = Path.Combine(g, "meta.json");
            if (!File.Exists(caminho)) continue;

            JsonNode? meta;
            try { meta = JsonNode.Parse(File.ReadAllText(caminho)); }
            catch (Exception) { continue; }   // ilegível não é problema desta releitura

            if (Falta(meta) is not { } id || jaPerguntados.Contains(id)) continue;

            var (evento, status) = await buscar(id);
            if (evento is null)
            {
                if (status == StatusDaAgenda.SemEvento) { sumidas++; jaPerguntados.Add(id); }
                else adiadas++;
                // Sem token, nenhuma outra vai dar certo nesta abertura.
                if (status is StatusDaAgenda.NaoAutorizado or StatusDaAgenda.TokenExpirado
                    or StatusDaAgenda.NaoConfigurado) break;
                continue;
            }

            jaPerguntados.Add(id);
            if (!Completar(meta!, evento)) { divergentes++; continue; }

            // Escrita atômica: um meta.json pela metade perde a reunião na lista.
            string tmp = caminho + ".tmp";
            using (var arq = File.Create(tmp))
            using (var w = new Utf8JsonWriter(arq, new JsonWriterOptions
            {
                Indented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            }))
                meta!.WriteTo(w);
            File.Move(tmp, caminho, overwrite: true);
            completadas++;
        }
        return new(completadas, divergentes, sumidas, adiadas);
    }

    /// <summary>Onde ficam os ids já respondidos, ao lado do token.</summary>
    public static string ArquivoDosPerguntados => Path.Combine(Caminhos.Base, "releitura_da_agenda.json");

    public static HashSet<string> LerPerguntados()
    {
        try
        {
            return JsonSerializer.Deserialize(File.ReadAllText(ArquivoDosPerguntados), AgendaJson.Default.ListString)?
                .ToHashSet() ?? [];
        }
        catch (Exception) { return []; }
    }

    public static void GuardarPerguntados(ISet<string> ids)
    {
        try
        {
            Directory.CreateDirectory(Caminhos.Base);
            File.WriteAllText(ArquivoDosPerguntados, JsonSerializer.Serialize(ids.Order().ToList(), AgendaJson.Default.ListString));
        }
        catch (Exception) { /* perguntar de novo na próxima abertura é o pior caso */ }
    }
}
