using System.Runtime.InteropServices;

namespace MeetingApp.App.Nativo;

/// <summary>
/// Diálogo de escolha de pasta via <c>IFileOpenDialog</c> com
/// <c>FOS_PICKFOLDERS</c>. Substitui o <c>FolderBrowserDialog</c> — que por
/// baixo usa exatamente este COM.
/// </summary>
/// <remarks>
/// Cópia da que o gravador usa: as duas precisam da mesma coisa e não têm um
/// assembly em comum — os dois executáveis são separados por desenho, e criar
/// uma biblioteca compartilhada por causa de setenta linhas de COM acoplaria
/// justamente o que a Fase 2 decidiu manter apartado.
///
/// Exige a thread STA (<c>[STAThread]</c> no Main) e o COM embutido.
/// </remarks>
internal static class SeletorDePasta
{
    /// <returns>O caminho escolhido, ou <c>null</c> se cancelado.</returns>
    public static string? Escolher(IntPtr dono, string pastaInicial, string titulo)
    {
        var dialogo = (IFileDialog)new FileOpenDialogRcw();
        dialogo.SetOptions(FOS_PICKFOLDERS | FOS_FORCEFILESYSTEM);
        dialogo.SetTitle(titulo);

        try
        {
            dialogo.SetFolder(ItemDoCaminho(pastaInicial));
        }
        catch
        {
            // Pasta inicial inválida não impede escolher outra.
        }

        if (dialogo.Show(dono) != 0) return null;   // cancelado

        dialogo.GetResult(out var item);
        item.GetDisplayName(SIGDN_FILESYSPATH, out IntPtr ptr);
        string caminho = Marshal.PtrToStringUni(ptr)!;
        Marshal.FreeCoTaskMem(ptr);
        return caminho;
    }

    private const uint FOS_PICKFOLDERS = 0x00000020;
    private const uint FOS_FORCEFILESYSTEM = 0x00000040;
    private const uint SIGDN_FILESYSPATH = 0x80058000;

    /// <summary>
    /// O <c>IShellItem</c> de um caminho, para o <c>SetFolder</c>.
    /// </summary>
    /// <remarks>
    /// O ponteiro cru e a conversão manual existem porque declarar o P/Invoke
    /// com <c>out IShellItem</c> — marshalling COM direto — é IL2050 na análise
    /// de trim: o linker não consegue provar que a interface sobrevive. Aqui só
    /// o <c>IUnknown*</c> atravessa a fronteira, e o RCW resolve a interface
    /// deste lado, onde o linker enxerga o uso.
    /// </remarks>
    private static IShellItem ItemDoCaminho(string caminho)
    {
        var iid = typeof(IShellItem).GUID;
        SHCreateItemFromParsingName(caminho, IntPtr.Zero, ref iid, out IntPtr bruto);
        try
        {
            return (IShellItem)Marshal.GetObjectForIUnknown(bruto);
        }
        finally
        {
            // O RCW tem a própria referência; esta é a da criação.
            Marshal.Release(bruto);
        }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    private static extern void SHCreateItemFromParsingName(string pszPath, IntPtr pbc,
        ref Guid riid, out IntPtr ppv);

    [ComImport, Guid("DC1C5A9C-E88A-4dde-A5A1-60F82A20AEF7")]
    private class FileOpenDialogRcw { }

    // A ordem dos métodos é a vtable do COM; mudar a ordem chama o método errado.
    [ComImport, Guid("42f85136-db7e-439c-85f1-e4075d135fc8"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IFileDialog
    {
        [PreserveSig] int Show(IntPtr hwndOwner);              // IModalWindow
        void SetFileTypes(uint cFileTypes, IntPtr rgFilterSpec);
        void SetFileTypeIndex(uint iFileType);
        void GetFileTypeIndex(out uint piFileType);
        void Advise(IntPtr pfde, out uint pdwCookie);
        void Unadvise(uint dwCookie);
        void SetOptions(uint fos);
        void GetOptions(out uint pfos);
        void SetDefaultFolder(IShellItem psi);
        void SetFolder(IShellItem psi);
        void GetFolder(out IShellItem ppsi);
        void GetCurrentSelection(out IShellItem ppsi);
        void SetFileName([MarshalAs(UnmanagedType.LPWStr)] string pszName);
        void GetFileName([MarshalAs(UnmanagedType.LPWStr)] out string pszName);
        void SetTitle([MarshalAs(UnmanagedType.LPWStr)] string pszTitle);
        void SetOkButtonLabel([MarshalAs(UnmanagedType.LPWStr)] string pszText);
        void SetFileNameLabel([MarshalAs(UnmanagedType.LPWStr)] string pszLabel);
        void GetResult(out IShellItem ppsi);
        void AddPlace(IShellItem psi, uint fdap);
        void SetDefaultExtension([MarshalAs(UnmanagedType.LPWStr)] string pszExtension);
        void Close(int hr);
        void SetClientGuid(ref Guid guid);
        void ClearClientData();
        void SetFilter(IntPtr pFilter);
    }

    [ComImport, Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItem
    {
        void BindToHandler(IntPtr pbc, ref Guid bhid, ref Guid riid, out IntPtr ppv);
        void GetParent(out IShellItem ppsi);
        void GetDisplayName(uint sigdnName, out IntPtr ppszName);
        void GetAttributes(uint sfgaoMask, out uint psfgaoAttribs);
        void Compare(IShellItem psi, uint hint, out int piOrder);
    }
}
