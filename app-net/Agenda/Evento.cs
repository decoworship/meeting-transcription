using MeetingRecorder.Core;

namespace MeetingRecorder.Agenda;

/// <summary>Um participante do evento, como a API do Google o devolve.</summary>
public sealed record Participante(string? NomeExibido, string? Email, bool EhRecurso);

/// <summary>
/// O que interessa de um evento da agenda, já achatado.
/// </summary>
/// <remarks>
/// O objetivo não é só rotular a gravação: os nomes dos participantes alimentam
/// o vocabulário do transcritor, que é o que faz nome próprio parar de virar
/// outra coisa. Ver a correção fonética em <c>tools/correcao_fonetica.py</c>.
/// </remarks>
public sealed record Evento(
    string Id,
    string Titulo,
    DateTimeOffset? Inicio,
    DateTimeOffset? Fim,
    IReadOnlyList<Participante> Participantes,
    string? Organizador)
{
    /// <summary>Nomes para o vocabulário. Sem e-mails, sem salas.</summary>
    public List<string> NomesDosParticipantes()
    {
        var nomes = new List<string>();
        foreach (var p in Participantes)
        {
            if (p.EhRecurso) continue;              // salas e equipamentos não falam

            string nome = (p.NomeExibido ?? "").Trim();
            if (nome.Length == 0) nome = DoEmail(p.Email);
            if (nome.Length > 0 && !nomes.Contains(nome)) nomes.Add(nome);
        }
        return nomes;
    }

    /// <summary>
    /// Sem displayName, o começo do e-mail costuma ser o nome:
    /// "dimi.randel@..." vira "Dimi Randel".
    /// </summary>
    private static string DoEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email)) return "";

        string local = email.Split('@')[0].Replace(".", " ");
        var partes = local.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(p => char.ToUpperInvariant(p[0]) + p[1..].ToLowerInvariant());
        return string.Join(" ", partes);
    }

    /// <summary>No formato que o <c>meta.json</c> já reserva.</summary>
    public MetaMeeting ParaMeta() => new()
    {
        Title = Titulo,
        Client = null,          // preenchidos depois pelo transcritor
        Project = null,
        Attendees = NomesDosParticipantes(),
        CalendarEventId = Id,
        // O gravador Python grava estes três só quando há evento, e o formato é
        // o mesmo ISO 8601 que veio da API — o transcritor compara com o que já
        // tem no banco.
        Start = Inicio?.ToString("yyyy-MM-ddTHH:mm:sszzz"),
        End = Fim?.ToString("yyyy-MM-ddTHH:mm:sszzz"),
        Organizer = Organizador,
    };
}
