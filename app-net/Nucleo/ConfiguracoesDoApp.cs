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
    /// Onde as atas exportadas vão parar.
    /// </summary>
    /// <remarks>
    /// Separada da pasta de transcrições porque os dois arquivos têm destino e
    /// finalidade diferentes: a transcrição é material de trabalho, que se
    /// consulta e se corrige; a ata é o que se manda para fora — para o cliente,
    /// para o time, para a pasta do projeto. Misturá-las obrigaria a escolher a
    /// pasta na hora de exportar, toda vez.
    /// </remarks>
    [JsonPropertyName("pasta_de_atas")] public string? PastaDeAtas { get; set; }

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

    /// <summary>
    /// Corrigir a grafia dos termos do projeto no texto transcrito.
    /// </summary>
    /// <remarks>
    /// Ligada por padrão, ao contrário do filtro de silêncio: ela <b>troca</b>
    /// palavra por palavra do vocabulário que o usuário mesmo escreveu, e cada
    /// troca fica marcada na revisão e é reversível num clique. O erro que ela
    /// corrige — "Dimi" virando "Jimmy" — aparece em toda reunião; o erro que
    /// ela pode cometer aparece marcado.
    /// </remarks>
    [JsonPropertyName("correcao_fonetica")] public bool CorrecaoFonetica { get; set; } = true;

    /// <summary>
    /// Descartar os trechos que o ASR inventou sobre silêncio digital.
    /// </summary>
    /// <remarks>
    /// Desligado por padrão: o filtro remove texto, e remover texto por engano
    /// é o erro caro dos dois. Quem liga está trocando ~5% de invenção
    /// (FASE0 6-A) pelo risco de perder uma fala baixa mal gravada. Ver
    /// <see cref="FiltroDeSilencio"/>.
    /// </remarks>
    [JsonPropertyName("filtrar_silencio")] public bool FiltrarSilencio { get; set; }

    /// <summary>O GGUF que escreve as atas, dentro de motores/ata/modelos.</summary>
    /// <remarks>
    /// Nome de arquivo, e não caminho: o motor sabe onde a pasta fica, e guardar
    /// caminho absoluto quebraria no dia em que o app mudasse de lugar.
    /// </remarks>
    [JsonPropertyName("modelo_de_ata")] public string ModeloDeAta { get; set; }
        = "qwen3-4b-instruct-q4km.gguf";

    /// <summary>
    /// Os domínios de e-mail da nossa organização.
    /// </summary>
    /// <remarks>
    /// Só os nossos: quem não é da casa é cliente, e essa regra não precisa de
    /// manutenção quando aparece um cliente novo. É o que permite a ata separar
    /// as pendências por lado sem o modelo deduzir organização pelo assunto da
    /// conversa — dedução que já pôs alguém da equipe como sendo do cliente.
    /// </remarks>
    [JsonPropertyName("dominios_da_casa")] public List<string> DominiosDaCasa { get; set; } = [];

    /// <summary>O tipo de ata sugerido quando o projeto não tem preferência.</summary>
    [JsonPropertyName("tipo_de_ata_padrao")] public string TipoDeAtaPadrao { get; set; }
        = "cliente-update";

    /// <summary>Conferir, de vez em quando, se saiu versão nova.</summary>
    /// <remarks>
    /// <para>
    /// Ligado por padrão, e desligável. É a <b>única</b> conexão que o app abre
    /// por conta própria — a promessa de que a reunião não sai da máquina
    /// continua inteira, porque o que vai nessa conexão é um GET sem parâmetro
    /// nenhum: nada de identificador, nada de versão instalada, nada de
    /// telemetria. Quem hospeda vê um download de um arquivo público, como
    /// qualquer visita a uma página.
    /// </para>
    /// <para>
    /// Existe porque o app passou a ser instalado na máquina de outras pessoas
    /// (Fase 4), e sem isto elas só ficam sabendo de uma correção se alguém
    /// contar. Ver <see cref="Atualizacao"/>.
    /// </para>
    /// </remarks>
    [JsonPropertyName("avisar_de_atualizacao")]
    public bool AvisarDeAtualizacao { get; set; } = true;

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
