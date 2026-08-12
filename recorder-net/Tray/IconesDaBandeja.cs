using System.ComponentModel;
using System.Reflection;
using MeetingRecorder.Core;
using MeetingRecorder.Tray.Nativo;

namespace MeetingRecorder.Tray;

/// <summary>
/// Os ícones da bandeja, um por cor de estado, criados dos <c>.ico</c>
/// embutidos com <c>CreateIconFromResourceEx</c>.
/// </summary>
/// <remarks>
/// <para>
/// Substitui o tingimento em runtime com GDI+ (<c>System.Drawing</c>), que era a
/// última dependência do WindowsDesktop na bandeja. Os quatro <c>.ico</c> saem
/// prontos do <c>tools/gerar_icone.py</c>, em 16/20/24/32 px — a cor do estado
/// continua sendo o único aviso que existe durante a gravação.
/// </para>
/// <para>
/// O tamanho vem de <c>GetSystemMetrics(SM_CXSMICON)</c>, que acompanha o DPI.
/// Cache porque a bandeja repinta a cada segundo, e handle de ícone é recurso
/// limitado no Windows.
/// </para>
/// </remarks>
internal static class IconesDaBandeja
{
    private static readonly Dictionary<CorDaBandeja, IntPtr> Cache = [];

    public static IntPtr Obter(CorDaBandeja cor)
    {
        if (Cache.TryGetValue(cor, out var pronto)) return pronto;

        string nome = $"MeetingRecorder.Tray.bandeja-{cor.ToString().ToLowerInvariant()}.ico";
        using var fluxo = Assembly.GetExecutingAssembly().GetManifestResourceStream(nome)
            ?? throw new InvalidOperationException($"recurso embutido ausente: {nome}");
        var dados = new byte[fluxo.Length];
        fluxo.ReadExactly(dados);

        var (deslocamento, tamanho) = MelhorEntrada(dados, Win32.GetSystemMetrics(Win32.SM_CXSMICON));

        IntPtr icone;
        unsafe
        {
            fixed (byte* p = &dados[deslocamento])
            {
                icone = Win32.CreateIconFromResourceEx((IntPtr)p, (uint)tamanho,
                    fIcon: true, 0x00030000, 0, 0, 0);
            }
        }
        if (icone == IntPtr.Zero)
            throw new Win32Exception($"CreateIconFromResourceEx falhou para {nome}");

        Cache[cor] = icone;
        return icone;
    }

    /// <summary>
    /// Lê o ICONDIR do <c>.ico</c> e escolhe a imagem: tamanho exato, senão a
    /// menor maior que o pedido (encolher perde menos que esticar), senão a maior.
    /// </summary>
    private static (int Deslocamento, int Tamanho) MelhorEntrada(byte[] ico, int alvo)
    {
        int n = BitConverter.ToUInt16(ico, 4);
        (int Largura, int Deslocamento, int Tamanho) melhor = default;

        for (int i = 0; i < n; i++)
        {
            int e = 6 + 16 * i;
            int largura = ico[e] == 0 ? 256 : ico[e];
            var atual = (largura,
                         BitConverter.ToInt32(ico, e + 12),
                         BitConverter.ToInt32(ico, e + 8));

            if (largura == alvo) return (atual.Item2, atual.Item3);
            bool serve = largura >= alvo;
            bool melhorServe = melhor.Largura >= alvo;
            if (melhor.Largura == 0
                || (serve && (!melhorServe || largura < melhor.Largura))
                || (!serve && !melhorServe && largura > melhor.Largura))
                melhor = atual;
        }
        return (melhor.Deslocamento, melhor.Tamanho);
    }

    public static void Liberar()
    {
        foreach (var icone in Cache.Values) Win32.DestroyIcon(icone);
        Cache.Clear();
    }
}
