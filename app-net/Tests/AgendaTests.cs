using System.Text.Json;
using MeetingRecorder.Agenda;
using MeetingRecorder.Core;
using Xunit;

namespace MeetingRecorder.Tests;

public sealed class EscolhaDeEventoTests
{
    private static readonly DateTimeOffset Agora = new(2026, 8, 8, 14, 0, 0, TimeSpan.Zero);

    private static Evento Ev(string id, DateTimeOffset? ini, DateTimeOffset? fim) =>
        new(id, id, ini, fim, [], null);

    [Fact]
    public void PrefereOEventoQueCobreOInstante()
    {
        var cobrindo = Ev("agora", Agora.AddMinutes(-5), Agora.AddMinutes(25));
        var proximo = Ev("logo", Agora.AddMinutes(2), Agora.AddMinutes(32));

        Assert.Equal("agora", EscolhaDeEvento.Escolher([proximo, cobrindo], Agora)!.Id);
    }

    [Fact]
    public void EntreDoisQueCobrem_VenceOMaisCurto()
    {
        // O caso real: um bloco de "foco" de 4h e uma reunião de 30 min
        // sobrepostos. A reunião é a resposta certa.
        var foco = Ev("foco", Agora.AddHours(-2), Agora.AddHours(2));
        var reuniao = Ev("reuniao", Agora.AddMinutes(-10), Agora.AddMinutes(20));

        Assert.Equal("reuniao", EscolhaDeEvento.Escolher([foco, reuniao], Agora)!.Id);
    }

    [Fact]
    public void SemNenhumCobrindo_PegaOInicioMaisProximo()
    {
        var antes = Ev("antes", Agora.AddMinutes(-12), Agora.AddMinutes(-2));
        var depois = Ev("depois", Agora.AddMinutes(4), Agora.AddMinutes(34));

        Assert.Equal("depois", EscolhaDeEvento.Escolher([antes, depois], Agora)!.Id);
    }

    [Fact]
    public void EventoDeDiaInteiroNuncaEEscolhido()
    {
        // Sem horário de início não identifica reunião; um aniversário na agenda
        // rotularia a gravação inteira errado.
        var aniversario = Ev("aniversario", null, null);
        Assert.Null(EscolhaDeEvento.Escolher([aniversario], Agora));
    }

    [Fact]
    public void ListaVaziaNaoEscolheNada() =>
        Assert.Null(EscolhaDeEvento.Escolher([], Agora));

    [Fact]
    public void EventoSemFimNaoCobre_MasAindaConcorreComoProximo()
    {
        var semFim = Ev("semfim", Agora.AddMinutes(-1), null);
        Assert.Equal("semfim", EscolhaDeEvento.Escolher([semFim], Agora)!.Id);
    }
}

