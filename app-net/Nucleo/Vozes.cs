using System.Text.Json;
using System.Text.Json.Serialization;

namespace MeetingApp.Nucleo;

/// <summary>
/// De onde veio uma amostra de voz.
/// </summary>
/// <remarks>
/// A procedência é o que distingue esta biblioteca da anterior. No modelo
/// antigo cada amostra era um vetor solto, e um vetor contaminado — cross-talk,
/// erro de diarização — envenenava o perfil para sempre, sem ninguém conseguir
/// descobrir qual era. Ver VOZES.md §1.
/// </remarks>
public sealed class Origem
{
    [JsonPropertyName("gravacao")] public required string Gravacao { get; init; }

    /// <summary>"mic" ou "system" — a faixa de onde o trecho saiu.</summary>
    [JsonPropertyName("faixa")] public required string Faixa { get; init; }

    [JsonPropertyName("t0")] public required double T0 { get; init; }
    [JsonPropertyName("t1")] public required double T1 { get; init; }

    /// <summary>
    /// O dispositivo que gravou, copiado do <c>meta.json</c>.
    /// </summary>
    /// <remarks>
    /// Sai de graça e é o rótulo de condição mais confiável que existe: "pelo
    /// headset" e "pelo microfone do notebook" são vozes que combinam mal entre
    /// si, e agrupar por dispositivo separa as duas sem nenhum algoritmo.
    /// </remarks>
    [JsonPropertyName("dispositivo")] public string? Dispositivo { get; init; }
}

/// <summary>Uma amostra de voz de alguém.</summary>
public sealed class AmostraDeVoz
{
    [JsonPropertyName("vetor")] public required float[] Vetor { get; init; }
    [JsonPropertyName("criada_em")] public required string CriadaEm { get; init; }
    [JsonPropertyName("duracao_s")] public required double DuracaoS { get; init; }
    [JsonPropertyName("origem")] public required Origem Origem { get; init; }

    /// <summary>
    /// O trecho de áudio que gerou o vetor, relativo à pasta de vozes.
    /// </summary>
    /// <remarks>
    /// Ninguém consegue julgar um vetor; qualquer um julga quatro segundos de
    /// áudio. É o que torna a limpeza humana possível — sem ele, a tela de
    /// gestão vira uma tabela de números que ninguém sabe avaliar.
    /// </remarks>
    [JsonPropertyName("trecho")] public string? Trecho { get; init; }

    /// <summary>
    /// A amostra destoa do perfil e espera revisão humana.
    /// </summary>
    /// <remarks>
    /// Não é descarte: distância grande tanto pode ser contaminação quanto
    /// condição nova legítima — primeira vez na sala de reunião, resfriado. A
    /// máquina não distingue; quem ouve o trecho distingue em quatro segundos.
    /// </remarks>
    [JsonPropertyName("quarentena")] public bool Quarentena { get; set; }

    /// <summary>
    /// O modelo que produziu este vetor.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Um vetor só significa alguma coisa dentro do modelo que o gerou.</b>
    /// Modelos diferentes produzem espaços vetoriais diferentes, e o cosseno
    /// entre vetores de dois modelos não dá erro: dá um número plausível e
    /// errado. O sintoma não aparece na hora — aparece meses depois, como "o app
    /// chamou a Vanessa de Carla", sem nada no arquivo que explique por quê.
    /// </para>
    /// <para>
    /// <b>Nulo é o modelo de sempre</b>, e não "desconhecido". Toda amostra
    /// gravada antes de 20/08/2026 saiu do
    /// <c>wespeaker-voxceleb-resnet34-LM</c>, porque ele nunca foi escolhível —
    /// isso não é suposição, é a única possibilidade. Ver
    /// <see cref="Vozes.ModeloDeVozPadrao"/>.
    /// </para>
    /// </remarks>
    [JsonPropertyName("modelo")] public string? Modelo { get; init; }

