using System;

namespace MeetingRecorder.Core;

/// <summary>
/// Estado observável de uma faixa: o que a bandeja mostra durante, e o que o
/// <c>meta.json</c> registra depois.
/// </summary>
/// <remarks>
/// <para>
/// Um booleano "nunca teve áudio" não basta. A gravação de 06/08 saiu 95% muda
/// depois de um início saudável, e o <c>meta.json</c> a declarou boa porque o
/// canal <em>tinha</em> produzido áudio nos primeiros 81 segundos. Os campos de
/// tempo (<see cref="TotalSilencioS"/>, <see cref="MaiorSilencioS"/>,
/// <see cref="MudoS"/>) existem para tornar esse tipo de falha visível depois do
/// fato.
/// </para>
/// <para>
/// <see cref="AmostrasDescartadas"/> é novo no porte (requisito 3.5): o
/// gravador Python descarta silenciosamente quando a fila enche, e não havia
/// como saber que isso aconteceu.
/// </para>
/// </remarks>
public sealed class TrackStats
{
    public required string Nome { get; init; }

    public long AmostrasEscritas { get; set; }
    public int CorrecoesDeriva { get; set; }
    public long DerivaLiquidaAmostras { get; set; }
    public double PicoRms { get; set; }

    /// <summary>
    /// Amostras perdidas por fila cheia. Preferimos perder áudio a travar o
    /// callback do driver — um callback lento causa glitch em <em>todo</em> o
    /// áudio da máquina —, mas a perda tem que ficar registrada.
    /// </summary>
    public long AmostrasDescartadas { get; set; }

    /// <summary>
    /// Se o canal já produziu áudio alguma vez. Separa "configuração errada" de
    /// "pausa na conversa", que precisam de limiares muito diferentes.
    /// </summary>
    public bool JaOuviu { get; set; }

    public double SilencioAtualS { get; set; }
    public double TotalSilencioS { get; set; }
    public double MaiorSilencioS { get; set; }
    public double MudoS { get; set; }
    public bool AvisouSilencio { get; set; }

    /// <summary>Falha de configuração: o canal nunca produziu áudio.</summary>
    public bool SemAudio => !JaOuviu;

    /// <summary>
    /// A faixa parou antes do fim porque o dispositivo sumiu (requisito 3.7).
    /// </summary>
    /// <remarks>
    /// Escrito pela thread de captura, lido depois do <c>Join</c> dela — que é a
    /// barreira que torna a leitura segura sem precisar de <c>volatile</c> aqui.
    /// O aviso ao vivo na bandeja continua vindo do campo volátil da captura.
    /// </remarks>
    public bool Desconectado { get; set; }

    /// <summary>
    /// Quanto da faixa tem conteúdo útil. É a leitura que interessa de relance:
    /// baixo aqui significa gravação suspeita mesmo com <see cref="SemAudio"/>
    /// falso.
    /// </summary>
    public double PercentualUtil(double duracaoS) =>
        duracaoS > 0
            ? Math.Round(100 * Math.Max(0, duracaoS - TotalSilencioS - MudoS) / duracaoS, 1)
            : 0.0;
}
