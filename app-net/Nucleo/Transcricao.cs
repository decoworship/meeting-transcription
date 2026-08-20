using System.Text.Json;
using System.Text.Json.Serialization;
using MeetingApp.Sidecar;

namespace MeetingApp.Nucleo;

/// <summary>
/// Um segmento pronto: texto com tempo e falante.
/// </summary>
/// <remarks>
/// Os nomes em inglês e a ordem dos campos reproduzem o
/// <c>TranscriptionSegment</c> do app atual, porque o <c>history/</c> gravado
/// por ele precisa continuar legível — e porque a paridade da Fase 2 se mede
/// comparando este JSON com o dele.
/// </remarks>
public sealed class SegmentoFinal
{
    [JsonPropertyName("start")] public required double Start { get; init; }
    [JsonPropertyName("end")] public required double End { get; init; }
    // Mutável como o Speaker: a correção fonética reescreve o texto, e a edição
    // no lugar (E3 do FEATURES) vai reescrevê-lo de novo.
    [JsonPropertyName("text")] public required string Text { get; set; }
    [JsonPropertyName("speaker")] public string? Speaker { get; set; }

    /// <summary>
    /// O que a correção fonética trocou neste trecho.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Guardado porque a correção é um palpite, e palpite que ninguém pode
    /// conferir é um defeito esperando. Ela existe para recuperar "Dimi" de
    /// "Jimmy"; a mesma régua um pouco frouxa transforma uma palavra comum num
    /// nome do vocabulário, e sem esta lista o usuário lê o resultado sem saber
    /// que houve troca — o texto parece simplesmente ter saído assim do modelo.
    /// </para>
    /// <para>
    /// A posição não é guardada. Ela é do texto <b>antes</b> da correção, e o
    /// texto que a UI mostra é o de depois — pior ainda, a edição manual o
    /// reescreve de novo. Um índice que aponta para um texto que não existe
    /// mais marcaria a palavra errada, que é pior que não marcar nada.
    /// </para>
    /// </remarks>
    [JsonPropertyName("swaps")] public List<TrocaFeita>? Swaps { get; set; }

    /// <summary>
    /// O alinhamento por palavra do ASR — presente <b>só</b> no parcial.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Serve a uma coisa só: cortar este segmento onde a diarização disser que o
    /// falante mudou (<see cref="Montagem.RepartirPorFalante"/>). Depois do
    /// corte ele não tem mais uso, e é apagado — por isso o arquivo pronto sai
    /// como sempre saiu, e a paridade com o <c>history/</c> do Python continua
    /// medível byte a byte. É a mesma regra do <c>pending</c>, e pelo mesmo
    /// motivo.
    /// </para>
    /// <para>
    /// <b>Fica no parcial porque a retomada precisa dele.</b> O texto é gravado
    /// antes de diarizar; se as palavras não fossem junto, a retomada chegaria à
    /// diarização sem como cortar, e o defeito da §4.1 voltaria exatamente nas
    /// gravações que já custaram uma queda de máquina.
    /// </para>
    /// <para>
    /// Nulo em parcial escrito antes de 19/08/2026, e o corte simplesmente não
    /// acontece — o resultado é o de antes, nunca um erro.
    /// </para>
    /// </remarks>
    [JsonPropertyName("words")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<PalavraDita>? Words { get; set; }
}

/// <summary>Uma palavra com o tempo em que foi dita, como o parcial a guarda.</summary>
public sealed class PalavraDita
{
    [JsonPropertyName("start")] public required double Start { get; init; }
    [JsonPropertyName("end")] public required double End { get; init; }
    [JsonPropertyName("text")] public required string Text { get; init; }
}

/// <summary>Uma troca da correção fonética, como o arquivo a guarda.</summary>
public sealed class TrocaFeita
{
    [JsonPropertyName("from")] public required string De { get; init; }
    [JsonPropertyName("to")] public required string Para { get; init; }
}

/// <summary>
/// O que ainda falta nesta transcrição, e com que parâmetros o ASR rodou.
/// </summary>
/// <remarks>
/// <para>
/// <b>Por que existe.</b> Em 19/08/2026 o segundo usuário do app perdeu, mais
/// de uma vez, uma transcrição que já tinha dado certo: o ASR terminava, a
/// máquina dele desligava durante a diarização, e o texto — que só era escrito
/// no fim de tudo — ia junto. Ver <c>docs/FASE6.md</c> §3.0.
/// </para>
/// <para>
/// Com este campo o <c>transcricao.json</c> passa a existir <b>assim que o
/// texto existe</b>. Quem abrir a gravação lê a reunião; quem transcrever de
/// novo não paga o ASR outra vez.
/// </para>
/// <para>
/// <b>Os três parâmetros não são enfeite.</b> Modelo, idioma e vocabulário são
/// o que decide a saída do ASR — retomar um parcial feito com <c>medium</c>
/// para quem agora pediu <c>large-v3</c> devolveria o texto do modelo errado
/// sem avisar. Quando qualquer um deles muda, o ASR roda de novo.
/// </para>
/// </remarks>
public sealed class Pendencia
{
    /// <summary>As etapas que faltam. Hoje só <c>"diarizacao"</c>.</summary>
    [JsonPropertyName("steps")] public required List<string> Steps { get; init; }

