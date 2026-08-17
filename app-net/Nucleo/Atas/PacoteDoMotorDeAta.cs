using System.IO.Compression;
using System.Text.Json.Serialization;

namespace MeetingApp.Nucleo.Atas;

/// <summary>O motor de ata como pacote baixável, e não como parte do instalador.</summary>
/// <param name="Instalado">O <c>llama-server.exe</c> e o <c>ggml-cuda.dll</c> estão lá.</param>
/// <param name="BytesEmDisco">Quanto a pasta ocupa hoje.</param>
public sealed record EstadoDoMotorDeAta(
    [property: JsonPropertyName("instalado")] bool Instalado,
    [property: JsonPropertyName("bytes_em_disco")] long BytesEmDisco,
    [property: JsonPropertyName("bytes_do_download")] long BytesDoDownload,
    [property: JsonPropertyName("pasta")] string Pasta);

/// <summary>
/// Baixa e instala o llama.cpp que escreve as atas.
/// </summary>
/// <remarks>
/// <para>
/// <b>Por que ele deixou de viajar no instalador (Fase 4).</b> O motor de ata
/// são 1,1 GB descompactados — o segundo maior item do payload, atrás só do
/// torch — e ele serve a uma funcionalidade que nem toda instalação vai usar.
/// Tirá-lo tira <b>400 MB do instalador</b>, e é a mesma decisão que os modelos
/// já seguiam: o que é grande e opcional se baixa quando fizer falta, pela tela
/// que sabe mostrar barra e tamanho.
/// </para>
/// <para>
/// <b>Nada é hospedado por nós.</b> Os dois arquivos vêm da release oficial do
/// llama.cpp no GitHub, exatamente como o
/// <c>tools/empacotar_motor_de_ata.sh</c> já os buscava. Não há espelho para
/// manter, e a origem é verificável por quem quiser conferir.
/// </para>
/// <para>
/// <b>A versão de CUDA é 12.4, e isso não é descuido.</b> O build 13.3 falha na
/// máquina do usuário com <i>"the provided PTX was compiled with an unsupported
/// toolchain"</i> — driver 595.97, que anuncia 13.2. A 12.4 roda em driver novo
/// e velho. Decisão do dono do produto em 14/08/2026, registrada em
/// FASE3-HANDOFF §4: compatibilidade ganha de um desempenho que ninguém mediu
/// fazer falta. <b>Não troque para 13.x sem medir.</b>
/// </para>
/// </remarks>
public static class PacoteDoMotorDeAta
{
    private const string Versao = "b10427";
    private const string Cuda = "12.4";

    /// <summary>Os dois zips, e o tamanho de cada um, medido em 14/08/2026.</summary>
    /// <remarks>
    /// O <c>cudart</c> é o maior dos dois e é o que traz o cuBLAS — sem ele o
    /// llama.cpp cai para CPU e ninguém avisa, que é o defeito que a régua do
    /// fim existe para pegar.
    /// </remarks>
    private static readonly (string Nome, long Bytes)[] Arquivos =
    [
        ($"llama-{Versao}-bin-win-cuda-{Cuda}-x64.zip", 250_000_000),
        ($"cudart-llama-bin-win-cuda-{Cuda}-x64.zip", 391_000_000),
    ];

    public static long BytesDoDownload => Arquivos.Sum(a => a.Bytes);

    private static string Base =>
        $"https://github.com/ggml-org/llama.cpp/releases/download/{Versao}";

    /// <summary>A pasta <c>motores/ata/bin</c>, que é onde o servidor mora.</summary>
    public static string Pasta =>
        Path.GetDirectoryName(CaminhosDoMotorDeAta.AoLadoDoExecutavel().Servidor)!;

    public static EstadoDoMotorDeAta Estado()
    {
        string pasta = Pasta;
        bool instalado = File.Exists(Path.Combine(pasta, "llama-server.exe"))
                      && File.Exists(Path.Combine(pasta, "ggml-cuda.dll"));

        long bytes = 0;
        try
        {
            if (Directory.Exists(pasta))
                bytes = Directory.EnumerateFiles(pasta, "*", SearchOption.AllDirectories)
                                 .Sum(f => new FileInfo(f).Length);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Pasta ilegível vira "ausente", que é o pior caso honesto.
        }

        return new EstadoDoMotorDeAta(instalado, bytes, BytesDoDownload, pasta);
    }

