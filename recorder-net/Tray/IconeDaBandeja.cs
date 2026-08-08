using System.Drawing;
using System.Drawing.Imaging;
using System.Reflection;
using MeetingRecorder.Core;

namespace MeetingRecorder.Tray;

/// <summary>
/// Desenha o ícone da bandeja: o logo do app, tingido com a cor do estado.
/// </summary>
/// <remarks>
/// <para>
/// <b>Por que tingir em vez de simplesmente usar o logo.</b> Durante a gravação a
/// cor do ícone é o <i>único</i> aviso que existe — é ela que distingue gravando
/// (vermelho), mudo por sua escolha (laranja) e canal sem áudio (amarelo). Trocar
/// o ícone colorido pelo logo perderia isso, e o amarelo é justamente o
/// mecanismo que existe por causa da gravação de 06/08 que saiu 95% muda.
/// </para>
/// <para>
/// O logo é uma silhueta monocromática (preto puro, ~25% de cobertura), o que o
/// torna ideal para tingir: basta trocar o RGB preservando o alfa, e a forma
/// continua reconhecível em qualquer cor. Um logo multicolorido não permitiria
/// isso e exigiria um selo sobreposto.
/// </para>
/// <para>
/// O PNG de 256 px vem embutido no executável. O Windows reduz para 16, 24 ou 32
/// conforme o DPI, e reduzir de 256 com interpolação de qualidade sai melhor que
/// embutir cada tamanho.
/// </para>
/// </remarks>
public static class IconeDaBandeja
{
    private static readonly Dictionary<CorDaBandeja, Color> Cores = new()
    {
        [CorDaBandeja.Cinza] = Color.FromArgb(120, 120, 120),
        [CorDaBandeja.Vermelho] = Color.FromArgb(220, 50, 50),
        [CorDaBandeja.Laranja] = Color.FromArgb(240, 150, 30),
        [CorDaBandeja.Amarelo] = Color.FromArgb(230, 200, 30),
    };

    private static readonly Dictionary<CorDaBandeja, Icon> Cache = [];
    private static readonly Lazy<Bitmap?> Logo = new(CarregarLogo);

    public static Icon De(CorDaBandeja cor)
    {
        // A bandeja repinta a cada segundo; recriar o bitmap sempre vazaria
        // handles de GDI, que são recurso limitado no Windows.
        if (Cache.TryGetValue(cor, out var pronto)) return pronto;

        using var bmp = Logo.Value is { } logo
            ? Tingir(logo, Cores[cor])
            : Circulo(Cores[cor]);

        var icone = Icon.FromHandle(bmp.GetHicon());
        Cache[cor] = icone;
        return icone;
    }

    private static Bitmap? CarregarLogo()
    {
        try
        {
            using var fluxo = Assembly.GetExecutingAssembly()
                .GetManifestResourceStream("MeetingRecorder.Tray.logo-256.png");
            return fluxo is null ? null : new Bitmap(fluxo);
        }
        catch
        {
            // Sem logo o app continua utilizável com o círculo — um recurso
            // ausente não pode impedir gravar.
            return null;
        }
    }

    /// <summary>Troca o RGB preservando o alfa, que é o que mantém a forma.</summary>
    private static Bitmap Tingir(Bitmap origem, Color cor)
    {
        var destino = new Bitmap(32, 32, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(destino))
        {
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;

            // Matriz que zera as componentes de cor da origem e injeta a cor
            // desejada, mantendo o alfa intacto.
            var matriz = new ColorMatrix(
            [
                [0, 0, 0, 0, 0],
                [0, 0, 0, 0, 0],
                [0, 0, 0, 0, 0],
                [0, 0, 0, 1, 0],
                [cor.R / 255f, cor.G / 255f, cor.B / 255f, 0, 1],
            ]);
            using var atributos = new ImageAttributes();
            atributos.SetColorMatrix(matriz);

            g.DrawImage(origem, new Rectangle(0, 0, 32, 32),
                0, 0, origem.Width, origem.Height, GraphicsUnit.Pixel, atributos);
        }
        return destino;
    }

    /// <summary>Reserva: círculo simples, caso o recurso do logo falte.</summary>
    private static Bitmap Circulo(Color cor)
    {
        var bmp = new Bitmap(32, 32, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        using var pincel = new SolidBrush(cor);
        g.FillEllipse(pincel, 3, 3, 26, 26);
        return bmp;
    }
}
