using System.Text;

namespace MeetingApp.Nucleo;

/// <summary>
/// As imagens da reunião, em <c>capturas/</c> ao lado do <c>notas.md</c>.
/// </summary>
/// <remarks>
/// <para>
/// Duas portas e um lugar só: o print que o app tira durante a gravação (pelo
/// atalho ou pelo botão) e a imagem que a pessoa cola ou anexa nas notas. As
/// duas viram arquivo nesta pasta e uma linha de markdown nas notas —
/// <c>[00:12:34] ![captura de tela](capturas/tela-00-12-34.png)</c> —, que é o
/// que as liga ao momento da reunião e ao que se escreveu sobre ela.
/// </para>
/// <para>
/// <b>A nota é o índice, e a pasta é o registro.</b> Apagar a linha das notas
/// não apaga o arquivo: o print continua na pasta da gravação, que é onde a
/// pessoa o procuraria fora do app. Pelo mesmo motivo nada disto entra no
/// <c>meta.json</c>, que é do gravador e tem schema congelado.
/// </para>
/// </remarks>
public static class Capturas
{
    public const string Pasta = "capturas";

    /// <summary>
    /// 30 MB. Um print 4K em PNG passa pouco de 10; o teto existe para colar um
    /// arquivo errado não encher a pasta da gravação.
    /// </summary>
    public const int TamanhoMaximo = 30 * 1024 * 1024;

    /// <summary>
    /// Grava a imagem e devolve o caminho relativo à pasta da gravação, com
    /// barra normal — é ele que vai para o markdown e para a URL da tela.
    /// </summary>
    /// <param name="nomeBase">Sem extensão: ela vem do conteúdo.</param>
    /// <exception cref="InvalidDataException">Não é imagem, ou é grande demais.</exception>
    public static string Guardar(string pastaDaGravacao, byte[] bytes, string nomeBase)
    {
        if (bytes.Length > TamanhoMaximo)
            throw new InvalidDataException(
                $"imagem grande demais ({bytes.Length / (1024 * 1024)} MB; o limite é "
                + $"{TamanhoMaximo / (1024 * 1024)} MB)");
        string extensao = Extensao(bytes)
            ?? throw new InvalidDataException("o arquivo não é uma imagem (PNG, JPEG, GIF ou WebP)");

        string pasta = Path.Combine(pastaDaGravacao, Pasta);
        Directory.CreateDirectory(pasta);
        string nome = NomeLimpo(nomeBase);

        // Temporário e move sem sobrescrever: dois prints no mesmo segundo
        // (o atalho apertado duas vezes) disputam o mesmo nome, e o segundo
        // não pode apagar o primeiro.
        string temporario = Path.Combine(pasta, $".{Guid.NewGuid():N}.tmp");
        File.WriteAllBytes(temporario, bytes);
        try
        {
            for (int n = 1; ; n++)
            {
                string arquivo = n == 1 ? $"{nome}.{extensao}" : $"{nome}-{n}.{extensao}";
                string destino = Path.Combine(pasta, arquivo);
                if (File.Exists(destino)) continue;
                try
                {
                    File.Move(temporario, destino, overwrite: false);
                    return $"{Pasta}/{arquivo}";
                }
                catch (IOException) when (File.Exists(destino))
                {
                    // Outro chegou primeiro entre a conferência e o move.
                }
            }
        }
        finally
        {
            if (File.Exists(temporario)) File.Delete(temporario);
        }
    }

    /// <summary>"tela-00-12-34": o nome do arquivo é o momento da reunião.</summary>
    public static string NomeNoMomento(string prefixo, double segundos) =>
        $"{prefixo}-{Relogio(segundos).Replace(':', '-')}";

    /// <summary>
    /// A linha que entra nas notas. Com momento, é um momento marcado como
    /// outro qualquer — e a reunião a mostra como botão que toca dali.
    /// </summary>
    public static string LinhaDaNota(string relativo, double? segundos,
                                     string legenda = "captura de tela")
    {
        string imagem = $"![{legenda}]({relativo})";
        return segundos is { } s ? $"[{Relogio(s)}] {imagem}" : imagem;
    }

    /// <summary>
    /// Acrescenta a linha no fim das notas, numa linha própria.
    /// </summary>
    /// <remarks>
    /// No fim, e não onde está o cursor: o atalho é apertado com o Teams na
    /// frente, e o núcleo não sabe onde está cursor nenhum. Quem tem a nota
    /// aberta recebe o evento e põe a mesma linha no campo (ver notas.js), de
    /// modo que o próximo salvamento da tela não a apague.
    /// </remarks>
    public static void AnexarANotas(string pastaDaGravacao, string linha)
    {
        // Dois prints seguidos codificam em paralelo, e ler-somar-gravar sem
        // trava deixaria só a linha do último.
        lock (Trava)
        {
            string atual = Notas.Ler(pastaDaGravacao);
            string junto = atual.Length == 0 ? linha
                         : atual.EndsWith('\n') ? atual + linha
                         : atual + "\n" + linha;
            Notas.Salvar(pastaDaGravacao, junto);
        }
    }

    private static readonly object Trava = new();

    /// <summary>"00:12:34" — o mesmo relógio do "Marcar momento" das notas.</summary>
    public static string Relogio(double segundos)
    {
        int s = (int)Math.Max(0, Math.Round(segundos));
        return $"{s / 3600:00}:{s % 3600 / 60:00}:{s % 60:00}";
    }

    /// <summary>A extensão pelos primeiros bytes, ou nulo se não é imagem que se guarde.</summary>
    private static string? Extensao(ReadOnlySpan<byte> b)
    {
        if (b.StartsWith((ReadOnlySpan<byte>)[0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A])) return "png";
        if (b.StartsWith((ReadOnlySpan<byte>)[0xFF, 0xD8, 0xFF])) return "jpg";
        if (b.StartsWith("GIF87a"u8) || b.StartsWith("GIF89a"u8)) return "gif";
        if (b.Length >= 12 && b.StartsWith("RIFF"u8) && b[8..12].SequenceEqual("WEBP"u8)) return "webp";
        return null;
    }

    /// <summary>
    /// Só o último pedaço, e só letras, dígitos e hífen: o nome vem da tela, e
    /// nada do que ela mande pode sair de <c>capturas/</c>.
    /// </summary>
    private static string NomeLimpo(string nomeBase)
    {
        string ultimo = nomeBase.Split('/', '\\').LastOrDefault(p => p.Length > 0) ?? "";
        var sb = new StringBuilder();
        foreach (char c in ultimo.ToLowerInvariant())
            if (char.IsAsciiLetterOrDigit(c) || c == '-' || c == '_') sb.Append(c);
        return sb.Length > 0 ? sb.ToString(0, Math.Min(sb.Length, 60)) : "imagem";
    }
}
