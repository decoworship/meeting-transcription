using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace MeetingApp.Nucleo;

/// <summary>As preferências de transcrição guardadas por projeto.</summary>
/// <remarks>
/// Os nomes das chaves são os do app Python, e não os nossos: o arquivo é o
/// mesmo, e enquanto os dois convivem quem editar num tem que ser lido pelo
/// outro. Ver <c>src/web/projects.py</c>, <c>SETTINGS_KEYS</c>.
/// </remarks>
public sealed class PreferenciasDoProjeto
{
    [JsonPropertyName("language")] public string? Language { get; set; }
    [JsonPropertyName("model_size")] public string? ModelSize { get; set; }
    [JsonPropertyName("engine")] public string? Engine { get; set; }
    [JsonPropertyName("diarization")] public bool? Diarization { get; set; }

    [JsonPropertyName("condition_on_previous_text")]
    public bool? ConditionOnPreviousText { get; set; }

    [JsonPropertyName("diar_model")] public string? DiarModel { get; set; }

    /// <summary>
    /// O vocabulário do projeto.
    /// </summary>
    /// <remarks>
    /// Continua chamado <c>initial_prompt</c> no arquivo por compatibilidade,
    /// mas há duas gerações de decisão em cima disso: o app atual já o envia
    /// como <c>hotwords</c>, e a correção fonética a jusante tirou o teto de
    /// 224 tokens que ele tinha. Renomear a chave quebraria o app Python, que
    /// ainda é a ferramenta de produção.
    /// </remarks>
    [JsonPropertyName("initial_prompt")] public string? InitialPrompt { get; set; }
}

/// <summary>
/// Clientes, projetos e as preferências de cada um.
/// </summary>
/// <remarks>
/// <para>
/// Mesmo arquivo e mesmo formato do app Python
/// (<c>~/.meeting-transcription/projects.json</c>), porque os dois convivem
/// durante a migração e quem cadastrar um cliente num tem que vê-lo no outro.
/// </para>
/// <para>
/// A escrita é feita sobre o JSON existente, com <see cref="JsonNode"/> em vez
/// de um objeto tipado: assim nenhuma chave que este código não modela — as que
/// o app Python vier a acrescentar — se perde no caminho. É a mesma decisão do
/// token do Google no gravador.
/// </para>
/// </remarks>
public sealed class Projetos
{
    public static string CaminhoPadrao => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".meeting-transcription", "projects.json");

    private readonly string _caminho;
    private JsonNode _raiz;

    public Projetos(string? caminho = null)
    {
        _caminho = caminho ?? CaminhoPadrao;
        _raiz = Carregar(_caminho);
    }

    private static JsonNode Carregar(string caminho)
    {
        try
        {
            if (File.Exists(caminho) &&
                JsonNode.Parse(File.ReadAllText(caminho)) is JsonObject o &&
                o["clients"] is JsonObject)
                return o;
        }
        catch (Exception)
        {
            // Arquivo ilegível não pode impedir de transcrever: cai no vazio, e
            // o primeiro salvamento reescreve. Mesma postura do settings.json.
        }
        return new JsonObject { ["clients"] = new JsonObject() };
    }

    private JsonObject Clientes => (JsonObject)_raiz["clients"]!;

    /// <summary>Os clientes, em ordem alfabética.</summary>
    public List<string> ListarClientes() =>
        [.. Clientes.Select(p => p.Key).Order(StringComparer.CurrentCultureIgnoreCase)];

    /// <summary>Os projetos de um cliente, em ordem alfabética.</summary>
    public List<string> ListarProjetos(string cliente)
    {
        if (Clientes[cliente] is not JsonObject c || c["projects"] is not JsonObject p)
            return [];
        return [.. p.Select(x => x.Key).Order(StringComparer.CurrentCultureIgnoreCase)];
    }

    public PreferenciasDoProjeto? Preferencias(string cliente, string projeto)
    {
        if (Clientes[cliente] is not JsonObject c
            || c["projects"] is not JsonObject ps
            || ps[projeto] is not JsonObject s)
            return null;

        return s.Deserialize(ProjetosJson.Default.PreferenciasDoProjeto);
    }

    /// <summary>
    /// Grava as preferências, criando cliente e projeto se ainda não existem.
    /// </summary>
    /// <remarks>
    /// Criar por escrita, e não por um "novo cliente" separado, é o que permite
    /// digitar um nome inédito no campo e sair transcrevendo — que é como o app
    /// Python funciona e o que o usuário pediu para manter.
    /// </remarks>
    public void Salvar(string cliente, string projeto, PreferenciasDoProjeto prefs)
    {
        if (string.IsNullOrWhiteSpace(cliente) || string.IsNullOrWhiteSpace(projeto)) return;

        if (Clientes[cliente] is not JsonObject c)
        {
            c = new JsonObject { ["projects"] = new JsonObject() };
            Clientes[cliente] = c;
        }
        if (c["projects"] is not JsonObject ps)
        {
            ps = new JsonObject();
            c["projects"] = ps;
        }

        // Sobre o que já existia: o app Python pode ter escrito chaves que este
        // código não conhece, e sobrescrever o objeto inteiro as apagaria.
        var alvo = ps[projeto] as JsonObject ?? new JsonObject();
        var novo = JsonSerializer.SerializeToNode(prefs, ProjetosJson.Default.PreferenciasDoProjeto)!
            .AsObject();
        foreach (var (chave, valor) in novo)
            alvo[chave] = valor?.DeepClone();
        ps[projeto] = alvo;

        Gravar();
    }

    private void Gravar()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_caminho))!);

        // Escrita atômica: um desligamento no meio não pode deixar o cadastro
        // pela metade.
        string tmp = _caminho + ".tmp";
        File.WriteAllText(tmp, _raiz.ToJsonString(new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        }));
        File.Move(tmp, _caminho, overwrite: true);
        _raiz = Carregar(_caminho);
    }
}

[JsonSourceGenerationOptions(DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(PreferenciasDoProjeto))]
internal sealed partial class ProjetosJson : JsonSerializerContext;
