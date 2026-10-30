using System.Runtime.InteropServices;

namespace MeetingApp.App.Nativo;

/// <summary>
/// Quadros BGRA para um MP4 H.264, pelo encoder do próprio Windows.
/// </summary>
/// <remarks>
/// <para>
/// <b>Media Foundation, e não FFmpeg.</b> O encoder vem com o Windows, usa o da
/// placa quando ela tem um (NVENC, Quick Sync, AMF), e o WebView2 toca o MP4
/// que sai dele sem nada a mais. O FFmpeg seria mais 80 MB no instalador para
/// fazer a mesma coisa.
/// </para>
/// <para>
/// <b>Chamadas pela vtable, e não por <c>ComImport</c>.</b> As interfaces do MF
/// herdam do <c>IMFAttributes</c>, de trinta métodos, e o COM embutido do .NET
/// exige redeclarar a base inteira em cada derivada. Além disso, devolver
/// interface de P/Invoke é IL2050 no trimming (ver <c>SeletorDePasta</c>). Aqui
/// só atravessam ponteiros, e cada método é o índice dele na vtable — a ordem é
/// a de <c>mfobjects.h</c> e <c>mfreadwrite.h</c>, e mudar um índice chama o
/// método errado.
/// </para>
/// <para>
/// <b>O MP4 só fica legível depois do <see cref="Dispose"/>.</b> O índice
/// (<c>moov</c>) é escrito no fim: um processo morto no meio deixa um arquivo
/// que nenhum tocador abre. O áudio continua salvo — o WAV é à prova de queda —,
/// e é por isso que o vídeo é opcional e nasce desligado.
/// </para>
/// <para>Não é seguro entre threads: uma thread escreve, e a mesma fecha.</para>
/// </remarks>
internal sealed unsafe partial class CodificadorH264 : IDisposable
{
    public int Largura { get; }
    public int Altura { get; }

    private IntPtr _escritor;   // IMFSinkWriter*
    private readonly uint _fluxo;
    private readonly int _bytesPorQuadro;
    private bool _comecou;
    private static int _iniciados;

    /// <param name="largura">Par: o H.264 trabalha em blocos de 2×2.</param>
    /// <param name="quadrosPorSegundo">
    /// O nominal, para o encoder dimensionar o bitrate. O tempo de cada quadro é
    /// o que vem em <see cref="Escrever"/>, e pode variar.
    /// </param>
    public CodificadorH264(string caminho, int largura, int altura, int quadrosPorSegundo)
    {
        if (largura <= 0 || altura <= 0 || largura % 2 != 0 || altura % 2 != 0)
            throw new ArgumentException($"tamanho inválido para H.264: {largura}×{altura}");
        Largura = largura;
        Altura = altura;
        _bytesPorQuadro = largura * altura * 4;

        if (Interlocked.Increment(ref _iniciados) == 1)
            Ok(MFStartup(MF_VERSION, 0), "MFStartup");

        try
        {
            // Com o encoder da placa primeiro. Quando a combinação não serve —
            // driver velho, placa sem encoder —, o do Windows em software.
            try
            {
                (_escritor, _fluxo) = Abrir(caminho, largura, altura, quadrosPorSegundo, hardware: true);
            }
            catch (COMException)
            {
                try { File.Delete(caminho); } catch { }
                (_escritor, _fluxo) = Abrir(caminho, largura, altura, quadrosPorSegundo, hardware: false);
            }
        }
        catch
        {
            Desligar();
            throw;
        }
    }

