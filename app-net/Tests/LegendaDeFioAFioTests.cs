using System.Diagnostics;
using MeetingRecorder.Core;
using MeetingApp.Nucleo;
using Xunit;
using Xunit.Abstractions;

namespace MeetingApp.Tests;

/// <summary>
/// A legenda ao vivo de fio a fio: gravador de verdade, motor de verdade.
/// </summary>
/// <remarks>
/// <para>
/// <b>Este arnês existe porque sete versões seguidas saíram sem funcionar.</b>
/// Cada defeito foi achado numa reunião real do dono do produto, consertado às
/// cegas, publicado, e o seguinte aparecia — a exclusão das chaves, a ordem das
/// guardas, a corrida do arquivo, o header atrasado. Os testes de unidade
/// passavam em todos eles, porque cobriam as peças e nunca o <b>percurso</b>.
/// </para>
/// <para>
/// O que ele faz é o percurso: escreve um WAV com o
/// <see cref="CrashSafeWavWriter"/> de verdade, <b>no ritmo do relógio</b>, e
/// aponta a <see cref="LegendaAoVivo"/> para ele enquanto ele cresce. O
/// gravador, a leitura, o canal, o sidecar e o modelo são os mesmos que rodam
/// na reunião. A única coisa simulada é o microfone.
/// </para>
/// <para>
/// <b>Ele se pula sozinho</b> onde o motor ou o modelo não existem — numa
/// máquina sem GPU, ou em CI. Falhar ali seria reprovar o ambiente, não o
/// código; e um teste que reprova o ambiente é desligado na semana seguinte.
/// </para>
/// </remarks>
public sealed class LegendaDeFioAFioTests
{
    private readonly ITestOutputHelper _saida;

    public LegendaDeFioAFioTests(ITestOutputHelper saida) => _saida = saida;

    /// <summary>Quanto áudio real alimentar, em segundos.</summary>
    /// <summary>
    /// Quanto áudio real alimentar.
    /// </summary>
    /// <remarks>
    /// <b>Quarenta, e o número tem origem.</b> O <c>stable_prefix</c> leva
    /// ~14 s para firmar a primeira palavra — medido em 15/09/2026, e o mesmo
    /// ponto em três durações diferentes. Com 20 s o teste mediria a janela em
    /// que a legenda ainda não firmou nada e chamaria isso de defeito.
    /// </remarks>
    private const int Segundos = 40;

    [Fact]
    public async Task ALegendaTranscreveUmWavQueCresce()
    {
        var motores = MotoresDaMaquina();
        string? ausente = Ambiente(motores);
        if (ausente is not null)
        {
            _saida.WriteLine($"pulado: {ausente}");
            return;
        }

        string pasta = Path.Combine(Path.GetTempPath(), "legenda-" + Path.GetRandomFileName());
        Directory.CreateDirectory(pasta);
        var pedacos = new List<PedacoDaLegenda>();

        try
        {
            // **Do começo da gravação, e não da parte mais falada.** A janela de
            // maior energia é fala contínua sem pausa, e o prefixo estável firma
            // nas pausas: escolhendo por energia, o teste media justamente o pior
            // caso para confirmação e não firmava nada em 20 s.
            float[] fala = AudioDeVerdade(Segundos);
            double rms = Math.Sqrt(fala.Select(v => (double)v * v).Average());
            _saida.WriteLine($"áudio: RMS {rms:F4} pico {fala.Max(Math.Abs):F3}");

            using var legenda = new LegendaAoVivo(
                pasta, motores, Motores.Ambiente(),
                p => { lock (pedacos) pedacos.Add(p); });
            legenda.Comecar();

            // **O gravador, no ritmo do relógio.** Escrever tudo de uma vez
            // esconderia exatamente os defeitos que este arnês existe para pegar:
            // o header que só é reescrito a cada 10 s e o arquivo que ainda não
            // existe quando a legenda sobe.
            await SimularGravacaoAsync(pasta, fala);

            // A legenda tem o que sobrou do áudio para drenar.
            await Task.Delay(TimeSpan.FromSeconds(8));

            string texto;
            lock (pedacos) texto = string.Concat(pedacos.Select(p => p.Novo));
            _saida.WriteLine($"quadros lidos: {legenda.Quadros}");
            _saida.WriteLine($"pedaços recebidos: {pedacos.Count}");
            _saida.WriteLine($"texto firme: {texto}");
            lock (pedacos)
            {
                _saida.WriteLine($"último tentativo: "
                    + $"{pedacos.LastOrDefault(x => x.Tentativo.Length > 0)?.Tentativo}");
                foreach (var x in pedacos.Take(4))
                    _saida.WriteLine($"  novo=[{x.Novo}] tent=[{x.Tentativo}]");
            }

            // **As três afirmações que as sete versões quebraram**, em ordem de
            // profundidade — a primeira que falhar diz onde procurar.
            Assert.True(legenda.Quadros > Segundos * 2,
                $"leu {legenda.Quadros} quadros de ~{Segundos * 5} esperados — "
                + "a leitura não está acompanhando o áudio.");
            Assert.True(pedacos.Count > 0,
                "nenhum pedaço chegou à tela: o motor não devolveu parcial.");

            // **E o firme, que leva ~14 s para começar.** É por isso que o teste
            // alimenta 40 s: com 20 ele reprovaria uma legenda sadia.
            Assert.False(string.IsNullOrWhiteSpace(texto),
                "nada firmou em 40 s de fala — o prefixo estável parou de confirmar.");
        }
        finally
        {
            try { Directory.Delete(pasta, true); } catch (IOException) { }
        }
    }

