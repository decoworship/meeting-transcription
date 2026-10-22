using System.Buffers.Binary;
using System.IO.Compression;

namespace MeetingApp.Nucleo;

/// <summary>
/// Um codificador de PNG mínimo, para o print da tela.
/// </summary>
/// <remarks>
/// <para>
/// <b>Escrito aqui, e não emprestado.</b> O <c>System.Drawing</c> é o GDI+ que a
/// bandeja já tirou (ver <c>IconesDaBandeja</c>), e o WIC seria mais uma
/// interface COM para declarar à mão. Um PNG é uma assinatura, três pedaços e
/// um zlib que o .NET já tem — menos código que qualquer uma das duas pontes.
/// </para>
/// <para>
/// RGB de 8 bits, sem alfa e sem filtro por linha. O alfa do <c>BitBlt</c> vem
/// zerado, e copiá-lo faria um print transparente. O filtro "nenhum" comprime
/// pior que o "Sub" em foto, e quase igual em tela, que é cor chapada e texto.
/// </para>
/// </remarks>
public static class Png
{
    private static readonly byte[] Assinatura = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    /// <param name="bgra">Os pixels de cima para baixo, quatro bytes cada, como o DIB os dá.</param>
    /// <param name="stride">Bytes por linha em <paramref name="bgra"/>, enchimento incluído.</param>
    public static byte[] Codificar(int largura, int altura, ReadOnlySpan<byte> bgra, int stride)
    {
        if (largura <= 0 || altura <= 0) throw new ArgumentOutOfRangeException(nameof(largura));
        if (stride < largura * 4 || bgra.Length < stride * (altura - 1) + largura * 4)
            throw new ArgumentException("pixels a menos para o tamanho dado", nameof(bgra));

        using var saida = new MemoryStream();
        saida.Write(Assinatura);

        Span<byte> ihdr = stackalloc byte[13];
        BinaryPrimitives.WriteInt32BigEndian(ihdr, largura);
        BinaryPrimitives.WriteInt32BigEndian(ihdr[4..], altura);
        ihdr[8] = 8;    // bits por canal
        ihdr[9] = 2;    // RGB
        ihdr[10] = 0;   // deflate
        ihdr[11] = 0;   // filtro adaptativo (o único que existe)
        ihdr[12] = 0;   // sem entrelaçamento
        Pedaco(saida, "IHDR", ihdr);

        using (var idat = new MemoryStream())
        {
            using (var z = new ZLibStream(idat, CompressionLevel.Fastest, leaveOpen: true))
            {
                byte[] linha = new byte[1 + largura * 3];
                for (int y = 0; y < altura; y++)
                {
                    var origem = bgra.Slice(y * stride, largura * 4);
                    linha[0] = 0;   // filtro "nenhum"
                    for (int x = 0, o = 1; x < largura; x++, o += 3)
                    {
                        linha[o] = origem[x * 4 + 2];
                        linha[o + 1] = origem[x * 4 + 1];
                        linha[o + 2] = origem[x * 4];
                    }
                    z.Write(linha);
                }
            }
            Pedaco(saida, "IDAT", idat.GetBuffer().AsSpan(0, (int)idat.Length));
        }

        Pedaco(saida, "IEND", []);
        return saida.ToArray();
    }

    private static void Pedaco(Stream saida, string tipo, ReadOnlySpan<byte> dados)
    {
        Span<byte> quatro = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(quatro, dados.Length);
        saida.Write(quatro);

        byte[] tipoEDados = new byte[4 + dados.Length];
        for (int i = 0; i < 4; i++) tipoEDados[i] = (byte)tipo[i];
        dados.CopyTo(tipoEDados.AsSpan(4));
        saida.Write(tipoEDados);

        BinaryPrimitives.WriteUInt32BigEndian(quatro, Crc32(tipoEDados));
        saida.Write(quatro);
    }

    private static readonly uint[] Tabela = MontarTabela();

    private static uint[] MontarTabela()
    {
        var t = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            uint c = n;
            for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            t[n] = c;
        }
        return t;
    }

    /// <summary>O CRC-32 do PNG (o mesmo do zip), sobre tipo e dados do pedaço.</summary>
    public static uint Crc32(ReadOnlySpan<byte> dados)
    {
        uint c = 0xFFFFFFFFu;
        foreach (byte b in dados) c = Tabela[(c ^ b) & 0xFF] ^ (c >> 8);
        return c ^ 0xFFFFFFFFu;
    }
}
