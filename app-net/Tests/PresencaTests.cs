using MeetingApp.Nucleo;
using MeetingApp.Nucleo.Atas;
using Xunit;

namespace MeetingApp.Tests;

/// <summary>
/// Quem foi convidado contra quem falou.
/// </summary>
/// <remarks>
/// O caso que originou isto é real: uma ata com onze nomes no cabeçalho e seis
/// falantes na transcrição. Cinco pessoas que nunca abriram a boca — e talvez
/// nunca tenham entrado — apareciam como participantes.
/// </remarks>
public sealed class PresencaTests
{
    private static Pessoa Casa(string nome) => new(nome, "beegol.com", true);
    private static Pessoa Cliente(string nome) => new(nome, "telefonica.com", false);

    [Fact]
    public void SeparaQuemFalouDeQuemSoFoiConvidado()
    {
        IReadOnlyList<Pessoa> pessoas =
            [Casa("Andre Yuri"), Casa("Alexandre Farias"), Cliente("Carla Hack")];

        var q = Presenca.Cruzar(pessoas, ["Andre Yuri", "Carla Hack"]);

        Assert.True(q.DaParaSeparar);
        Assert.Equal(["Andre Yuri", "Carla Hack"], q.Falaram.Select(p => p.Nome));
        Assert.Equal(["Alexandre Farias"], q.SoConvidados.Select(p => p.Nome));
        Assert.Equal(0, q.NaoIdentificados);
    }

    [Fact]
    public void PrimeiroNomeBasta()
    {
        // A diarização nomeia "Vanessa" e a agenda traz o nome inteiro. Exigir
        // igualdade exata mandaria metade da reunião para "não falou".
        IReadOnlyList<Pessoa> pessoas = [Cliente("Vanessa Levorato Sao Bernado")];

        var q = Presenca.Cruzar(pessoas, ["Vanessa"]);

        Assert.Single(q.Falaram);
        Assert.Empty(q.SoConvidados);
    }

    [Fact]
    public void DonoDoMicrofoneEhDaCasa()
    {
        IReadOnlyList<Pessoa> pessoas = [Cliente("Carla Hack"), Casa("Andre Yuri")];

        var q = Presenca.Cruzar(pessoas, ["You"]);

        Assert.Equal("Andre Yuri", Assert.Single(q.Falaram).Nome);
    }

    [Fact]
    public void FalanteSemNomeContaMasNaoVirouNinguem()
    {
        // "Speaker 3" falou. Sumir com ele faria a ata dizer que menos gente
        // falou do que falou.
        IReadOnlyList<Pessoa> pessoas = [Casa("Andre Yuri"), Casa("Ellen Rodrigues")];

        var q = Presenca.Cruzar(pessoas, ["Andre Yuri", "Speaker 3", "Unknown"]);

        Assert.Single(q.Falaram);
        Assert.Equal(2, q.NaoIdentificados);
        Assert.Equal(["Ellen Rodrigues"], q.SoConvidados.Select(p => p.Nome));
    }

    [Fact]
    public void QuemNaoEstaNoConviteContaComoNaoIdentificado()
    {
        IReadOnlyList<Pessoa> pessoas = [Casa("Andre Yuri")];

        var q = Presenca.Cruzar(pessoas, ["Andre Yuri", "Joverson Pagnussat"]);

        Assert.Single(q.Falaram);
        Assert.Equal(1, q.NaoIdentificados);
    }

    [Fact]
    public void SemFalantesNaoSeparaNada()
    {
        // Diarização desligada, ou transcrição antiga. O cabeçalho volta a ser
        // o de antes — dizer que ninguém falou seria pior que não separar.
        IReadOnlyList<Pessoa> pessoas = [Casa("Andre Yuri"), Cliente("Carla Hack")];

        var q = Presenca.Cruzar(pessoas, []);

        Assert.False(q.DaParaSeparar);
        Assert.Equal(2, q.SoConvidados.Count);
    }

    [Fact]
    public void SoRotulosGenericosNaoSepara()
    {
        IReadOnlyList<Pessoa> pessoas = [Casa("Andre Yuri")];

        var q = Presenca.Cruzar(pessoas, ["Speaker 1", "Speaker 2"]);

        Assert.False(q.DaParaSeparar);
        Assert.Equal(2, q.NaoIdentificados);
    }

    [Fact]
    public void NomeQueVeioDoEmailAindaCasa()
    {
        // Medido: a agenda guarda "andre.monlevade" como nome, e ele falou 47
        // vezes. Com casamento por primeiro nome a ata afirmava que ele não
        // falou — pior que a lista genérica de antes, porque é uma afirmação
        // falsa sobre uma pessoa com nome.
        IReadOnlyList<Pessoa> pessoas =
            [Casa("andre.monlevade"), Casa("Andre Yuri"), Casa("ellen.rodrigues")];

        var q = Presenca.Cruzar(pessoas, ["Andre Monlevade", "Andre Yuri"]);

        Assert.Equal(["andre.monlevade", "Andre Yuri"], q.Falaram.Select(p => p.Nome));
        Assert.Equal(["ellen.rodrigues"], q.SoConvidados.Select(p => p.Nome));
        Assert.Equal(0, q.NaoIdentificados);
    }

    [Fact]
    public void SobrenomeDesempataQuandoOPrimeiroNomeRepete()
    {
        // Dois Andrés na mesma reunião é o caso real deste projeto.
        IReadOnlyList<Pessoa> pessoas = [Casa("Andre Monlevade"), Casa("Andre Yuri")];

        var q = Presenca.Cruzar(pessoas, ["Andre Yuri"]);

        Assert.Equal("Andre Yuri", Assert.Single(q.Falaram).Nome);
        Assert.Equal("Andre Monlevade", Assert.Single(q.SoConvidados).Nome);
    }

