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
[Collection("marcador de etapa")]
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

    [Fact]
    public void PlacaVistaPeloWindowsMasNaoPeloMotorEDefeito()
    {
        // O caso relatado em 18/08/2026: o bloco de diagnóstico dizia
        // "RTX 4050" e a transcrição rodava na CPU até derrubar o Windows.
        //
        // As duas causas pedem coisas diferentes de quem lê, e juntá-las numa
        // frase só mandaria metade das pessoas fazer a coisa errada.
        string frase = Transcritor.SemPlaca(new MeetingApp.Sidecar.DispositivoDoMotor(
            Cuda: false, Nome: null, CudaDoTorch: "12.4",
            Motivo: "o torch tem CUDA 12.4, mas não encontrou placa nenhuma"));

        // Em máquina sem placa (o CI, e o Linux desta suíte) a frase é a outra —
        // o que se protege aqui é que ela sempre diga a saída.
        Assert.Contains("Ajustes", frase);
        Assert.Contains("CPU", frase);
        Assert.Contains("12.4", frase);
    }

    [Fact]
    public void SemPlacaNenhumaAFraseNaoAcusaDefeito()
    {
        // Quem de fato não tem placa não precisa mandar diagnóstico para
        // ninguém: precisa saber que dá para ligar, e o que custa.
        string frase = Transcritor.SemPlaca(new MeetingApp.Sidecar.DispositivoDoMotor(
            Cuda: false, Nome: null, CudaDoTorch: null, Motivo: null));

        Assert.Contains("Transcrever sem placa", frase);
        Assert.DoesNotContain("Isso é um defeito", frase);
    }

    [Fact]
    public void TranscreverSemPlacaEDesligadoPorPadrao()
    {
        // A chave existe para tornar a escolha deliberada. Ligada por padrão,
        // ela não protegeria de nada — que era exatamente o estado anterior.
        Assert.False(new ConfiguracoesDoApp().PermitirCpu);
    }

    [Fact]
    public void MotorDeDiarizacaoPadraoEOOnnx()
    {
        // O torch saiu do empacotamento em 22/09/2026 (docs/DIARIZACAO-ONNX.md):
        // as réguas fecharam com acordo 1,0000 contra o pyannote, e carregar
        // 3,6 GB para não usá-los não se defende mais. Quem não mexeu no
        // app.json cai no onnx, não no torch de antes.
        Assert.Equal("onnx", new ConfiguracoesDoApp().MotorDeDiarizacao);
    }

    [Fact]
    public void AppJsonComTorchCarregaSemLevantarErro()
    {
        // O cenário real: alguém experimentou o porte, o app.json ficou com
        // "torch" escrito, e o torch saiu do empacotamento em 22/09/2026. A
        // chave é editável à mão, e um valor que já existiu não pode impedir
        // ninguém de abrir o app nem de separar falantes numa reunião que já
        // aconteceu — quem de fato recusa "torch" e cai no onnx é o motor
        // Python (escolher_motor em motores/diarizacao/motor.py), não este
        // lado; aqui só se garante que a leitura do arquivo não lança e que o
        // valor atravessa intacto até a chamada ao sidecar.
        string caminho = Path.Combine(_pasta, "app.json");
        File.WriteAllText(caminho, "{ \"motor_de_diarizacao\": \"torch\" }");

        var cfg = ConfiguracoesDoApp.Carregar(caminho);

        Assert.Equal("torch", cfg.MotorDeDiarizacao);
    }

    [Fact]
    public void ATelemetriaDoPyannoteSaiDesligadaEmTodoSidecar()
    {
        // A chave NÃO tem valor padrão do lado do pyannote: sem ela o
        // is_metrics_enabled levanta ValueError e o motor morre ao carregar o
        // pipeline. Então esta não é uma preferência — é obrigação, e é por isso
        // que ela mora no Ambiente() e não em cada chamador.
        //
        // O valor é "false" porque a promessa do app é que a reunião não sai da
        // máquina, e o pyannote 4.x exporta um span para otel.pyannote.ai a cada
        // carga de pipeline. Ver Motores.Ambiente().
        Assert.Equal("false",
            MeetingApp.Nucleo.Motores.Ambiente()["PYANNOTE_METRICS_ENABLED"]);
    }

    [Fact]
    public void NenhumSegredoDoHuggingFaceVaiEmbutidoNoBinario()
    {
        // A régua da Fase 4, em código e não só no publicar.sh: o .exe é
        // entregue a outras pessoas, e um token embutido vai junto para a
        // máquina delas. O recurso se chamava "MeetingApp.hf_token.txt" e
        // reintroduzi-lo é uma linha de .csproj — barato de fazer sem querer,
        // e invisível até alguém rodar strings.
        var recursos = typeof(MeetingApp.Nucleo.Motores).Assembly
            .GetManifestResourceNames();

        Assert.DoesNotContain(recursos, r => r.Contains("hf_token"));
    }
}
