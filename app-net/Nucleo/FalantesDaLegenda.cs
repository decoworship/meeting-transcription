using MeetingApp.Sidecar;

namespace MeetingApp.Nucleo;

/// <summary>
/// Quem falou em cada trecho da legenda, depois que a reunião acaba.
/// </summary>
/// <remarks>
/// <para>
/// <b>É o <c>VIVO-2</c>, e ele só existe porque o <c>VIVO-1</c> carimbou o
/// tempo.</b> A atribuição de falante é por sobreposição temporal; até
/// 17/09/2026 o <c>legenda.json</c> não tinha tempo nenhum, e não havia o que
/// sobrepor.
/// </para>
/// <para>
/// <b>O que isto NÃO é: a passada final.</b> O texto continua sendo o do
/// Nemotron, que fica a 26% de WER do <c>large-v3</c> (BACKLOG, <c>VIVO-4</c>).
/// Isto entrega <b>quem falou no rascunho</b>, em ~2 min para uma reunião de
/// uma hora — a diarização custa 32× o tempo real —, e nada além.
/// </para>
/// <para>
/// <b>A régua é a mesma da passada final</b>, pelo
/// <see cref="Montagem.DonoDoIntervalo"/>. Duas implementações da mesma
/// sobreposição fariam a legenda e a transcrição discordarem sobre quem falou
/// no mesmo segundo.
/// </para>
/// </remarks>
public static class FalantesDaLegenda
{
    /// <summary>
    /// Devolve os trechos com o falante preenchido.
    /// </summary>
    /// <remarks>
    /// <b>Conservador por construção:</b> sem diarização nenhuma, os trechos
    /// saem como entraram. O pior caso é o comportamento de antes.
    /// </remarks>
    public static List<TrechoDaLegenda> Atribuir(
        IReadOnlyList<TrechoDaLegenda> trechos,
        IReadOnlyList<SegmentoDeFalante> diarizacao)
    {
        var inteiros = Juntar(trechos);
        var saida = new List<TrechoDaLegenda>(inteiros.Count);
        string? anterior = null;

        foreach (var t in inteiros)
        {
            string? quem;
            if (t.Dono)
            {
                // **A faixa do microfone sabe; o pyannote acha.** Deixar a
                // diarização decidir aqui trocaria certeza por estimativa.
                quem = VozDoDono.Rotulo;
            }
            else if (diarizacao.Count == 0)
            {
                quem = null;
            }
            else
            {
                quem = Montagem.DonoDoIntervalo(
                    t.InicioMs / 1000.0, t.FimMs / 1000.0, diarizacao);

                // Trecho sem diarização em cima — silêncio entre turnos, ou fala
                // que o pyannote não pegou — fica com o vizinho anterior, para
                // não abrir troca de falante onde não há informação de troca.
                quem ??= anterior;
            }

            saida.Add(t.ComFalante(quem));
            if (quem is not null) anterior = quem;
        }

        return saida;
    }

    /// <summary>
    /// Funde os trechos que partem uma palavra ao meio.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Antes de atribuir, e não depois</b>, porque é a atribuição que causa o
    /// dano: em 18/09/2026 o motor firmou <c>'a Palo'</c> e <c>'ma tem
    /// Uberlândia'</c>, e a diarização deu falantes diferentes às duas metades
    /// de "Paloma". Corrigir depois exigiria decidir qual metade estava certa;
    /// fundir antes faz a pergunta não existir — <b>uma palavra tem um dono
    /// só</b>.
    /// </para>
    /// <para>
    /// <b>O texto é concatenado sem espaço</b>, que é como ele saiu do motor: os
    /// dois pedaços são fatias de um mesmo fluxo, e o espaço que não havia entre
    /// eles é justamente o sinal de que a palavra continua.
    /// </para>
    /// <para>
    /// <b>O intervalo vira a união.</b> Isso engrossa o carimbo pelo tamanho de
    /// um commit — ~1,1 s de mediana —, e é o preço de não partir a palavra.
    /// </para>
    /// </remarks>
    private static List<TrechoDaLegenda> Juntar(IReadOnlyList<TrechoDaLegenda> trechos)
    {
        var juntos = new List<TrechoDaLegenda>(trechos.Count);
        foreach (var t in trechos)
        {
            if (t.Colado && juntos.Count > 0)
            {
                var a = juntos[^1];
                juntos[^1] = new TrechoDaLegenda
                {
                    InicioMs = a.InicioMs,
                    FimMs = Math.Max(a.FimMs, t.FimMs),
                    // O dono do pedaço maior manda: a palavra inteira é de quem
                    // falou a maior parte dela.
                    Dono = (t.FimMs - t.InicioMs) > (a.FimMs - a.InicioMs) ? t.Dono : a.Dono,
                    Texto = a.Texto + t.Texto,
                    Colado = a.Colado,
                };
                continue;
            }
            juntos.Add(t);
        }
        return juntos;
    }
}