    /// <summary>
    /// Sob quais regras de inscrição esta amostra foi colhida.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Ausente é a geração 1</b>, e não "atual": tudo o que foi aprendido
    /// até 20/08/2026 passou pelo caminho que a FASE6 §4.2 descreve — o vetor
    /// usava o intervalo inteiro do segmento, sem nenhuma guarda contra outra
    /// pessoa <b>dentro</b> dele. Medido: um bloco com 30% de voz de quem não
    /// era o falante. Não dá para saber quais amostras foram atingidas, e por
    /// isso a geração inteira é descartada.
    /// </para>
    /// <para>
    /// <b>Por que uma geração e não uma data.</b> A guarda passa a valer no
    /// build em que ela existe, e ninguém sabe de antemão quando ele será
    /// instalado — uma data cravada aqui classificaria errado tudo o que fosse
    /// aprendido entre a decisão e a atualização. A geração viaja com a amostra
    /// e não depende de relógio nenhum.
    /// </para>
    /// <para>
    /// Descartar é <b>não usar</b>, e não apagar: o arquivo continua lá, a tela
    /// mostra as amostras apagadas, e "Esquecer" continua sendo de quem lê. A
    /// regra do risco 4 do PLANO.md §5 é que perfil de voz se reinscreve — e
    /// reinscrever acontece sozinho, à medida que as pessoas são nomeadas de
    /// novo.
    /// </para>
    /// </remarks>
    [JsonPropertyName("regras")] public int? Regras { get; init; }
}

public sealed class PerfilDeVoz
{
    [JsonPropertyName("amostras")] public List<AmostraDeVoz> Amostras { get; init; } = [];
}

public sealed class BibliotecaDeVozes
{
    /// <summary>Versão do formato. A biblioteca antiga (vetores soltos) é a 1.</summary>
    [JsonPropertyName("versao")] public int Versao { get; set; } = 2;

    [JsonPropertyName("pessoas")]
    public Dictionary<string, PerfilDeVoz> Pessoas { get; init; } = [];
}

[JsonSourceGenerationOptions(WriteIndented = true,
                             DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(BibliotecaDeVozes))]
internal sealed partial class VozesJson : JsonSerializerContext;

