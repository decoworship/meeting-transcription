using MeetingRecorder.Core;
using Xunit;

namespace MeetingRecorder.Tests;

/// <summary>
/// A fonte "automático": qual reunião gravar, e de que plataforma é o link da agenda.
/// </summary>
public sealed class FonteAutomaticaTests
{
    private const EscolhaAutomatica Nenhuma = EscolhaAutomatica.Nenhuma;
    private const EscolhaAutomatica Navegador = EscolhaAutomatica.Navegador;
    private const EscolhaAutomatica Zoom = EscolhaAutomatica.Zoom;

    [Fact]
    public void GravaAReuniaoQueEstiverAbertaComOuSemAgenda()
    {
        Assert.Equal(Navegador, FonteAutomatica.Escolher("meet", false, null, Nenhuma));
        Assert.Equal(Navegador, FonteAutomatica.Escolher("zoom", false, null, Nenhuma));   // Zoom na web
        Assert.Equal(Zoom, FonteAutomatica.Escolher(null, true, null, Nenhuma));
        // A agenda é plano; o que está aberto é fato.
        Assert.Equal(Zoom, FonteAutomatica.Escolher(null, true, "meet", Nenhuma));
        Assert.Equal(Nenhuma, FonteAutomatica.Escolher(null, false, "zoom", Navegador));
    }

    [Fact]
    public void ComDuasAbertasAAgendaDesempata()
    {
        Assert.Equal(Zoom, FonteAutomatica.Escolher("teams", true, "zoom", Navegador));
        Assert.Equal(Navegador, FonteAutomatica.Escolher("teams", true, "teams", Zoom));
        // O Zoom da agenda aberto no navegador é a do navegador.
        Assert.Equal(Navegador, FonteAutomatica.Escolher("zoom", true, "zoom", Zoom));
    }

    [Fact]
    public void ComDuasAbertasESemDesempateFicaAQueJaEstava()
    {
        Assert.Equal(Zoom, FonteAutomatica.Escolher("meet", true, null, Zoom));
        Assert.Equal(Navegador, FonteAutomatica.Escolher("meet", true, null, Navegador));
        Assert.Equal(Navegador, FonteAutomatica.Escolher("meet", true, "teams", Nenhuma));
    }

    [Theory]
    [InlineData("meet", "Meet")]
    [InlineData("zoom", "Zoom")]
    [InlineData(null, "Meet, Teams ou Zoom")]
    public void OAvisoDeEsperaDizQualReuniao(string? plataforma, string trecho) =>
        Assert.Contains(trecho, FonteAutomatica.Espera(plataforma));

    [Theory]
    [InlineData("meet", "https://meet.google.com/abc-defg-hij", null, null)]
    [InlineData("teams", null, "https://teams.microsoft.com/l/meetup-join/19%3ameeting", null)]
    [InlineData("zoom", null, "https://us02web.zoom.us/j/81234567890?pwd=x", null)]
    [InlineData("zoom", null, null, "Entrar: https://zoom.us/j/81234567890")]
    [InlineData("teams", null, null, "Reunião do Microsoft Teams\nhttps://teams.live.com/meet/9312")]
    [InlineData(null, null, null, "Sala 3, segundo andar")]
    [InlineData(null, null, null, "o site zoom.us é bom")]       // domínio sem link de reunião
    public void APlataformaSaiDoLinkDoEvento(string? esperada, string? hangout, string? conferencia,
                                             string? descricao) =>
        Assert.Equal(esperada, FonteAutomatica.Plataforma(hangout, conferencia, null, descricao));

    [Fact]
    public void OPrimeiroTextoComLinkVence() =>
        Assert.Equal("meet", FonteAutomatica.Plataforma("https://meet.google.com/x", null, null,
                                                        "convite antigo: https://zoom.us/j/1"));
}
