using System.Net;
using MeetingApp.Nucleo;
using Xunit;

namespace MeetingApp.Tests;

/// <summary>
/// A rota de atualização — o único caminho até quem já instalou.
/// </summary>
/// <remarks>
/// O que estes testes protegem é a comparação de versões, e a razão é o modo de
/// falha: uma rota de atualização quebrada <b>não dá erro</b>. Ela simplesmente
/// para de avisar, e ninguém descobre — porque "não apareceu aviso" é
/// indistinguível de "não há versão nova".
/// </remarks>
public sealed class AtualizacaoTests
{
    [Theory]
    [InlineData("0.1.1", "0.1.0", true)]
    [InlineData("0.2.0", "0.1.9", true)]
    [InlineData("1.0.0", "0.9.9", true)]
    [InlineData("0.1.0", "0.1.0", false)]
    [InlineData("0.1.0", "0.1.1", false)]
    [InlineData("0.1.0", "0.2.0", false)]
    public void ComparaVersaoPorNumeroENaoPorTexto(string candidata, string instalada, bool esperado)
    {
        Assert.Equal(esperado, Atualizacao.EhMaisNova(candidata, instalada));
    }

    [Fact]
    public void ADecimaVersaoNaoDesaparece()
    {
        // O erro clássico, e o motivo de esta classe existir: em ordem
        // alfabética "0.10.0" vem ANTES de "0.9.0". Comparando como texto, o
        // aviso pararia de aparecer exatamente na décima versão — em silêncio.
        Assert.True(Atualizacao.EhMaisNova("0.10.0", "0.9.0"));
        Assert.True(Atualizacao.EhMaisNova("0.2.10", "0.2.9"));
        Assert.False(Atualizacao.EhMaisNova("0.9.0", "0.10.0"));
    }

    [Theory]
    [InlineData("0.1.1", "0.1", true)]      // menos componentes de um lado
    [InlineData("0.1", "0.1.0", false)]     // "0.1" e "0.1.0" são a mesma coisa
    [InlineData("0.1.1-teste", "0.1.0", true)]
    [InlineData("", "0.1.0", false)]
    [InlineData("0.1.0", "", true)]
    public void AguentaVersaoMalFormada(string candidata, string instalada, bool esperado)
    {
        // O versao.json é editado à mão, então ele vai vir errado algum dia. O
        // que não pode é derrubar a tela por causa disso.
        Assert.Equal(esperado, Atualizacao.EhMaisNova(candidata, instalada));
    }

    private static HttpClient ClienteQue(HttpStatusCode codigo, string corpo) =>
        new(new RespostaFixa(codigo, corpo));

    [Fact]
    public async Task QuandoSaiVersaoNovaElaVoltaComAsNotas()
    {
        var estado = await Atualizacao.ProcurarAsync(
            new ConfiguracoesDoApp(),
            ClienteQue(HttpStatusCode.OK,
                """{"versao":"99.0.0","publicada":"2026-09-01","notas":"conserta tudo"}"""));

        Assert.NotNull(estado.Nova);
        Assert.Equal("99.0.0", estado.Nova!.Versao);
        Assert.Equal("conserta tudo", estado.Nova.Notas);
        Assert.Null(estado.NaoDeu);
    }

    [Fact]
    public async Task VersaoIgualOuVelhaNaoViraAviso()
    {
        var estado = await Atualizacao.ProcurarAsync(
            new ConfiguracoesDoApp(),
            ClienteQue(HttpStatusCode.OK, """{"versao":"0.0.1"}"""));

        Assert.Null(estado.Nova);
    }

    [Fact]
    public async Task SemRedeNaoEErro()
    {
        // O caso mais comum de todos: máquina offline, proxy de empresa, GitHub
        // fora do ar. Nada disso pode aparecer como problema para quem só queria
        // transcrever uma reunião.
        var estado = await Atualizacao.ProcurarAsync(
            new ConfiguracoesDoApp(), new HttpClient(new SempreFalha()));

        Assert.Null(estado.Nova);
        Assert.NotNull(estado.NaoDeu);
        Assert.NotEmpty(estado.VersaoInstalada);
    }

    [Fact]
    public async Task RespostaIlegivelNaoDerrubaNada()
    {
        var estado = await Atualizacao.ProcurarAsync(
            new ConfiguracoesDoApp(), ClienteQue(HttpStatusCode.OK, "isto não é json"));

        Assert.Null(estado.Nova);
        Assert.NotNull(estado.NaoDeu);
    }

    [Fact]
    public async Task DesligadoNaoAbreConexaoNenhuma()
    {
        // A preferência tem que valer ANTES do pedido, e não filtrar o resultado
        // depois: quem desligou o aviso desligou a conexão, não a mensagem.
        var espiao = new SempreFalha();
        var config = new ConfiguracoesDoApp { AvisarDeAtualizacao = false };

        var estado = await Atualizacao.ProcurarAsync(config, new HttpClient(espiao));

        Assert.True(estado.Desligado);
        Assert.Null(estado.Nova);
        Assert.Equal(0, espiao.Pedidos);
    }

    private sealed class RespostaFixa(HttpStatusCode codigo, string corpo) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage pedido, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(codigo)
            {
                Content = new StringContent(corpo),
            });
    }

    private sealed class SempreFalha : HttpMessageHandler
    {
        public int Pedidos { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage pedido, CancellationToken ct)
        {
            Pedidos++;
            throw new HttpRequestException("sem rede");
        }
    }
}
