using MeetingApp.Nucleo;
using Xunit;

namespace MeetingApp.Tests;

/// <summary>
/// A lista de pipelines de diarização que existem em disco (FASE6 §4.6).
/// </summary>
/// <remarks>
/// Ela existe para o seletor da tela e o motor não terem como discordar: até
/// 20/08/2026 a tela oferecia a escolha a partir do catálogo — que perdeu a
/// família <c>diarizacao</c> na Fase 4 — e o valor escolhido era ignorado pelo
/// pipeline, que pedia o <c>community-1</c> pelo nome.
/// </remarks>
public sealed class ModelosDeDiarizacaoTests : IDisposable
{
    private readonly string _raiz =
        Directory.CreateTempSubdirectory("diarizadores").FullName;

    public void Dispose() => Directory.Delete(_raiz, recursive: true);

    /// <summary>Motores apontando para um motor de diarização dentro da pasta temporária.</summary>
    private Motores Motores()
    {
        string pasta = Path.Combine(_raiz, "diarizacao");
        Directory.CreateDirectory(pasta);
        return new Motores("python", "asr.py", Path.Combine(pasta, "motor.py"), "modelos.py");
    }

    private void Empacotar(string nome, bool comConfig = true)
    {
        string pasta = Path.Combine(_raiz, "diarizacao", "modelos", nome);
        Directory.CreateDirectory(pasta);
        if (comConfig) File.WriteAllText(Path.Combine(pasta, "config.yaml"), "version: 1\n");
    }

    [Fact]
    public void SemPastaDeModelosAListaEVazia()
    {
        // O empacotador ainda não rodou: o motor cai no HuggingFace e a tela não
        // oferece escolha nenhuma, que é a verdade.
        Assert.Empty(Motores().ModelosDeDiarizacao());
    }

    [Fact]
    public void ListaOQueTemConfigYaml()
    {
        var motores = Motores();
        Empacotar("community-1");

        Assert.Equal(["community-1"], motores.ModelosDeDiarizacao());
    }

    [Fact]
    public void PastaSemConfigNaoConta()
    {
        // O mesmo critério do motor. Uma pasta pela metade — download
        // interrompido, empacotador morto no meio — não é um modelo carregável,
        // e oferecê-la seria oferecer uma transcrição que falha no fim.
        var motores = Motores();
        Empacotar("community-1");
        Empacotar("pela-metade", comConfig: false);

        Assert.Equal(["community-1"], motores.ModelosDeDiarizacao());
    }

    [Fact]
    public void OrdemEstavel()
    {
        // A lista vira as opções de um select; ordem do sistema de arquivos
        // faria o seletor trocar de ordem entre máquinas.
        var motores = Motores();
        Empacotar("pyannote-3.1");
        Empacotar("community-1");

        Assert.Equal(["community-1", "pyannote-3.1"], motores.ModelosDeDiarizacao());
    }
}
