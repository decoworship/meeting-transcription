namespace MeetingRecorder.Agenda;

/// <summary>
/// Qual dos eventos da janela corresponde à gravação.
/// </summary>
/// <remarks>
/// Separado do cliente HTTP porque é a única parte com regra de negócio de
/// verdade, e a única que dá para testar sem rede.
/// </remarks>
public static class EscolhaDeEvento
{
    /// <summary>
    /// Prefere o evento que cobre o instante; senão, o de início mais próximo.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Empates vão para o mais curto: numa agenda com um bloco de "foco" de 4h e
    /// uma reunião de 30 min sobrepostos, a reunião é a resposta certa.
    /// </para>
    /// <para>
    /// Evento de dia inteiro (sem horário de início) nunca é escolhido — não
    /// identifica uma reunião, e um aniversário na agenda rotularia a gravação
    /// inteira errado.
    /// </para>
    /// </remarks>
    public static Evento? Escolher(IReadOnlyList<Evento> candidatos, DateTimeOffset agora)
    {
        Evento? melhorCobrindo = null;
        TimeSpan menorDuracao = TimeSpan.MaxValue;

        Evento? melhorProximo = null;
        double menorDistancia = double.MaxValue;

        foreach (var e in candidatos)
        {
            if (e.Inicio is not { } inicio) continue;

            if (e.Fim is { } fim && inicio <= agora && agora <= fim)
            {
                var duracao = fim - inicio;
                if (duracao < menorDuracao) { menorDuracao = duracao; melhorCobrindo = e; }
            }
            else
            {
                double distancia = Math.Abs((inicio - agora).TotalSeconds);
                if (distancia < menorDistancia) { menorDistancia = distancia; melhorProximo = e; }
            }
        }

        return melhorCobrindo ?? melhorProximo;
    }
}
