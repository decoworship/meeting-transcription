using System.Text.Json;
using System.Text.Json.Serialization;

namespace MeetingApp.Nucleo;

/// <summary>
/// O que o usuário diz sobre a reunião <b>antes</b> de ela ser transcrita.
/// </summary>
/// <remarks>
/// <para>
/// Mora em <c>reuniao.json</c>, ao lado de <c>mic.wav</c> e <c>meta.json</c>.
/// Nasceu de um defeito relatado em 13/08/2026: escolher cliente e projeto na
/// tela de preparo, sair e voltar, e encontrar os campos vazios. O dado existia
/// só dentro de <c>transcricao.json</c> — um arquivo que, por definição, ainda
/// não existe quando se está preparando a transcrição.
/// </para>
/// <para>
/// <b>Arquivo próprio, e não um campo novo no <c>meta.json</c>.</b> Aquele é do
/// gravador, tem schema congelado e é escrito enquanto a reunião acontece
/// (<see cref="MeetingRecorder.Core.Meta"/>); este é do usuário e muda depois,
/// inclusive com a gravação parada. Misturá-los faria a tela de preparo escrever
/// no arquivo que o gravador considera dele.
/// </para>
/// <para>
/// É também onde o tipo de ata vai morar quando a Fase 3 chegar no item 3
/// (FASE3.md §4): "esta reunião é uma sprint" é da mesma natureza que "esta
/// reunião é do projeto X".
/// </para>
/// </remarks>
public sealed class DadosDaReuniao
{
    [JsonPropertyName("cliente")] public string? Cliente { get; set; }
    [JsonPropertyName("projeto")] public string? Projeto { get; set; }

    /// <summary>Quando foi mexido pela última vez, em ISO. Só para diagnóstico.</summary>
    [JsonPropertyName("atualizado_em")] public string? AtualizadoEm { get; set; }

    public const string NomeDoArquivo = "reuniao.json";

    public bool Vazio => Cliente is not { Length: > 0 } && Projeto is not { Length: > 0 };

    /// <summary>
    /// Lê o que a gravação sabe de si, caindo na transcrição quando existir.
    /// </summary>
    /// <remarks>
    /// A ordem importa: o <c>reuniao.json</c> é o que a pessoa escolheu por
    /// último, e a transcrição guarda o que valia quando ela rodou. Para as
    /// gravações transcritas <b>antes</b> deste arquivo existir, a segunda fonte
    /// é a única — e é o que faz o histórico aparecer preenchido em vez de em
    /// branco.
    /// </remarks>
    public static DadosDaReuniao Ler(string pastaDaGravacao)
    {
        try
        {
            string caminho = Path.Combine(pastaDaGravacao, NomeDoArquivo);
            if (File.Exists(caminho))
            {
                var lido = JsonSerializer.Deserialize(
                    File.ReadAllText(caminho), ReuniaoJson.Default.DadosDaReuniao);
                if (lido is not null && !lido.Vazio) return lido;
            }
        }
        catch (Exception)
        {
            // Um JSON corrompido não pode impedir de abrir a reunião: o pior
            // caso aceitável é a tela pedir cliente e projeto de novo.
        }

        return DaTranscricao(pastaDaGravacao);
    }

    private static DadosDaReuniao DaTranscricao(string pastaDaGravacao)
    {
        try
        {
            string caminho = Path.Combine(pastaDaGravacao, "transcricao.json");
            if (!File.Exists(caminho)) return new DadosDaReuniao();

            using var doc = JsonDocument.Parse(File.ReadAllText(caminho));
            return new DadosDaReuniao
            {
                Cliente = Texto(doc.RootElement, "client"),
                Projeto = Texto(doc.RootElement, "project"),
            };
        }
        catch (Exception)
        {
            return new DadosDaReuniao();
        }
    }

    private static string? Texto(JsonElement raiz, string campo) =>
        raiz.TryGetProperty(campo, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString() : null;

    /// <summary>Grava o vínculo, ou apaga o arquivo quando não sobrou nada nele.</summary>
    public void Salvar(string pastaDaGravacao)
    {
        string caminho = Path.Combine(pastaDaGravacao, NomeDoArquivo);
        if (Vazio)
        {
            // Limpar os dois campos é dizer "esta reunião não é de projeto
            // nenhum". Um arquivo com dois nulos diria a mesma coisa de forma
            // mais confusa para quem abrir a pasta.
            if (File.Exists(caminho)) File.Delete(caminho);
            return;
        }

        AtualizadoEm = DateTimeOffset.Now.ToString("o");
        File.WriteAllText(caminho,
            JsonSerializer.Serialize(this, ReuniaoJson.Default.DadosDaReuniao));
    }
}

/// <summary>
/// O serializador gerado em tempo de compilação para o <c>reuniao.json</c>.
/// </summary>
/// <remarks>
/// <b>Não trocar por <c>JsonSerializer.Serialize&lt;T&gt;</c> com opções.</b> O
/// app é publicado com <c>PublishTrimmed</c>, e a versão por reflexão é erro de
/// build ali (IL2026) — compila e testa bem no loop de desenvolvimento, e
/// reprova só na publicação. Mesmo caminho do <c>MetaJson</c> do gravador.
/// </remarks>
[JsonSourceGenerationOptions(
    WriteIndented = true,
    // Acento literal, como todo JSON deste projeto: nome de cliente tem acento.
    UseStringEnumConverter = false,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(DadosDaReuniao))]
internal sealed partial class ReuniaoJsonBase : JsonSerializerContext;

internal static class ReuniaoJson
{
    public static readonly ReuniaoJsonBase Default = new(new JsonSerializerOptions
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    });
}