    [JsonPropertyName("model")] public string? Model { get; init; }
    [JsonPropertyName("language")] public string? Language { get; init; }
    [JsonPropertyName("vocabulary")] public string? Vocabulary { get; init; }
}

/// <summary>O resultado que a UI consome e o histórico persiste.</summary>
public sealed class ResultadoDaTranscricao
{
    [JsonPropertyName("language")] public string? Language { get; init; }
    [JsonPropertyName("duration")] public double? Duration { get; init; }

    /// <summary>
    /// De que reunião esta transcrição é.
    /// </summary>
    /// <remarks>
    /// Os mesmos nomes que o <c>history/</c> do app Python usa. Sem eles, o
    /// cliente e o projeto escolhidos antes de transcrever se perdiam ao sair
    /// da tela — e o cabeçalho do arquivo exportado saía sem dizer de quem era
    /// a reunião, que foi como o defeito apareceu.
    /// </remarks>
    [JsonPropertyName("client")] public string? Client { get; set; }
    [JsonPropertyName("project")] public string? Project { get; set; }

    /// <summary>Data e hora da reunião, em ISO. Vem da agenda quando existe.</summary>
    [JsonPropertyName("date")] public string? Date { get; set; }

    [JsonPropertyName("segments")] public required List<SegmentoFinal> Segments { get; init; }

    /// <summary>
    /// O que falta, quando este arquivo é um parcial; <c>null</c> quando está
    /// pronto.
    /// </summary>
    /// <remarks>
    /// <b>Omitido quando nulo</b>, e é o único campo desta classe que se omite:
    /// um arquivo completo precisa sair byte a byte como saía antes de a
    /// retomada existir, porque é assim que a paridade com o <c>history/</c> do
    /// Python se mede. Ver <see cref="Pendencia"/>.
    /// </remarks>
    [JsonPropertyName("pending")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Pendencia? Pending { get; set; }

    public string ParaJson() =>
        JsonSerializer.Serialize(this, TranscricaoJson.Default.ResultadoDaTranscricao);

    /// <summary>Lê o que <see cref="ParaJson"/> escreveu.</summary>
    public static ResultadoDaTranscricao? DeJson(string json) =>
        JsonSerializer.Deserialize(json, TranscricaoJson.Default.ResultadoDaTranscricao);
}

[JsonSourceGenerationOptions(WriteIndented = true,
                             DefaultIgnoreCondition = JsonIgnoreCondition.Never)]
[JsonSerializable(typeof(ResultadoDaTranscricao))]
internal sealed partial class TranscricaoJsonBase : JsonSerializerContext;

internal static class TranscricaoJson
{
    // UnsafeRelaxedJsonEscaping pelo mesmo motivo do meta.json: o Python grava
    // com ensure_ascii=False, e sem isto todo acento viraria escape unicode.
    public static readonly TranscricaoJsonBase Default = new(new JsonSerializerOptions
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    });
}

/// <summary>
/// Junta o que os motores devolveram: texto do ASR, falantes da diarização, e a
/// certeza que só as duas faixas separadas dão.
/// </summary>
public static class Montagem
{
    /// <summary>
    /// Quanto o microfone precisa superar o áudio do sistema para o segmento ser
    /// seu. 2,0 (~6 dB) tolera o vazamento de quem usa caixas em vez de fone.
    /// </summary>
    public const double MargemDoDono = 2.0;

