using MeetingApp.Nucleo;
using MeetingApp.Nucleo.Atas;
using Xunit;

namespace MeetingApp.Tests;

/// <summary>
/// Quem é da casa e quem é do cliente, pelo domínio do e-mail.
/// </summary>
/// <remarks>
/// Nasceu de um erro concreto na ata de uma reunião real: alguém da equipe
/// apareceu como "Andre Monlevade (Vivo)", porque o modelo deduziu a organização
/// pelo assunto da conversa. Domínio de e-mail é fato; contexto de conversa é
/// chute.
/// </remarks>
public sealed class OrganizacoesTests
{
    private static readonly string[] Casa = ["beegol.com"];

    [Fact]
    public void ODominioDaCasaMarcaAEquipe()
    {
        var p = Organizacoes.Classificar(["andre.monlevade@beegol.com"], Casa);

        Assert.True(p[0].DaCasa);
        Assert.Equal("nosso", p[0].Lado);
        Assert.Equal("Andre Monlevade", p[0].Nome);
    }

    [Fact]
    public void QuemNaoEDaCasaECliente()
    {
        // Não se cadastra o domínio de cada cliente: quem não é da casa é
        // cliente, e a regra não precisa de manutenção quando aparece um novo.
        var p = Organizacoes.Classificar(["vanessa@telefonica.com"], Casa);

        Assert.False(p[0].DaCasa);
        Assert.Equal("cliente", p[0].Lado);
    }

    [Fact]
    public void SubdominioDaCasaTambemEDaCasa()
    {
        var p = Organizacoes.Classificar(["fulano@mail.beegol.com"], Casa);

        Assert.True(p[0].DaCasa);
    }

    [Fact]
    public void SemEmailNaoSeAfirmaNada()
    {
        // As gravações anteriores a esta versão só têm o nome. Preferir "não
        // sei" a chutar é o que separa uma ata conferível de uma que inventa
        // com confiança.
        var p = Organizacoes.Classificar(["Vanessa Levorato"], Casa);

        Assert.Null(p[0].DaCasa);
        Assert.Equal("Vanessa Levorato", p[0].Nome);
    }

    [Fact]
    public void SemDominioConfiguradoNinguemTemLado()
    {
        var p = Organizacoes.Classificar(["a@beegol.com", "b@telefonica.com"], []);

        Assert.All(p, x => Assert.Null(x.DaCasa));
    }

    [Fact]
    public void OPromptSeparaOsDoisLadosENomeiaOCliente()
    {
        var p = Organizacoes.Classificar(
            ["andre.monlevade@beegol.com", "vanessa@telefonica.com"], Casa);

        string texto = Organizacoes.ParaPrompt(p, "Vivo");

        Assert.Contains("Da nossa equipe: Andre Monlevade", texto);
        Assert.Contains("Do cliente (Vivo): Vanessa", texto);
        // A instrução é o que impede a dedução por assunto de conversa.
        Assert.Contains("Não deduza a organização", texto);
    }

    [Fact]
    public void SoNossaEquipeNaoPedeSeparacaoDeLado()
    {
        var p = Organizacoes.Classificar(["a@beegol.com", "b@beegol.com"], Casa);

        string texto = Organizacoes.ParaPrompt(p, null);

        Assert.Contains("Da nossa equipe", texto);
        Assert.DoesNotContain("Não deduza", texto);
    }

    [Fact]
    public void OArrobaSolitarioNoDominioConfiguradoNaoAtrapalha()
    {
        var p = Organizacoes.Classificar(["a@beegol.com"], ["@beegol.com", " BEEGOL.COM "]);

        Assert.True(p[0].DaCasa);
    }
}

/// <summary>O verificador usando o domínio para decidir o lado da pendência.</summary>
public sealed class LadoDaPendenciaTests
{
    private static readonly IReadOnlyList<Pessoa> Pessoas = Organizacoes.Classificar(
        ["andre.monlevade@beegol.com", "vanessa@telefonica.com"], ["beegol.com"]);

    private static SegmentoFinal Fala(string t) =>
        new() { Start = 0, End = 1, Text = t };

    [Fact]
    public void QuemEDaCasaVoltaParaONossoLado()
    {
        // Exatamente o erro visto na ata real: Andre é da equipe e foi para o
        // lado do cliente porque a reunião falava de Vivo o tempo todo.
        var ata = new AtaGerada
        {
            Acoes = [new AcaoDaAta
            {
                Acao = "Gerar a base", Responsavel = "Andre Monlevade",
                Prazo = "amanhã", Lado = "cliente",
            }],
        };

        VerificadorDeAta.Conferir(ata, [Fala("o André gera a base amanhã")],
                                  ["Andre Monlevade"], [], Pessoas);

        Assert.Equal("nosso", ata.Acoes[0].Lado);
        Assert.Contains(ata.Observacoes, o => o.Contains("mudaram de lado"));
    }

    [Fact]
    public void QuemEDoClienteVaiParaOLadoDoCliente()
    {
        var ata = new AtaGerada
        {
            Acoes = [new AcaoDaAta
            {
                Acao = "Analisar os exemplos", Responsavel = "Vanessa",
                Prazo = "hoje", Lado = "nosso",
            }],
        };

        VerificadorDeAta.Conferir(ata, [Fala("a Vanessa analisa hoje")],
                                  ["Vanessa Levorato"], [], Pessoas);

        Assert.Equal("cliente", ata.Acoes[0].Lado);
    }

    [Fact]
    public void SemPessoasClassificadasOLadoDoModeloFica()
    {
        // Sem e-mail não se afirma nada: mexer aqui seria trocar o chute do
        // modelo pelo nosso.
        var ata = new AtaGerada
        {
            Acoes = [new AcaoDaAta
            {
                Acao = "X", Responsavel = "Andre Monlevade", Prazo = "hoje", Lado = "cliente",
            }],
        };

        VerificadorDeAta.Conferir(ata, [Fala("o André faz hoje")],
                                  ["Andre Monlevade"], [], []);

        Assert.Equal("cliente", ata.Acoes[0].Lado);
        Assert.Empty(ata.Observacoes);
    }

    [Fact]
    public void OLadoCertoNaoViraObservacao()
    {
        var ata = new AtaGerada
        {
            Acoes = [new AcaoDaAta
            {
                Acao = "X", Responsavel = "Vanessa", Prazo = "hoje", Lado = "cliente",
            }],
        };

        VerificadorDeAta.Conferir(ata, [Fala("a Vanessa faz hoje")],
                                  ["Vanessa Levorato"], [], Pessoas);

        Assert.Empty(ata.Observacoes);
    }
}
