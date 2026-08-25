using System.Text.Json;

namespace MeetingApp.Nucleo;

/// <summary>
/// Quem a agenda listou, lido do <c>meta.json</c> da gravação.
/// </summary>
/// <remarks>
/// <para>
/// Morava dentro do <c>App/Ponte.cs</c>, e por isso o <c>Sidecar.exe --ata</c>
/// não tinha como chegar nele: a ata gerada pela linha de comando saía sem
/// <c>Pessoas</c>, e o cabeçalho dela caía no caminho antigo enquanto o do app
/// usava o novo. <b>É o CLI que as ferramentas de medição usam</b>
/// (<c>tools/medir_motor_de_ata.py</c>, <c>tools/comparar_modelos_de_ata.py</c>),
/// então a divergência fazia a medição olhar para uma ata que o usuário nunca
/// veria.
/// </para>
/// <para>
/// As duas listas são paralelas — mesmo índice, mesma pessoa —, e é o
/// <see cref="Atas.Organizacoes.Classificar(IReadOnlyList{string},
/// IReadOnlyList{string}, IEnumerable{string})"/> que as junta.
/// </para>
/// </remarks>
public static class ConvidadosDaAgenda
{
    /// <returns>
    /// Listas vazias quando não há <c>meta.json</c>, quando ele não tem
    /// <c>meeting</c>, ou quando está ilegível — <b>nunca</b> uma exceção.
    /// Reunião sem agenda é o caso comum de quem grava uma conversa que não
    /// estava marcada, e não pode impedir a ata de existir.
    /// </returns>
    public static (List<string> Nomes, List<string> Emails) Ler(string pasta)
    {
        var nomes = new List<string>();
        var emails = new List<string>();
        try
        {
            string meta = Path.Combine(pasta, "meta.json");
            if (!File.Exists(meta)) return (nomes, emails);

            using var doc = JsonDocument.Parse(File.ReadAllText(meta));
            if (!doc.RootElement.TryGetProperty("meeting", out var reuniao))
                return (nomes, emails);

            if (reuniao.TryGetProperty("attendees", out var a)
                && a.ValueKind == JsonValueKind.Array)
                foreach (var x in a.EnumerateArray())
                    if (x.GetString() is { Length: > 0 } n) nomes.Add(n);

            if (reuniao.TryGetProperty("attendee_emails", out var e)
                && e.ValueKind == JsonValueKind.Array)
                foreach (var x in e.EnumerateArray())
                    if (x.GetString() is { Length: > 0 } m) emails.Add(m);
        }
        catch (Exception)
        {
            // meta.json ilegível não pode impedir de escrever a ata.
        }
        return (nomes, emails);
    }
}
