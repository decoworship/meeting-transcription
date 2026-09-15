using MeetingApp.Nucleo;
using MeetingApp.Sidecar;
using Xunit;

namespace MeetingApp.Tests;

/// <summary>
/// O caminho do MOSS: o sidecar, o recorte em blocos e a costura de falantes.
/// </summary>
/// <remarks>
/// <para>
/// <b>Sem GPU e sem GGUF</b>, e não por comodidade. O que estes testes verificam
/// é aritmética do núcleo — deslocar carimbo, marcar a origem do rótulo, pular
/// bloco mudo, seguir depois de um bloco que falhou — e o motor de verdade só
/// tornaria isso lento e dependente de placa. A qualidade do modelo já foi
/// medida, contra o acervo e contra o Gemini, e está na
/// <c>docs/FASE7-RESULTADOS.md</c> §7; o que não estava guardado em lugar nenhum
/// é a orquestração.
/// </para>
/// <para>
/// O <c>motores/moss/motor.py</c> de verdade aparece aqui num teste só, e num
/// pedaço que não carrega modelo: o protocolo. Ele importa o
/// <c>transcribe_cpp</c> tarde de propósito, então o aperto de mão e o
/// tratamento de erro rodam com o Python pelado.
/// </para>
/// </remarks>
public sealed class MossTests : IDisposable
{
    private readonly string _pasta =
        Directory.CreateTempSubdirectory("moss-testes").FullName;

    public void Dispose() => Directory.Delete(_pasta, recursive: true);

    private static readonly string Falso =
        Path.Combine(AppContext.BaseDirectory, "motor_moss_de_teste.py");

