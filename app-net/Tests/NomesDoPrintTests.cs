using MeetingApp.Nucleo;
using MeetingApp.Sidecar;
using Xunit;

namespace MeetingApp.Tests;

/// <summary>
/// Os nomes que um print da reunião mostra (UI-12).
/// </summary>
/// <remarks>
/// <para>
/// As linhas abaixo imitam o que o OCR leu em prints reais do Teams em
/// 21/10/2026 — a barra de botões, o cronômetro, o fundo de marca da câmera, o
/// ícone de sinal grudado no nome de quem fala, o sufixo da empresa e o nome em
/// maiúsculas —, mas com nomes inventados: o repositório não guarda o nome de
/// quem estava na reunião de ninguém.
/// </para>
/// <para>
/// O erro que mais custa aqui é o nome inventado — uma linha de tabela de slide
/// virando participante. Por isso a leitura nunca grava sozinha: ela sugere, a
/// pessoa confirma, e <see cref="NomesDoPrint.Leitura.PareceReuniao"/> decide se
/// as caixinhas já vêm marcadas.
/// </para>
/// </remarks>
public sealed class NomesDoPrintTests
{
    private static LinhaLida L(string texto, double confianca = 0.99) =>
        new(texto, confianca, 0, 0, 10, 10);

    /// <summary>A galeria do Teams em inglês, como no print 1.</summary>
    private static readonly LinhaLida[] Galeria =
    [
        L("Microsoft Teams meeting"), L("□"), L("×"), L("48:35"),
        L("Chat"), L("People"), L("Raise"), L("React"), L("View"), L("More"),
        L("Camera"), L("Mic"), L("Share"), L("Leave"),
        L("Il Marina Costa-Acme"), L("Rafael Tavares De Lima"), L("Otavio Brandao- Acme"),
        L("Luiza Fontes De Andrade"), L("joao.pereira"), L("Sergio Mattos De Campos"),
        L("2"), L("Helena Dias"), L("BEATRIZ SOARES NUNES"),
        L("tamojunto"), L("Algar:"), L("tamo"), L("junto"),
    ];

    private static List<string> Nomes(NomesDoPrint.Leitura l) => [.. l.Nomes.Select(n => n.Nome)];

    [Fact]
    public void AGaleriaDoTeamsDaOsNomesESoOsNomes()
    {
        var l = NomesDoPrint.Ler(Galeria, []);

        Assert.Equal(
            ["Marina Costa", "Rafael Tavares De Lima", "Otavio Brandao", "Luiza Fontes De Andrade",
             "Joao Pereira", "Sergio Mattos De Campos", "Helena Dias", "Beatriz Soares Nunes"],
            Nomes(l));
        Assert.True(l.PareceReuniao);
    }

    [Fact]
    public void OOriginalFicaGuardadoParaATelaMostrar()
    {
        var l = NomesDoPrint.Ler(Galeria, []);

        Assert.Equal("Marina Costa-Acme", l.Nomes[0].Original);
        Assert.Equal("joao.pereira", l.Nomes.Single(n => n.Nome == "Joao Pereira").Original);
    }

    [Fact]
    public void OSufixoSoSaiQuandoEDeEmpresa()
    {
        // Um hífen sem espaço, num nome só, pode ser sobrenome composto — e não
        // se apaga sobrenome de ninguém por palpite.
        var l = NomesDoPrint.Ler([L("Chat"), L("Leave"), L("Marina Costa-Acme"), L("Ana Silva-Santos")], []);

        Assert.Equal(["Marina Costa-Acme", "Ana Silva-Santos"], Nomes(l));
    }

    [Fact]
    public void OSufixoComEspacoEEmpresaMesmoSozinho()
    {
        // "Paloma - Beegol": o espaço antes do hífen é como o Teams separa a
        // empresa, e o que sobra pode ser um nome de uma palavra só.
        var l = NomesDoPrint.Ler([L("Organizer"), L("Clara - Acme"), L("Rui Matos")], []);

        Assert.Equal(["Clara", "Rui Matos"], Nomes(l));
    }

    [Fact]
    public void ALinhaDePessoasComOrganizadorEUmaListaDeNomes()
    {
        // O painel "In this meeting": quase só nomes, e um "Organizer" no meio.
        // Não tem a barra de botões, e mesmo assim é print de reunião.
        var l = NomesDoPrint.Ler(
            [L("Marina Costa-Acme"), L("RAFAEL TAVARES JUNIOR"), L("&", 0.67), L("Organizer"),
             L("Helena Dias"), L("BEATRIZ SOARES NUNES"), L("Clara - Acme")], []);

        Assert.Equal(["Marina Costa", "Rafael Tavares Junior", "Helena Dias", "Beatriz Soares Nunes", "Clara"],
                     Nomes(l));
        Assert.True(l.PareceReuniao);
    }