    /// <summary>Abaixo disto o microfone é ruído de fundo, não fala.</summary>
    public const double RmsMinimoDoDono = 5e-3;

    /// <summary>
    /// Um pedaço de segmento não sai menor que isto, em segundos.
    /// </summary>
    /// <remarks>
    /// Sem piso, um "uhum" de 0,2 s no meio da fala de outra pessoa vira um
    /// trecho próprio, e a revisão — que é uma linha por trecho — enche de
    /// confete. Meio segundo é curto o bastante para não engolir uma resposta
    /// curta de verdade ("não", "isso") e longo o bastante para o eco da
    /// diarização não virar linha.
    /// </remarks>
    public const double MinimoDoPedaco = 0.5;

    /// <summary>
    /// Corta os segmentos em que a diarização diz que mais de uma pessoa falou.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>O defeito que isto conserta.</b> A atribuição é um rótulo por segmento
    /// do ASR. Quando o segmento tem 43,7 s e três pessoas dentro, duas somem —
    /// e não somem só da linha "quem falou": some a fala delas do que a ata vai
    /// ler. Medido na reunião de 13/08/2026: <b>12% dos segmentos carregavam 46%
    /// das palavras</b> (docs/FASE6.md §4.1).
    /// </para>
    /// <para>
    /// <b>Por que aqui e não no ASR.</b> Quem sabe onde o falante trocou é a
    /// diarização, que roda depois. O ASR já entregava o insumo do corte — o
    /// tempo de cada palavra — e o núcleo o descartava (§4.5). Juntar os dois é
    /// o conserto de raiz; desligar o <c>hotwords</c>, que encurta os segmentos,
    /// ataca a causa de eles serem tão longos, e os dois são independentes.
    /// </para>
    /// <para>
    /// <b>Corta na palavra, nunca no meio dela.</b> Cada palavra recebe o
    /// falante de maior sobreposição, palavras seguidas do mesmo falante viram
    /// um pedaço, e o texto de cada pedaço é a concatenação das palavras dele.
    /// Um pedaço curto demais volta para o vizinho anterior em vez de virar
    /// linha — ver <see cref="MinimoDoPedaco"/>.
    /// </para>
    /// <para>
    /// <b>Conservador por construção.</b> Segmento sem palavras (parcial antigo,
    /// motor antigo) e segmento em que todas as palavras são da mesma pessoa
    /// saem <b>intactos</b>, o mesmo objeto que entrou. Sem diarização não há
    /// onde cortar, e a chamada não faz nada. O pior caso é o comportamento de
    /// antes, nunca um resultado pior que ele.
    /// </para>
    /// <para>
    /// Roda <b>antes</b> da correção fonética, que reescreve o texto: depois
    /// dela as palavras já não somam o texto do segmento, e o corte montaria
    /// pedaços com o texto de antes da correção.
    /// </para>
    /// </remarks>
    /// <returns>Quantos segmentos foram cortados em mais de um pedaço.</returns>
    public static int RepartirPorFalante(
        List<SegmentoFinal> segmentos, IReadOnlyList<SegmentoDeFalante> diarizacao)
    {
        if (diarizacao.Count == 0) return 0;

        var saida = new List<SegmentoFinal>(segmentos.Count);
        int cortados = 0;

        foreach (var seg in segmentos)
        {
            var pedacos = Repartir(seg, diarizacao);
            if (pedacos.Count > 1) cortados++;
            saida.AddRange(pedacos);
        }

        // Reescreve a lista recebida, e não devolve outra: quem chama já a
        // passou adiante — Retomada guardou a mesma referência, e o resultado
        // final a serializa. Trocar a lista deixaria os dois olhando para a
        // versão não cortada.
        segmentos.Clear();
        segmentos.AddRange(saida);
        return cortados;
    }

