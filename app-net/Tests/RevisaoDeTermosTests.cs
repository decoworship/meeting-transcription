using MeetingApp.Nucleo;
using Xunit;

namespace MeetingApp.Tests;

/// <summary>
/// A rede que recupera nome próprio e sigla contra o que o app já sabe.
/// </summary>
/// <remarks>
/// Os dez casos são os mesmos do <see cref="AuditoriaCorrecaoFonetica"/>, e
/// vieram das gravações reais. Aqui está registrado quais a regra pega e quais
/// ela deixa passar <b>de propósito</b> — as que precisam entender a frase, e
/// que são o lugar do propositor de modelo.
/// </remarks>
public sealed class RevisaoDeTermosTests
{
    private static IReadOnlyList<Proposta> Propor(string texto, params string[] entidades) =>
        RevisaoDeTermos.Validar(
            RevisaoDeTermos.Propor([texto], entidades), entidades);

    // ───────────────────────────────────────────── o que a regra recupera

    [Theory]
    [InlineData("comparado inclusive com o G6CB", "G6CB", "GCCB",
                "GCCB, Sorocaba, Uberlândia, NextBest")]
    [InlineData("diferenciar a nível de PDB", "PDB", "PDV",
                "PDV, NextBest, SKU")]
    [InlineData("o cliente Moevade ainda não marcou", "Moevade", "Monlevade",
                "Monlevade, Beegol, Algar")]
    public void Recupera(string frase, string de, string para, string entidades)
    {
        var p = Assert.Single(Propor(frase, entidades));

        Assert.Equal(de, p.De);
        Assert.Equal(para, p.Para);
        Assert.Equal("regra", p.Fonte);
    }

    // ──────────────────────────────────── o que ela deixa para o modelo

    [Theory]
    // Minúsculas: decidir estas exige entender a frase, e uma regra que
    // tentasse pegá-las reescreveria português correto.
    [InlineData("a sexta é a mesma, cara", "cesta, SKU, NextBest")]
    [InlineData("utilizando o sistema de pooling", "polling, webhook, API")]
    [InlineData("com as outras engalafadoras", "engarrafadoras, Sorocaba")]
    // Longe demais: três edições. "Tim" e "Tulsa" são palavras que existem, e
    // trocá-las por regra quebraria qualquer reunião que cite a operadora.
    [InlineData("olhando a agenda do Tim, sabe", "Teams, Google Meet, Algar")]
    [InlineData("nos secrets da Tulsa API", "Tools, secrets, Algar")]
    // Duas edições em palavra curta: é onde "Edgar" viraria "Algar". Medido no
    // acervo — ver DistanciaPara. Estas duas o modelo pega; a regra não arrisca.
    [InlineData("o pessoal da Algarve tem um problema", "Algar, Beegol, Redir")]
    [InlineData("perguntar pro Cláudio, né", "Claude, Excel, Sorocaba")]
    public void DeixaParaOModelo(string frase, string entidades)
    {
        Assert.Empty(Propor(frase, entidades));
    }

    // ─────────────────────────────────────────────── o controle negativo

    [Theory]
    [InlineData("comparado inclusive com o GCCB", "GCCB, Sorocaba, Uberlândia")]
    [InlineData("a aderência de Sorocaba comparada com Uberlândia", "GCCB, Sorocaba, Uberlândia")]
    [InlineData("o desconto do Siebel está igual ao Kina", "Siebel, Kina, prorrata")]
    [InlineData("uma atualização de data da Redir, para setembro", "Algar, Redir, Beegol")]
    public void TextoCertoNaoEhReescrito(string frase, string entidades)
    {
        Assert.Empty(Propor(frase, entidades));
    }

    [Fact]
    public void NomeDePessoaNaoViraNomeDeCliente()
    {
        // O caso que definiu o corte de distância: "o que o Edgar tinha
        // indicado", numa reunião cujo cliente é a Algar.
        Assert.Empty(Propor("o que o Edgar tinha indicado que deve ser feito",
                            "Algar, Agentes, Beegol"));
    }

