using System.Text.Json;
using System.Text.Json.Serialization;

namespace MeetingApp.Nucleo.Atas;

/// <summary>Uma seção do corpo da ata — a parte que muda de tipo para tipo.</summary>
public sealed class SecaoDaAta
{
    [JsonPropertyName("titulo")] public string Titulo { get; set; } = "";

    /// <summary>"em andamento", "concluído"… Vazio quando a seção não tem estado.</summary>
    [JsonPropertyName("situacao")] public string? Situacao { get; set; }

    [JsonPropertyName("texto")] public string Texto { get; set; } = "";
}

/// <summary>Um item de ação: o que a ata existe para cobrar depois.</summary>
public sealed class AcaoDaAta
{
    [JsonPropertyName("acao")] public string Acao { get; set; } = "";

    /// <summary>Nome de quem faz, ou <c>[responsável a definir]</c>.</summary>
    [JsonPropertyName("responsavel")] public string Responsavel { get; set; } = "";

    [JsonPropertyName("prazo")] public string Prazo { get; set; } = "";

    /// <summary>
    /// "nosso" ou "cliente".
    /// </summary>
    /// <remarks>
    /// Separar pendências por lado é, nas palavras da própria skill, "a razão de
    /// existir" da ata de update com cliente: um item que depende do cliente,
    /// misturado na lista geral, desaparece. Nos tipos internos todos os itens
    /// caem em "nosso", e o redator omite a divisão.
    /// </remarks>
    [JsonPropertyName("lado")] public string Lado { get; set; } = "nosso";
}

/// <summary>
/// A ata como o modelo a devolve: campos, não prosa.
/// </summary>
/// <remarks>
/// <para>
/// <b>Um esquema universal, e não um por tipo</b> (ATA.md §3). O que muda entre
/// um update com cliente e uma daily é <em>quais seções</em> aparecem e o que se
/// escreve nelas — e isso vem do arquivo do tipo, não da forma do JSON. É o que
/// permite customizar um tipo escrevendo Markdown, sem escrever JSON Schema.
/// </para>
/// <para>
/// O que é verificável tem campo próprio: ações com dono e prazo, decisões,
/// pontos em aberto. "Este item tem dono?" é pergunta sobre um campo; sobre um
/// parágrafo, não é pergunta nenhuma.
/// </para>
/// </remarks>
public sealed class AtaGerada
{
    [JsonPropertyName("resumo")] public string Resumo { get; set; } = "";
    [JsonPropertyName("secoes")] public List<SecaoDaAta> Secoes { get; set; } = [];
    [JsonPropertyName("decisoes")] public List<string> Decisoes { get; set; } = [];
    [JsonPropertyName("acoes")] public List<AcaoDaAta> Acoes { get; set; } = [];
    [JsonPropertyName("pontos_em_aberto")] public List<string> PontosEmAberto { get; set; } = [];
    [JsonPropertyName("riscos")] public List<string> Riscos { get; set; } = [];

    /// <summary>
    /// O que o modelo achou digno de nota sobre a transcrição — e, depois, o que
    /// o verificador mexeu.
    /// </summary>
    [JsonPropertyName("observacoes")] public List<string> Observacoes { get; set; } = [];

    public static AtaGerada? DeJson(string json) =>
        JsonSerializer.Deserialize(json, AtaJson.Default.AtaGerada);

    public string ParaJson() => JsonSerializer.Serialize(this, AtaJson.Default.AtaGerada);

    /// <summary>
    /// O JSON Schema que prende a saída do modelo.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Escrito à mão, e não gerado por reflexão: o esquema tem descrições que
    /// <b>instruem</b> o modelo ("se não houver dono, escreva [responsável a
    /// definir]"), e essas frases são parte do prompt tanto quanto as regras da
    /// skill. Um gerador as jogaria fora.
    /// </para>
    /// <para>
    /// <c>additionalProperties: false</c> em tudo: sem isso o modelo inventa
    /// campos, e o que ele inventa não é lido por ninguém.
    /// </para>
    /// </remarks>
    public const string Esquema = """
    {
      "type": "object",
      "properties": {
        "resumo": {
          "type": "string",
          "description": "3 a 5 linhas: o que avançou, o que travou, o que precisa de decisão."
        },
        "secoes": {
          "type": "array",
          "description": "O corpo da ata, nas seções que a estrutura pedir, na ordem dela.",
          "items": {
            "type": "object",
            "properties": {
              "titulo": { "type": "string" },
              "situacao": {
                "type": "string",
                "description": "Só quando a seção tiver estado. Vazio se não tiver."
              },
              "texto": { "type": "string" }
            },
            "required": ["titulo", "texto"],
            "additionalProperties": false
          }
        },
        "decisoes": {
          "type": "array",
          "description": "Só o que teve conclusão explícita. Hipótese não é decisão.",
          "items": { "type": "string" }
        },
        "acoes": {
          "type": "array",
          "items": {
            "type": "object",
            "properties": {
              "acao": { "type": "string" },
              "responsavel": {
                "type": "string",
                "description": "Nome de quem faz. Se ninguém foi nomeado, escreva exatamente: [responsável a definir]"
              },
              "prazo": {
                "type": "string",
                "description": "Se não houve prazo, escreva exatamente: [prazo a definir]"
              },
              "lado": {
                "type": "string",
                "enum": ["nosso", "cliente"],
                "description": "De quem depende a ação."
              }
            },
            "required": ["acao", "responsavel", "prazo", "lado"],
            "additionalProperties": false
          }
        },
        "pontos_em_aberto": {
          "type": "array",
          "description": "O que foi levantado e não concluiu, com a pergunta que ficou.",
          "items": { "type": "string" }
        },
        "riscos": {
          "type": "array",
          "description": "Só quando alguém sinalizou preocupação. Vazio se ninguém sinalizou.",
          "items": { "type": "string" }
        },
        "observacoes": {
          "type": "array",
          "description": "Trechos inaudíveis, números divergentes, nomes com grafia incerta.",
          "items": { "type": "string" }
        }
      },
      "required": ["resumo", "secoes", "decisoes", "acoes", "pontos_em_aberto", "riscos", "observacoes"],
      "additionalProperties": false
    }
    """;
}

[JsonSourceGenerationOptions(DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(AtaGerada))]
internal sealed partial class AtaJsonBase : JsonSerializerContext;

/// <remarks>
/// Contexto gerado em compilação, como o resto do projeto: a versão por
/// reflexão é erro de build sob <c>PublishTrimmed</c> e passa despercebida até a
/// publicação — ver <see cref="DadosDaReuniao"/>, onde isso já mordeu uma vez.
/// </remarks>
internal static class AtaJson
{
    public static readonly AtaJsonBase Default = new(new JsonSerializerOptions
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    });
}
