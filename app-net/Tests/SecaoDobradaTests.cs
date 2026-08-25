using MeetingApp.Nucleo.Atas;
using Xunit;

namespace MeetingApp.Tests;

/// <summary>
/// A seção que veio pelo lugar errado, recuperada em vez de descartada.
/// </summary>
/// <remarks>
/// Os casos vêm da varredura do acervo em 21/08/2026: 59 itens de ata gerados e
/// jogados fora em 5 das 28 gravações, sem nenhuma linha dizendo que sumiram.
/// O pior deles está reproduzido em <see cref="ADailyQuePareciaNaoTerGeradoAcao"/>.
/// </remarks>
public sealed class SecaoDobradaTests
{
    private static SecaoDaAta Secao(string titulo, string texto) =>
        new() { Titulo = titulo, Texto = texto };

    [Fact]
    public void DecisaoQueVeioComoSecaoVoltaParaOCampo()
    {
        var ata = new AtaGerada
        {
            Secoes = [Secao("Decisões", "- Não clusterizar por preço.\n- Testar margem.")],
        };

        var notas = SecaoDobrada.Dobrar(ata);

        Assert.Equal(["Não clusterizar por preço.", "Testar margem."], ata.Decisoes);
        Assert.Empty(ata.Secoes);
        Assert.Single(notas);
    }

    [Fact]
    public void ADailyQuePareciaNaoTerGeradoAcao()
    {
        // 2026-08-17_15-29-18: 2 KB de ata.md, só Resumo e a rodada, enquanto o
        // ata.json ao lado trazia quatro pendências com dono e prazo.
        var ata = new AtaGerada
        {
            Secoes =
            [
                Secao("Rodada", "- Fulano: apresentou o roteiro."),
                Secao("Acoes",
                      "- [ ] Revisar a linguagem do prompt — Andre Yuri — prazo a definir\n"
                      + "- [ ] Investigar a API do portal — **Eduardo Almeida** — sexta"),
                Secao("Riscos e alertas", "- Risco: a demo não inclui reboot real."),
            ],
        };

        SecaoDobrada.Dobrar(ata);

        Assert.Equal(2, ata.Acoes.Count);
        Assert.Equal("Revisar a linguagem do prompt", ata.Acoes[0].Acao);
        Assert.Equal("Andre Yuri", ata.Acoes[0].Responsavel);
        Assert.Equal("prazo a definir", ata.Acoes[0].Prazo);
        Assert.Equal("Eduardo Almeida", ata.Acoes[1].Responsavel);   // sem o negrito
        Assert.Equal("sexta", ata.Acoes[1].Prazo);
        Assert.Single(ata.Riscos);

        // A seção que é corpo de verdade continua sendo corpo.
        Assert.Equal("Rodada", Assert.Single(ata.Secoes).Titulo);
    }

    [Fact]
    public void CampoJaCheioNaoEhSobrescrito()
    {
        // Aqui não há perda: o redator ignora a seção duplicada, e ignorar é o
        // certo. Sobrescrever trocaria o campo conferido pela prosa.
        var ata = new AtaGerada
        {
            Decisoes = ["a decisão de verdade"],
            Secoes = [Secao("Decisões", "- outra coisa")],
        };

        var notas = SecaoDobrada.Dobrar(ata);

        Assert.Equal(["a decisão de verdade"], ata.Decisoes);
        Assert.Empty(notas);
        Assert.Single(ata.Secoes);
    }

    [Fact]
    public void ObservacoesNaoSaoDobradas()
    {
        // O VerificadorDeAta descarta a prosa do modelo e reescreve a seção com
        // o que ele mediu. Recuperar para lá seria devolver o texto para quem
        // vai apagá-lo.
        var ata = new AtaGerada
        {
            Secoes = [Secao("Observações sobre a transcrição", "- o áudio some aos 12:03")],
        };

        Assert.Empty(SecaoDobrada.Dobrar(ata));
        Assert.Single(ata.Secoes);
    }

    [Fact]
    public void SecaoDeCorpoNaoEhTocada()
    {
        var ata = new AtaGerada { Secoes = [Secao("Status por frente", "- Frente A: ok")] };

        Assert.Empty(SecaoDobrada.Dobrar(ata));
        Assert.Single(ata.Secoes);
        Assert.Empty(ata.Decisoes);
    }

    [Fact]
    public void ProsaSemMarcadorViraUmItemSo()
    {
        // Quebrar prosa em frases inventaria divisão que o modelo não fez.
        var ata = new AtaGerada
        {
            Secoes = [Secao("Pontos em aberto", "Falta definir o prazo. E o escopo também.")],
        };

        SecaoDobrada.Dobrar(ata);

        Assert.Equal("Falta definir o prazo. E o escopo também.",
                     Assert.Single(ata.PontosEmAberto));
    }

    [Fact]
    public void SecaoVaziaNaoGeraItem()
    {
        var ata = new AtaGerada { Secoes = [Secao("Decisões", "   \n\n  ")] };

        Assert.Empty(SecaoDobrada.Dobrar(ata));
        Assert.Empty(ata.Decisoes);
    }

    [Fact]
    public void AcaoSemDonoFicaSemDono()
    {
        // Inventar dono é o erro que o verificador existe para impedir.
        var ata = new AtaGerada
        {
            Secoes = [Secao("Pendências", "- [ ] Levantar os dados de margem")],
        };

        SecaoDobrada.Dobrar(ata);

        var acao = Assert.Single(ata.Acoes);
        Assert.Equal("Levantar os dados de margem", acao.Acao);
        Assert.Equal("", acao.Responsavel);
    }
}
