using System.Threading.Channels;
using MeetingApp.Sidecar;

namespace MeetingApp.Nucleo;

/// <summary>
/// Quem está falando, durante a reunião — para pôr o nome na legenda
/// (30/09/2026, <b>em teste</b>, atrás da chave <c>falantes_ao_vivo</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>Roda ao lado da <see cref="LegendaAoVivo"/>, não dentro dela.</b> São dois
/// sidecars: a legenda recebe o mix, e este recebe só o <c>system.wav</c> — os
/// outros —, porque a sua fala já é decidida pela faixa do microfone, e dá-la ao
/// diarizador o faria tentar separar você de você. É a mesma regra da passada
/// final.
/// </para>
/// <para>
/// <b>O nome sai do mesmo lugar que na passada final.</b> O sidecar manda o
/// vetor de voz de cada vaga do Nemotron quando ela junta fala limpa, e o
/// <see cref="Vozes.Reconhecer"/> decide — com o mesmo banco, o mesmo limiar e as
/// mesmas gerações. Quem não é reconhecido aparece como "Pessoa N", na ordem em
/// que falou.
/// </para>
/// <para>
/// <b>O custo medido é o do <c>MOD-1</c></b>: o Nemotron em <c>low_latency</c>
/// ao lado da legenda leva a legenda de 3,17x a 2,90x e soma ~1,5 GB de VRAM —
/// sem o Meet (docs/NEMOTRON-DIARIZACAO.md §4b). Com o Meet e a caixa de
/// perguntar, é o que este teste existe para descobrir.
/// </para>
/// </remarks>
public sealed class FalantesAoVivo : IDisposable
{
    /// <summary>A pasta de modelo que ele exige.</summary>
    public const string Modelo = "nemotron-3";

    /// <summary>Quanto de fala se guarda para responder "quem falou".</summary>
    /// <remarks>
    /// A legenda pergunta sobre o último segundo; dez minutos é folga de sobra e
    /// não deixa a lista crescer pela reunião inteira.
    /// </remarks>
    private const long MemoriaMs = 10 * 60 * 1000;

    private readonly string _pasta;
    private readonly Motores _motores;
    private readonly IReadOnlyDictionary<string, string> _ambiente;
    private readonly Vozes _vozes;
    private readonly CancellationTokenSource _cancelar = new();
    private readonly object _trava = new();
    private readonly List<MotorSidecar.FalaAoVivo> _falas = [];
    private readonly Dictionary<int, string> _nomes = [];
    private readonly Dictionary<int, int> _ordem = [];
    private Task? _laco;

    public FalantesAoVivo(string pastaDaGravacao, Motores motores,
                          IReadOnlyDictionary<string, string> ambiente, Vozes? vozes = null)
    {
        _pasta = pastaDaGravacao;
        _motores = motores;
        _ambiente = ambiente;
        _vozes = vozes ?? new Vozes();
    }

    /// <summary>Por que não liga, ou nulo.</summary>
    public static string? OQueImpede(Motores motores, ConfiguracoesDoApp config)
    {
        if (!config.FalantesAoVivo) return "os nomes ao vivo estão desligados em Ajustes.";
        if (!config.LegendaAoVivo) return "os nomes ao vivo precisam da legenda ao vivo ligada.";
        if (!motores.ModelosDeDiarizacao().Contains(Modelo))
            return $"o modelo {Modelo} não está instalado.";
        return null;
    }

    public void Comecar() => _laco ??= Task.Run(() => LacoAsync(_cancelar.Token));

    /// <summary>
    /// Quem falou mais entre <paramref name="deS"/> e <paramref name="ateS"/>:
    /// o nome, "Pessoa N", ou nulo quando o Nemotron ainda não decidiu esse
    /// trecho — ele anda ~1 s atrás do áudio.
    /// </summary>
    public string? QuemFalou(double deS, double ateS)
    {
        long de = (long)(deS * 1000), ate = (long)(ateS * 1000);
        lock (_trava)
        {
            var soma = new Dictionary<int, long>();
            foreach (var f in _falas)
            {
                long sobre = Math.Min(f.FimMs, ate) - Math.Max(f.InicioMs, de);
                if (sobre > 0) soma[f.Vaga] = soma.GetValueOrDefault(f.Vaga) + sobre;
            }
            if (soma.Count == 0) return null;
            int vaga = soma.MaxBy(kv => kv.Value).Key;
            return RotuloDe(vaga);
        }
    }

    /// <summary>O nome reconhecido da vaga, ou "Pessoa N" pela ordem de fala.</summary>
    private string RotuloDe(int vaga) =>
        _nomes.TryGetValue(vaga, out var nome) ? nome
        : $"Pessoa {(_ordem.TryGetValue(vaga, out int n) ? n : _ordem.Count + 1)}";

