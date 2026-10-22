using MeetingApp.Nucleo;
using Xunit;

namespace MeetingApp.Tests;

/// <summary>
/// O nome do dono, escrito em Ajustes › Geral, no lugar do "You" — o mesmo
/// renomear que a revisão faz à mão, feito no fim da transcrição.
/// </summary>
public sealed class NomeDoDonoTests
{
    private static SegmentoFinal Seg(string? falante) =>
        new() { Start = 0, End = 1, Text = "oi", Speaker = falante };

    [Fact]
    public void TrocaSoOYou()
    {
        var segs = new List<SegmentoFinal> { Seg("You"), Seg("Marcos"), Seg("Speaker 2"), Seg(null) };
        VozDoDono.Nomear(segs, "André");
        Assert.Equal(["André", "Marcos", "Speaker 2", null], segs.Select(s => s.Speaker));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void SemNomeOYouFica(string? nome)
    {
        var segs = new List<SegmentoFinal> { Seg("You") };
        VozDoDono.Nomear(segs, nome);
        Assert.Equal("You", segs[0].Speaker);
    }

    [Fact]
    public void ONomeSaiSemEspacoNasPontas()
    {
        var segs = new List<SegmentoFinal> { Seg("You") };
        VozDoDono.Nomear(segs, "  André  ");
        Assert.Equal("André", segs[0].Speaker);
    }

    [Fact]
    public void AChaveVemDoAppJson()
    {
        var c = System.Text.Json.JsonSerializer.Deserialize<ConfiguracoesDoApp>(
            """{"nome_do_dono":"André"}""")!;
        Assert.Equal("André", c.NomeDoDono);
        Assert.Equal("", new ConfiguracoesDoApp().NomeDoDono);
    }
}