    /// <summary>Os pedaços de um segmento. Um só — ele mesmo — quando não há o que cortar.</summary>
    private static List<SegmentoFinal> Repartir(
        SegmentoFinal seg, IReadOnlyList<SegmentoDeFalante> diarizacao)
    {
        var palavras = seg.Words;
        if (palavras is not { Count: > 1 }) return [seg];

        // O falante de cada palavra, pela mesma régua da atribuição: maior
        // sobreposição. Uma palavra dura ~0,3 s, então a soma por falante que
        // AtribuirFalantes faz não teria o que somar aqui.
        var donos = new string?[palavras.Count];
        for (int i = 0; i < palavras.Count; i++)
            donos[i] = DonoDoIntervalo(palavras[i].Start, palavras[i].End, diarizacao);

        // Palavra sem diarização nenhuma em cima (silêncio entre turnos, ou
        // fala que o pyannote não pegou) fica com o vizinho anterior, para não
        // abrir corte onde não há informação de troca.
        for (int i = 0; i < donos.Length; i++)
            if (donos[i] is null) donos[i] = i > 0 ? donos[i - 1] : PrimeiroNaoNulo(donos);

        var corridas = Corridas(donos, palavras);
        if (corridas.Count <= 1) return [seg];

        var pedacos = new List<SegmentoFinal>(corridas.Count);
        for (int i = 0; i < corridas.Count; i++)
        {
            var (de, ate) = corridas[i];
            pedacos.Add(new SegmentoFinal
            {
                // As pontas do segmento original são preservadas: os tempos das
                // palavras não cobrem o silêncio da borda, e encolher o primeiro
                // e o último pedaço para dentro deles perderia áudio que a
                // revisão usa para tocar o trecho.
                Start = i == 0 ? seg.Start : palavras[de].Start,
                End = i == corridas.Count - 1 ? seg.End : palavras[ate].End,
                // Concatenação crua, sem trim: a palavra do Whisper já vem com
                // o espaço à esquerda, e é o que faz os pedaços somarem
                // exatamente o texto que o segmento tinha.
                Text = string.Concat(palavras.GetRange(de, ate - de + 1).Select(p => p.Text)),
                Words = palavras.GetRange(de, ate - de + 1),
            });
        }
        return pedacos;
    }

    /// <summary>
    /// Palavras seguidas do mesmo falante, como faixas de índice.
    /// </summary>
    /// <remarks>
    /// A fusão dos pedaços curtos acontece aqui e não depois: um pedaço de
    /// 0,2 s absorvido pelo anterior pode fazer o seguinte, do mesmo falante do
    /// anterior, ficar colado nele — e é isso que se quer.
    /// </remarks>
    private static List<(int De, int Ate)> Corridas(
        string?[] donos, List<PalavraDita> palavras)
    {
        var corridas = new List<(int De, int Ate)>();
        int inicio = 0;

        for (int i = 1; i <= donos.Length; i++)
        {
            if (i < donos.Length && donos[i] == donos[inicio]) continue;

            double duracao = palavras[i - 1].End - palavras[inicio].Start;
            if (corridas.Count > 0 && duracao < MinimoDoPedaco)
                corridas[^1] = (corridas[^1].De, i - 1);      // curto demais: volta ao vizinho
            else
                corridas.Add((inicio, i - 1));
            inicio = i;
        }

        // Uma corrida colada na anterior pode ter ficado do mesmo falante que a
        // seguinte; juntar evita duas linhas seguidas da mesma pessoa, que é o
        // que a revisão mostraria.
        for (int i = corridas.Count - 1; i > 0; i--)
            if (donos[corridas[i].De] == donos[corridas[i - 1].De])
            {
                corridas[i - 1] = (corridas[i - 1].De, corridas[i].Ate);
                corridas.RemoveAt(i);
            }

        return corridas;
    }