/// <summary>
/// A promessa que a tela do Gravador faz: "se você gravar agora, é esta".
/// </summary>
/// <remarks>
/// O que estes testes guardam é o recorte da janela. A lista das próximas
/// reuniões vem de doze horas adiante e a gravação só enxerga quinze minutos
/// para cada lado — sem o recorte, a tela apontaria a reunião das 16h às 14h e
/// o gravador não acharia nada.
/// </remarks>
public sealed class SeGravasseAgoraTests
{
    private static readonly DateTimeOffset Agora = new(2026, 8, 25, 14, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Janela = TimeSpan.FromMinutes(ClienteDaAgenda.JanelaMinutos);

    private static Evento Ev(string id, DateTimeOffset? ini, DateTimeOffset? fim) =>
        new(id, id, ini, fim, [], null);

    [Fact]
    public void ForaDaJanelaNaoEPrometida()
    {
        // A das 16h aparece na lista da tela, e não pode ganhar a marca: às 14h
        // o gravador não a veria.
        var tarde = Ev("tarde", Agora.AddHours(2), Agora.AddHours(3));
        Assert.Null(EscolhaDeEvento.SeGravasseAgora([tarde], Agora, Janela));
    }

    [Fact]
    public void DentroDaJanelaVenceAQueComecaLogo()
    {
        var logo = Ev("logo", Agora.AddMinutes(10), Agora.AddMinutes(40));
        var tarde = Ev("tarde", Agora.AddHours(3), Agora.AddHours(4));

        Assert.Equal("logo", EscolhaDeEvento.SeGravasseAgora([tarde, logo], Agora, Janela)!.Id);
    }

    [Fact]
    public void AQueEstaCorrendoHaUmaHoraContinuaConcorrendo()
    {
        // O recorte é por sobreposição, e não por início: é assim que a API do
        // Google responde a um intervalo, e uma reunião longa já começada é
        // exatamente a que se quer gravar ao apertar o botão atrasado.
        var correndo = Ev("correndo", Agora.AddHours(-1), Agora.AddMinutes(30));
        Assert.Equal("correndo",
            EscolhaDeEvento.SeGravasseAgora([correndo], Agora, Janela)!.Id);
    }

    [Fact]
    public void AQueAcabouDeTerminarSaiDaPromessa()
    {
        var acabou = Ev("acabou", Agora.AddHours(-1), Agora.AddMinutes(-20));
        Assert.Null(EscolhaDeEvento.SeGravasseAgora([acabou], Agora, Janela));
    }

    [Fact]
    public void AQueAtrasouContinuaNaLista_MasNaoNaPromessa()
    {
        // Reunião de 14:00–14:30 em que todo mundo entrou às 14:40: às 15:00 ela
        // já terminou no papel. Precisa estar na tela para ser escolhida à mão —
        // e não pode ganhar a marca, porque o rótulo automático não a veria.
        var atrasada = Ev("atrasada", Agora.AddMinutes(-60), Agora.AddMinutes(-30));

        Assert.True(EscolhaDeEvento.NaJanela(atrasada, Agora, ClienteDaAgenda.Retrospecto));
        Assert.Null(EscolhaDeEvento.SeGravasseAgora([atrasada], Agora, Janela));
    }

    [Fact]
    public void ADeDiaInteiroNuncaEntra()
    {
        Assert.False(EscolhaDeEvento.NaJanela(Ev("aniversario", null, null), Agora, Janela));
    }

    [Fact]
    public void ConcordaComOQueAGravacaoEscolheria()
    {
        // A régua que dá sentido ao resto: a marca da tela e o rótulo que o
        // meta.json recebe têm de sair da mesma decisão. Duas reuniões
        // sobrepostas é o caso em que uma heurística própria da tela divergiria.
        var foco = Ev("foco", Agora.AddHours(-2), Agora.AddHours(2));
        var reuniao = Ev("reuniao", Agora.AddMinutes(-10), Agora.AddMinutes(20));
        var tarde = Ev("tarde", Agora.AddHours(5), Agora.AddHours(6));

        var naTela = EscolhaDeEvento.SeGravasseAgora([foco, reuniao, tarde], Agora, Janela);
        // O que a gravação vê é só o que a consulta de ±15 min devolve.
        var naGravacao = EscolhaDeEvento.Escolher([foco, reuniao], Agora);

        Assert.Equal(naGravacao!.Id, naTela!.Id);
    }
}

public sealed class ParticipantesTests
{
    private static Evento Com(params Participante[] p) =>
        new("id", "Reunião", null, null, p, null);

    [Fact]
    public void SalasENaoFalam()
    {
        var e = Com(new Participante("Sala Azul", "sala@x.com", EhRecurso: true),
                    new Participante("Ana", "ana@x.com", false));
        Assert.Equal(["Ana"], e.NomesDosParticipantes());
    }

    [Fact]
    public void SemNomeExibido_DerivaDoEmail()
    {
        // "dimi.randel@..." -> "Dimi Randel", que é o que alimenta o vocabulário
        // do transcritor.
        var e = Com(new Participante(null, "dimi.randel@empresa.com", false));
        Assert.Equal(["Dimi Randel"], e.NomesDosParticipantes());
    }

