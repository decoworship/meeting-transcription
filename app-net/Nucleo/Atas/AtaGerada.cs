using System.Text.Json;
using System.Text.Json.Serialization;

namespace MeetingApp.Nucleo.Atas;

/// <summary>
/// Uma decisão, um ponto em aberto ou um risco, com a seção onde ele mora.
/// </summary>
/// <remarks>
/// <para>
/// Era <c>string</c> até a ata por tema (ATA-5). A seção é o que deixa o redator
/// escrever "Decidido" dentro do tema, em vez de numa lista no fim que repete o
/// tema de novo.
/// </para>
/// <para>
/// A conversão de <c>string</c> existe para o código que monta ata à mão — a
/// dobra de seções, os testes — continuar legível. O JSON antigo, com as listas
/// em <c>string[]</c>, abre pelo <see cref="ConversorDeItemDaAta"/>.
/// </para>
/// </remarks>
[JsonConverter(typeof(ConversorDeItemDaAta))]
public sealed record ItemDaAta(string Texto, string? Secao = null)
{
    public static implicit operator ItemDaAta(string texto) => new(texto);

    public override string ToString() => Texto;
}

/// <summary>Lê o item como texto (ata antiga) ou objeto; escreve sempre objeto.</summary>
public sealed class ConversorDeItemDaAta : JsonConverter<ItemDaAta>
{
    public override ItemDaAta Read(ref Utf8JsonReader leitor, Type tipo, JsonSerializerOptions opcoes)
    {
        if (leitor.TokenType == JsonTokenType.String)
            return new ItemDaAta(leitor.GetString() ?? "");

        if (leitor.TokenType != JsonTokenType.StartObject)
            throw new JsonException("item de ata: esperava texto ou objeto");

        string texto = "";
        string? secao = null;
        while (leitor.Read() && leitor.TokenType != JsonTokenType.EndObject)
        {
            string nome = leitor.GetString() ?? "";
            leitor.Read();
            string? valor = leitor.TokenType == JsonTokenType.String ? leitor.GetString() : null;
            if (leitor.TokenType is JsonTokenType.StartObject or JsonTokenType.StartArray)
                leitor.Skip();
            if (nome == "texto") texto = valor ?? "";
            else if (nome == "secao") secao = valor;
        }
        return new ItemDaAta(texto, string.IsNullOrWhiteSpace(secao) ? null : secao.Trim());
    }

    public override void Write(Utf8JsonWriter escritor, ItemDaAta item, JsonSerializerOptions opcoes)
    {
        escritor.WriteStartObject();
        escritor.WriteString("texto", item.Texto);
        if (item.Secao is { Length: > 0 } s) escritor.WriteString("secao", s);
        escritor.WriteEndObject();
    }
}

/// <summary>Uma seção do corpo da ata — a parte que muda de tipo para tipo.</summary>
public sealed class SecaoDaAta
{
    [JsonPropertyName("titulo")] public string Titulo { get; set; } = "";

    /// <summary>"em andamento", "concluído"… Vazio quando a seção não tem estado.</summary>
    [JsonPropertyName("situacao")] public string? Situacao { get; set; }

    [JsonPropertyName("texto")] public string Texto { get; set; } = "";

    /// <summary>O assunto maior de que esta seção é parte ("Pedido Sugerido").</summary>
    [JsonPropertyName("grupo")] public string? Grupo { get; set; }

    /// <summary>A área ou pessoa dona do tema, quando foi dita ou está nas notas.</summary>
    [JsonPropertyName("responsavel")] public string? Responsavel { get; set; }
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

    /// <summary>O título da seção onde a ação mora; nulo, vai para a lista geral.</summary>
    [JsonPropertyName("secao")] public string? Secao { get; set; }
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
    [JsonPropertyName("decisoes")] public List<ItemDaAta> Decisoes { get; set; } = [];
    [JsonPropertyName("acoes")] public List<AcaoDaAta> Acoes { get; set; } = [];
    [JsonPropertyName("pontos_em_aberto")] public List<ItemDaAta> PontosEmAberto { get; set; } = [];
    [JsonPropertyName("riscos")] public List<ItemDaAta> Riscos { get; set; } = [];

