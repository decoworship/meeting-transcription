using System.Drawing;
using System.Drawing.Drawing2D;
using MeetingRecorder.Core;

namespace MeetingRecorder.Tray;

/// <summary>
/// Desenha o ícone da bandeja em cada estado.
/// </summary>
/// <remarks>
/// Desenhado em código em vez de embutir .ico: são quatro círculos coloridos, e
/// gerar dispensa arquivo de recurso, ferramenta de conversão e o risco de o
/// ícone sair borrado em DPI alto — o <c>pystray</c> do gravador Python faz o
/// mesmo com o PIL.
/// </remarks>
public static class IconeDaBandeja
{
    private static readonly Dictionary<CorDaBandeja, Color> Cores = new()
    {
        [CorDaBandeja.Cinza] = Color.FromArgb(128, 128, 128),
        [CorDaBandeja.Vermelho] = Color.FromArgb(220, 50, 50),
        [CorDaBandeja.Laranja] = Color.FromArgb(240, 150, 30),
        [CorDaBandeja.Amarelo] = Color.FromArgb(240, 220, 40),
    };

    private static readonly Dictionary<CorDaBandeja, Icon> Cache = [];

    public static Icon De(CorDaBandeja cor)
    {
        // A bandeja repinta com frequência; recriar o bitmap a cada refresh
        // vazaria handles de GDI, que são recurso limitado no Windows.
        if (Cache.TryGetValue(cor, out var pronto)) return pronto;

        using var bmp = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);
            using var pincel = new SolidBrush(Cores[cor]);
            g.FillEllipse(pincel, 3, 3, 26, 26);
            // Borda escura: sem ela o cinza some numa barra de tarefas clara e o
            // amarelo some numa escura.
            using var caneta = new Pen(Color.FromArgb(90, 0, 0, 0), 2);
            g.DrawEllipse(caneta, 3, 3, 26, 26);
        }

        var icone = Icon.FromHandle(bmp.GetHicon());
        Cache[cor] = icone;
        return icone;
    }
}