    /// <summary>
    /// Os motores onde eles de fato estão, sem tocar na pasta do binário.
    /// </summary>
    /// <remarks>
    /// <b>Montar isto à mão em vez de usar o <c>AoLadoDoExecutavel</c></b>: pôr
    /// links para o motor dentro de <c>bin/</c> fez o <c>CatalogoTests</c> ver o
    /// GGUF como "parcial" — o tamanho de um link é o comprimento do caminho, e
    /// 95 bytes não são 750 MB. Um arnês que reprova o teste do vizinho é um
    /// arnês que alguém desliga.
    /// </remarks>
    private static Motores MotoresDaMaquina()
    {
        string raiz = "/mnt/c/Users/andre/AppData/Local/Programs/MeetingApp/motores";
        string venv = Environment.GetEnvironmentVariable("PYTHON_DA_LEGENDA")
                      ?? Path.Combine(Environment.GetFolderPath(
                             Environment.SpecialFolder.UserProfile),
                         ".cache/pulsemeet-medicoes/venv-moss/bin/python");
        return new Motores(venv,
            Path.Combine(raiz, "asr", "motor.py"),
            Path.Combine(raiz, "diarizacao", "motor.py"),
            Path.Combine(raiz, "modelos", "motor.py"))
        {
            ScriptLegenda = Path.Combine(
                Directory.GetCurrentDirectory(), "..", "..", "..", "..", "..",
                "motores", "legenda", "motor.py"),
        };
    }

    /// <summary>O que falta para este teste poder rodar, ou <c>null</c>.</summary>
    private static string? Ambiente(Motores motores)
    {
        if (!File.Exists(motores.Python)) return $"sem Python embarcado em {motores.Python}";
        if (motores.OQueFaltaParaLegenda() is { } falta) return falta;

        string modelo = Path.Combine(Path.GetDirectoryName(motores.ScriptLegenda)!,
                                     "modelos");
        if (!Directory.Exists(modelo) || Directory.GetFiles(modelo, "*.gguf").Length == 0)
            return $"sem GGUF em {modelo}";
        return null;
    }

    /// <summary>
    /// Escreve as duas faixas como o gravador escreve: aos poucos, em tempo real.
    /// </summary>
    private static async Task SimularGravacaoAsync(string pasta, float[] fala)
    {
        using var sistema = new CrashSafeWavWriter(Path.Combine(pasta, "system.wav"));
        using var mic = new CrashSafeWavWriter(Path.Combine(pasta, "mic.wav"));

        int passo = Faixas.TaxaDeAmostragem / 10;          // 100 ms, como a captura
        var relogio = Stopwatch.StartNew();
        for (int i = 0; i < fala.Length; i += passo)
        {
            int n = Math.Min(passo, fala.Length - i);
            sistema.Escrever(fala.AsSpan(i, n));
            // O microfone em silêncio: a fala vem do "outro lado", que é o caso
            // normal — e é o que faz o dono sair como falso.
            mic.Escrever(new float[n]);

            double devido = (i + n) / (double)Faixas.TaxaDeAmostragem;
            double atraso = devido - relogio.Elapsed.TotalSeconds;
            if (atraso > 0) await Task.Delay(TimeSpan.FromSeconds(atraso));
        }
    }

    /// <summary>
    /// Fala de verdade, tirada do acervo — ruído não vira texto.
    /// </summary>
    /// <remarks>
    /// A janela é escolhida <b>pela energia</b>, e não por conveniência: um
    /// recorte silencioso já me fez acusar dois runtimes injustamente
    /// (docs/CONVERGENCIA.md). Sem acervo, o teste cai para um tom — e aí ele
    /// mede o caminho, não a transcrição.
    /// </remarks>
    private static float[] AudioDeVerdade(int segundos)
    {
        string acervo = "/mnt/c/Users/andre/OneDrive/Documents/MeetingRecordings";
        if (Directory.Exists(acervo))
        {
            foreach (string g in Directory.GetDirectories(acervo).OrderByDescending(x => x))
            {
                string wav = Path.Combine(g, "mix.wav");
                if (!File.Exists(wav)) continue;

                var todo = Faixas.LerJanela(wav, 0, -1);
                if (todo.Length < segundos * Faixas.TaxaDeAmostragem * 2) continue;

                int jan = segundos * Faixas.TaxaDeAmostragem;
                return todo[..jan];
            }
        }

        var tom = new float[segundos * Faixas.TaxaDeAmostragem];
        for (int i = 0; i < tom.Length; i++)
            tom[i] = 0.2f * MathF.Sin(2 * MathF.PI * 220 * i / Faixas.TaxaDeAmostragem);
        return tom;
    }
}
