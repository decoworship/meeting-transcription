using System.Text;
using System.Text.Json;

namespace MeetingApp.Nucleo;

/// <summary>
/// A tela gravada em vídeo junto com a reunião: <c>tela.mp4</c> e o
/// <c>tela.json</c> que diz onde ele se encaixa no áudio.
/// </summary>
/// <remarks>
/// <para>
/// <b>O vídeo não tem som, e não precisa de mixagem.</b> Ele começa no mesmo
/// relógio das duas faixas (a origem QPC do <c>Gravador</c>), então o instante
/// <c>t</c> do áudio é o instante <c>t − InicioS</c> do vídeo. A tela toca o
/// <c>mix.wav</c> e arrasta o vídeo junto — e os tempos da transcrição, que são
/// os do áudio, servem de legenda sem conversão nenhuma.
/// </para>
/// <para>
/// <b>Arquivo à parte, e não campo no <c>meta.json</c></b>, pelo motivo de
/// <see cref="Notas"/>: o <c>meta.json</c> é do gravador e tem schema congelado.
/// </para>
/// </remarks>
/// <param name="InicioS">Onde o primeiro quadro cai no áudio, em segundos.</param>
/// <param name="Falha">
/// Por que o vídeo parou antes da gravação, ou nulo. O áudio nunca para por
/// causa dele.
/// </param>
public sealed record VideoDaTela(double InicioS, int Largura, int Altura, int QuadrosPorSegundo,
                                 int Quadros, string? Monitor, string? Falha)
{
    public const string Video = "tela.mp4";
    public const string Indice = "tela.json";

    /// <summary>
    /// O vídeo desta gravação, ou nulo se não há um que toque. Nunca lança.
    /// </summary>
    /// <remarks>
    /// Sem quadro nenhum o MP4 nem chega a existir, e sem o <c>tela.json</c> não
    /// se sabe onde ele cai no áudio — os dois juntos, ou nada. Um MP4 cortado
    /// por queda do processo também tem <c>tela.json</c> ausente, porque ele só
    /// é escrito depois de o vídeo fechar.
    /// </remarks>
    public static VideoDaTela? Ler(string pastaDaGravacao)
    {
        try
        {
            string indice = Path.Combine(pastaDaGravacao, Indice);
            if (!File.Exists(indice) || !File.Exists(Path.Combine(pastaDaGravacao, Video))) return null;
            using var doc = JsonDocument.Parse(File.ReadAllText(indice));
            var r = doc.RootElement;
            int quadros = Inteiro(r, "quadros");
            if (quadros <= 0) return null;
            return new VideoDaTela(
                r.TryGetProperty("inicio_s", out var i) && i.TryGetDouble(out var s) ? s : 0,
                Inteiro(r, "largura"), Inteiro(r, "altura"), Inteiro(r, "qps"), quadros,
                Texto(r, "monitor"), Texto(r, "falha"));
        }
        catch (Exception)
        {
            return null;
        }
    }

    public void Escrever(string pastaDaGravacao)
    {
        using var corpo = new MemoryStream();
        using (var w = new Utf8JsonWriter(corpo, new JsonWriterOptions { Indented = true }))
        {
            w.WriteStartObject();
            w.WriteNumber("inicio_s", Math.Round(InicioS, 3));
            w.WriteNumber("largura", Largura);
            w.WriteNumber("altura", Altura);
            w.WriteNumber("qps", QuadrosPorSegundo);
            w.WriteNumber("quadros", Quadros);
            if (Monitor is not null) w.WriteString("monitor", Monitor);
            if (Falha is not null) w.WriteString("falha", Falha);
            w.WriteEndObject();
        }
        File.WriteAllText(Path.Combine(pastaDaGravacao, Indice), Encoding.UTF8.GetString(corpo.ToArray()));
    }

    private static int Inteiro(JsonElement r, string nome) =>
        r.TryGetProperty(nome, out var v) && v.TryGetInt32(out int n) ? n : 0;

    private static string? Texto(JsonElement r, string nome) =>
        r.TryGetProperty(nome, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}
