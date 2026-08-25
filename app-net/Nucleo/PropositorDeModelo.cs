using System.Text;
using System.Text.Json;
using MeetingApp.Nucleo.Atas;

namespace MeetingApp.Nucleo;

/// <summary>
/// O segundo propositor da revisão de termos: um modelo lendo a transcrição.
/// </summary>
/// <remarks>
/// <para>
/// <b>Ele não decide nada.</b> Devolve <see cref="Proposta"/> igual à
/// <see cref="RevisaoDeTermos.Propor"/>, e passa pela mesma
/// <see cref="RevisaoDeTermos.Validar"/> — que descarta identidade, exige que o
/// alvo seja entidade conhecida e impõe a grafia do vocabulário. É o desenho de
/// sempre deste projeto: regra determinística embaixo, modelo por cima.
/// </para>
/// <para>
/// <b>Por que ele existe, medido em 25/08 sobre dez erros reais.</b> A regra
/// acerta 3 e o Gemma 4 acerta 8. A diferença são os casos que precisam de
/// contexto: <c>Cláudio</c> por <c>Claude</c>, <c>Tim</c> por <c>Teams</c>,
/// <c>sexta</c> por <c>cesta</c>. Nenhum é alcançável por distância de edição
/// — <c>sexta</c> é português correto, e só quem entende a frase sabe que ali
/// era cesta de produtos.
/// </para>
/// <para>
/// <b>O modelo importa, e não é o mesmo da ata.</b> Nos mesmos dez casos o
/// Qwen3.5 4B acertou 3 — igual à regra, e sem acrescentar nada. O Gemma
/// acertou 8. Em cinco trechos <b>sem erro nenhum</b>, o Gemma não propôs uma
/// troca danosa sequer: das dezessete propostas dele, oito eram identidade
/// (<c>Sorocaba → Sorocaba</c>), que a validação descarta.
/// </para>
/// <para>
/// <b>É opcional de propósito.</b> São 640 MB para baixar e uma passada a mais
/// de GPU. Sem o modelo, a revisão continua acontecendo pela regra — o que muda
/// é quantos casos ela alcança.
/// </para>
/// </remarks>
public static class PropositorDeModelo
{
    /// <summary>
    /// O esquema que prende a resposta. Sem ele o modelo delibera e não conclui.
    /// </summary>
    /// <remarks>
    /// Medido: com raciocínio e sem esquema, o Qwen3.5 gastou 3.000 tokens
    /// pensando num trecho de quatro linhas, chegou à resposta certa e continuou
    /// se questionando até estourar. Ver <c>MotorDeAta.ResponderAsync</c>.
    /// </remarks>
    public const string Esquema = """
        {
          "type": "object",
          "additionalProperties": false,
          "required": ["trocas"],
          "properties": {
            "trocas": {
              "type": "array",
              "items": {
                "type": "object",
                "additionalProperties": false,
                "required": ["de", "para"],
                "properties": {
                  "de": {"type": "string"},
                  "para": {"type": "string"}
                }
              }
            }
          }
        }
        """;

    public const string Sistema =
        "Você revisa transcrições automáticas de reuniões em português do Brasil. "
        + "Responda apenas com o objeto JSON pedido.";

    /// <summary>Caracteres de transcrição por pergunta.</summary>
    /// <remarks>
    /// Pedaço grande economiza chamadas e dilui o contexto: o modelo tem que
    /// achar uma palavra trocada em muito texto. Pedaço pequeno multiplica as
    /// chamadas — e cada uma custa entre 0,6 e 2 segundos. Seis mil caracteres
    /// são umas cinquenta falas, que é mais ou menos um assunto.
    /// </remarks>
    public const int CaracteresPorPedaco = 6_000;

    /// <summary>Teto de saída. A resposta é uma lista curta, não um texto.</summary>
    public const int TokensDeSaida = 512;

