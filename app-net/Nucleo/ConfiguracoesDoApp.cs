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

    /// <summary>
    /// Mandar o vocabulário do projeto ao ASR como <c>hotwords</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Desligado por padrão</b>, e a chave existe mais para poder medir do
    /// que para escolher. O vocabulário tem dois usos que sempre andaram
    /// juntos: enviesar o modelo enquanto ele transcreve, e corrigir a grafia
    /// depois (<see cref="CorrecaoFonetica"/>). A Fase 0 mediu que os dois
    /// recuperam nomes na mesma medida — 36 contra 36 — e concluiu que o
    /// primeiro era dispensável. A correção foi escrita; o hotwords ficou
    /// ligado por esquecimento.
    /// </para>
    /// <para>
    /// O que ele cobrava, medido em duas gravações: <b>207 segmentos contra
    /// 787</b> no mesmo áudio, 84,1% de cobertura da fala contra 89,8%, e 1514 s
    /// de decodificação contra 331 s. Segmento longo é falante errado, porque a
    /// atribuição é um rótulo por segmento. Ver docs/FASE6.md §4.1.
    /// </para>
    /// <para>
    /// Ligar continua sendo possível porque a régua da §5 pede exatamente esta
    /// comparação — o mesmo app, no mesmo áudio, com e sem — e porque quem
    /// tiver vocabulário muito fora do comum pode querer os dois.
    /// </para>
    /// </remarks>
    [JsonPropertyName("usar_hotwords")] public bool UsarHotwords { get; set; }

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

    /// <summary>Transcrever mesmo quando a placa não estiver disponível.</summary>
    /// <remarks>
    /// <para>
    /// Desligado por padrão, e não é excesso de zelo: em 18/08/2026 a
    /// transcrição de um usuário caiu para CPU numa máquina <b>com</b> RTX 4050,
    /// e o <c>large-v3</c> em CPU consumiu RAM por horas até **derrubar o
    /// Windows**. Um app que grava reunião não pode ter esse modo de falha
    /// ligado por acidente.
    /// </para>
    /// <para>
    /// Quem de fato não tem placa liga isto e aceita o custo — a tela diz qual
    /// é. O que a chave impede é o caso em que a placa existe e o motor não a
    /// enxerga: aí rodar em CPU não é escolha, é defeito disfarçado de
    /// lentidão.
    /// </para>
    /// </remarks>
    [JsonPropertyName("permitir_cpu")] public bool PermitirCpu { get; set; }

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

    /// <summary>Claro, escuro, ou o que o Windows estiver usando.</summary>
    /// <remarks>
    /// <para>
    /// O tema escuro estava pronto no design system desde o começo — sessenta
    /// linhas de tokens semânticos redefinidos — e não havia como chegar nele:
    /// o <c>data-tema</c> do <c>index.html</c> era fixo em <c>claro</c>. Esta
    /// chave é o caminho que faltava.
    /// </para>
    /// <para>
    /// O padrão é <c>claro</c>, e não <c>auto</c>, de propósito: quem já usa o
    /// app instalado não deve ver a interface trocar de cor porque atualizou.
    /// Quem quer o escuro pede.
    /// </para>
    /// <para>
    /// Os três valores são fechados. O valor sai daqui para dentro de um
    /// atributo do HTML — ver <c>Conteudo.Buscar</c> —, então qualquer coisa
    /// que não seja um dos três vira <c>claro</c> em vez de ser escrita na
    /// página. Um <c>app.json</c> editado à mão não injeta marcação.
    /// </para>
    /// </remarks>
    [JsonPropertyName("tema")] public string Tema { get; set; } = TemaPadrao;

    public const string TemaPadrao = "claro";

    /// <summary>
    /// Um tema qualquer reduzido a um dos três aceitos.
    /// </summary>
    /// <remarks>
    /// A única definição do que é um tema válido, porque o valor atravessa a
    /// ponte, o <c>app.json</c> e um atributo do HTML — e três lugares com a
    /// mesma regra escrita à mão é como uma delas fica para trás.
    /// </remarks>
    public static string TemaAceito(string? tema) =>
        tema is "escuro" or "auto" ? tema : TemaPadrao;

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
