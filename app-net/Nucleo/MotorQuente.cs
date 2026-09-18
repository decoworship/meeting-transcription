using MeetingApp.Nucleo.Atas;

namespace MeetingApp.Nucleo;

/// <summary>Uma conversa aberta com o motor, que responde várias perguntas.</summary>
/// <remarks>
/// A interface existe para a vida do <see cref="MotorQuente"/> ser testável sem
/// placa: a implementação de verdade é um processo, uma porta e 3,2 GB de GGUF.
/// </remarks>
public interface ISessaoDoMotor : IDisposable
{
    Task<string> PerguntarAsync(string prompt, CancellationToken ct);
}

/// <summary>
/// O motor que fica de pé entre perguntas, quando a chave permite.
/// </summary>
/// <remarks>
/// <para>
/// <b>Ligado, ele é o terceiro contexto CUDA residente</b> — a carga que
/// derrubou a legenda de 2,46x para 0,45x em 11/09/2026. Por isso a chave
/// (<see cref="ConfiguracoesDoApp.ModeloQuente"/>) nasce desligada, e por isso
/// esta classe tem três formas de morrer e nenhuma delas depende de alguém
/// lembrar: <see cref="Dispose"/> na parada da gravação e ao desligar a chave,
/// e <see cref="FecharSeOcioso"/> no relógio que já roda.
/// </para>
/// <para>
/// <b>Uma sessão que falhou não é reaproveitada.</b> O <c>llama-server</c> morre
/// por VRAM mais do que gostaríamos, e reusar o processo morto devolveria erro
/// em toda pergunta seguinte, para sempre.
/// </para>
/// </remarks>
public sealed class MotorQuente(
    Func<CancellationToken, Task<ISessaoDoMotor>> abrir, TimeSpan ocioso) : IDisposable
{
    /// <summary>Quanto tempo sem pergunta antes de devolver a placa.</summary>
    /// <remarks>
    /// Dez minutos é o compromisso: curto o bastante para não segurar a placa
    /// uma reunião inteira por causa de uma pergunta no minuto 3, e longo o
    /// bastante para cobrir a pausa entre perguntas de quem está acompanhando.
    /// </remarks>
    public static readonly TimeSpan OciosoPadrao = TimeSpan.FromMinutes(10);

    private readonly SemaphoreSlim _vez = new(1, 1);
    private ISessaoDoMotor? _sessao;

    /// <summary>Há motor de pé agora.</summary>
    public bool Aberto => _sessao is not null;

    /// <summary>Quando foi a última pergunta. Base do <see cref="FecharSeOcioso"/>.</summary>
    public DateTime UltimoUso { get; private set; } = DateTime.UtcNow;

    public async Task<string> PerguntarAsync(string prompt, CancellationToken ct)
    {
        // Uma pergunta por vez: duas gerações no mesmo slot dividiriam o
        // contexto ao meio, e o servidor sobe com `-np 1`.
        await _vez.WaitAsync(ct);
        try
        {
            _sessao ??= await abrir(ct);
            try
            {
                string r = await _sessao.PerguntarAsync(prompt, ct);
                UltimoUso = DateTime.UtcNow;
                return r;
            }
            catch
            {
                Fechar();
                throw;
            }
        }
        finally
        {
            _vez.Release();
        }
    }

    /// <summary>Devolve a placa se ninguém perguntou nada no prazo.</summary>
    public void FecharSeOcioso(DateTime agora)
    {
        if (_sessao is not null && agora - UltimoUso >= ocioso) Fechar();
    }

    public void Dispose() => Fechar();

    private void Fechar()
    {
        var s = _sessao;
        _sessao = null;
        try { s?.Dispose(); } catch { /* o processo já morreu */ }
    }
}
