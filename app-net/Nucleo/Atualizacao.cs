using System.Text.Json;
using System.Text.Json.Serialization;

namespace MeetingApp.Nucleo;

/// <summary>Uma versão publicada, como o <c>versao.json</c> a descreve.</summary>
public sealed class VersaoPublicada
{
    [JsonPropertyName("versao")] public required string Versao { get; init; }

    /// <summary>Data em ISO, só para a tela dizer "de 15/08".</summary>
    [JsonPropertyName("publicada")] public string? Publicada { get; init; }

    /// <summary>O que mudou, em uma ou duas frases, para quem usa.</summary>
    [JsonPropertyName("notas")] public string? Notas { get; init; }

    /// <summary>Onde pegar o instalador, quando há um endereço para dar.</summary>
    [JsonPropertyName("onde")] public string? Onde { get; init; }
}

/// <summary>O que a tela precisa saber sobre atualização.</summary>
public sealed class EstadoDaAtualizacao
{
    [JsonPropertyName("versao_instalada")] public required string VersaoInstalada { get; init; }

    /// <summary>A publicada, quando é mais nova que a instalada. Nulo caso contrário.</summary>
    [JsonPropertyName("nova")] public VersaoPublicada? Nova { get; init; }

    /// <summary>Se a conferência está desligada nas preferências.</summary>
    [JsonPropertyName("desligado")] public bool Desligado { get; init; }

    /// <summary>Por que não deu para conferir, quando não deu. Nunca é erro fatal.</summary>
    [JsonPropertyName("nao_deu")] public string? NaoDeu { get; init; }
}

/// <summary>
/// Saber que saiu versão nova.
/// </summary>
/// <remarks>
/// <para>
/// <b>Por que existe.</b> A carta da Fase 4 dispensou atualização automática com
/// um argumento que era verdadeiro e deixou de ser no dia seguinte: *"não vai
/// haver servidor de update por causa de três amigos"*. Havia um amigo; passou a
/// haver um amigo com uma instalação numa máquina que não é a nossa. Sem isto,
/// toda correção fica presa aqui.
/// </para>
/// <para>
/// <b>E é pré-requisito de acrescentar modelo.</b> O catálogo de modelos é
/// código (<see cref="Catalogo.Pacotes"/>), então oferecer um modelo novo é
/// publicar uma versão nova do app — não adianta achar um modelo de ata melhor
/// se ele não chega em ninguém.
/// </para>
/// <para>
/// <b>O degrau mais barato dos três</b> (FASE4-HANDOFF §6.1): o app só
/// <i>avisa</i>. Não baixa, não troca binário, não executa nada. Baixar e
/// substituir o próprio executável exige assinatura de código para não ser um
/// vetor de ataque, e assinatura ainda não existe.
/// </para>
/// <para>
/// <b>O canal é um arquivo no repositório público</b>, servido pelo
/// raw.githubusercontent. Não há servidor para manter, não há conta, não há
/// custo — e o arquivo é editado no mesmo commit que sobe a versão, que é o que
/// impede os dois de divergirem.
/// </para>
/// </remarks>
public static class Atualizacao
{
    public const string Endereco =
        "https://raw.githubusercontent.com/decoworship/meeting-transcription/main/versao.json";

    /// <summary>
    /// Compara duas versões pelos números, da esquerda para a direita.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Comparar como texto seria o erro clássico: <c>"0.10.0"</c> vem
    /// <b>antes</b> de <c>"0.9.0"</c> em ordem alfabética, e o aviso de
    /// atualização simplesmente pararia de aparecer na décima versão — em
    /// silêncio, que é o pior jeito de uma rota de atualização falhar.
    /// </para>
    /// <para>
    /// O que não for número é ignorado: <c>0.1.1-teste</c> vale
    /// <c>0.1.1</c>. Sufixo de pré-lançamento não é caso deste projeto, e
    /// inventar precedência para ele agora seria inventar regra sem uso.
    /// </para>
    /// </remarks>
    public static bool EhMaisNova(string candidata, string instalada)
    {
        int[] a = Numeros(candidata);
        int[] b = Numeros(instalada);

        for (int i = 0; i < Math.Max(a.Length, b.Length); i++)
        {
            int x = i < a.Length ? a[i] : 0;
            int y = i < b.Length ? b[i] : 0;
            if (x != y) return x > y;
        }
        return false;
    }

    private static int[] Numeros(string versao) =>
        [.. (versao ?? "").Split('.', '-', '+')
            .Select(p => int.TryParse(new string([.. p.TakeWhile(char.IsDigit)]), out int n) ? n : 0)];

    /// <summary>
    /// Vai ver se saiu versão nova. Nunca levanta exceção.
    /// </summary>
    /// <remarks>
    /// Falhar aqui é o caso comum e não é erro: máquina sem rede, GitHub fora,
    /// proxy de empresa no caminho. Nada disso pode aparecer como problema para
    /// quem só queria transcrever uma reunião — some, e tenta de novo depois.
    /// </remarks>
    /// <param name="cliente">Injetado pelos testes; nulo cria um com timeout curto.</param>
    public static async Task<EstadoDaAtualizacao> ProcurarAsync(
        ConfiguracoesDoApp config, HttpClient? cliente = null,
        CancellationToken ct = default)
    {
        string instalada = Diagnostico.VersaoDoApp();

        if (!config.AvisarDeAtualizacao)
            return new EstadoDaAtualizacao { VersaoInstalada = instalada, Desligado = true };

        // Timeout curto: isto roda enquanto alguém espera uma tela. Uma rede que
        // demora dez segundos para responder é uma rede que, para este fim, não
        // respondeu.
        var http = cliente ?? new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
        try
        {
            string json = await http.GetStringAsync(Endereco, ct);
            var publicada = JsonSerializer.Deserialize(json, AtualizacaoJson.Default.VersaoPublicada);

            if (publicada is null || string.IsNullOrWhiteSpace(publicada.Versao))
                return new EstadoDaAtualizacao
                {
                    VersaoInstalada = instalada,
                    NaoDeu = "o arquivo de versão veio ilegível",
                };

            return new EstadoDaAtualizacao
            {
                VersaoInstalada = instalada,
                Nova = EhMaisNova(publicada.Versao, instalada) ? publicada : null,
            };
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException
                                       or JsonException or UriFormatException)
        {
            return new EstadoDaAtualizacao
            {
                VersaoInstalada = instalada,
                NaoDeu = "não deu para conferir agora",
            };
        }
        finally
        {
            if (cliente is null) http.Dispose();
        }
    }
}

[JsonSourceGenerationOptions(DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(VersaoPublicada))]
[JsonSerializable(typeof(EstadoDaAtualizacao))]
internal sealed partial class AtualizacaoJson : JsonSerializerContext;
