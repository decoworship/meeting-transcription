namespace MeetingRecorder.Core;

/// <summary>Estado de saúde do disco durante a gravação.</summary>
public enum EstadoDisco { Ok, Aviso, Critico }

/// <summary>
/// Vigia o espaço em disco antes e durante a gravação.
/// </summary>
/// <remarks>
/// <para>
/// Requisito 3.3 e critério de aceite D. O gravador Python não checa nada: se o
/// disco enche no meio da reunião, a escrita falha, a exceção morre dentro da
/// thread e a gravação continua "rodando" sem gravar nada. O ícone segue
/// vermelho e o usuário só descobre depois.
/// </para>
/// <para>
/// A regra aqui é <b>degradar visivelmente, nunca morrer em silêncio</b>: falta
/// de espaço promove o ícone a aviso e o que já foi gravado permanece — o writer
/// mantém o header válido a cada 10 s justamente para isso.
/// </para>
/// <para>
/// Os limiares são em <b>minutos de gravação restantes</b>, não em bytes: 500 MB
/// livres não dizem nada a quem está numa reunião, "13 minutos" diz.
/// </para>
/// </remarks>
public sealed class GuardaDeDisco(long bytesPorSegundo = CrashSafeWavWriter.TaxaAlvo * 2 * 2)
{
    /// <summary>Abaixo disto, avisa. Uma reunião típica passa disso com folga.</summary>
    public static readonly TimeSpan LimiteAviso = TimeSpan.FromMinutes(15);

    /// <summary>Abaixo disto, o fim é iminente e a mensagem muda de tom.</summary>
    public static readonly TimeSpan LimiteCritico = TimeSpan.FromMinutes(3);

    public EstadoDisco Estado { get; private set; } = EstadoDisco.Ok;
    public TimeSpan TempoRestante { get; private set; } = TimeSpan.MaxValue;
    public string? Mensagem { get; private set; }

    /// <summary>bytes/s das duas faixas somadas (16 kHz × 2 bytes × 2 faixas).</summary>
    public long BytesPorSegundo => bytesPorSegundo;

    /// <summary>Reavalia com o espaço livre atual.</summary>
    public EstadoDisco Avaliar(long bytesLivres)
    {
        TempoRestante = TimeSpan.FromSeconds(bytesLivres / (double)bytesPorSegundo);

        Estado = TempoRestante <= LimiteCritico ? EstadoDisco.Critico
               : TempoRestante <= LimiteAviso ? EstadoDisco.Aviso
               : EstadoDisco.Ok;

        Mensagem = Estado switch
        {
            EstadoDisco.Critico =>
                $"Disco quase cheio: cabem ~{TempoRestante.TotalMinutes:F0} min de gravação.",
            EstadoDisco.Aviso =>
                $"Pouco espaço em disco: ~{TempoRestante.TotalMinutes:F0} min restantes.",
            _ => null,
        };
        return Estado;
    }

    /// <summary>
    /// Se dá para começar. Recusar cedo é melhor que parar no meio.
    /// </summary>
    public bool PodeComecar(long bytesLivres, out string? motivo)
    {
        Avaliar(bytesLivres);
        if (Estado == EstadoDisco.Critico)
        {
            motivo = $"Espaço insuficiente: cabem só ~{TempoRestante.TotalMinutes:F0} min. " +
                     "Libere espaço ou escolha outra pasta.";
            return false;
        }
        motivo = Mensagem;      // aviso não impede, só informa
        return true;
    }
}
