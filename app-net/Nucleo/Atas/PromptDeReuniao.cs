namespace MeetingApp.Nucleo.Atas;

/// <summary>
/// O que se pede ao modelo sobre a reunião em curso.
/// </summary>
/// <remarks>
/// <para>
/// <b>Voltou a existir em 16/09/2026, e agora com medição por trás.</b> A
/// primeira versão foi retirada por decisão do dono do produto — <i>"não vamos
/// ter prompt nem esquema, por enquanto é livre"</i> — e a comparação que essa
/// decisão liberou é justamente quem escreveu estas linhas: seis modelos, três
/// reuniões, quatro versões de instrução
/// (docs/ESTUDO-RESUMO-AO-VIVO.md §8 a §10).
/// </para>
/// <para>
/// <b>São duas instruções, e a distinção não é enfeite.</b> Prender toda
/// pergunta ao formato de lista faria <i>"quem ficou de mandar o material?"</i>
/// devolver uma lista de assuntos. O botão tem forma; a caixa livre tem só as
/// regras.
/// </para>
/// <para>
/// <b>A instrução vai depois da transcrição</b>, e foi o ajuste decisivo da v3:
/// antes ela vivia 16 KB antes do que o modelo precisava lembrar, e o
/// <c>ministral-3-3b</c> errava a âncora. Movida para o fim, passou a acertar.
/// </para>
/// </remarks>
public static class PromptDeReuniao
{
    /// <summary>O papel e as regras, iguais nos dois caminhos.</summary>
    /// <remarks>
    /// <b>Cada linha existe por um defeito medido.</b> A proibição de copiar
    /// saiu do <c>qwen3-1.7b</c>, que reproduzia falas literais no lugar do
    /// resumo; a de inventar acordo saiu do <c>qwen3-4b-instruct</c>, que
    /// escreveu <i>"a equipe concorda"</i> sobre o que ninguém acordou; a de não
    /// concluir saiu de todos, que escreviam encerramento para uma conversa que
    /// continuava.
    /// </remarks>
    public const string Sistema =
        "Você atualiza quem chegou atrasado numa reunião que ainda está acontecendo. "
        + "Escreva em português do Brasil.\n"
        + "NUNCA copie linhas da transcrição. Não escreva \"Nome: fala\". Conte com as "
        + "suas palavras.\n"
        + "Só escreva o que está na transcrição. Não invente exemplo, número, horário "
        + "nem acordo entre as pessoas.\n"
        + "A reunião não acabou: não escreva conclusão.";

    /// <summary>O botão: o resumo de entrada, em lista e nada mais.</summary>
    /// <remarks>
    /// <b>Só os bullets, por decisão do dono do produto em 16/09/2026</b>, depois
    /// de ver a resposta livre em uso: <i>"achei muito longa a resposta, por mim
    /// poderia trazer só os bullets e deixar eu perguntar sobre o detalhe"</i>.
    /// <para>
    /// <b>A forma do item vem por exemplo, e não por nome das partes.</b> Dizer
    /// "título" e "frase" fazia o <c>qwen3-1.7b</c> escrever <c>Título:</c> e
    /// <c>Frase:</c> literalmente (§8).
    /// </para>
    /// </remarks>
    public const string Resumo =
        "Me atualize sobre esta reunião. Responda apenas com uma lista de 4 a 6 itens, "
        + "do assunto MAIS RECENTE para o mais antigo — o que está no fim da "
        + "transcrição vem primeiro.\n\n"
        + "Cada item numa linha, assim:\n\n"
        + "**Assunto em duas ou três palavras** — o que foi dito sobre ele, em uma "
        + "frase. Se alguém ficou de fazer alguma coisa, diga aqui.\n\n"
        + "Não escreva título, introdução, conclusão nem parágrafo de contexto. "
        + "Só a lista.";

    /// <summary>A caixa livre: sem forma, com as regras.</summary>
    public const string Livre =
        "Responda em uma ou duas frases, direto e sem preâmbulo, usando só o que está "
        + "na transcrição acima. Se a resposta não estiver lá, diga que isso não foi "
        + "falado até agora.";
}
