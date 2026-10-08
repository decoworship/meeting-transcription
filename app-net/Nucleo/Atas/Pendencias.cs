using System.Text.RegularExpressions;

namespace MeetingApp.Nucleo.Atas;

/// <summary>
/// A caixinha da pendência, marcada na tela e gravada no próprio <c>ata.md</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>A marca mora no arquivo, e não ao lado dele.</b> Com <c>- [x]</c> no
/// <c>ata.md</c>, o resto já funciona sem saber de nada: o <c>.docx</c> sai com
/// ☑, o Copiar leva a marca, e a contagem de pendências abertas
/// (<see cref="EstadoDaAta"/>, que só conta <c>- [ ]</c>) cai sozinha.
/// </para>
/// <para>
/// <b>A tela diz o número e o texto da pendência, e os dois têm que bater.</b>
/// Se a ata foi gerada de novo com a tela aberta, o número aponta outra linha —
/// marcar por ele marcaria a pendência errada, em silêncio.
/// </para>
/// </remarks>
public static class Pendencias
{
    /// <summary>
    /// A linha de pendência, com o recuo, a caixinha e o fim de linha à parte.
    /// </summary>
    /// <remarks>
    /// A mesma régua do <c>renderizar()</c> da tela (<c>atas.js</c>): a linha
    /// sem o recuo começa com <c>-</c> ou <c>*</c>, e a caixinha é
    /// <c>[ ]</c> ou <c>[x]</c> minúsculo. Contar diferente da tela faria o
    /// número dela apontar outra linha.
    /// </remarks>
    private static readonly Regex Linha = new(
        @"^(\s*[-*]\s+\[)( |x)(\]\s+)(.*?)(\r?)$", RegexOptions.Compiled);

    /// <summary>Troca a caixinha da pendência de número <paramref name="indice"/>.</summary>
    /// <param name="texto">O texto da pendência como a tela a leu, para conferir.</param>
    /// <exception cref="InvalidOperationException">A ata mudou desde que a tela a leu.</exception>
    public static string Marcar(string markdown, int indice, string texto, bool feita)
    {
        var linhas = markdown.Split('\n');
        int n = -1;
        for (int i = 0; i < linhas.Length; i++)
        {
            var m = Linha.Match(linhas[i]);
            if (!m.Success || ++n != indice) continue;

            if (m.Groups[4].Value.Trim() != texto.Trim()) break;
            linhas[i] = m.Groups[1].Value + (feita ? "x" : " ") + m.Groups[3].Value
                        + m.Groups[4].Value + m.Groups[5].Value;
            return string.Join('\n', linhas);
        }
        throw new InvalidOperationException(
            "a ata mudou desde que foi aberta — feche e abra a ata de novo para marcar");
    }

    /// <summary>Marca no <c>ata.md</c> da pasta, sem mudar a data dele.</summary>
    /// <remarks>
    /// <para>
    /// A data fica porque o aviso de "ata desatualizada"
    /// (<see cref="EstadoDaAta.EstaVelha"/>) compara a data do <c>ata.md</c> com
    /// a da transcrição: marcar uma caixinha não é escrever a ata de novo, e não
    /// pode esconder que a transcrição foi corrigida depois dela.
    /// </para>
    /// <para>
    /// Escreve num <c>.tmp</c> e troca, como o <c>app.json</c>: uma queda no
    /// meio não deixa a ata pela metade.
    /// </para>
    /// </remarks>
    public static void MarcarNoArquivo(string pasta, int indice, string texto, bool feita)
    {
        string caminho = Path.Combine(pasta, "ata.md");
        if (!File.Exists(caminho))
            throw new InvalidOperationException("esta reunião não tem ata");

        var data = File.GetLastWriteTimeUtc(caminho);
        string novo = Marcar(File.ReadAllText(caminho), indice, texto, feita);

        string tmp = caminho + ".tmp";
        File.WriteAllText(tmp, novo);
        File.Move(tmp, caminho, overwrite: true);
        File.SetLastWriteTimeUtc(caminho, data);
    }
}