    private static (IntPtr, uint) Abrir(string caminho, int largura, int altura, int qps, bool hardware)
    {
        IntPtr atributos = IntPtr.Zero, saida = IntPtr.Zero, entrada = IntPtr.Zero, escritor = IntPtr.Zero;
        try
        {
            Ok(MFCreateAttributes(out atributos, 2), "MFCreateAttributes");
            DefinirUInt32(atributos, MF_READWRITE_ENABLE_HARDWARE_TRANSFORMS, hardware ? 1u : 0u);
            Ok(MFCreateSinkWriterFromURL(caminho, IntPtr.Zero, atributos, out escritor),
               "MFCreateSinkWriterFromURL");

            ulong tamanho = ((ulong)(uint)largura << 32) | (uint)altura;
            ulong taxa = ((ulong)(uint)qps << 32) | 1u;

            // Tela é texto e bordas paradas, e comprime bem: 0,08 bit por pixel
            // por quadro dá ~1,7 Mb/s em 1080p a 10 qps, perto de 750 MB por
            // hora, e o texto continua legível.
            uint bitrate = (uint)Math.Clamp((long)largura * altura * qps * 8 / 100, 500_000, 8_000_000);

            Ok(MFCreateMediaType(out saida), "MFCreateMediaType");
            DefinirGuid(saida, MF_MT_MAJOR_TYPE, MFMediaType_Video);
            DefinirGuid(saida, MF_MT_SUBTYPE, MFVideoFormat_H264);
            DefinirUInt32(saida, MF_MT_AVG_BITRATE, bitrate);
            DefinirUInt32(saida, MF_MT_INTERLACE_MODE, MFVideoInterlace_Progressive);
            DefinirUInt32(saida, MF_MT_MPEG2_PROFILE, eAVEncH264VProfile_Main);
            DefinirUInt64(saida, MF_MT_FRAME_SIZE, tamanho);
            DefinirUInt64(saida, MF_MT_FRAME_RATE, taxa);
            DefinirUInt64(saida, MF_MT_PIXEL_ASPECT_RATIO, (1ul << 32) | 1u);
            uint fluxo;
            Ok(((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, uint*, int>)Vtbl(escritor, 3))(escritor, saida, &fluxo),
               "IMFSinkWriter::AddStream");

            // RGB32 entra, e o escritor põe o conversor para NV12 no meio.
            // Passo positivo: de cima para baixo, como o DIB do BitBlt.
            Ok(MFCreateMediaType(out entrada), "MFCreateMediaType");
            DefinirGuid(entrada, MF_MT_MAJOR_TYPE, MFMediaType_Video);
            DefinirGuid(entrada, MF_MT_SUBTYPE, MFVideoFormat_RGB32);
            DefinirUInt32(entrada, MF_MT_INTERLACE_MODE, MFVideoInterlace_Progressive);
            DefinirUInt32(entrada, MF_MT_DEFAULT_STRIDE, (uint)(largura * 4));
            DefinirUInt64(entrada, MF_MT_FRAME_SIZE, tamanho);
            DefinirUInt64(entrada, MF_MT_FRAME_RATE, taxa);
            DefinirUInt64(entrada, MF_MT_PIXEL_ASPECT_RATIO, (1ul << 32) | 1u);
            Ok(((delegate* unmanaged[Stdcall]<IntPtr, uint, IntPtr, IntPtr, int>)Vtbl(escritor, 4))(escritor, fluxo, entrada, IntPtr.Zero),
               "IMFSinkWriter::SetInputMediaType");

            Ok(((delegate* unmanaged[Stdcall]<IntPtr, int>)Vtbl(escritor, 5))(escritor), "IMFSinkWriter::BeginWriting");

            var pronto = escritor;
            escritor = IntPtr.Zero;
            return (pronto, fluxo);
        }
        finally
        {
            Soltar(atributos);
            Soltar(saida);
            Soltar(entrada);
            Soltar(escritor);
        }
    }

    /// <summary>Um quadro BGRA de cima para baixo, sem enchimento entre linhas.</summary>
    /// <param name="tempo">Em unidades de 100 ns, desde o começo do vídeo.</param>
    /// <param name="duracao">Até o próximo quadro, nas mesmas unidades.</param>
    public void Escrever(IntPtr bgra, long tempo, long duracao)
    {
        ObjectDisposedException.ThrowIf(_escritor == IntPtr.Zero, this);
        IntPtr buffer = IntPtr.Zero, amostra = IntPtr.Zero;
        try
        {
            Ok(MFCreateMemoryBuffer((uint)_bytesPorQuadro, out buffer), "MFCreateMemoryBuffer");
            byte* destino;
            uint max, atual;
            Ok(((delegate* unmanaged[Stdcall]<IntPtr, byte**, uint*, uint*, int>)Vtbl(buffer, 3))(buffer, &destino, &max, &atual),
               "IMFMediaBuffer::Lock");
            Buffer.MemoryCopy((void*)bgra, destino, max, _bytesPorQuadro);
            ((delegate* unmanaged[Stdcall]<IntPtr, int>)Vtbl(buffer, 4))(buffer);
            Ok(((delegate* unmanaged[Stdcall]<IntPtr, uint, int>)Vtbl(buffer, 6))(buffer, (uint)_bytesPorQuadro),
               "IMFMediaBuffer::SetCurrentLength");

            Ok(MFCreateSample(out amostra), "MFCreateSample");
            Ok(((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, int>)Vtbl(amostra, 42))(amostra, buffer), "IMFSample::AddBuffer");
            Ok(((delegate* unmanaged[Stdcall]<IntPtr, long, int>)Vtbl(amostra, 36))(amostra, tempo), "IMFSample::SetSampleTime");
            Ok(((delegate* unmanaged[Stdcall]<IntPtr, long, int>)Vtbl(amostra, 38))(amostra, duracao), "IMFSample::SetSampleDuration");

            Ok(((delegate* unmanaged[Stdcall]<IntPtr, uint, IntPtr, int>)Vtbl(_escritor, 6))(_escritor, _fluxo, amostra),
               "IMFSinkWriter::WriteSample");
            _comecou = true;
        }
        finally
        {
            Soltar(amostra);
            Soltar(buffer);
        }
    }