    [Fact]
    public void OMesmoConvidadoNaoEntraDuasVezes()
    {
        // Os falantes chegam distintos, mas dois rótulos podem casar com a
        // mesma pessoa ("Andre Yuri" e "Andre"). Sem remover da lista, ela
        // apareceria duas vezes no cabeçalho.
        IReadOnlyList<Pessoa> pessoas = [Casa("Andre Yuri"), Casa("Ellen Rodrigues")];

        var q = Presenca.Cruzar(pessoas, ["Andre Yuri", "Andre"]);

        Assert.Single(q.Falaram);
        Assert.Equal(1, q.NaoIdentificados);
    }

    // ------------------------------------------------ o cabeçalho renderizado
    //
    // O cruzamento acima é a regra; isto é o que a pessoa lê. Os dois testes
    // separados existem porque o layout já mudou duas vezes sem a regra mudar.

    private static string Cabecalho(IReadOnlyList<Pessoa> pessoas,
                                    IReadOnlyList<string> falantes,
                                    string? cliente = "Vivo")
    {
        var ctx = new ContextoDaReuniao
        {
            Titulo = "Reunião",
            Cliente = cliente,
            Pessoas = pessoas,
            Falantes = falantes,
        };
        string md = RedatorDeAta.Escrever(
            new AtaGerada { Resumo = "resumo" },
            ModelosDeAta.Buscar("cliente-update")!, ctx);
        return md;
    }

    [Fact]
    public void CabecalhoTemConvidadosEmCimaEFalantesEmbaixo()
    {
        string md = Cabecalho(
            [Casa("Andre Yuri"), Casa("Ellen Rodrigues"), Cliente("Carla Hack")],
            ["Andre Yuri", "Carla Hack"]);

        Assert.Contains("**Convidados:** Andre Yuri, Ellen Rodrigues · "
                        + "**Vivo:** Carla Hack", md);
        Assert.Contains("**Falaram:** Andre Yuri, Carla Hack", md);
        // Quem não falou sai da subtração, e não de uma terceira linha.
        Assert.DoesNotContain("não falaram", md);
    }

    [Fact]
    public void OsFalantesSaemNaOrdemDoConvite()
    {
        // Na ordem da fala sairia "Carla Hack, Andre Yuri", e comparar as duas
        // linhas de relance deixaria de funcionar.
        string md = Cabecalho(
            [Casa("Andre Yuri"), Casa("Ellen Rodrigues"), Cliente("Carla Hack")],
            ["Carla Hack", "Andre Yuri"]);

        Assert.Contains("**Falaram:** Andre Yuri, Carla Hack", md);
    }

    [Fact]
    public void FalanteSemNomeVirouContagemNoCabecalho()
    {
        string md = Cabecalho([Casa("Andre Yuri")], ["Andre Yuri", "Speaker 2"]);

        Assert.Contains("**Falaram:** Andre Yuri · **+1** falante não identificado", md);
    }

    [Fact]
    public void SemFalantesOCabecalhoEhODeAntes()
    {
        string md = Cabecalho([Casa("Andre Yuri"), Cliente("Carla Hack")], []);

        Assert.Contains("**Participantes:** Andre Yuri · **Vivo:** Carla Hack", md);
        Assert.DoesNotContain("**Falaram:**", md);
    }

    // ------------------------------------------- o nome que vem da agenda
    //
    // Estes cobrem a origem do nome, e não o cruzamento: o cruzamento estava
    // certo e ainda assim a ata errava, porque o nome chegava grudado.

    [Fact]
    public void NomeDeExibicaoVenceOEmailGrudado()
    {
        // lilianioshimoto@telefonica.com viraria "Lilianioshimoto", que não casa
        // com o falante "Lilian Ioshimoto" — e a ata dizia que ela não falou.
        var pessoas = Organizacoes.Classificar(
            ["Lilian Ioshimoto"], ["lilianioshimoto@telefonica.com"], ["beegol.com"]);

        var p = Assert.Single(pessoas);
        Assert.Equal("Lilian Ioshimoto", p.Nome);
        Assert.False(p.DaCasa);          // o lado continua vindo do domínio

        var q = Presenca.Cruzar(pessoas, ["Lilian Ioshimoto"]);
        Assert.Single(q.Falaram);
        Assert.Equal(0, q.NaoIdentificados);
    }

    [Fact]
    public void NomeDeExibicaoQueEhOProprioEmailPerdeParaOEmail()
    {
        // A agenda às vezes repete o local-part no lugar do nome. Aí o
        // NomeLegivel do e-mail sai melhor que o "nome" cru.
        var pessoas = Organizacoes.Classificar(
            ["andre.monlevade"], ["andre.monlevade@beegol.com"], ["beegol.com"]);

        Assert.Equal("Andre Monlevade", Assert.Single(pessoas).Nome);
    }

    [Fact]
    public void ListasDeTamanhoDiferenteNaoSaoPareadas()
    {
        // Parear na marra alinharia gente errada, e nome trocado é pior que
        // nome feio.
        var pessoas = Organizacoes.Classificar(
            ["Lilian Ioshimoto"], ["a@x.com", "b@x.com"], ["beegol.com"]);

        Assert.Equal(2, pessoas.Count);
        Assert.DoesNotContain(pessoas, p => p.Nome == "Lilian Ioshimoto");
    }
}