    /// <summary>O motor de verdade, quando este teste roda dentro do repositório.</summary>
    private static string? MotorDeVerdade()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            string t = Path.Combine(dir.FullName, "motores", "moss", "motor.py");
            if (File.Exists(t)) return t;
            dir = dir.Parent;
        }
        return null;
    }

    private static Motores ComMotorFalso() =>
        new("python3", "asr.py", "diarizacao.py", "modelos.py") { ScriptMoss = Falso };

    /// <summary>
    /// O ambiente do motor, com o modo do motor falso dentro.
    /// </summary>
    /// <remarks>
    /// Pelo ambiente e não por argumento: quem monta a linha de comando é o
    /// <see cref="MossEmBlocos"/>, a partir do <c>Motores.ScriptMoss</c>, e o
    /// teste não tem onde enfiar um segundo argumento sem que o núcleo saiba
    /// dele. O ambiente já é o canal por onde o app configura os motores.
    /// </remarks>
    private static Dictionary<string, string> Ambiente(string modo = "feliz")
    {
        var a = new Dictionary<string, string>(Motores.Ambiente())
        {
            ["MOSS_TESTE_MODO"] = modo,
        };
        return a;
    }

    // ────────────────────────────────────────────── o contrato do sidecar

    [Fact]
    public async Task OMotorDeVerdadeApertaAMaoSemCarregarModelo()
    {
        if (MotorDeVerdade() is not { } script) return;   // fora do repositório

        using var m = await MotorSidecar.IniciarAsync("python3", [script]);
        Assert.Equal("moss", m.Nome);
    }

    [Fact]
    public async Task OMotorDeVerdadeRecusaAudioQueNaoExisteESegueVivo()
    {
        if (MotorDeVerdade() is not { } script) return;

        using var m = await MotorSidecar.IniciarAsync("python3", [script]);

        // Erro encerra a REQUISIÇÃO, não o motor (docs/SIDECAR.md) — é o que faz
        // um bloco custar um bloco e não a reunião inteira.
        var e = await Assert.ThrowsAsync<MotorException>(
            () => m.TranscreverESepararAsync(Path.Combine(_pasta, "nao-existe.wav")));
        Assert.Contains("áudio não encontrado", e.Message);

        var outra = await Assert.ThrowsAsync<MotorException>(
            () => m.TranscreverESepararAsync(Path.Combine(_pasta, "nem-esse.wav")));
        Assert.Contains("áudio não encontrado", outra.Message);
    }

    [Fact]
    public async Task OFalanteVemPreenchidoNoMesmoSegmentoQueOTexto()
    {
        // É a razão de existir do motor: os outros dois preenchem um campo cada.
        using var m = await MotorSidecar.IniciarAsync("python3", [Falso]);

        var r = await m.TranscreverESepararAsync("qualquer.wav", 0, 180);

        Assert.Equal(2, r.Segmentos.Count);
        Assert.Equal(" bloco 0 um", r.Segmentos[0].Texto);
        Assert.Equal("S0", r.Segmentos[0].Falante);
        Assert.Equal("cuda", r.Dispositivo);
    }

    [Fact]
    public async Task CancelarMataOProcessoEDevolveAPlaca()
    {
        // Não há op de cancelamento e não deve haver: dentro de uma inferência o
        // motor não está num ponto em que possa cooperar. Matar libera a VRAM na
        // hora — critério B da Fase 2.
        using var m = await MotorSidecar.IniciarAsync("python3", [Falso], default, Ambiente("demorado"));
        using var cts = new CancellationTokenSource();

        var tarefa = m.TranscreverESepararAsync("qualquer.wav", 0, 180, null, cts.Token);
        cts.CancelAfter(50);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => tarefa);
    }

    // ─────────────────────────────────────────────── o recorte em blocos

    /// <summary>Uma gravação com <paramref name="segundos"/> de fala nas duas faixas.</summary>
    private string Gravacao(double segundos, double mudoDe = -1, double mudoAte = -1)
    {
        int n = (int)(segundos * Faixas.TaxaDeAmostragem);
        var mic = new float[n];
        var sistema = new float[n];
        for (int i = 0; i < n; i++)
        {
            double t = i / (double)Faixas.TaxaDeAmostragem;
            // Amplitude bem acima do LimiarDeFala, senão o portão de RMS pula
            // tudo e o teste mede o portão em vez do recorte.
            float v = t >= mudoDe && t < mudoAte ? 0f
                    : (float)(0.3 * Math.Sin(2 * Math.PI * 220 * t));
            sistema[i] = v;
        }
        Faixas.Escrever(Path.Combine(_pasta, "mic.wav"), mic);
        Faixas.Escrever(Path.Combine(_pasta, "system.wav"), sistema);
        return _pasta;
    }

    private static Faixas Ler(string pasta) =>
        Faixas.Ler(Path.Combine(pasta, "mic.wav"), Path.Combine(pasta, "system.wav"));

    [Fact]
    public async Task OsCarimbosSaoDeslocadosParaALinhaDoTempoDaReuniao()
    {
        // O modelo devolve carimbos relativos à janela. Sem o deslocamento, a
        // reunião inteira sairia com os três primeiros minutos por cima de si
        // mesma — e nada abaixo perceberia.
        string pasta = Gravacao(400);
        var r = await MossEmBlocos.TranscreverAsync(
            pasta, Ler(pasta), ComMotorFalso(), Ambiente(), permitirCpu: false);

        Assert.Equal(3, r.Blocos);                    // 400 s ÷ 180 = 2 cheios + 1 curto
        Assert.Equal(6, r.Segmentos.Count);

        Assert.Equal(0.5, r.Segmentos[0].Start, 3);
        Assert.Equal(180.5, r.Segmentos[2].Start, 3);
        Assert.Equal(360.5, r.Segmentos[4].Start, 3);
    }

    [Fact]
    public async Task ORotuloCarregaOBlocoDeOndeVeio()
    {
        // O S0 do bloco 3 não é o S0 do bloco 7, e sem a marca de origem
        // qualquer etapa adiante — inclusive uma régua de medição — fingiria que
        // são a mesma pessoa.
        string pasta = Gravacao(400);
        var r = await MossEmBlocos.TranscreverAsync(
            pasta, Ler(pasta), ComMotorFalso(), Ambiente(), permitirCpu: false);

        Assert.Equal("b0_S0", r.Segmentos[0].Speaker);
        Assert.Equal("b1_S0", r.Segmentos[2].Speaker);
        Assert.Equal(6, r.Segmentos.Select(s => s.Speaker).Distinct().Count());
    }

    [Fact]
    public async Task BlocoMudoNaoChegaAoModelo()
    {
        // **O portão de RMS não é economia.** Bloco mudo faz o MOSS alucinar em
        // chinês: sem áudio para transcrever ele completa o prompt embutido no
        // GGUF. Apareceu em 1 bloco de 579 no acervo — raro, silencioso, e o
        // portão custa uma raiz quadrada.
        string pasta = Gravacao(360, mudoDe: 180, mudoAte: 360);
        var r = await MossEmBlocos.TranscreverAsync(
            pasta, Ler(pasta), ComMotorFalso(), Ambiente(), permitirCpu: false);

        Assert.Equal(2, r.Blocos);
        Assert.Equal(1, r.Mudos);
        Assert.Equal(2, r.Segmentos.Count);           // só o primeiro bloco falou
    }

    [Fact]
    public async Task UmBlocoQueFalhaCustaUmBlocoENaoAReuniao()
    {
        string pasta = Gravacao(400);
        var r = await MossEmBlocos.TranscreverAsync(
            pasta, Ler(pasta), ComMotorFalso(), Ambiente("erra-o-segundo"),
            permitirCpu: false);

        Assert.Equal(1, r.Falhados);
        Assert.Equal(4, r.Segmentos.Count);           // os outros dois blocos vieram
    }

    [Fact]
    public async Task CairParaCpuParaNoPrimeiroBloco()
    {
        // Rodar em CPU não é um modo do app: é o que acontece quando o backend
        // nativo não subiu. Descobrir isso no primeiro bloco custa 3 minutos de
        // áudio; descobrir no fim custa a tarde.
        string pasta = Gravacao(400);
        var e = await Assert.ThrowsAsync<MotorException>(() =>
            MossEmBlocos.TranscreverAsync(pasta, Ler(pasta), ComMotorFalso(),
                                          Ambiente("cpu"), permitirCpu: false));

        Assert.Contains("Transcrever sem placa", e.Message);
    }

    [Fact]
    public async Task ComAChaveLigadaACpuPassa()
    {
        string pasta = Gravacao(200);
        var r = await MossEmBlocos.TranscreverAsync(
            pasta, Ler(pasta), ComMotorFalso(), Ambiente("cpu"), permitirCpu: true);

        Assert.NotEmpty(r.Segmentos);
    }

    [Fact]
    public void SemOMotorNoDiscoADiferencaEDita()
    {
        // Ele é o único motor opcional, e por isso não entra no OQueFalta: pôr
        // um arquivo que só existe para quem ligou a chave na porta de toda
        // transcrição faria o app recusar-se a transcrever sem nada estar errado.
        var sem = new Motores("python3", "asr.py", "diarizacao.py", "modelos.py");

        Assert.Contains("motor MOSS não está em", sem.OQueFaltaParaMoss());
        Assert.Contains("volta ao motor clássico", sem.OQueFaltaParaMoss());
    }
}
