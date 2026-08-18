using MeetingApp.Nucleo;
using Xunit;

namespace MeetingApp.Tests;

/// <summary>
/// O tema da interface: claro, escuro, ou o que o Windows estiver usando.
/// </summary>
/// <remarks>
/// <para>
/// O tema escuro existia inteiro no design system desde o começo — sessenta
/// linhas de tokens semânticos redefinidos, superfícies em marrom-carvão,
/// séries de gráfico reordenadas — e <b>não havia como chegar nele</b>: o
/// <c>data-tema</c> do <c>index.html</c> era literal, e o app inteiro não
/// mencionava a palavra em nenhum outro lugar. Ninguém nunca o viu rodando.
/// </para>
/// <para>
/// O que estes testes guardam é o caminho de fora para dentro: o valor vem de
/// um <c>app.json</c> que qualquer um pode editar, atravessa a ponte e termina
/// dentro de um atributo do HTML servido. <see cref="ConfiguracoesDoApp.TemaAceito"/>
/// é o portão único desse caminho, e é por isso que ele é fechado numa lista em
/// vez de escapado: só saem daqui três constantes deste repositório.
/// </para>
/// </remarks>
public sealed class TemaTests : IDisposable
{
    private readonly string _pasta =
        Directory.CreateTempSubdirectory("tema").FullName;

    public void Dispose() => Directory.Delete(_pasta, recursive: true);

    private string AppJson => Path.Combine(_pasta, "app.json");

    [Fact]
    public void OPadraoEClaro()
    {
        // E não "auto", de propósito: quem já tem o app instalado não deve ver
        // a interface trocar de cor porque atualizou.
        Assert.Equal("claro", new ConfiguracoesDoApp().Tema);
    }

    [Theory]
    [InlineData("claro")]
    [InlineData("escuro")]
    [InlineData("auto")]
    public void OsTresValoresAtravessamOArquivo(string tema)
    {
        new ConfiguracoesDoApp { Tema = tema }.Salvar(AppJson);
        Assert.Equal(tema, ConfiguracoesDoApp.Carregar(AppJson).Tema);
    }

    [Theory]
    [InlineData("escuro", "escuro")]
    [InlineData("auto", "auto")]
    [InlineData("claro", "claro")]
    public void OQueEValidoPassaInteiro(string escrito, string esperado)
        => Assert.Equal(esperado, ConfiguracoesDoApp.TemaAceito(escrito));

    [Theory]
    [InlineData(null)]                       // chave ausente num app.json antigo
    [InlineData("")]
    [InlineData("Escuro")]                   // a comparação é exata
    [InlineData("dark")]
    [InlineData("auto ")]
    public void QualquerOutraCoisaViraClaro(string? escrito)
        => Assert.Equal("claro", ConfiguracoesDoApp.TemaAceito(escrito));

    [Fact]
    public void UmTemaComAspasNaoEscapaDoAtributo()
    {
        // O valor termina dentro de data-tema="…" no HTML servido. Se o portão
        // deixasse passar o que está escrito no arquivo, um app.json editado à
        // mão fecharia o atributo e escreveria marcação na página — e a CSP,
        // que é fechada justamente para que a página só rode o que veio do
        // executável, não veria nada de errado num atributo.
        Assert.Equal("claro", ConfiguracoesDoApp.TemaAceito(
            "claro\" data-x=\"<script>alert(1)</script>"));
    }

    [Fact]
    public void UmAppJsonSemAChaveContinuaAbrindo()
    {
        // Todo mundo que instalou 0.1.0 ou 0.2.0 tem um app.json sem "tema".
        File.WriteAllText(AppJson, """{ "modelo_padrao": "large-v3" }""");

        var c = ConfiguracoesDoApp.Carregar(AppJson);
        Assert.Equal("claro", c.Tema);
        Assert.Equal("large-v3", c.ModeloPadrao);
    }
}