    /// <summary>
    /// As trocas que o modelo propõe, ainda sem validação.
    /// </summary>
    public static async Task<IReadOnlyList<Proposta>> ProporAsync(
        MotorDeAta motor, IEnumerable<string> textos, IReadOnlyList<string> entidades,
        Action<ProgressoDaAta>? progresso = null, CancellationToken ct = default)
    {
        var pedacos = Pedacos(textos);
        if (pedacos.Count == 0 || entidades.Count == 0) return [];

        // A MESMA lista expandida que a regra usa, e não as entidades cruas.
        // Medido em 25/08: com o rótulo inteiro ("Coca Cola - GCCB"), o modelo
        // achou o erro e propôs "G6CB → Coca Cola" — alvo errado, porque era o
        // único que ele via. Com a lista aberta, "GCCB" passa a ser opção.
        string lista = string.Join(", ", RevisaoDeTermos.Alvos(entidades));
        var perguntas = pedacos.Select(p => Pergunta(lista, p)).ToList();

        var respostas = await motor.ResponderAsync(
            Sistema, perguntas, "trocas", Esquema, TokensDeSaida, progresso, ct);

        var vistas = new Dictionary<string, Proposta>(StringComparer.OrdinalIgnoreCase);
        foreach (string r in respostas)
            foreach (var p in Ler(r))
                vistas.TryAdd(p.De, p);

        return [.. vistas.Values];
    }

    private static string Pergunta(string entidades, string trecho) =>
        $"""
        O reconhecimento de fala erra nomes próprios, siglas e termos técnicos:
        ele escreve a palavra comum que soa parecido, ou uma sigla parecida.
        Ache essas trocas no trecho abaixo.

        Termos corretos conhecidos deste projeto: {entidades}

        Regras:
        - proponha troca APENAS quando o contexto deixar claro;
        - NUNCA troque uma palavra que está correta e faz sentido na frase;
        - o alvo da troca tem que ser um dos termos conhecidos acima;
        - NÃO complete nome: se está escrito "Carla" e a lista tem "Carla Hack",
          está certo do jeito que está — a pessoa disse só o primeiro nome;
        - NÃO troque uma palavra por ela mesma;
        - o que você procura é a palavra que soa parecido e está ERRADA, como
          uma sigla com letra trocada ou um nome de sistema escrito de ouvido;
        - se não houver nada a trocar, devolva a lista vazia.

        Trecho da transcrição:
        {trecho}
        """;

    /// <summary>
    /// Junta as falas em pedaços do tamanho de uma pergunta.
    /// </summary>
    /// <remarks>
    /// Corta entre falas, nunca dentro de uma: metade de uma frase tira do
    /// modelo justamente o contexto pelo qual ele foi chamado.
    /// </remarks>
    private static List<string> Pedacos(IEnumerable<string> textos)
    {
        var pedacos = new List<string>();
        var atual = new StringBuilder();

        foreach (string t in textos)
        {
            string fala = (t ?? "").Trim();
            if (fala.Length == 0) continue;

            if (atual.Length > 0 && atual.Length + fala.Length > CaracteresPorPedaco)
            {
                pedacos.Add(atual.ToString());
                atual.Clear();
            }
            atual.AppendLine(fala);
        }
        if (atual.Length > 0) pedacos.Add(atual.ToString());
        return pedacos;
    }

    /// <summary>
    /// Lê o que o modelo devolveu, tolerando o que ele erra na volta.
    /// </summary>
    /// <remarks>
    /// JSON ilegível não derruba a transcrição: a revisão é acabamento, e um
    /// pedaço que não voltou vale menos que a reunião inteira. O que se perde é
    /// a proposta daquele pedaço.
    /// </remarks>
    public static IReadOnlyList<Proposta> Ler(string resposta)
    {
        try
        {
            using var doc = JsonDocument.Parse(resposta);
            if (!doc.RootElement.TryGetProperty("trocas", out var trocas)
                || trocas.ValueKind != JsonValueKind.Array)
                return [];

            var saida = new List<Proposta>();
            foreach (var t in trocas.EnumerateArray())
            {
                string? de = t.TryGetProperty("de", out var d) ? d.GetString() : null;
                string? para = t.TryGetProperty("para", out var a) ? a.GetString() : null;
                if (de is { Length: > 0 } && para is { Length: > 0 })
                    saida.Add(new Proposta(de.Trim(), para.Trim(), "modelo"));
            }
            return saida;
        }
        catch (JsonException)
        {
            return [];
        }
    }
}
