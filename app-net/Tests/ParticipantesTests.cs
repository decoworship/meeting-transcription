using MeetingApp.Nucleo;
using Xunit;

namespace MeetingApp.Tests;

/// <summary>
/// Quem estava na reunião, confirmado pela pessoa a partir de um print (UI-12).
/// </summary>
public sealed class ParticipantesTests : IDisposable
{
    private readonly string _pasta =
        Directory.CreateTempSubdirectory("participantes-testes").FullName;

    public void Dispose() => Directory.Delete(_pasta, recursive: true);

    [Fact]
    public void SemArquivoNaoHaNinguem()
    {
        Assert.Empty(Participantes.Ler(_pasta));
    }

    [Fact]
    public void OQueSeJuntaVoltaNaOrdem()
    {
        Participantes.Juntar(_pasta, ["Helena Dias", "Rui Matos"]);

        Assert.Equal(["Helena Dias", "Rui Matos"], Participantes.Ler(_pasta));
    }

    [Fact]
    public void JuntarDeOutroPrintAcrescentaSemRepetir()
    {
        // Dois prints da mesma reunião — a galeria e a lista de pessoas — têm
        // quase os mesmos nomes, às vezes com outra grafia.
        Participantes.Juntar(_pasta, ["Helena Dias", "João Pereira"]);
        Participantes.Juntar(_pasta, ["HELENA DIAS", "Joao Pereira", "Clara"]);

        Assert.Equal(["Helena Dias", "João Pereira", "Clara"], Participantes.Ler(_pasta));
    }

    [Fact]
    public void NomeVazioNaoEntra()
    {
        Participantes.Juntar(_pasta, ["  ", "", "Rui Matos "]);

        Assert.Equal(["Rui Matos"], Participantes.Ler(_pasta));
    }

    [Fact]
    public void ArquivoIlegivelNaoImpedeNada()
    {
        File.WriteAllText(Participantes.Caminho(_pasta), "{ isto não é json");

        Assert.Empty(Participantes.Ler(_pasta));
        Participantes.Juntar(_pasta, ["Rui Matos"]);
        Assert.Equal(["Rui Matos"], Participantes.Ler(_pasta));
    }

    [Fact]
    public void AsSugestoesSaoOPrintPrimeiroEDepoisAAgenda()
    {
        // O print diz quem entrou; a agenda, quem foi chamado. Para batizar um
        // falante, quem entrou vem antes.
        File.WriteAllText(Path.Combine(_pasta, "meta.json"),
            """{"meeting": {"attendees": ["Ana Lima", "Helena Dias"]}}""");
        Participantes.Juntar(_pasta, ["Helena Dias", "Rui Matos"]);

        Assert.Equal(["Helena Dias", "Rui Matos", "Ana Lima"], Participantes.Sugestoes(_pasta));
    }

    [Fact]
    public void NaoDeixaTemporarioParaTras()
    {
        Participantes.Juntar(_pasta, ["Rui Matos"]);
        Participantes.Juntar(_pasta, ["Clara"]);

        Assert.Equal(["participantes.json"],
            Directory.GetFiles(_pasta).Select(f => Path.GetFileName(f)!));
    }
}