    [Fact]
    public void ATabelaDeUmSlideNaoPareceReuniao()
    {
        // O print de um painel de vendas: nomes em maiúsculas numa tabela, e
        // muito número em volta. Os nomes vêm, porque podem ser os que a pessoa
        // quer — mas desmarcados.
        var linhas = new List<LinhaLida>
        {
            L("ADERÊNCIA POR VENDEDOR"), L("FILTRO"), L("26,4%"), L("Meta"), L("Aderência Acu..."),
            L("01/10/2026"), L("Supervisor"), L("Todos"), L("Vendedor"), L("Setor"),
            L("Acompanhamento por vendedor (mesmo conteúdo do e-mail diário)"),
            L("00011 ALBERTO MOURA DA SILVA"), L("CARLOS EDUARDO PIRES"), L("31,0%"), L("2.937"),
            L("DANIELA ROCHA DOS SANTOS"), L("1.100"), L("33,7%"), L("Relatório de"),
        };
        var l = NomesDoPrint.Ler(linhas, []);

        Assert.False(l.PareceReuniao);
        Assert.Contains("Carlos Eduardo Pires", Nomes(l));
        Assert.DoesNotContain(Nomes(l), n => n.Contains("Aderência", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void LinhaComNumeroOuPontuacaoNaoENome()
    {
        var l = NomesDoPrint.Ler(
            [L("Chat"), L("Leave"), L("Planner 2026 - Merchan"), L("Last 365days"), L("Aderência Acu..."),
             L("(0.2)"), L("Stop sharing"), L("O que o cliente lê:"), L("Helena Dias")], []);

        Assert.Equal(["Helena Dias"], Nomes(l));
    }

    [Fact]
    public void FraseEmMinusculasNaoENome()
    {
        var l = NomesDoPrint.Ler(
            [L("Chat"), L("Leave"), L("Eu acho que a Carla vai querer esse gráfico"), L("sugerido aqui"),
             L("compra parcelada"), L("Helena Dias")], []);

        Assert.Equal(["Helena Dias"], Nomes(l));
    }

    [Fact]
    public void LeituraPoucoConfiavelFicaDeFora()
    {
        var l = NomesDoPrint.Ler([L("Chat"), L("Leave"), L("Qitar Rirvays", 0.6), L("Helena Dias")], []);

        Assert.Equal(["Helena Dias"], Nomes(l));
    }

    [Fact]
    public void AMesmaPessoaDuasVezesViraUmaSo()
    {
        var l = NomesDoPrint.Ler(
            [L("Chat"), L("Leave"), L("joao.pereira"), L("João Pereira"), L("HELENA DIAS"), L("Helena Dias")], []);

        Assert.Equal(["Joao Pereira", "Helena Dias"], Nomes(l));
    }

    [Fact]
    public void ParticulaEmMaiusculasViraMinuscula()
    {
        var l = NomesDoPrint.Ler([L("Chat"), L("Leave"), L("ANTONIO DE LEVA DOS SANTOS E SILVA")], []);

        Assert.Equal(["Antonio de Leva dos Santos e Silva"], Nomes(l));
    }

    [Fact]
    public void OTextoDoTeamsEmPortuguesTambemSai()
    {
        var l = NomesDoPrint.Ler(
            [L("Bate-papo"), L("Pessoas"), L("Levantar"), L("Reagir"), L("Exibir"), L("Mais"),
             L("Câmera"), L("Microfone"), L("Compartilhar"), L("Sair"), L("Parar compartilhamento"),
             L("Organizador"), L("Nesta reunião (3)"), L("Helena Dias (Você)"), L("Rui Matos")], []);

        Assert.Equal(["Helena Dias", "Rui Matos"], Nomes(l));
        Assert.True(l.PareceReuniao);
    }

    [Fact]
    public void QuemEstaNaAgendaVemComONomeDaAgenda()
    {
        // O print escreve como a pessoa se cadastrou no Teams; a agenda, como
        // o convite a chama. Casar os dois é o que deixa o nome do falante
        // igual ao da ata.
        var l = NomesDoPrint.Ler(
            [L("Chat"), L("Leave"), L("HELENA DIAS"), L("Rui Matos"), L("joao.pereira")],
            ["Helena Dias Fonseca", "João Pereira", "Outra Pessoa"]);

        Assert.Equal("Helena Dias Fonseca", l.Nomes.Single(n => n.Original == "HELENA DIAS").Convidado);
        Assert.Equal("João Pereira", l.Nomes.Single(n => n.Original == "joao.pereira").Convidado);
        Assert.Null(l.Nomes.Single(n => n.Original == "Rui Matos").Convidado);
    }

    [Fact]
    public void SemLinhasNaoHaNomesNemReuniao()
    {
        var l = NomesDoPrint.Ler([], []);

        Assert.Empty(l.Nomes);
        Assert.False(l.PareceReuniao);
    }
}