    [Fact]
    public void NomeEmBrancoContaComoAusente()
    {
        var e = Com(new Participante("   ", "joao.silva@x.com", false));
        Assert.Equal(["Joao Silva"], e.NomesDosParticipantes());
    }

    [Fact]
    public void NaoRepeteNome()
    {
        var e = Com(new Participante("Ana", "ana@x.com", false),
                    new Participante("Ana", "ana.outra@x.com", false));
        Assert.Equal(["Ana"], e.NomesDosParticipantes());
    }

    [Fact]
    public void ParticipanteSemNomeNemEmailEIgnorado()
    {
        var e = Com(new Participante(null, null, false));
        Assert.Empty(e.NomesDosParticipantes());
    }
}

public sealed class MetaDaReuniaoTests
{
    [Fact]
    public void SemEvento_OBlocoTemAsCincoChavesDoPython()
    {
        var meta = Meta.Montar(DateTimeOffset.UtcNow, new TrackStats { Nome = "system" },
            new TrackStats { Nome = "mic" }, "alto", "mic", 48000, 48000);

        var bloco = JsonDocument.Parse(meta.ParaJson()).RootElement.GetProperty("meeting");
        var chaves = bloco.EnumerateObject().Select(p => p.Name).ToList();

        Assert.Equal(["title", "client", "project", "attendees", "calendar_event_id"], chaves);
    }

    [Fact]
    public void ComEvento_AcrescentaStartEndEOrganizador()
    {
        var ev = new Evento("abc", "Semanal",
            new DateTimeOffset(2026, 8, 8, 14, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 8, 8, 15, 0, 0, TimeSpan.Zero),
            [new Participante("Ana", "ana@x.com", false)], "Bruno");

        var meta = Meta.Montar(DateTimeOffset.UtcNow, new TrackStats { Nome = "system" },
            new TrackStats { Nome = "mic" }, "alto", "mic", 48000, 48000, ev.ParaMeta());

        var bloco = JsonDocument.Parse(meta.ParaJson()).RootElement.GetProperty("meeting");
        Assert.Equal("Semanal", bloco.GetProperty("title").GetString());
        Assert.Equal("abc", bloco.GetProperty("calendar_event_id").GetString());
        Assert.Equal("Bruno", bloco.GetProperty("organizer").GetString());
        Assert.Equal("Ana", bloco.GetProperty("attendees")[0].GetString());
        Assert.StartsWith("2026-08-08T14:00:00", bloco.GetProperty("start").GetString());
    }
}

public sealed class CredenciaisTests
{
    [Fact]
    public void ExpiryEscritoNoFormatoQueOGoogleAuthLe()
    {
        // O google-auth corta o "Z" e a fração e faz strptime com
        // "%Y-%m-%dT%H:%M:%S". Fora desse formato, o gravador Python passa a
        // não conseguir ler o token que este aqui gravou.
        string s = Credenciais.Expiry(
            new DateTimeOffset(2026, 8, 8, 17, 5, 9, TimeSpan.FromHours(-3)));

        Assert.Equal("2026-08-08T20:05:09Z", s);
        Assert.True(DateTime.TryParseExact(s.TrimEnd('Z'), "yyyy-MM-ddTHH:mm:ss",
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None, out _));
    }
}

public sealed class QueryDoRedirectTests
{
    [Fact]
    public void ExtraiCodigoEEstado()
    {
        var q = Autorizacao.Query("/?code=4%2F0Ab_c-d&state=xyz&scope=https%3A%2F%2Fx");
        Assert.Equal("4/0Ab_c-d", q["code"]);
        Assert.Equal("xyz", q["state"]);
    }

    [Fact]
    public void SemQueryNaoQuebra() => Assert.Empty(Autorizacao.Query("/"));

    [Fact]
    public void ErroDoGoogleNaoTemCodigo()
    {
        var q = Autorizacao.Query("/?error=access_denied&state=xyz");
        Assert.False(q.ContainsKey("code"));
    }
}