    /// <summary>
    /// O que o verificador mexeu e o que a conferência de cobertura achou.
    /// </summary>
    /// <remarks>
    /// <b>O modelo preenche, e o <see cref="VerificadorDeAta"/> descarta o que
    /// ele escreveu.</b> Não é desperdício por acidente: o campo continua no
    /// esquema porque tirá-lo faria o modelo espalhar as observações pelas
    /// outras seções, que é pior. Ele escreve, e a lista é substituída pela
    /// medição.
    /// <para>
    /// O motivo está na FASE6 §1.6: a seção afirmava que todos os números foram
    /// registrados e listava onze que faltavam, na mesma página. Uma medição e
    /// uma opinião sobre a medição, sem como distinguir qual é a fonte.
    /// </para>
    /// </remarks>
    [JsonPropertyName("observacoes")] public List<string> Observacoes { get; set; } = [];

    public static AtaGerada? DeJson(string json)
    {
        var ata = JsonSerializer.Deserialize(json, AtaJson.Default.AtaGerada);
        if (ata is null) return null;
        // O esquema por tema exige "secao", e o modelo escreve "" no item sem
        // tema. Vazio e ausente são a mesma coisa daqui para a frente.
        foreach (var a in ata.Acoes)
            if (string.IsNullOrWhiteSpace(a.Secao)) a.Secao = null;
        return ata;
    }

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

    /// <summary>
    /// O esquema da ata por tema: os itens dizem a seção, a seção diz grupo e dono.
    /// </summary>
    /// <remarks>
    /// <c>secao</c> é obrigatória nos itens, e pode vir vazia: obrigar o campo é
    /// o que faz o modelo pensar no tema de cada item, e o vazio é a saída
    /// honesta para o que não pertence a nenhum. <c>grupo</c> e
    /// <c>responsavel</c> da seção são opcionais — não inventar dono é regra.
    /// </remarks>
    public const string EsquemaPorTema = """
    {
      "type": "object",
      "properties": {
        "resumo": {
          "type": "string",
          "description": "No máximo três frases. Os temas abaixo detalham; não os repita aqui."
        },
        "secoes": {
          "type": "array",
          "description": "Um tema por seção, na ordem em que a reunião os tratou. Nunca uma seção com subtítulos dentro.",
          "items": {
            "type": "object",
            "properties": {
              "titulo": { "type": "string", "description": "O nome curto do tema." },
              "situacao": {
                "type": "string",
                "description": "Só quando a seção tiver estado. Vazio se não tiver."
              },
              "texto": {
                "type": "string",
                "description": "O contexto do tema em uma ou duas frases. Não repita as decisões, ações, pontos em aberto e riscos, que vão nos campos próprios."
              },
              "grupo": {
                "type": "string",
                "description": "O assunto maior, quando várias seções são partes dele. Mesmo texto em todas."
              },
              "responsavel": {
                "type": "string",
                "description": "A área ou pessoa dona do tema (Comercial, Trade, um nome), só se foi dita na reunião ou está nas notas. Nunca 'nosso' nem 'cliente', que é o lado da ação."
              }
            },
            "required": ["titulo", "texto"],
            "additionalProperties": false
          }
        },
        "decisoes": {
          "type": "array",
          "description": "Só o que teve conclusão explícita. Hipótese não é decisão.",
          "items": {
            "type": "object",
            "properties": {
              "texto": { "type": "string" },
              "secao": {
                "type": "string",
                "description": "O título exato da seção a que o item pertence, ou vazio."
              }
            },
            "required": ["texto", "secao"],
            "additionalProperties": false
          }
        },
        "acoes": {
          "type": "array",
          "items": {
            "type": "object",
            "properties": {
              "acao": { "type": "string", "description": "O que fazer, curto. O tema já diz para quê." },
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
              },
              "secao": {
                "type": "string",
                "description": "O título exato da seção a que a ação pertence, ou vazio."
              }
            },
            "required": ["acao", "responsavel", "prazo", "lado", "secao"],
            "additionalProperties": false
          }
        },
        "pontos_em_aberto": {
          "type": "array",
          "description": "O que foi levantado e não concluiu, com a pergunta que ficou.",
          "items": {
            "type": "object",
            "properties": {
              "texto": { "type": "string" },
              "secao": {
                "type": "string",
                "description": "O título exato da seção a que o item pertence, ou vazio."
              }
            },
            "required": ["texto", "secao"],
            "additionalProperties": false
          }
        },
        "riscos": {
          "type": "array",
          "description": "Só quando alguém sinalizou preocupação. Vazio se ninguém sinalizou.",
          "items": {
            "type": "object",
            "properties": {
              "texto": { "type": "string" },
              "secao": {
                "type": "string",
                "description": "O título exato da seção a que o item pertence, ou vazio."
              }
            },
            "required": ["texto", "secao"],
            "additionalProperties": false
          }
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

    public static string EsquemaPara(bool agrupa) => agrupa ? EsquemaPorTema : Esquema;
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
