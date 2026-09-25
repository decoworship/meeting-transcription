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
    /// Com qual motor de transcrição a identidade desta amostra foi formada.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Nulo é <see cref="Vozes.MotorClassico"/></b>, e não "desconhecido":
    /// toda amostra gravada antes de 03/09/2026 saiu do pipeline de dois motores,
    /// porque não havia outro. Ver <see cref="Vozes.MotorDe"/>.
    /// </para>
    /// <para>
    /// <b>Por que isto vale ser guardado.</b> Transcrever com o MOSS não
    /// contamina o banco — o pipeline só chama o <c>ReconhecerAsync</c>, que lê e
    /// não escreve. Mas <b>nomear</b> um falante olhando para uma transcrição do
    /// MOSS grava um vetor que atravessa para todas as reuniões seguintes, e
    /// retranscrever não desfaz. E o falante do MOSS é uma identidade
    /// <b>costurada</b>: se a <see cref="CosturaDeFalantes"/> fundiu duas
    /// pessoas, o vetor sai com as duas dentro, e o erro só aparece meses depois
    /// como "o app chamou a Vanessa de Carla".
    /// </para>
    /// <para>
    /// A origem não impede nada — ela permite <b>desfazer em bloco</b> se um dos
    /// caminhos se mostrar ruim. É a mesma lição do <see cref="Regras"/>: quando
    /// não se sabe quais amostras a regra velha estragou, o que salva é poder
    /// separar a geração inteira. É barato agora e caro depois.
    /// </para>
    /// </remarks>
    [JsonPropertyName("motor")] public string? Motor { get; init; }

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

    /// <summary>
    /// Acima disto, é a mesma pessoa — <b>entre</b> reuniões.
    /// </summary>
    /// <remarks>
    /// <para>
    /// O limiar do app Python, mantido: ele governa o <b>banco de vozes</b>, que
    /// é o caso difícil. Outro dia, outro fone, outra sala, e do outro lado uma
    /// biblioteca com dezenas de pessoas — errar aqui grava o nome de alguém na
    /// fala de outra pessoa, e a transcrição sai plausível e errada.
    /// </para>
    /// <para>
    /// <b>Ele não era o mesmo problema do limiar de costura dentro da reunião</b>
    /// — que saiu em 18/09/2026 junto com a CosturaDeFalantes —, e por isso não
    /// era o mesmo
    /// número.</b> Até 03/09/2026 havia um limiar só para os dois usos, e a
    /// Fase 7 mediu duas vezes, independentemente, que 0,70 é apertado demais
    /// <b>dentro</b> da mesma reunião: o T3.1 achou 0,60 para nomear cedo, com
    /// 33 pontos de cobertura a mais e nenhum erro novo, e a costura achou 0,55
    /// (<c>docs/FASE7-RESULTADOS.md</c> §8.2, §8.3 e §11.3). A conclusão que os
    /// dados sustentam é um limiar mais frouxo para o caso fácil — <b>não</b> a
    /// troca desta constante, que ninguém mediu contra o banco.
    /// </para>
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

    /// <summary>O pipeline de dois motores: faster-whisper + pyannote.</summary>
    /// <remarks>
    /// O mesmo texto que a chave <c>motor_de_transcricao</c> do
    /// <c>app.json</c> guarda — <see cref="ConfiguracoesDoApp.MotorDeTranscricao"/>.
    /// Ter as duas pontas escrevendo a mesma palavra por conta própria é como
    /// uma delas fica para trás.
    /// </remarks>
    public const string MotorClassico = "classico";

    /// <summary>O motor de uma amostra; ausente é o clássico.</summary>
    /// <remarks>
    /// Ausente não é "desconhecido": antes de 03/09/2026 não havia outro motor,
    /// então a resposta é certa, não suposta. Ver <see cref="AmostraDeVoz.Motor"/>.
    /// </remarks>
    public static string MotorDe(AmostraDeVoz a) =>
        a.Motor is { Length: > 0 } m ? m : MotorClassico;

    /// <summary>
    /// O que se carimba numa amostra nova: <c>null</c> para o clássico.
    /// </summary>
    /// <remarks>
    /// Nulo e não <c>"classico"</c> de propósito. O campo existe para poder
    /// <b>desfazer em bloco</b> o que veio de um caminho novo, e escrevê-lo em
    /// toda amostra faria o arquivo de vozes de todo mundo mudar numa
    /// atualização em que nada mudou. Quem lê já sabe que ausente é o clássico
    /// (<see cref="MotorDe"/>), porque antes de 03/09/2026 não havia outro.
    /// </remarks>
    /// <remarks>
    /// <b>Sempre nulo desde 17/09/2026</b>, quando o MOSS saiu e voltou a haver
    /// um motor só. A função fica porque o <b>arquivo de vozes de quem
    /// experimentou o MOSS tem o campo escrito</b>, e quem lê precisa continuar
    /// tratando isso — o desfazer em bloco depende de saber de onde a amostra
    /// veio. Ver docs/CONVERGENCIA.md.
    /// </remarks>
    public static string? MotorAceitoNaAmostra(string? motor) => null;

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
    private AmostraDeVoz AprenderNaTrava(string pessoa, AmostraDeVoz amostra)
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
    private bool AprovarNaTrava(string pessoa, int indice, string? criadaEm = null)
    {
        if (!Achar(pessoa, indice, criadaEm, out var perfil)) return false;

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
    private bool EsquecerNaTrava(string pessoa, int indice, string? criadaEm = null)
    {
        if (!Achar(pessoa, indice, criadaEm, out var perfil)) return false;

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

    /// <summary>
    /// Junta duas pessoas numa só — as amostras de <paramref name="de"/> passam
    /// para <paramref name="para"/>, e <paramref name="de"/> deixa de existir.
    /// </summary>
    /// <returns>Quantas amostras mudaram de dono; 0 quando não havia o que juntar.</returns>
    /// <remarks>
    /// <para>
    /// <b>Por que isto precisa existir.</b> O nome de uma pessoa é digitado à
    /// mão, uma vez por reunião, e ninguém digita igual todas as vezes: "Andre
    /// Yuri" e "André Yuri", "Diego" e "Diego Lacerda". Cada grafia vira um
    /// perfil separado, e o reconhecimento passa a comparar contra dois
    /// centróides mais fracos em vez de um forte — quanto mais a pessoa é
    /// nomeada, pior ela é reconhecida. Sem uma forma de juntar, a única saída
    /// era esquecer um dos dois e perder as amostras.
    /// </para>
    /// <para>
    /// <b>Junta, e não copia.</b> As amostras carregam <see cref="Origem"/>,
    /// <see cref="AmostraDeVoz.Modelo"/> e <see cref="AmostraDeVoz.Motor"/>, e
    /// todos seguem intactos: é isso que mantém a auditoria possível depois — em
    /// particular a de desfazer em bloco o que veio de um motor novo.
    /// </para>
    /// <para>
    /// <b>Não confere se são a mesma voz, de propósito.</b> Quem manda aqui é a
    /// pessoa olhando a tela, que sabe quem é quem melhor que qualquer limiar; o
    /// app já tem o número da semelhança para <i>sugerir</i>, e sugerir é o
    /// <c>VOZ-1</c> do backlog. Decidir por ela seria fundir duas pessoas de voz
    /// parecida sem que ninguém tivesse pedido, que é o erro caro.
    /// </para>
    /// </remarks>
    private int JuntarNaTrava(string de, string para, int? amostras = null)
    {
        de = (de ?? "").Trim();
        para = (para ?? "").Trim();

        if (de.Length == 0 || para.Length == 0) return 0;
        if (string.Equals(de, para, StringComparison.Ordinal)) return 0;
        if (!_dados.Pessoas.TryGetValue(de, out var origem)) return 0;
        if (amostras is int n && n != origem.Amostras.Count) return 0;

        if (!_dados.Pessoas.TryGetValue(para, out var destino))
        {
            // Juntar numa pessoa que ainda não existe é renomear — e renomear é
            // exatamente o que resolve o caso de ter digitado o nome torto uma
            // única vez.
            destino = new PerfilDeVoz();
            _dados.Pessoas[para] = destino;
        }

        int quantas = origem.Amostras.Count;
        destino.Amostras.AddRange(origem.Amostras);
        _dados.Pessoas.Remove(de);

        Gravar();
        Registro.Escrever("vozes", $"'{de}' juntado a '{para}' — {quantas} amostra(s)");
        return quantas;
    }

    /// <summary>
    /// A amostra que a tela mostrou, e não outra que caiu no mesmo índice.
    /// </summary>
    /// <remarks>
    /// A tela manda o índice e o <c>criada_em</c> da amostra que desenhou. Entre
    /// o desenho e o clique a biblioteca pode ter mudado — uma transcrição
    /// aprendendo, outra ação que tirou uma amostra antes desta —, e aí o mesmo
    /// índice é outra voz. Sem o carimbo vale o índice, como sempre valeu.
    /// </remarks>
    private bool Achar(string pessoa, int indice, string? criadaEm, out PerfilDeVoz perfil)
    {
        if (!_dados.Pessoas.TryGetValue(pessoa ?? "", out perfil!)
            || indice < 0 || indice >= perfil.Amostras.Count)
            return false;
        return criadaEm is not { Length: > 0 }
               || string.Equals(perfil.Amostras[indice].CriadaEm, criadaEm, StringComparison.Ordinal);
    }

    /// <summary>
    /// O quanto uma amostra se parece com o resto do perfil dela — o número da
    /// fila de revisão.
    /// </summary>
    /// <returns>
    /// A mesma conta que decidiu a quarentena (<see cref="Semelhanca"/>, o melhor
    /// sub-perfil), contra o perfil <b>sem ela</b>. Nulo quando não há com o que
    /// comparar: a amostra é inerte, ou é a única do modelo dela.
    /// </returns>
    /// <remarks>Só lê: não muda a quarentena nem o reconhecimento.</remarks>
    public double? SemelhancaNoPerfil(string pessoa, int indice)
    {
        if (!Achar(pessoa, indice, null, out var perfil)) return null;
        var a = perfil.Amostras[indice];
        if (RegrasDe(a) != RegrasAtuais) return null;

        string modelo = ModeloDe(a);
        var resto = new PerfilDeVoz
        {
            Amostras = [.. perfil.Amostras.Where((o, i) => i != indice && Conta(o, modelo))],
        };
        if (resto.Amostras.Count == 0) return null;
        return Semelhanca(a.Vetor, resto, modelo);
    }

    /// <summary>
    /// Passa uma amostra para outra pessoa — "é outra pessoa", na revisão.
    /// </summary>
    /// <remarks>
    /// Sai da quarentena: quem ouviu o trecho decidiu de quem é a voz, e é esse
    /// julgamento que a quarentena esperava. Mover para quem não existe cria a
    /// pessoa; tirar a última amostra de alguém apaga o perfil vazio, como o
    /// <see cref="Esquecer"/> faz.
    /// </remarks>
    private bool MoverNaTrava(string pessoa, int indice, string para, string? criadaEm = null)
    {
        para = (para ?? "").Trim();
        // Estes dois são erro de quem pediu, e não a biblioteca ter mudado:
        // dizem o motivo em vez de cair no "false" da recusa por carimbo.
        if (para.Length == 0) throw new ArgumentException("falta o nome de para quem mover");
        if (string.Equals(pessoa, para, StringComparison.Ordinal))
            throw new ArgumentException($"a amostra já é de {para}");
        if (!Achar(pessoa, indice, criadaEm, out var perfil)) return false;

        var a = perfil.Amostras[indice];
        perfil.Amostras.RemoveAt(indice);
        if (perfil.Amostras.Count == 0) _dados.Pessoas.Remove(pessoa);

        if (!_dados.Pessoas.TryGetValue(para, out var destino))
        {
            destino = new PerfilDeVoz();
            _dados.Pessoas[para] = destino;
        }
        a.Quarentena = false;
        destino.Amostras.Add(a);

        Gravar();
        Registro.Escrever("vozes", $"amostra de '{pessoa}' movida para '{para}'");
        return true;
    }

    /// <summary>Esquece uma pessoa inteira, com os trechos de áudio dela.</summary>
    /// <param name="amostras">
    /// Quantas amostras a tela mostrou; se já não são essas, recusa — a pessoa
    /// mudou por baixo (o mesmo papel do <c>criada_em</c> nas ops por amostra).
    /// </param>
    private bool ApagarNaTrava(string pessoa, int? amostras = null)
    {
        if (!_dados.Pessoas.TryGetValue(pessoa ?? "", out var perfil)) return false;
        if (amostras is int n && n != perfil.Amostras.Count) return false;

        foreach (var a in perfil.Amostras)
        {
            if (a.Trecho is not { Length: > 0 }) continue;
            try { File.Delete(Path.Combine(_pasta, a.Trecho)); }
            catch (IOException) { /* o áudio some depois; o vetor já saiu */ }
        }
        _dados.Pessoas.Remove(pessoa!);

        Gravar();
        Registro.Escrever("vozes", $"'{pessoa}' apagado — {perfil.Amostras.Count} amostra(s)");
        return true;
    }

    /// <summary>
    /// Pares de pessoas que parecem a mesma — o <c>VOZ-1</c>, "Élio" e "Elio".
    /// </summary>
    /// <remarks>
    /// <para>
    /// O centróide de cada pessoa sai das amostras que contam no modelo de
    /// sempre, e o corte é o <see cref="LimiarDeReconhecimento"/>: acima dele o
    /// próprio reconhecimento já confundiria as duas, e é esse o sinal de que
    /// são uma. Só sugere — juntar continua sendo de quem lê (<see cref="Juntar"/>).
    /// </para>
    /// </remarks>
    public IReadOnlyList<(string A, string B, double Semelhanca)> Parecidos()
    {
        var centros = new List<(string Nome, float[] Centro)>();
        foreach (var nome in Pessoas())
        {
            var vetores = _dados.Pessoas[nome].Amostras
                .Where(a => Conta(a, ModeloDeVozPadrao)).Select(a => a.Vetor).ToList();
            if (vetores.Count > 0) centros.Add((nome, Centroide(vetores)));
        }

        var pares = new List<(string, string, double)>();
        for (int i = 0; i < centros.Count; i++)
            for (int j = i + 1; j < centros.Count; j++)
            {
                double s = Cosseno(centros[i].Centro, centros[j].Centro);
                if (s >= LimiarDeReconhecimento) pares.Add((centros[i].Nome, centros[j].Nome, s));
            }
        return [.. pares.OrderByDescending(p => p.Item3)];
    }

    /// <summary>
    /// A trava de toda escrita no vozes.json, no processo inteiro.
    /// </summary>
    /// <remarks>
    /// Cada instância guarda uma cópia da biblioteca, e gravar reescreve o
    /// arquivo inteiro a partir dela. Uma transcrição abre a sua no começo e
    /// aprende no fim; se a tela apagou uma pessoa no meio, a cópia velha a
    /// trazia de volta sem os trechos. Por isso toda mudança relê o arquivo
    /// dentro da trava e aplica sobre o que está no disco agora — o que ela
    /// decide (limiares, quarentena) continua o mesmo.
    /// </remarks>
    private static readonly object Trava = new();

    private T Mudando<T>(Func<T> mudanca)
    {
        lock (Trava)
        {
            _dados = Carregar();
            return mudanca();
        }
    }

    /// <inheritdoc cref="AprenderNaTrava"/>
    public AmostraDeVoz Aprender(string pessoa, AmostraDeVoz amostra) => Mudando(() => AprenderNaTrava(pessoa, amostra));

    /// <inheritdoc cref="AprovarNaTrava"/>
    public bool Aprovar(string pessoa, int indice, string? criadaEm = null) => Mudando(() => AprovarNaTrava(pessoa, indice, criadaEm));

    /// <inheritdoc cref="EsquecerNaTrava"/>
    public bool Esquecer(string pessoa, int indice, string? criadaEm = null) => Mudando(() => EsquecerNaTrava(pessoa, indice, criadaEm));

    /// <inheritdoc cref="MoverNaTrava"/>
    public bool Mover(string pessoa, int indice, string para, string? criadaEm = null) => Mudando(() => MoverNaTrava(pessoa, indice, para, criadaEm));

    /// <inheritdoc cref="ApagarNaTrava"/>
    public bool Apagar(string pessoa, int? amostras = null) => Mudando(() => ApagarNaTrava(pessoa, amostras));

    /// <inheritdoc cref="JuntarNaTrava"/>
    public int Juntar(string de, string para, int? amostras = null) => Mudando(() => JuntarNaTrava(de, para, amostras));

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
