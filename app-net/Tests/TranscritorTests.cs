using MeetingApp.Nucleo;
using MeetingApp.Sidecar;
using Xunit;

namespace MeetingApp.Tests;

/// <summary>
/// O que o pipeline faz antes de gastar um motor: checar que dá para rodar.
/// </summary>
/// <remarks>
/// A checagem existe porque a alternativa é o processo morrer no spawn e a UI
/// dizer "o motor morreu" — mensagem que manda procurar no lugar errado. É o
/// critério C da Fase 2 aplicado à causa mais provável de falha na máquina de
/// quem instala: os motores não estão lá.
/// </remarks>
public sealed class TranscritorTests : IDisposable
{
    private readonly string _pasta =
        Directory.CreateTempSubdirectory("transcritor-testes").FullName;

    public void Dispose() => Directory.Delete(_pasta, recursive: true);

    private Motores Motores(bool python = false, bool asr = false, bool diar = false)
    {
        string p = Path.Combine(_pasta, "python.exe");
        string a = Path.Combine(_pasta, "asr.py");
        string d = Path.Combine(_pasta, "diarizacao.py");
        if (python) File.WriteAllText(p, "");
        if (asr) File.WriteAllText(a, "");
        if (diar) File.WriteAllText(d, "");
        // O motor de modelos não entra no OQueFalta: ele só é exigido para
        // baixar, e faltar não pode impedir uma transcrição com o modelo que
        // já está no disco.
        return new Motores(p, a, d, Path.Combine(_pasta, "modelos.py"));
    }

    [Fact]
    public void SemPythonDizQualCaminhoFoiProcurado()
    {
        // O caminho na mensagem é o que transforma "não funciona" em algo
        // acionável: quem lê descobre onde o app esperava encontrar o motor.
        string? falta = Motores().OQueFalta();
        Assert.Contains("Python dos motores não está em", falta);
        Assert.Contains(_pasta, falta);
    }

    [Fact]
    public void ComPythonMasSemOsScriptsApontaOScriptQueFalta()
    {
        Assert.Contains("motor de transcrição", Motores(python: true).OQueFalta());
        Assert.Contains("motor de diarização",
                        Motores(python: true, asr: true).OQueFalta());
    }

    [Fact]
    public void ComTudoNoLugarNaoReclama()
    {
        Assert.Null(Motores(python: true, asr: true, diar: true).OQueFalta());
    }

    [Fact]
    public async Task MotorAusenteViraErroLegivelEmVezDeProcessoMorto()
    {
        var t = new Transcritor(Motores());
        var e = await Assert.ThrowsAsync<MotorException>(
            () => t.ExecutarAsync(_pasta));

        Assert.Contains("Python dos motores", e.Message);
    }

    [Fact]
    public async Task GravacaoSemAsFaixasDizQualArquivoFalta()
    {
        var t = new Transcritor(Motores(python: true, asr: true, diar: true));
        var e = await Assert.ThrowsAsync<MotorException>(
            () => t.ExecutarAsync(_pasta));

        Assert.Contains("mic.wav", e.Message);
    }
}
