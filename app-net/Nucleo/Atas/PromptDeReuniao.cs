using System.Text;

namespace MeetingApp.Nucleo.Atas;

/// <summary>
/// A instrução de quem responde perguntas sobre a reunião em curso.
/// </summary>
/// <remarks>
/// <para>
/// <b>Mora sozinha num arquivo porque é o que mais vai mudar.</b> A montagem do
/// texto e o encanamento da pergunta são estáveis; a redação da instrução se
/// ajusta contra reunião de verdade, e uma mudança aqui não deve obrigar a
/// tocar em mais nada.
/// </para>
/// <para>
/// <b>A ordem é a mesma do <see cref="PromptDeAta"/>, e pelo mesmo motivo:</b>
/// instrução antes de dado, transcrição por último. O que fica perto do fim do
/// prompt é o que mais pesa na hora de gerar — e aqui o fim é a pergunta.
/// </para>
/// </remarks>
public static class PromptDeReuniao
{
    /// <summary>
    /// O papel, e a única regra que não se negocia.
    /// </summary>
    /// <remarks>
    /// <b>Responder só do que está na transcrição</b> é o que separa esta
    /// funcionalidade de uma que inventa reunião. A transcrição é automática e
    /// erra palavras; um modelo que preenche buraco com plausibilidade produz
    /// decisão que ninguém tomou — e quem lê não tem como saber.
    /// </remarks>
    public const string Sistema =
        "Você acompanha uma reunião em andamento e responde perguntas sobre o que já "
        + "foi dito nela, em português do Brasil. Responda APENAS com base na "
        + "transcrição fornecida: se a resposta não estiver lá, diga que isso não foi "
        + "falado até agora, em vez de supor. A transcrição é automática e contém erros "
        + "de palavra — leia pelo sentido, e não cite trechos que não fazem sentido. "
        + "Seja direto e curto: quem pergunta está no meio da reunião.";

    /// <summary>O que a transcrição não sabe dizer, dito uma vez só.</summary>
    /// <remarks>
    /// Sem isto o modelo responde "às 10h15 ficou decidido…" sobre um texto que
    /// não tem relógio nenhum, e inventa o horário com a mesma naturalidade com
    /// que inventaria o resto.
    /// </remarks>
    private const string OQueOTextoNaoTem =
        "A transcrição não traz horários nem nomes próprios dos participantes: "
        + "\"Você\" é quem está usando o app, e os outros aparecem sem nome. "
        + "Não invente horário, minuto nem nome de pessoa.";

    /// <summary>O aviso de que o começo da reunião não coube no contexto.</summary>
    private const string SoAParteFinal =
        "ATENÇÃO: o que segue é apenas a parte final da reunião — o começo não coube. "
        + "Não afirme como a reunião começou nem o que foi tratado antes deste ponto.";

    /// <summary>Monta a mensagem do usuário: contexto, transcrição, pergunta.</summary>
    public static string Montar(string transcricao, bool cortado, string pergunta)
    {
        var sb = new StringBuilder();
        sb.AppendLine(OQueOTextoNaoTem);
        if (cortado) sb.AppendLine(SoAParteFinal);
        sb.AppendLine();
        sb.AppendLine("=== TRANSCRIÇÃO DA REUNIÃO ATÉ AGORA ===");
        sb.AppendLine(transcricao);
        sb.AppendLine("=== FIM DA TRANSCRIÇÃO ===");
        sb.AppendLine();
        sb.Append("Pergunta: ");
        sb.Append(pergunta);
        return sb.ToString();
    }
}
