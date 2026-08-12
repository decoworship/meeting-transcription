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

        // Quarentena só faz sentido contra um perfil que já existe; a primeira
        // amostra de alguém não tem com o que ser comparada.
        if (perfil.Amostras.Count > 0)
        {
            double s = Semelhanca(amostra.Vetor, perfil);
            if (s < LimiarDeQuarentena) amostra.Quarentena = true;
        }

        perfil.Amostras.Add(amostra);
        Gravar();
        return amostra;
    }

    /// <summary>Quem é esta voz, ou <c>null</c> se ninguém conhecido.</summary>
    public (string Pessoa, double Semelhanca)? Reconhecer(float[] vetor)
    {
        (string, double)? melhor = null;
        foreach (var (nome, perfil) in _dados.Pessoas)
        {
            double s = Semelhanca(vetor, perfil);
            if (s >= LimiarDeReconhecimento && (melhor is null || s > melhor.Value.Item2))
                melhor = (nome, s);
        }
        return melhor;
    }

    /// <summary>
    /// Semelhança com uma pessoa: o melhor dos sub-perfis dela.
    /// </summary>
    /// <remarks>
    /// Os grupos saem do dispositivo e da faixa, que já vêm de graça no
    /// <c>meta.json</c>. Amostras em quarentena ficam de fora — elas esperam
    /// julgamento, e usá-las para reconhecer seria justamente deixar a
    /// contaminação agir.
    /// </remarks>
    public static double Semelhanca(float[] vetor, PerfilDeVoz perfil)
    {
        var grupos = perfil.Amostras
            .Where(a => !a.Quarentena)
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
