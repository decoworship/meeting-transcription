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
        var saida = new List<TrechoDaLegenda>(trechos.Count);
        string? anterior = null;

        foreach (var t in trechos)
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

            saida.Add(new TrechoDaLegenda
            {
                InicioMs = t.InicioMs, FimMs = t.FimMs, Dono = t.Dono,
                Texto = t.Texto, Falante = quem,
            });
            if (quem is not null) anterior = quem;
        }

        return saida;
    }
}
