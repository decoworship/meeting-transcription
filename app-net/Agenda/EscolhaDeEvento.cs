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

    /// <summary>
    /// O que seria escolhido se a gravação começasse neste instante.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Existe para a tela poder <b>prometer</b> um rótulo antes de gravar, e por
    /// isso ela não pode ter regra própria: a lista das próximas reuniões vem de
    /// uma janela larga (horas), e a gravação de verdade só enxerga
    /// <see cref="ClienteDaAgenda.JanelaMinutos"/> minutos para cada lado. Sem
    /// recortar a janela aqui, a tela apontaria a reunião das 16h às 14h e o
    /// gravador não acharia nada — a promessa mais cara que uma tela pode fazer.
    /// </para>
    /// <para>
    /// O recorte é por <b>sobreposição</b>, e não por início, porque é assim que
    /// a API do Google responde a um intervalo: uma reunião que começou há uma
    /// hora e ainda está correndo volta na consulta e concorre.
    /// </para>
    /// </remarks>
    public static Evento? SeGravasseAgora(IReadOnlyList<Evento> candidatos,
                                          DateTimeOffset agora, TimeSpan janela) =>
        Escolher([.. candidatos.Where(e => NaJanela(e, agora, janela))], agora);

    /// <summary>O evento toca o intervalo <c>[agora - janela, agora + janela]</c>.</summary>
    public static bool NaJanela(Evento e, DateTimeOffset agora, TimeSpan janela)
    {
        if (e.Inicio is not { } inicio) return false;   // dia inteiro nunca conta
        var fim = e.Fim ?? inicio;
        return inicio <= agora + janela && fim >= agora - janela;
    }
}
