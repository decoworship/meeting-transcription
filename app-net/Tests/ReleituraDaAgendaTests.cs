using System.Text.Json.Nodes;
using MeetingRecorder.Agenda;
using Xunit;

namespace MeetingRecorder.Tests;

public sealed class ReleituraDaAgendaTests : IDisposable
{
    private readonly string _pasta = Path.Combine(Path.GetTempPath(), "releitura-" + Guid.NewGuid().ToString("N"));

    public ReleituraDaAgendaTests() => Directory.CreateDirectory(_pasta);
    public void Dispose() => Directory.Delete(_pasta, recursive: true);

    private static Evento Ev(params (string Nome, string? Email)[] p) =>
        new("ev1", "Semanal", null, null, p.Select(x => new Participante(x.Nome, x.Email, false)).ToList(), null);

    private string Gravacao(string nome, string meta)
    {
        string g = Path.Combine(_pasta, nome);
        Directory.CreateDirectory(g);
        File.WriteAllText(Path.Combine(g, "meta.json"), meta);
        return Path.Combine(g, "meta.json");
    }

    private const string SemEmails = """
        {"started_at":"x","meeting":{"title":"Semanal","attendees":["Ana","Bruno"],"calendar_event_id":"ev1"},"extra":{"k":1}}
        """;

    [Fact]
    public async Task CompletaOsEmailsEPreservaORestoDoMeta()
    {
        string meta = Gravacao("a", SemEmails);
        var perguntados = new HashSet<string>();

        var r = await ReleituraDaAgenda.RelerAsync(_pasta,
            _ => Task.FromResult<(Evento?, StatusDaAgenda)>((Ev(("Ana", "ana@x.com"), ("Bruno", "b@cli.com")), StatusDaAgenda.Ok)),
            perguntados);

        Assert.Equal(1, r.Completadas);
        var lido = JsonNode.Parse(File.ReadAllText(meta))!;
        Assert.Equal("b@cli.com", lido["meeting"]!["attendee_emails"]![1]!.GetValue<string>());
        Assert.Equal(1, lido["extra"]!["k"]!.GetValue<int>());
        Assert.Equal("x", lido["started_at"]!.GetValue<string>());
        Assert.Contains("ev1", perguntados);
    }

    [Fact]
    public void NomesDiferentesNaoGanhamEmails()
    {
        // Alguém entrou no convite depois: os e-mails casariam com a pessoa errada.
        var meta = JsonNode.Parse(SemEmails)!;
        Assert.False(ReleituraDaAgenda.Completar(meta, Ev(("Ana", "a@x"), ("Carla", "c@x"), ("Bruno", "b@x"))));
        Assert.Null(meta["meeting"]!["attendee_emails"]);
    }

    [Fact]
    public void ParticipanteSemEmailDesalinhaAsListas_EntaoNada()
    {
        var meta = JsonNode.Parse(SemEmails)!;
        Assert.False(ReleituraDaAgenda.Completar(meta, Ev(("Ana", null), ("Bruno", "b@x"))));
    }

    [Fact]
    public async Task QuemJaTemEmailsOuNaoTemIdNaoEPerguntado()
    {
        Gravacao("tem", """{"meeting":{"attendees":["Ana"],"attendee_emails":["a@x"],"calendar_event_id":"e"}}""");
        Gravacao("semid", """{"meeting":{"attendees":["Ana"]}}""");
        Gravacao("semreuniao", """{"started_at":"x"}""");
        int chamadas = 0;

        await ReleituraDaAgenda.RelerAsync(_pasta,
            _ => { chamadas++; return Task.FromResult<(Evento?, StatusDaAgenda)>((null, StatusDaAgenda.Erro)); },
            new HashSet<string>());

        Assert.Equal(0, chamadas);
    }

    [Fact]
    public async Task ErroDeRedeVoltaNaProxima_EventoApagadoNao()
    {
        Gravacao("a", SemEmails);
        Gravacao("b", SemEmails.Replace("ev1", "ev2"));
        var perguntados = new HashSet<string>();

        var r = await ReleituraDaAgenda.RelerAsync(_pasta,
            id => Task.FromResult<(Evento?, StatusDaAgenda)>(
                (null, id == "ev1" ? StatusDaAgenda.Erro : StatusDaAgenda.SemEvento)),
            perguntados);

        Assert.Equal(1, r.Adiadas);
        Assert.Equal(1, r.Sumidas);
        Assert.Equal(["ev2"], perguntados);
    }

    [Fact]
    public async Task SemTokenParaNaPrimeira()
    {
        Gravacao("a", SemEmails);
        Gravacao("b", SemEmails.Replace("ev1", "ev2"));
        int chamadas = 0;

        await ReleituraDaAgenda.RelerAsync(_pasta,
            _ => { chamadas++; return Task.FromResult<(Evento?, StatusDaAgenda)>((null, StatusDaAgenda.TokenExpirado)); },
            new HashSet<string>());

        Assert.Equal(1, chamadas);
    }
}
