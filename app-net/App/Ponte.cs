using System.Text.Json;
using System.Text.Json.Serialization;
using MeetingApp.Nucleo;

namespace MeetingApp.App;

/// <summary>
/// O que a página pede ao núcleo, e o que o núcleo devolve.
/// </summary>
/// <remarks>
/// Mesma forma do contrato com os motores (docs/SIDECAR.md): JSON com um campo
/// <c>op</c> e um <c>id</c> que volta na resposta. Ter um só formato de
/// mensagem no projeto inteiro significa um só jeito de depurar quando algo não
/// chega do outro lado.
/// </remarks>
internal sealed class Pedido
{
    [JsonPropertyName("id")] public int Id { get; init; }
    [JsonPropertyName("op")] public string? Op { get; init; }
    [JsonPropertyName("gravacao")] public string? Gravacao { get; init; }
}

internal sealed class Resposta
{
    [JsonPropertyName("id")] public int Id { get; init; }
    [JsonPropertyName("erro")] public string? Erro { get; init; }
    [JsonPropertyName("gravacoes")] public List<GravacaoResumo>? Gravacoes { get; init; }
}

/// <summary>Uma gravação como a lista precisa mostrá-la.</summary>
/// <remarks>
/// Os avisos vêm prontos do núcleo, e não como campos crus para a página
/// interpretar: decidir que 3% de conteúdo útil é um problema é regra de
/// produto, e regra de produto fica de um lado só.
/// </remarks>
internal sealed class GravacaoResumo
{
    [JsonPropertyName("nome")] public required string Nome { get; init; }
    [JsonPropertyName("caminho")] public required string Caminho { get; init; }
    [JsonPropertyName("duracao_s")] public double DuracaoS { get; init; }
    [JsonPropertyName("titulo")] public string? Titulo { get; init; }
    [JsonPropertyName("participantes")] public int Participantes { get; init; }
    [JsonPropertyName("transcrita")] public bool Transcrita { get; init; }
    [JsonPropertyName("avisos")] public List<string> Avisos { get; init; } = [];
}

[JsonSourceGenerationOptions(DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(Pedido))]
[JsonSerializable(typeof(Resposta))]
internal sealed partial class PonteJsonBase : JsonSerializerContext;

internal static class PonteJson
{
    // Acento literal, como em todo JSON deste projeto: o nome do dispositivo e o
    // título da reunião passam por aqui.
    public static readonly PonteJsonBase Default = new(new JsonSerializerOptions
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    });
}

/// <summary>Atende os pedidos da página.</summary>
internal sealed class Ponte(string pastaDasGravacoes)
{
    public string Atender(string mensagem)
    {
        Pedido? p;
        try
        {
            p = JsonSerializer.Deserialize(mensagem, PonteJson.Default.Pedido);
        }
        catch (JsonException e)
        {
            return Serializar(new Resposta { Id = 0, Erro = $"pedido ilegível: {e.Message}" });
        }
        if (p is null) return Serializar(new Resposta { Id = 0, Erro = "pedido vazio" });

        try
        {
            return p.Op switch
            {
                "gravacoes" => Serializar(new Resposta { Id = p.Id, Gravacoes = Listar() }),
                _ => Serializar(new Resposta { Id = p.Id, Erro = $"operação desconhecida: {p.Op}" }),
            };
        }
        catch (Exception e)
        {
            // A página precisa poder mostrar o erro; derrubar a janela por causa
            // de uma pasta ilegível seria pior que a falha original.
            return Serializar(new Resposta { Id = p.Id, Erro = e.Message });
        }
    }

    private static string Serializar(Resposta r) =>
        JsonSerializer.Serialize(r, PonteJson.Default.Resposta);

    /// <summary>As gravações que o gravador deixou, mais recentes primeiro.</summary>
    private List<GravacaoResumo> Listar()
    {
        if (!Directory.Exists(pastaDasGravacoes)) return [];

        var lista = new List<GravacaoResumo>();
        foreach (string pasta in Directory.EnumerateDirectories(pastaDasGravacoes))
        {
            string meta = Path.Combine(pasta, "meta.json");
            if (!File.Exists(meta)) continue;

            try
            {
                lista.Add(LerResumo(pasta, meta));
            }
            catch (Exception)
            {
                // Uma gravação ilegível não pode esconder as outras da lista.
            }
        }
        return [.. lista.OrderByDescending(g => g.Nome)];
    }

    private static GravacaoResumo LerResumo(string pasta, string caminhoMeta)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(caminhoMeta));
        var raiz = doc.RootElement;

        double duracao = raiz.TryGetProperty("duration_s", out var d) ? d.GetDouble() : 0;
        var avisos = new List<string>();

        if (raiz.TryGetProperty("tracks", out var faixas))
        {
            foreach (var faixa in faixas.EnumerateObject())
            {
                string nome = faixa.Name == "mic" ? "microfone" : "áudio do sistema";
                var t = faixa.Value;

                if (t.TryGetProperty("no_audio", out var sem) && sem.GetBoolean())
                    avisos.Add($"O {nome} não teve áudio nenhum.");
                else if (t.TryGetProperty("usable_pct", out var util) && util.GetDouble() < 20)
                    avisos.Add($"O {nome} tem só {util.GetDouble():F0}% de conteúdo útil.");

                // Campo novo do gravador nativo, que até agora ninguém lia.
                if (t.TryGetProperty("disconnected", out var caiu) && caiu.GetBoolean())
                    avisos.Add($"O dispositivo do {nome} caiu durante a gravação.");
            }
        }

        string? titulo = null;
        int participantes = 0;
        if (raiz.TryGetProperty("meeting", out var reuniao))
        {
            if (reuniao.TryGetProperty("title", out var t) && t.ValueKind == JsonValueKind.String)
                titulo = t.GetString();
            if (reuniao.TryGetProperty("attendees", out var a) && a.ValueKind == JsonValueKind.Array)
                participantes = a.GetArrayLength();
        }

        return new GravacaoResumo
        {
            Nome = Path.GetFileName(pasta),
            Caminho = pasta,
            DuracaoS = duracao,
            Titulo = titulo,
            Participantes = participantes,
            Transcrita = File.Exists(Path.Combine(pasta, "transcricao.json")),
            Avisos = avisos,
        };
    }
}
