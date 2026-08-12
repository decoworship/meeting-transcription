using System.Diagnostics;
using System.Runtime.InteropServices;

namespace MeetingApp.Sidecar;

/// <summary>
/// Amarra os motores ao app: se o app morrer, o Windows mata os motores junto.
/// </summary>
/// <remarks>
/// <para>
/// <b>O buraco que isto fecha.</b> O encerramento normal já funcionava — o
/// <c>Dispose</c> do <see cref="MotorSidecar"/> chama
/// <c>Kill(entireProcessTree: true)</c>, e é ele que faz o cancelamento devolver
/// a VRAM em ≤0,3 s. Mas <b>o <c>Dispose</c> só roda se o app estiver vivo para
/// rodá-lo</b>. Matar o app pelo Gerenciador de Tarefas, ou um travamento,
/// pulava esse caminho inteiro: o Python continuava rodando, invisível, com o
/// modelo carregado e a GPU ocupada. A única forma de perceber era a próxima
/// transcrição falhar por falta de memória.
/// </para>
/// <para>
/// O Job Object resolve <b>no sistema operacional</b>, e não no nosso código,
/// que é o único lugar de onde dá para garantir. Com
/// <c>KILL_ON_JOB_CLOSE</c>, o Windows mata todo processo do job quando o
/// último identificador dele fecha — e o identificador do app fecha quando o
/// app morre, de qualquer jeito que ele morra, inclusive de formas que nenhum
/// <c>finally</c> alcança.
/// </para>
/// <para>
/// É o critério B da Fase 2: "matar o app não deixa motor órfão". Antes disto,
/// ele não era só não medido — ele não tinha como funcionar.
/// </para>
/// </remarks>
internal static class JobDosMotores
{
    private const uint LimiteMatarAoFechar = 0x2000;   // JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE
    private const int InformacaoEstendida = 9;          // JobObjectExtendedLimitInformation

    /// <summary>
    /// O job vive enquanto o processo viver: guardado num estático de propósito.
    /// </summary>
    /// <remarks>
    /// Se este identificador for coletado ou fechado antes da hora, o Windows
    /// entende que o job acabou e mata os motores no meio de uma transcrição.
    /// O tempo de vida dele <b>é</b> a funcionalidade.
    /// </remarks>
    private static readonly nint _job = Criar();

    private static nint Criar()
    {
        if (!OperatingSystem.IsWindows()) return 0;

        nint job = CreateJobObjectW(0, null);
        if (job == 0) return 0;

        var limites = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION();
        limites.BasicLimitInformation.LimitFlags = LimiteMatarAoFechar;

        int tamanho = Marshal.SizeOf<JOBOBJECT_EXTENDED_LIMIT_INFORMATION>();
        nint buffer = Marshal.AllocHGlobal(tamanho);
        try
        {
            Marshal.StructureToPtr(limites, buffer, fDeleteOld: false);
            if (!SetInformationJobObject(job, InformacaoEstendida, buffer, (uint)tamanho))
            {
                CloseHandle(job);
                return 0;
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
        return job;
    }

    /// <summary>Põe um motor recém-iniciado dentro do job.</summary>
    /// <remarks>
    /// Falhar aqui <b>não</b> é motivo para não transcrever: sem o job o app
    /// funciona igual, e só perde a garantia contra órfão. Numa escolha entre
    /// "não grava a reunião" e "pode sobrar um processo se o app for morto", a
    /// segunda é claramente a menos ruim.
    /// </remarks>
    public static void Adotar(Process processo)
    {
        if (_job == 0) return;
        try
        {
            AssignProcessToJobObject(_job, processo.Handle);
        }
        catch (Exception e) when (e is InvalidOperationException or EntryPointNotFoundException
                                       or DllNotFoundException)
        {
            // Processo que já saiu, ou plataforma sem a API.
        }
    }

    // DllImport e não LibraryImport: o gerador do LibraryImport exige
    // AllowUnsafeBlocks no projeto, e ligar `unsafe` num projeto inteiro para
    // quatro assinaturas triviais é pagar caro por conveniência de geração. É a
    // mesma escolha que o Win32.cs do app já faz onde o gerador não serve.
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint CreateJobObjectW(nint atributos, string? nome);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetInformationJobObject(
        nint job, int classe, nint informacao, uint tamanho);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AssignProcessToJobObject(nint job, nint processo);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(nint handle);

    // O layout tem de bater byte a byte com o do Windows: um campo fora de
    // ordem faria o SetInformationJobObject aceitar lixo, e o KILL_ON_JOB_CLOSE
    // simplesmente não valeria — sem erro nenhum para denunciar.
    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_BASIC_LIMIT_INFORMATION
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public nuint MinimumWorkingSetSize;
        public nuint MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public nuint Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IO_COUNTERS
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
    {
        public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
        public IO_COUNTERS IoInfo;
        public nuint ProcessMemoryLimit;
        public nuint JobMemoryLimit;
        public nuint PeakProcessMemoryUsed;
        public nuint PeakJobMemoryUsed;
    }
}
