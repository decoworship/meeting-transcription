using MeetingApp.Nucleo;
using Xunit;

namespace MeetingApp.Tests;

/// <summary>
/// As notas escritas durante a reunião.
/// </summary>
/// <remarks>
/// O que se testa aqui é o que dói perder: o texto que sobrevive à escrita
/// interrompida, e a extração de termos que alimenta o vocabulário sem inventar
/// nome de gente.
/// </remarks>
public sealed class NotasTests : IDisposable
{
    private readonly string _pasta =
        Directory.CreateTempSubdirectory("notas-testes").FullName;

    public void Dispose() => Directory.Delete(_pasta, recursive: true);

    [Fact]
    public void OQueFoiEscritoVoltaIgual()
    {
        Notas.Salvar(_pasta, "- decidimos adiar o piloto\n- **cobrar** a Vivo até sexta");

        Assert.Equal("- decidimos adiar o piloto\n- **cobrar** a Vivo até sexta",
                     Notas.Ler(_pasta));
        Assert.True(Notas.Existem(_pasta));
    }

    [Fact]
    public void GravacaoSemNotasNaoEUmErro()
    {
        Assert.Equal("", Notas.Ler(_pasta));
        Assert.False(Notas.Existem(_pasta));
    }

    [Fact]
    public void ApagarTudoRemoveOArquivo()
    {
        Notas.Salvar(_pasta, "alguma coisa");
        Notas.Salvar(_pasta, "   \n  ");

        Assert.False(File.Exists(Notas.Caminho(_pasta)));
        Assert.False(Notas.Existem(_pasta));
    }

    [Fact]
    public void NaoDeixaTemporarioParaTras()
    {
        // A escrita é em arquivo temporário e move por cima, para uma queda no
        // meio não truncar o que já estava escrito. O .tmp não pode sobrar na
        // pasta da gravação, que é uma pasta que o usuário abre.
        Notas.Salvar(_pasta, "nota");
        Notas.Salvar(_pasta, "nota maior");

        Assert.Equal(["notas.md"],
            Directory.GetFiles(_pasta).Select(f => Path.GetFileName(f)!).Order());
    }

    [Fact]
    public void SobrescreverNaoDeixaRestoDoTextoAntigo()
    {
        Notas.Salvar(_pasta, "um texto bem comprido que estava aqui antes");
        Notas.Salvar(_pasta, "curto");

        Assert.Equal("curto", Notas.Ler(_pasta));
    }

    [Fact]
    public void OAcentoSobrevive()
    {
        Notas.Salvar(_pasta, "reunião de manutenção — ficou pendente a validação");

        Assert.Contains("manutenção", Notas.Ler(_pasta));
    }

    [Theory]
    [InlineData("falamos com a Vanessa sobre o Jira", new[] { "Vanessa", "Jira" })]
    [InlineData("subir o CSV para o SFTP", new[] { "CSV", "SFTP" })]
    [InlineData("- o Datalake da Vivo está fora", new[] { "Datalake", "Vivo" })]
    public void OsNomesEAsSiglasViramSugestaoDeVocabulario(string nota, string[] esperados)
    {
        var termos = Notas.TermosSugeridos(nota);

        foreach (string e in esperados) Assert.Contains(e, termos);
    }

    [Fact]
    public void APrimeiraPalavraDaFraseNaoEUmNomeProprio()
    {
        // Abrir frase com maiúscula é gramática. Sugerir "Ficou" como termo de
        // vocabulário encheria a lista de lixo e faria a pessoa parar de olhar.
        var termos = Notas.TermosSugeridos("Ficou pendente a validação com o Bruno");

        Assert.DoesNotContain("Ficou", termos);
        Assert.Contains("Bruno", termos);
    }

    [Fact]
    public void OTempoMarcadoNaoViraTermo()
    {
        var termos = Notas.TermosSugeridos("[00:12:34] a Ana entrou");

        Assert.DoesNotContain("00:12:34", termos);
        Assert.Contains("Ana", termos);
    }

    [Fact]
    public void NaoRepeteOMesmoTermo()
    {
        var termos = Notas.TermosSugeridos("o Jira do Jira, sempre o Jira");

        Assert.Single(termos, t => t == "Jira");
    }

    [Fact]
    public void NotaVaziaNaoSugereNada()
    {
        Assert.Empty(Notas.TermosSugeridos(""));
        Assert.Empty(Notas.TermosSugeridos("tudo em minúsculas mesmo"));
    }
}
