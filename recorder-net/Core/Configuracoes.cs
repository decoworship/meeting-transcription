using System.Text.Json;
using System.Text.Json.Serialization;

namespace MeetingRecorder.Core;

/// <summary>
/// Configurações do gravador, persistidas entre execuções.
/// </summary>
/// <remarks>
/// <para>
/// Mesmo arquivo e mesmo formato do gravador Python
/// (<c>%USERPROFILE%\.meeting-recorder\settings.json</c>): durante a Fase 1 os
/// dois coexistem, e quem trocar de um para o outro não deve reconfigurar nada.
/// </para>
/// <para>
/// Fica ao lado do ambiente e não dentro do repositório, porque é estado da
/// máquina e não do projeto.
/// </para>
/// </remarks>
public sealed class Configuracoes
{
    [JsonPropertyName("mic_index")] public int? MicIndex { get; set; }
    [JsonPropertyName("loopback_index")] public int? LoopbackIndex { get; set; }
    [JsonPropertyName("output_dir")] public string? OutputDir { get; set; }
    [JsonPropertyName("start_muted")] public bool StartMuted { get; set; }
    [JsonPropertyName("use_calendar")] public bool UseCalendar { get; set; } = true;

    /// <summary>
    /// Requisito A14. Chave nova: o gravador Python ignora o que não conhece, e
    /// o padrão <c>true</c> mantém o comportamento atual para quem não mexer.
    /// </summary>
    [JsonPropertyName("notifications")] public bool Notifications { get; set; } = true;

    public static string CaminhoPadrao => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".meeting-recorder", "settings.json");

    public static Configuracoes Carregar(string? caminho = null)
    {
        caminho ??= CaminhoPadrao;
        try
        {
            if (File.Exists(caminho))
                return JsonSerializer.Deserialize(File.ReadAllText(caminho),
                           ConfigJson.Default.Configuracoes) ?? new Configuracoes();
        }
        catch (Exception)
        {
            // settings.json ilegível não pode impedir gravação: cai nos padrões.
            // É a mesma postura do Python, que loga e segue.
        }
        return new Configuracoes();
    }

    public void Salvar(string? caminho = null)
    {
        caminho ??= CaminhoPadrao;
        Directory.CreateDirectory(Path.GetDirectoryName(caminho)!);

        // Escrita atômica: um Ctrl+C no meio não deixa o arquivo corrompido —
        // mesma proteção que o settings.py do gravador Python tem.
        string tmp = caminho + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(this, ConfigJson.Default.Configuracoes));
        File.Move(tmp, caminho, overwrite: true);
    }
}

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(Configuracoes))]
internal sealed partial class ConfigJson : JsonSerializerContext;
