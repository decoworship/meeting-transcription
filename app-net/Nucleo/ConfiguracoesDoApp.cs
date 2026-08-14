using System.Text.Json;
using System.Text.Json.Serialization;

namespace MeetingApp.Nucleo;

/// <summary>
/// As preferências do aplicativo, persistidas entre execuções.
/// </summary>
/// <remarks>
/// <para>
/// Arquivo próprio, e não o <c>config.json</c> do app Python: as chaves são
/// outras, e escrever no arquivo dele arriscaria confundir a ferramenta que
/// ainda está em produção. Fica ao lado dos demais dados, em
/// <c>~/.meeting-transcription/</c>.
/// </para>
/// <para>
/// O que é <b>por projeto</b> — modelo, idioma, vocabulário — mora no
/// <see cref="Projetos"/>. Aqui fica só o que vale para o app inteiro.
/// </para>
/// </remarks>
public sealed class ConfiguracoesDoApp
{
    /// <summary>Onde procurar as gravações. Vazio significa a pasta do gravador.</summary>
    [JsonPropertyName("pasta_das_gravacoes")] public string? PastaDasGravacoes { get; set; }

    /// <summary>Para onde a cópia da exportação vai, quando pedida.</summary>
    [JsonPropertyName("pasta_de_exportacao")] public string? PastaDeExportacao { get; set; }

    /// <summary>
    /// Mostrar o botão de transcrever de novo numa reunião já transcrita.
    /// </summary>
    /// <remarks>
    /// Desligado por padrão, a pedido do dono do produto: refazer sobrescreve o
    /// que foi revisado — nomes de falante, texto corrigido — e uma reunião de
    /// duas horas custa meia hora de GPU. É uma ação de exceção, e um botão
    /// permanente a transformaria numa ação de rotina, ao alcance de um clique
    /// distraído.
    /// </remarks>
    [JsonPropertyName("permitir_retranscrever")] public bool PermitirRetranscrever { get; set; }

    /// <summary>Modelo de ASR usado quando o projeto não diz outro.</summary>
    [JsonPropertyName("modelo_padrao")] public string ModeloPadrao { get; set; } = "large-v3";

    [JsonPropertyName("diarizacao_padrao")] public string DiarizacaoPadrao { get; set; } = "community-1";

    /// <summary>O GGUF que escreve as atas, dentro de motores/ata/modelos.</summary>
    /// <remarks>
    /// Nome de arquivo, e não caminho: o motor sabe onde a pasta fica, e guardar
    /// caminho absoluto quebraria no dia em que o app mudasse de lugar.
    /// </remarks>
    [JsonPropertyName("modelo_de_ata")] public string ModeloDeAta { get; set; }
        = "qwen3-4b-instruct-q4km.gguf";

    /// <summary>O tipo de ata sugerido quando o projeto não tem preferência.</summary>
    [JsonPropertyName("tipo_de_ata_padrao")] public string TipoDeAtaPadrao { get; set; }
        = "cliente-update";

    public static string CaminhoPadrao => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".meeting-transcription", "app.json");

    public static ConfiguracoesDoApp Carregar(string? caminho = null)
    {
        caminho ??= CaminhoPadrao;
        try
        {
            if (File.Exists(caminho))
                return JsonSerializer.Deserialize(File.ReadAllText(caminho),
                           ConfigDoAppJson.Default.ConfiguracoesDoApp) ?? new ConfiguracoesDoApp();
        }
        catch (Exception)
        {
            // Configuração ilegível não pode impedir o app de abrir: cai nos
            // padrões, como o gravador faz com o settings.json dele.
        }
        return new ConfiguracoesDoApp();
    }

    public void Salvar(string? caminho = null)
    {
        caminho ??= CaminhoPadrao;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(caminho))!);

        string tmp = caminho + ".tmp";
        File.WriteAllText(tmp,
            JsonSerializer.Serialize(this, ConfigDoAppJson.Default.ConfiguracoesDoApp));
        File.Move(tmp, caminho, overwrite: true);
    }
}

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(ConfiguracoesDoApp))]
internal sealed partial class ConfigDoAppJson : JsonSerializerContext;
