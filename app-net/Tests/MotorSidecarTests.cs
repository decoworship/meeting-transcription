using System.Diagnostics;
using MeetingApp.Sidecar;
using Xunit;

namespace MeetingApp.Tests;

/// <summary>
/// O contrato do sidecar (docs/SIDECAR.md), exercitado contra um motor falso.
/// </summary>
/// <remarks>
/// Motor falso e não pyannote de propósito: o que estes testes verificam é o
/// protocolo e o ciclo de vida do processo, e amarrá-los a um modelo de 2 GB
/// com token do HuggingFace trocaria uma verificação rápida e determinística
/// por uma lenta que falha por motivos alheios. O motor real se valida por CLI,
/// contra áudio de verdade.
/// </remarks>
public sealed class MotorSidecarTests
{
    private static readonly string Script =
        Path.Combine(AppContext.BaseDirectory, "motor_de_teste.py");

    private static Task<MotorSidecar> Subir(string modo = "feliz") =>
        MotorSidecar.IniciarAsync("python3", [Script, modo]);

    [Fact]
    public async Task HandshakeIdentificaOMotor()
    {
        using var m = await Subir();
        Assert.Equal("teste", m.Nome);
        Assert.Equal("1", m.Versao);
    }

    [Fact]
    public async Task DiarizacaoDevolveOsSegmentosEOProgresso()
    {
        using var m = await Subir();
        var avisos = new List<(double, string)>();

        var segs = await m.DiarizarAsync("qualquer.wav", (p, t) => avisos.Add((p, t)));

        Assert.Equal(2, segs.Count);
        Assert.Equal(new SegmentoDeFalante(0.5, 3.25, "SPEAKER_00"), segs[0]);
        // Rótulo cru: nomear falante é apresentação, e vive no núcleo.
        Assert.Equal("SPEAKER_01", segs[1].Falante);
        Assert.Equal(("analisando falantes"), avisos.Single().Item2);
    }

    [Fact]
    public async Task TranscricaoDevolveTextoIdiomaEDuracao()
    {
        using var m = await Subir();

        var t = await m.TranscreverAsync("qualquer.wav", vocabulario: "Acme, Elio");

        Assert.Equal("pt", t.Idioma);
        Assert.Equal(7.0, t.Duracao);
        // O texto sai como o motor o produziu, com o espaço à esquerda que o
        // whisper põe: aparar é decisão de apresentação, e o app atual apara na
        // hora de formatar, não na de guardar.
        Assert.Equal(" bom dia a todos", t.Segmentos[0].Texto);
    }

    [Fact]
    public async Task MotorQueNaoSobeDizPorQue()
    {
        // "Não consegui subir o motor" e "o motor falhou nesta gravação" pedem
        // reações diferentes, então a distinção tem que sobreviver à mensagem.
        var e = await Assert.ThrowsAsync<MotorException>(() => Subir("morre-no-handshake"));
        Assert.Contains("antes de dizer que estava pronto", e.Message);
        Assert.Contains("faltou o modelo", e.Message);   // a cauda do stderr
    }

    [Fact]
    public async Task ErroEncerraARequisicaoENaoOMotor()
    {
        using var m = await Subir("erro");

        var e = await Assert.ThrowsAsync<MotorException>(() => m.DiarizarAsync("x.wav"));
        Assert.Equal("não foi possível ler o áudio", e.Message);

        // O processo continua vivo e atende a próxima — é o que separa "esta
        // gravação falhou" de "o motor caiu".
        var outro = await Assert.ThrowsAsync<MotorException>(() => m.DiarizarAsync("y.wav"));
        Assert.Equal("não foi possível ler o áudio", outro.Message);
    }

    [Fact]
    public async Task MotorQueMorreNoMeioViraErroLegivel()
    {
        // Critério C da Fase 2: a UI mostra o erro e o app continua vivo.
        using var m = await Subir("morre-no-meio");
        var e = await Assert.ThrowsAsync<MotorException>(() => m.DiarizarAsync("x.wav"));
        // A operação aparece na mensagem: com dois motores no pipeline, "o motor
        // morreu" sem dizer o quê fazia manda procurar no lugar errado.
        Assert.Contains("morreu durante a operação 'diarizar'", e.Message);
    }

    [Fact]
    public async Task LixoNoStdoutApontaParaACausa()
    {
        // O modo de falha mais provável do motor real: uma biblioteca escrevendo
        // no canal do protocolo. Sem esta mensagem, o sintoma é JSON inválido em
        // ponto imprevisível e a causa não aparece em lugar nenhum.
        var e = await Assert.ThrowsAsync<MotorException>(() => Subir("lixo-no-stdout"));
        Assert.Contains("lixo no canal do protocolo", e.Message);
        Assert.Contains("Downloading model", e.Message);
    }

    [Fact]
    public async Task CancelarMataOProcesso()
    {
        // Critério B da Fase 2: cancelar libera a GPU em ≤2 s. Não há operação
        // de cancelamento no protocolo — dentro da inferência o motor não teria
        // como cooperar —, então cancelar é matar.
        using var m = await Subir("demorado");
        using var cts = new CancellationTokenSource();

        var relogio = Stopwatch.StartNew();
        var tarefa = m.DiarizarAsync("x.wav", null, cts.Token);
        cts.CancelAfter(TimeSpan.FromMilliseconds(200));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => tarefa);
        Assert.True(relogio.Elapsed < TimeSpan.FromSeconds(2),
            $"cancelamento levou {relogio.Elapsed.TotalSeconds:F1} s");
    }
}