    [Fact]
    public void NomeVindoDeEmailNaoViraAlvo()
    {
        // "Felipeof" e "Emalina" são local-parts que a agenda devolveu como se
        // fossem nome. Com eles na lista, a regra reescrevia "Felipe" — pessoa
        // real, dita na reunião — para o lixo da agenda. O filtro está em
        // Transcritor.EntidadesConhecidas; aqui fica a prova de que, se algum
        // escapar, uma edição não basta para alcançá-lo.
        Assert.Empty(Propor("o Felipe vai mandar o arquivo", "Felipeof, Algar"));
    }

    [Fact]
    public void SemEntidadesNaoProporNada()
    {
        // A regra não inventa alvo: sem vocabulário e sem agenda, ela se cala.
        Assert.Empty(RevisaoDeTermos.Propor(["o pessoal da Algarve"], []));
    }

    [Fact]
    public void EmpateEntreDoisTermosNaoViraTroca()
    {
        // "PDA" está a uma edição de PDV e de PDB. Duas entidades igualmente
        // próximas significam que quem decide é o contexto — e contexto é o que
        // esta regra não tem.
        Assert.Empty(Propor("subiu para o PDA ontem", "PDV, PDB, SKU"));
    }

    [Fact]
    public void NomeDeParticipanteNaoEhTrocadoPorOutro()
    {
        // Dois convidados com nomes parecidos é o caso real deste projeto.
        // A regra só reescreve quem NÃO está na lista.
        Assert.Empty(Propor("o Andre Yuri falou com o Andre Monlevade",
                            "Andre Yuri, Andre Monlevade, Beegol"));
    }

    // ─────────────────────────────────────────── a porta e o aplicador

    [Fact]
    public void PropostaIdenticaEhDescartada()
    {
        // O modelo propõe "Sorocaba -> Sorocaba" com frequência. Medido em
        // 25/08: oito das dezessete propostas do Gemma eram identidade.
        var boas = RevisaoDeTermos.Validar(
            [new Proposta("Sorocaba", "Sorocaba", "modelo")], ["Sorocaba, GCCB"]);

        Assert.Empty(boas);
    }

    [Fact]
    public void AlvoDesconhecidoEhDescartado()
    {
        // Sem isto, um propositor escreveria no texto uma palavra que ninguém
        // no projeto usa.
        var boas = RevisaoDeTermos.Validar(
            [new Proposta("webhooks", "webhook", "modelo")], ["Algar, Redir"]);

        Assert.Empty(boas);
    }

    [Fact]
    public void AGrafiaQueValeEhADoVocabulario()
    {
        // O modelo escreve "gccb"; o projeto escreve "GCCB".
        var boas = RevisaoDeTermos.Validar(
            [new Proposta("G6CB", "gccb", "modelo")], ["GCCB, Sorocaba"]);

        Assert.Equal("GCCB", Assert.Single(boas).Para);
    }

    [Fact]
    public void AplicarTrocaPalavraInteiraEDeixaRastro()
    {
        var (texto, trocas) = RevisaoDeTermos.Aplicar(
            "o G6CB e o G6CBX comparados", [new Proposta("G6CB", "GCCB", "regra")]);

        // "G6CBX" não é "G6CB": sem fronteira, a troca espalharia.
        Assert.Equal("o GCCB e o G6CBX comparados", texto);
        var t = Assert.Single(trocas);
        Assert.Equal("G6CB", t.De);
        Assert.Equal("GCCB", t.Para);
    }

    [Fact]
    public void OModeloPassaPelaMesmaPorta()
    {
        // O desenho inteiro: propositor diferente, validação e aplicação iguais.
        // Aqui entra o que o Gemma acertou e a regra não pega.
        var doModelo = new[]
        {
            new Proposta("Sorocaba", "Sorocaba", "modelo"),   // identidade, cai
            new Proposta("sexta", "cesta", "modelo"),         // válida
            new Proposta("webhooks", "webhook", "modelo"),    // alvo desconhecido, cai
        };

        var boas = RevisaoDeTermos.Validar(doModelo, ["cesta, SKU, NextBest"]);

        var p = Assert.Single(boas);
        Assert.Equal("cesta", p.Para);
        Assert.Equal("modelo", p.Fonte);
    }
}
