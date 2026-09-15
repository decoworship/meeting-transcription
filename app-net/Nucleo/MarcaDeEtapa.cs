using System.Text.Json;
using System.Text.Json.Serialization;

namespace MeetingApp.Nucleo;

/// <summary>Em que etapa a transcrição estava quando o app parou de escrever.</summary>
/// <remarks>
/// Os campos são deliberadamente poucos: isto é escrito com o disco em modo
/// <c>WriteThrough</c> a cada etapa, e tudo o que não ajuda a responder
/// <i>onde parou</i> é peso no caminho crítico.
/// </remarks>
public sealed class EtapaEmAndamento
{
    [JsonPropertyName("quando")] public required string Quando { get; init; }
    [JsonPropertyName("gravacao")] public required string Gravacao { get; init; }
    [JsonPropertyName("etapa")] public required string Etapa { get; init; }
    [JsonPropertyName("motor")] public required string Motor { get; init; }
    [JsonPropertyName("modelo")] public required string Modelo { get; init; }
    [JsonPropertyName("versao")] public required string Versao { get; init; }
}

[JsonSourceGenerationOptions(WriteIndented = true,
                             DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(EtapaEmAndamento))]
internal sealed partial class MarcaJson : JsonSerializerContext;

/// <summary>
/// O marcador que diz em que etapa a transcrição estava — e que sobrevive à
/// máquina desligar no meio.
/// </summary>
/// <remarks>
/// <para>
/// <b>Por que existe.</b> O computador do segundo usuário <b>desliga sozinho</b>
/// durante a transcrição (o <c>SUP-2</c> do <c>docs/BACKLOG.md</c>): não trava e
/// não dá tela azul. Descobrir em que etapa isso acontece já custou duas idas e
/// voltas com respostas erradas — ele mandou o bloco de diagnóstico no lugar do
/// log, e depois o evento errado do Visualizador.
/// </para>
/// <para>
/// <b>O que o <see cref="Registro"/> não resolve.</b> O log responde a mesma
/// pergunta, mas só para quem o lê inteiro e sabe o que procurar. Este marcador
/// responde sozinho: se ele <b>existe no próximo início</b>, a transcrição
/// anterior não terminou — e o campo <c>etapa</c> diz onde parou. É o terceiro
/// dos cinco instrumentos do <c>SUP-1</c>, e o único dos três que faltavam que é
/// C# portátil: os outros dois leem o Event Log e amostram o <c>nvidia-smi</c>,
/// e são Windows-only.
/// </para>
/// <para>
/// <b>Escrito com <c>WriteThrough</c>, e é o ponto todo.</b> Um corte de energia
/// não dá ao Windows a chance de esvaziar o cache de escrita: um marcador que
/// ficou em memória é exatamente o que não existe quando a máquina volta. São
/// ~6 escritas por transcrição, então o custo é irrelevante e o benefício é
/// binário.
/// </para>
/// <para>
/// <b>Nunca levanta exceção e nunca segura quem chama</b>, pela mesma regra do
/// <see cref="Registro"/>: um instrumento que derruba o app é pior que a
/// ausência dele.
/// </para>
/// </remarks>
public static class MarcaDeEtapa
{
    /// <summary>Ao lado do <see cref="Registro"/>, e pelo mesmo motivo.</summary>
    /// <remarks>
    /// Um arquivo só, e não um por gravação: o núcleo recusa duas transcrições
    /// ao mesmo tempo, então só há uma etapa em andamento no app inteiro.
    /// </remarks>
    public static string Caminho => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".meeting-transcription", "em-andamento.json");

    private static EtapaEmAndamento? _atual;

    /// <summary>Abre o marcador. Devolve a marca órfã que estava lá, se havia.</summary>
    /// <remarks>
    /// <b>Ela é lida antes de ser sobrescrita</b>, e é esta a única chance de
    /// vê-la: quem chama registra o que recebe. Devolver em vez de registrar
    /// aqui mantém esta classe sem opinião sobre o que a informação significa.
    /// </remarks>
    public static EtapaEmAndamento? Comecar(string gravacao, string motor, string modelo)
    {
        var orfa = Ler();
        _atual = new EtapaEmAndamento
        {
            Quando = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
            Gravacao = gravacao,
            Etapa = "mix",
            Motor = motor,
            Modelo = modelo,
            Versao = Diagnostico.VersaoDoApp(),
        };
        Gravar(_atual);
        return orfa;
    }

    /// <summary>Anota a etapa nova. Sem efeito se não há transcrição aberta.</summary>
    public static void Etapa(string etapa)
    {
        if (_atual is null || _atual.Etapa == etapa) return;
        _atual = new EtapaEmAndamento
        {
            Quando = _atual.Quando,
            Gravacao = _atual.Gravacao,
            Etapa = etapa,
            Motor = _atual.Motor,
            Modelo = _atual.Modelo,
            Versao = _atual.Versao,
        };
        Gravar(_atual);
    }

    /// <summary>Fecha o marcador. <b>Um marcador que sobra é o sinal.</b></summary>
    public static void Terminar()
    {
        _atual = null;
        try
        {
            if (File.Exists(Caminho)) File.Delete(Caminho);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    /// <summary>O marcador em disco, ou <c>null</c> se não há.</summary>
    public static EtapaEmAndamento? Ler()
    {
        try
        {
            if (!File.Exists(Caminho)) return null;
            return JsonSerializer.Deserialize(File.ReadAllText(Caminho),
                                              MarcaJson.Default.EtapaEmAndamento);
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
        catch (JsonException) { return null; }
    }

    /// <summary>Uma linha para o log e para o bloco de diagnóstico.</summary>
    public static string Descrever(EtapaEmAndamento m) =>
        $"a transcrição de {m.Gravacao} começada em {m.Quando} "
        + $"(motor {m.Motor}, modelo {m.Modelo}, versão {m.Versao}) "
        + $"não terminou — parou na etapa \"{m.Etapa}\"";

    private static void Gravar(EtapaEmAndamento m)
    {
        try
        {
            string caminho = Caminho;
            Directory.CreateDirectory(Path.GetDirectoryName(caminho)!);
            byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(
                m, MarcaJson.Default.EtapaEmAndamento);
            // WriteThrough: sem ele o marcador pode existir só no cache do
            // Windows, e um corte de energia leva exatamente a informação que
            // este arquivo existe para preservar.
            using var f = new FileStream(caminho, FileMode.Create, FileAccess.Write,
                                         FileShare.Read, 4096, FileOptions.WriteThrough);
            f.Write(bytes);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