    /// <summary>Escreve o índice e fecha o arquivo. Sem quadro nenhum, o MP4 não existe.</summary>
    public void Dispose()
    {
        if (_escritor == IntPtr.Zero) return;
        try
        {
            // Finalize sem amostra nenhuma falha (MF_E_SINK_NO_SAMPLES_PROCESSED):
            // não há vídeo a fechar.
            if (_comecou)
                Ok(((delegate* unmanaged[Stdcall]<IntPtr, int>)Vtbl(_escritor, 11))(_escritor), "IMFSinkWriter::Finalize");
        }
        finally
        {
            Soltar(_escritor);
            _escritor = IntPtr.Zero;
            Desligar();
        }
    }

    private static void Desligar()
    {
        if (Interlocked.Decrement(ref _iniciados) == 0) MFShutdown();
    }

    // ───────────────────────────────────────────────── a vtable

    private static IntPtr Vtbl(IntPtr objeto, int indice) => (*(IntPtr**)objeto)[indice];

    private static void Soltar(IntPtr objeto)
    {
        if (objeto != IntPtr.Zero) Marshal.Release(objeto);
    }

    // IMFAttributes: SetUINT32 = 21, SetUINT64 = 22, SetGUID = 24.
    private static void DefinirUInt32(IntPtr a, Guid chave, uint valor) =>
        Ok(((delegate* unmanaged[Stdcall]<IntPtr, Guid*, uint, int>)Vtbl(a, 21))(a, &chave, valor), "SetUINT32");

    private static void DefinirUInt64(IntPtr a, Guid chave, ulong valor) =>
        Ok(((delegate* unmanaged[Stdcall]<IntPtr, Guid*, ulong, int>)Vtbl(a, 22))(a, &chave, valor), "SetUINT64");

    private static void DefinirGuid(IntPtr a, Guid chave, Guid valor) =>
        Ok(((delegate* unmanaged[Stdcall]<IntPtr, Guid*, Guid*, int>)Vtbl(a, 24))(a, &chave, &valor), "SetGUID");

    private static void Ok(int hr, string onde)
    {
        if (hr < 0) throw new COMException($"{onde} falhou (0x{hr:X8})", hr);
    }

    // ───────────────────────────────────────────────── mfplat e mfreadwrite

    private const uint MF_VERSION = 0x00020070;
    private const uint MFVideoInterlace_Progressive = 2;
    private const uint eAVEncH264VProfile_Main = 77;

    private static readonly Guid MF_MT_MAJOR_TYPE = new("48eba18e-f8c9-4687-bf11-0a74c9f96a8f");
    private static readonly Guid MF_MT_SUBTYPE = new("f7e34c9a-42e8-4714-b74b-cb29d72c35e5");
    private static readonly Guid MF_MT_AVG_BITRATE = new("20332624-fb0d-4d9e-bd0d-cbf6786c102e");
    private static readonly Guid MF_MT_INTERLACE_MODE = new("e2724bb8-e676-4806-b4b2-a8d6efb44ccd");
    private static readonly Guid MF_MT_FRAME_SIZE = new("1652c33d-d6b2-4012-b834-72030849a37d");
    private static readonly Guid MF_MT_FRAME_RATE = new("c459a2e8-3d2c-4e44-b132-fee5156c7bb0");
    private static readonly Guid MF_MT_PIXEL_ASPECT_RATIO = new("c6376a1e-8d0a-4027-be45-6d9a0ad39bb6");
    private static readonly Guid MF_MT_DEFAULT_STRIDE = new("644b4e48-1e02-4516-b0eb-c01ca9d49ac6");
    private static readonly Guid MF_MT_MPEG2_PROFILE = new("ad76a80b-2d5c-4e0b-b375-64e520137036");
    private static readonly Guid MF_READWRITE_ENABLE_HARDWARE_TRANSFORMS = new("a634a91c-822b-41b9-a494-4de4643612b0");
    private static readonly Guid MFMediaType_Video = new("73646976-0000-0010-8000-00aa00389b71");
    private static readonly Guid MFVideoFormat_H264 = new("34363248-0000-0010-8000-00aa00389b71");
    private static readonly Guid MFVideoFormat_RGB32 = new("00000016-0000-0010-8000-00aa00389b71");

    [LibraryImport("mfplat.dll")]
    private static partial int MFStartup(uint version, uint flags);

    [LibraryImport("mfplat.dll")]
    private static partial int MFShutdown();

    [LibraryImport("mfplat.dll")]
    private static partial int MFCreateAttributes(out IntPtr ppMFAttributes, uint cInitialSize);

    [LibraryImport("mfplat.dll")]
    private static partial int MFCreateMediaType(out IntPtr ppMFType);

    [LibraryImport("mfplat.dll")]
    private static partial int MFCreateSample(out IntPtr ppIMFSample);

    [LibraryImport("mfplat.dll")]
    private static partial int MFCreateMemoryBuffer(uint cbMaxLength, out IntPtr ppBuffer);

    [LibraryImport("mfreadwrite.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int MFCreateSinkWriterFromURL(string pwszOutputURL, IntPtr pByteStream,
        IntPtr pAttributes, out IntPtr ppSinkWriter);
}