    /// <summary>
    /// Baixa os dois zips e os extrai em <c>motores/ata/bin</c>.
    /// </summary>
    /// <param name="progresso">Fração de 0 a 1 sobre o total dos dois arquivos.</param>
    /// <remarks>
    /// Baixa para um arquivo temporário e só então extrai: interromper no meio
    /// deixa lixo em <c>%TEMP%</c>, e não uma pasta de motor pela metade que
    /// pareceria instalada.
    /// </remarks>
    public static async Task BaixarAsync(Action<double, string>? progresso,
                                         CancellationToken ct = default)
    {
        string pasta = Pasta;
        Directory.CreateDirectory(pasta);

        string temporario = Path.Combine(Path.GetTempPath(), "meetingapp-motor-de-ata");
        Directory.CreateDirectory(temporario);

        using var http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        long total = BytesDoDownload;
        long acumulado = 0;

        try
        {
            foreach (var (nome, tamanhoEsperado) in Arquivos)
            {
                string destino = Path.Combine(temporario, nome);
                long jaBaixado = acumulado;

                progresso?.Invoke((double)acumulado / total, $"baixando {nome}");

                using (var resposta = await http.GetAsync($"{Base}/{nome}",
                           HttpCompletionOption.ResponseHeadersRead, ct))
                {
                    resposta.EnsureSuccessStatusCode();

                    using var origem = await resposta.Content.ReadAsStreamAsync(ct);
                    using var arquivo = File.Create(destino);

                    var buffer = new byte[1 << 20];
                    int lidos;
                    while ((lidos = await origem.ReadAsync(buffer, ct)) > 0)
                    {
                        await arquivo.WriteAsync(buffer.AsMemory(0, lidos), ct);
                        acumulado += lidos;
                        progresso?.Invoke(Math.Min(1.0, (double)acumulado / total),
                                          $"baixando {nome}");
                    }
                }

                // O tamanho publicado é aproximado, então a régua é frouxa de
                // propósito: ela existe para pegar download interrompido e
                // página de erro, não para conferir o byte.
                long veio = new FileInfo(destino).Length;
                if (veio < tamanhoEsperado / 2)
                    throw new InvalidOperationException(
                        $"o download de {nome} veio incompleto "
                        + $"({veio / 1_000_000} MB de ~{tamanhoEsperado / 1_000_000} MB).");

                progresso?.Invoke((double)acumulado / total, $"instalando {nome}");
                ZipFile.ExtractToDirectory(destino, pasta, overwriteFiles: true);
                File.Delete(destino);

                acumulado = jaBaixado + tamanhoEsperado;
            }

            Enxugar(pasta);

            // A régua, e ela é a mesma do empacotar_motor_de_ata.sh: sem o
            // ggml-cuda.dll a ata roda em CPU, leva vinte minutos em vez de um,
            // e ninguém avisa.
            if (!File.Exists(Path.Combine(pasta, "llama-server.exe")))
                throw new InvalidOperationException(
                    "o pacote foi baixado mas não trouxe o llama-server.exe.");
            if (!File.Exists(Path.Combine(pasta, "ggml-cuda.dll")))
                throw new InvalidOperationException(
                    "o pacote foi baixado mas não trouxe o ggml-cuda.dll — "
                    + "sem ele a ata rodaria na CPU, e devagar.");

            progresso?.Invoke(1.0, "pronto");
        }
        finally
        {
            try { Directory.Delete(temporario, recursive: true); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
    }

    /// <summary>
    /// Tira do pacote o que o app não usa.
    /// </summary>
    /// <remarks>
    /// A release traz uma dezena de ferramentas de linha de comando
    /// (<c>llama-cli</c>, <c>llama-quantize</c>, <c>llama-bench</c>…) que só
    /// servem a quem trabalha com o llama.cpp. São dezenas de MB em disco de
    /// quem só queria uma ata. O mesmo recorte que o
    /// <c>empacotar_motor_de_ata.sh</c> faz.
    /// </remarks>
    private static void Enxugar(string pasta)
    {
        foreach (string exe in Directory.EnumerateFiles(pasta, "llama-*.exe"))
            if (!Path.GetFileName(exe).Equals("llama-server.exe", StringComparison.OrdinalIgnoreCase))
                TentarApagar(exe);

        foreach (string nome in new[] { "ggml-rpc-server.exe", "rpc-server.exe" })
            TentarApagar(Path.Combine(pasta, nome));
    }

    private static void TentarApagar(string caminho)
    {
        try { if (File.Exists(caminho)) File.Delete(caminho); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }
}