    /// <summary>
    /// O primeiro falante conhecido da lista.
    /// </summary>
    /// <remarks>
    /// Só para a palavra inicial, que não tem vizinho anterior de quem herdar.
    /// Quando ninguém é conhecido — nenhuma palavra caiu sobre diarização —
    /// devolve nulo, todas as palavras ficam iguais, e o segmento sai inteiro.
    /// </remarks>
    private static string? PrimeiroNaoNulo(string?[] donos)
    {
        foreach (var d in donos)
            if (d is not null) return d;
        return null;
    }

    /// <summary>O falante de maior sobreposição num intervalo; nulo se não há nenhum.</summary>
    private static string? DonoDoIntervalo(
        double inicio, double fim, IReadOnlyList<SegmentoDeFalante> diarizacao)
    {
        string? melhor = null;
        double maior = 0;
        foreach (var d in diarizacao)
        {
            double sobreposicao = Math.Min(fim, d.Fim) - Math.Max(inicio, d.Inicio);
            if (sobreposicao > maior) { maior = sobreposicao; melhor = d.Falante; }
        }
        return melhor;
    }

    /// <summary>
    /// Cada segmento transcrito recebe o falante com maior sobreposição
    /// temporal na diarização.
    /// </summary>
    /// <remarks>
    /// Os rótulos crus do motor (<c>SPEAKER_00</c>) viram "Speaker 1" aqui, e
    /// não no motor: nomear é apresentação, e o protocolo carrega o que o
    /// modelo produziu. A ordem é a alfabética dos rótulos crus, que é a do
    /// <c>_create_speaker_map</c> do app atual — trocá-la renomearia todo mundo
    /// e quebraria a comparação com o histórico já gravado.
    /// </remarks>
    public static void AtribuirFalantes(
        List<SegmentoFinal> segmentos, IReadOnlyList<SegmentoDeFalante> diarizacao)
    {
        var nomes = diarizacao.Select(d => d.Falante).Distinct().Order(StringComparer.Ordinal)
            .Select((cru, i) => (cru, nome: $"Speaker {i + 1}"))
            .ToDictionary(x => x.cru, x => x.nome);

        foreach (var seg in segmentos)
        {
            // Somar por falante, e não pegar o maior trecho isolado: um segmento
            // longo pode alternar entre duas pessoas, e quem fala três vezes por
            // um segundo domina quem falou uma vez por dois. Pegar o maior
            // trecho daria a resposta errada exatamente nas trocas de turno.
            var total = new Dictionary<string, double>();
            foreach (var d in diarizacao)
            {
                double sobreposicao = Math.Min(seg.End, d.Fim) - Math.Max(seg.Start, d.Inicio);
                if (sobreposicao > 0)
                    total[d.Falante] = total.GetValueOrDefault(d.Falante) + sobreposicao;
            }

            // "Unknown" e não nulo: é o que o app atual grava, e um rótulo
            // explícito diz "ninguém foi identificado aqui" onde a ausência
            // pareceria esquecimento.
            seg.Speaker = total.Count == 0
                ? "Unknown"
                : nomes[total.MaxBy(p => p.Value).Key];
        }
    }

    /// <summary>
    /// Marca como <paramref name="rotuloDoDono"/> os segmentos em que o
    /// microfone domina.
    /// </summary>
    /// <remarks>
    /// Roda <b>depois</b> da diarização e sobrescreve o palpite dela: onde o
    /// microfone tem energia claramente maior que o áudio do sistema, não se
    /// está estimando quem falou — se está sabendo.
    /// </remarks>
    /// <returns>Quantos segmentos foram atribuídos ao dono.</returns>
    public static int AtribuirDono(List<SegmentoFinal> segmentos, Faixas faixas,
                                   string rotuloDoDono = "You")
    {
        int meus = 0;
        foreach (var seg in segmentos)
        {
            double rmsMic = Faixas.Rms(faixas.Mic, seg.Start, seg.End);
            double rmsSistema = Faixas.Rms(faixas.Sistema, seg.Start, seg.End);

            if (rmsMic >= RmsMinimoDoDono && rmsMic > rmsSistema * MargemDoDono)
            {
                seg.Speaker = rotuloDoDono;
                meus++;
            }
        }
        return meus;
    }
}