    /// <summary>Regra pura, e por isso estática: guarda as falas e numera as vagas.</summary>
    public static void Acrescentar(List<MotorSidecar.FalaAoVivo> falas, Dictionary<int, int> ordem,
                                     IReadOnlyList<MotorSidecar.FalaAoVivo> novas, long memoriaMs)
    {
        foreach (var f in novas)
        {
            falas.Add(f);
            if (!ordem.ContainsKey(f.Vaga)) ordem[f.Vaga] = ordem.Count + 1;
        }
        if (falas.Count > 0)
        {
            long corte = falas[^1].FimMs - memoriaMs;
            falas.RemoveAll(f => f.FimMs < corte);
        }
    }

    private void AoFalar(IReadOnlyList<MotorSidecar.FalaAoVivo> novas)
    {
        lock (_trava) Acrescentar(_falas, _ordem, novas, MemoriaMs);
    }

    private void AoVetor(int vaga, float[] vetor, string? modelo)
    {
        var quem = _vozes.Reconhecer(vetor, modelo);
        lock (_trava)
        {
            if (quem is { } q) _nomes[vaga] = q.Pessoa;
            else _nomes.Remove(vaga);   // um segundo palpite pode desfazer o primeiro
        }
        Registro.Escrever("falantes-ao-vivo", quem is { } r
            ? $"vaga {vaga}: {r.Pessoa} ({r.Semelhanca:F2})"
            : $"vaga {vaga}: ninguém conhecido");
    }

    private async Task LacoAsync(CancellationToken ct)
    {
        string sistema = Path.Combine(_pasta, "system.wav");
        var canal = Channel.CreateUnbounded<float[]>(
            new UnboundedChannelOptions { SingleReader = true, SingleWriter = true });
        MotorSidecar? motor = null;
        try
        {
            motor = await MotorSidecar.IniciarAsync(
                _motores.Python, [_motores.ScriptDiarizacao], ct, _ambiente);
            motor.AoRegistrar += l => Registro.Escrever("falantes-ao-vivo", l);
            Registro.Escrever("falantes-ao-vivo", $"ligados em {Path.GetFileName(_pasta)}");

            var lendo = Task.Run(() => LerAsync(canal.Writer, sistema, ct), ct);
            await motor.FalantesAoVivoAsync(canal.Reader, AoFalar, AoVetor, Modelo, ct);
            await lendo;
        }
        catch (OperationCanceledException) { }
        catch (Exception e)
        {
            // Os nomes morrem; a legenda e a reunião, não.
            Registro.Escrever("falantes-ao-vivo", $"pararam: {e.Message}");
        }
        finally
        {
            canal.Writer.TryComplete();
            motor?.Dispose();
        }
    }

    /// <summary>O <c>system.wav</c> em quadros, como a legenda lê o mix.</summary>
    /// <remarks>
    /// As mesmas três regras de <see cref="LegendaAoVivo"/>: posição em amostras,
    /// leitura viva, e o que está em disco manda — nunca o relógio de parede.
    /// </remarks>
    private static async Task LerAsync(ChannelWriter<float[]> canal, string sistema,
                                       CancellationToken ct)
    {
        try
        {
            long lidas = 0;
            int porQuadro = (int)(LegendaAoVivo.QuadroS * Faixas.TaxaDeAmostragem);
            while (!ct.IsCancellationRequested)
            {
                double de = lidas / (double)Faixas.TaxaDeAmostragem;
                double ate = (lidas + porQuadro) / (double)Faixas.TaxaDeAmostragem;
                if (LegendaAoVivo.PrecisaEsperar(File.Exists(sistema),
                        LegendaAoVivo.SegundosEmDisco(sistema), ate))
                {
                    await Task.Delay(TimeSpan.FromSeconds(LegendaAoVivo.QuadroS), ct);
                    continue;
                }
                var quadro = Faixas.LerJanelaViva(sistema, de, ate);
                if (quadro.Length == 0)
                {
                    await Task.Delay(TimeSpan.FromSeconds(LegendaAoVivo.QuadroS), ct);
                    continue;
                }
                lidas += quadro.Length;
                await canal.WriteAsync(quadro, ct);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception e)
        {
            Registro.Escrever("falantes-ao-vivo", $"a leitura parou: {e.Message}");
        }
        finally
        {
            canal.TryComplete();
        }
    }

    public void Dispose()
    {
        try { _cancelar.Cancel(); } catch (ObjectDisposedException) { }
        _cancelar.Dispose();
    }
}