/// <summary>
/// As vozes conhecidas: quem já foi nomeado, e como reconhecê-lo depois.
/// </summary>
/// <remarks>
/// <para>
/// Biblioteca nova, começando vazia. A do app Python não é migrada — decisão do
/// dono do produto, e a razão está na VOZES.md: lá cada amostra é um vetor sem
/// procedência e sem áudio, então não há como auditar o que entrou. Os vetores
/// até seriam compatíveis (mesmo modelo, 256 dimensões), mas herdar 40 perfis
/// que ninguém pode inspecionar é herdar a contaminação junto.
/// </para>
/// <para>
/// O reconhecimento usa <b>sub-perfis por condição</b> (VOZES.md §3, nível 2):
/// as amostras são agrupadas por dispositivo e faixa, e a semelhança é o
/// máximo sobre os centróides dos grupos. Máximo sobre duas ou três médias
/// robustas é estável; máximo sobre vinte e cinco vetores crus não é — basta
/// um deles estar errado.
/// </para>
/// </remarks>
public sealed class Vozes
{
    public static string PastaPadrao => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".meeting-transcription", "vozes");

    /// <summary>Acima disto, é a mesma pessoa.</summary>
    /// <remarks>
    /// O limiar do app Python, mantido como ponto de partida: mexer nele sem
    /// medir contra um conjunto de vozes reais seria trocar um número mal
    /// justificado por outro.
    /// </remarks>
    public const double LimiarDeReconhecimento = 0.70;

    /// <summary>Abaixo disto, a amostra vai para revisão em vez de entrar direto.</summary>
    public const double LimiarDeQuarentena = 0.35;

    /// <summary>Fala de menos não vira voz: o vetor sai ruidoso e contamina.</summary>
    public const double SegundosMinimos = 3.0;

    /// <summary>
    /// O modelo de voz que produziu tudo o que existe hoje.
    /// </summary>
    /// <remarks>
    /// Serve de valor para <see cref="AmostraDeVoz.Modelo"/> quando ele está
    /// ausente. É o nome dos <b>pesos</b> — o mesmo se eles vierem da pasta ao
    /// lado do motor ou do HuggingFace, porque são os mesmos bytes e o mesmo
    /// espaço vetorial.
    /// </remarks>
    public const string ModeloDeVozPadrao = "pyannote/wespeaker-voxceleb-resnet34-LM";

    /// <summary>O modelo de uma amostra, com o ausente valendo o de sempre.</summary>
    public static string ModeloDe(AmostraDeVoz a) =>
        a.Modelo is { Length: > 0 } m ? m : ModeloDeVozPadrao;

    /// <summary>
    /// A geração de regras de inscrição que vale hoje.
    /// </summary>
    /// <remarks>
    /// <b>1</b> — até 20/08/2026: o vetor via o intervalo inteiro do segmento e
    /// não havia guarda contra outra pessoa dentro dele (FASE6 §4.2).<br/>
    /// <b>2</b> — desde então: a janela do vetor é a mesma do trecho guardado, e
    /// bloco com o microfone ativo dentro é descartado.
    /// <para>
    /// Subir este número descarta a geração anterior e faz o app reaprender as
    /// vozes. Só se sobe quando <b>não dá para saber</b> quais amostras a regra
    /// velha estragou — se desse, a resposta seria consertar as atingidas.
    /// </para>
    /// </remarks>
    public const int RegrasAtuais = 2;

    /// <summary>A geração de uma amostra; ausente é a 1.</summary>
    public static int RegrasDe(AmostraDeVoz a) => a.Regras ?? 1;

    /// <summary>
    /// Se esta amostra participa de alguma comparação.
    /// </summary>
    /// <remarks>
    /// Três razões para não participar, e as três levam ao mesmo lugar: a
    /// quarentena (espera julgamento humano), o modelo (vetor de outro espaço) e
    /// a geração de regras (colhida por um caminho que contaminava). Estão
    /// juntas aqui para não haver um lugar do código que se lembre de duas e
    /// esqueça a terceira.
    /// </remarks>
    public static bool Conta(AmostraDeVoz a, string modelo) =>
        !a.Quarentena && RegrasDe(a) == RegrasAtuais && ModeloDe(a) == modelo;

    private readonly string _pasta;
    private readonly string _arquivo;
    private BibliotecaDeVozes _dados;

    public Vozes(string? pasta = null)
    {
        _pasta = pasta ?? PastaPadrao;
        _arquivo = Path.Combine(_pasta, "vozes.json");
        _dados = Carregar();
    }

    private BibliotecaDeVozes Carregar()
    {
        try
        {
            if (File.Exists(_arquivo))
                return JsonSerializer.Deserialize(File.ReadAllText(_arquivo),
                           VozesJson.Default.BibliotecaDeVozes) ?? new BibliotecaDeVozes();
        }
        catch (Exception)
        {
            // Biblioteca ilegível não pode impedir de transcrever: o
            // reconhecimento é um plus, não um requisito.
        }
        return new BibliotecaDeVozes();
    }

    public IReadOnlyList<string> Pessoas() =>
        [.. _dados.Pessoas.Keys.Order(StringComparer.CurrentCultureIgnoreCase)];

    public PerfilDeVoz? Perfil(string pessoa) =>
        _dados.Pessoas.GetValueOrDefault(pessoa);

    /// <summary>
    /// Guarda uma amostra, marcando para revisão se ela destoar do perfil.
    /// </summary>
    /// <returns>A amostra como ficou — o chamador precisa saber se caiu em quarentena.</returns>
    public AmostraDeVoz Aprender(string pessoa, AmostraDeVoz amostra)
    {
        if (!_dados.Pessoas.TryGetValue(pessoa, out var perfil))
        {
            perfil = new PerfilDeVoz();
            _dados.Pessoas[pessoa] = perfil;
        }

        // Quarentena só faz sentido contra um perfil que já existe **no mesmo
        // modelo**; a primeira amostra de alguém não tem com o que ser
        // comparada, e a primeira amostra num modelo novo também não. Mandar
        // esta última para quarentena marcaria como suspeita a única voz limpa
        // que o modelo novo tem.
        //
        // A pergunta é feita aqui, e não pelo retorno da Semelhanca: ela devolve
        // -1 quando não há grupos, e -1 também é um cosseno possível. Confundir
        // "não dá para dizer" com "não parece nada" leva às duas decisões
        // opostas.
        string modeloDela = ModeloDe(amostra);
        if (perfil.Amostras.Any(a => RegrasDe(a) == RegrasAtuais && ModeloDe(a) == modeloDela))
        {
            double s = Semelhanca(amostra.Vetor, perfil, modeloDela);
            if (s < LimiarDeQuarentena) amostra.Quarentena = true;
        }

        perfil.Amostras.Add(amostra);
        Gravar();
        return amostra;
    }

    /// <summary>Quem é esta voz, ou <c>null</c> se ninguém conhecido.</summary>
    /// <param name="modelo">
    /// O modelo que produziu <paramref name="vetor"/>. Só amostras do mesmo
    /// modelo entram na conta; nulo vale o de sempre.
    /// </param>
    /// <remarks>
    /// Quando o modelo muda, ninguém é reconhecido e todo mundo se reinscreve —
    /// que é o comportamento certo e o único honesto. A alternativa é comparar
    /// mesmo assim, e comparar entre modelos não devolve "não sei": devolve um
    /// nome errado com aparência de certeza.
    /// </remarks>
    public (string Pessoa, double Semelhanca)? Reconhecer(float[] vetor, string? modelo = null)
    {
        (string, double)? melhor = null;
        string qual = modelo is { Length: > 0 } m ? m : ModeloDeVozPadrao;
        foreach (var (nome, perfil) in _dados.Pessoas)
        {
            double s = Semelhanca(vetor, perfil, qual);
            if (s >= LimiarDeReconhecimento && (melhor is null || s > melhor.Value.Item2))
                melhor = (nome, s);
        }
        return melhor;
    }

    /// <summary>
    /// Semelhança com uma pessoa: o melhor dos sub-perfis dela.
    /// </summary>
    /// <param name="modelo">
    /// Só amostras deste modelo contam. Nulo vale
    /// <see cref="ModeloDeVozPadrao"/>.
    /// </param>
    /// <returns>
    /// A semelhança. <b>-1 quando não há grupo nenhum</b> a comparar — mas -1
    /// também é um cosseno possível, então quem precisa distinguir "não dá para
    /// dizer" de "não parece nada" pergunta pelas amostras, e não pelo retorno.
    /// É o que <see cref="Aprender"/> faz.
    /// </returns>
    /// <remarks>
    /// <para>
    /// O modelo é <b>filtro</b> e não mais um critério de agrupamento. Como
    /// sub-perfil, um grupo de outro modelo continuaria disputando o máximo — e
    /// bastaria ele ganhar uma vez para o nome errado sair. O que se quer é que
    /// ele não exista para esta conta.
    /// </para>
    /// <para>
    /// Os grupos saem do dispositivo e da faixa, que já vêm de graça no
    /// <c>meta.json</c>. Amostras em quarentena ficam de fora — elas esperam
    /// julgamento, e usá-las para reconhecer seria justamente deixar a
    /// contaminação agir.
    /// </para>
    /// </remarks>
    public static double Semelhanca(float[] vetor, PerfilDeVoz perfil, string? modelo = null)
    {
        string qual = modelo is { Length: > 0 } m ? m : ModeloDeVozPadrao;
        var grupos = perfil.Amostras
            .Where(a => Conta(a, qual))
            .GroupBy(a => $"{a.Origem.Dispositivo}|{a.Origem.Faixa}");

        double melhor = -1;
        foreach (var grupo in grupos)
        {
            var centroide = Centroide([.. grupo.Select(a => a.Vetor)]);
            melhor = Math.Max(melhor, Cosseno(vetor, centroide));
        }
        return melhor;
    }

    /// <summary>Média dos vetores normalizados — o centro de uma condição.</summary>
    public static float[] Centroide(IReadOnlyList<float[]> vetores)
    {
        var soma = new float[vetores[0].Length];
        foreach (var v in vetores)
        {
            var n = Normalizado(v);
            for (int i = 0; i < soma.Length; i++) soma[i] += n[i];
        }
        for (int i = 0; i < soma.Length; i++) soma[i] /= vetores.Count;
        return Normalizado(soma);
    }

    private static float[] Normalizado(float[] v)
    {
        double norma = Math.Sqrt(v.Sum(x => (double)x * x));
        if (norma == 0) return v;

        var saida = new float[v.Length];
        for (int i = 0; i < v.Length; i++) saida[i] = (float)(v[i] / norma);
        return saida;
    }

    public static double Cosseno(float[] a, float[] b)
    {
        if (a.Length != b.Length) return -1;

        double produto = 0, na = 0, nb = 0;
        for (int i = 0; i < a.Length; i++)
        {
            produto += (double)a[i] * b[i];
            na += (double)a[i] * a[i];
            nb += (double)b[i] * b[i];
        }
        return na == 0 || nb == 0 ? -1 : produto / (Math.Sqrt(na) * Math.Sqrt(nb));
    }

    /// <summary>
    /// Aceita uma amostra que estava em quarentena.
    /// </summary>
    /// <remarks>
    /// É a outra metade da revisão humana: quem ouviu o trecho e reconheceu a
    /// pessoa diz que aquela condição — a sala nova, o resfriado — é legítima.
    /// Sem isto, uma condição nova ficaria para sempre fora do reconhecimento e
    /// a pessoa deixaria de ser reconhecida justamente onde ela mudou.
    /// </remarks>
    public bool Aprovar(string pessoa, int indice)
    {
        if (!_dados.Pessoas.TryGetValue(pessoa, out var perfil)
            || indice < 0 || indice >= perfil.Amostras.Count)
            return false;

        perfil.Amostras[indice].Quarentena = false;
        Gravar();
        return true;
    }

    /// <summary>Amostras à espera de julgamento, para a tela de gestão.</summary>
    public IReadOnlyList<(string Pessoa, int Indice, AmostraDeVoz Amostra)> EmQuarentena()
    {
        var fila = new List<(string, int, AmostraDeVoz)>();
        foreach (var (nome, perfil) in _dados.Pessoas)
            for (int i = 0; i < perfil.Amostras.Count; i++)
                if (perfil.Amostras[i].Quarentena) fila.Add((nome, i, perfil.Amostras[i]));
        return fila;
    }

    /// <summary>Tira uma amostra do perfil — o que a revisão humana decide.</summary>
    public bool Esquecer(string pessoa, int indice)
    {
        if (!_dados.Pessoas.TryGetValue(pessoa, out var perfil)
            || indice < 0 || indice >= perfil.Amostras.Count)
            return false;

        string? trecho = perfil.Amostras[indice].Trecho;
        perfil.Amostras.RemoveAt(indice);
        if (perfil.Amostras.Count == 0) _dados.Pessoas.Remove(pessoa);

        if (trecho is { Length: > 0 })
        {
            try { File.Delete(Path.Combine(_pasta, trecho)); }
            catch (IOException) { /* o áudio some depois; o vetor já saiu */ }
        }

        Gravar();
        return true;
    }

    public string CaminhoDoTrecho(string relativo) => Path.Combine(_pasta, relativo);

    private void Gravar()
    {
        Directory.CreateDirectory(_pasta);

        // Escrita atômica, como todo arquivo de estado deste projeto.
        string tmp = _arquivo + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(_dados, VozesJson.Default.BibliotecaDeVozes));
        File.Move(tmp, _arquivo, overwrite: true);
        _dados = Carregar();
    }
}
