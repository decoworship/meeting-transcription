using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MeetingApp.Nucleo;

/// <summary>
/// Quem estava na reunião, em <c>participantes.json</c> ao lado das notas.
/// </summary>
/// <remarks>
/// <para>
/// Os nomes vêm de um print (UI-12): o OCR lê, <see cref="NomesDoPrint"/>
/// separa os nomes, e a pessoa confirma. É a única fonte do app que diz quem
/// <em>entrou</em> — a agenda diz quem foi chamado, e a transcrição, quem falou.
/// </para>
/// <para>
/// Arquivo próprio, e não campo no <c>meta.json</c>: aquele é do gravador e tem
/// schema congelado (ver <see cref="Notas"/>, que fez a mesma escolha).
/// </para>
/// </remarks>
public static class Participantes
{
    public const string NomeDoArquivo = "participantes.json";

    public static string Caminho(string pastaDaGravacao) =>
        Path.Combine(pastaDaGravacao, NomeDoArquivo);

    /// <summary>Os nomes guardados, na ordem em que entraram. Nunca lança.</summary>
    public static List<string> Ler(string pastaDaGravacao)
    {
        try
        {
            string caminho = Caminho(pastaDaGravacao);
            if (!File.Exists(caminho)) return [];
            var arquivo = JsonSerializer.Deserialize(File.ReadAllText(caminho),
                                                     ParticipantesJson.Default.ArquivoDeParticipantes);
            return [.. (arquivo?.Nomes ?? []).Where(n => n is { Length: > 0 })];
        }
        catch (Exception)
        {
            // Como as notas: o acessório não impede de abrir a reunião.
            return [];
        }
    }

    /// <summary>
    /// Acrescenta os nomes que ainda não estão — sem caixa nem acento na
    /// comparação, porque dois prints da mesma reunião escrevem o mesmo nome de
    /// jeitos diferentes. O primeiro jeito fica.
    /// </summary>
    public static void Juntar(string pastaDaGravacao, IEnumerable<string> nomes)
    {
        var todos = Ler(pastaDaGravacao);
        var vistos = todos.Select(Chave).ToHashSet();
        foreach (string nome in nomes)
        {
            string limpo = nome.Trim();
            if (limpo.Length > 0 && vistos.Add(Chave(limpo))) todos.Add(limpo);
        }

        string caminho = Caminho(pastaDaGravacao);
        string temporario = caminho + ".tmp";
        File.WriteAllText(temporario, JsonSerializer.Serialize(
            new ArquivoDeParticipantes { Nomes = todos },
            ParticipantesJson.Default.ArquivoDeParticipantes));
        File.Move(temporario, caminho, overwrite: true);
    }

    /// <summary>
    /// Os nomes a oferecer quando se batiza um falante: quem o print mostrou,
    /// e depois quem a agenda convidou.
    /// </summary>
    /// <param name="convidados">
    /// Os convidados, quando quem chama já os tem — durante a gravação o
    /// <c>meta.json</c> ainda não existe. Nulo lê do <c>meta.json</c>.
    /// </param>
    public static List<string> Sugestoes(string pastaDaGravacao, IEnumerable<string>? convidados = null)
    {
        var todos = Ler(pastaDaGravacao);
        var vistos = todos.Select(Chave).ToHashSet();
        foreach (string nome in convidados ?? ConvidadosDaAgenda.Ler(pastaDaGravacao).Nomes)
            if (nome.Trim() is { Length: > 0 } n && vistos.Add(Chave(n))) todos.Add(n);
        return todos;
    }

    private static string Chave(string texto)
    {
        var sb = new StringBuilder();
        foreach (char c in texto.Trim().Normalize(NormalizationForm.FormD))
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                sb.Append(char.ToLowerInvariant(c));
        return string.Join(' ', sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }
}

public sealed class ArquivoDeParticipantes
{
    [JsonPropertyName("nomes")] public List<string> Nomes { get; init; } = [];
}

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(ArquivoDeParticipantes))]
internal sealed partial class ParticipantesJson : JsonSerializerContext;
