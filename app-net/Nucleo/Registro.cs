using System.Text;

namespace MeetingApp.Nucleo;

/// <summary>
/// O que o app fez, escrito em disco, para quando ele estiver na máquina de outra pessoa.
/// </summary>
/// <remarks>
/// <para>
/// <b>Por que existe.</b> Em 18/08/2026 a transcrição de um usuário derrubou o
/// Windows dele. O bloco de diagnóstico deu a foto — versão, placa, modelos — e
/// a foto não bastava: ela diz como a máquina <i>está</i>, e a pergunta era o
/// que o app <i>fez</i>. Sem log, a única saída era adivinhar, e a primeira
/// hipótese estava errada.
/// </para>
/// <para>
/// O <c>stderr</c> dos motores já era drenado e guardado em memória, e só
/// aparecia se o processo morresse. Tudo o que faltava era escrevê-lo.
/// </para>
/// <para>
/// <b>Nunca levanta exceção e nunca segura quem chama.</b> Um log que derruba o
/// app é pior que não ter log; um log que atrasa a transcrição some do caminho
/// crítico na primeira reclamação.
/// </para>
/// <para>
/// <b>Sobre privacidade:</b> aqui vão nomes de pasta de gravação (que são datas)
/// e mensagens dos motores. <b>Não</b> vai transcrição, nome de participante nem
/// de cliente. Ainda assim é um arquivo que a pessoa manda por escolha, e não
/// algo que o app envie — nada aqui sai da máquina sozinho.
/// </para>
/// </remarks>
public static class Registro
{
    private static readonly object Tranca = new();

    /// <summary>Dois megabytes: umas semanas de uso, e cabe num anexo.</summary>
    private const long TamanhoMaximo = 2L * 1024 * 1024;

    public static string Caminho => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".meeting-transcription", "registro.log");

    /// <summary>Uma linha, com hora e origem.</summary>
    /// <param name="origem">"asr", "diarizacao", "pipeline", "ata"…</param>
    public static void Escrever(string origem, string texto)
    {
        try
        {
            string linha = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{origem}] {texto}";
            lock (Tranca)
            {
                string caminho = Caminho;
                Directory.CreateDirectory(Path.GetDirectoryName(caminho)!);
                Girar(caminho);
                // UTF8 sem BOM: com BOM, cada arquivo começa com três bytes
                // invisíveis que aparecem como lixo quando alguém cola o log
                // numa mensagem. O projeto já usa esta codificação nos sidecars.
                File.AppendAllText(caminho, linha + Environment.NewLine,
                                   new UTF8Encoding(false));
            }
        }
        catch (Exception)
        {
            // Um log que derruba o app é pior que não ter log.
        }
    }

    /// <summary>As últimas linhas, para quem vai relatar um problema.</summary>
    public static string Ultimas(int quantas = 60)
    {
        try
        {
            if (!File.Exists(Caminho)) return "";
            var todas = File.ReadAllLines(Caminho);
            return string.Join('\n', todas.TakeLast(quantas));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return "";
        }
    }

    /// <remarks>
    /// Um arquivo anterior só, e não uma série numerada: o que interessa é a
    /// sessão que falhou e a anterior. Guardar dez seria guardar ruído.
    /// </remarks>
    private static void Girar(string caminho)
    {
        var info = new FileInfo(caminho);
        if (!info.Exists || info.Length < TamanhoMaximo) return;

        string velho = caminho + ".old";
        try
        {
            if (File.Exists(velho)) File.Delete(velho);
            File.Move(caminho, velho);
        }
        catch (IOException)
        {
            // Girar é higiene, não requisito: se não deu, segue escrevendo.
        }
    }
}
