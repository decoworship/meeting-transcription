using System.Reflection;

namespace MeetingRecorder.Agenda;

/// <summary>
/// De onde vem o <c>client_secret</c> do Google: do arquivo do usuário, ou
/// embutido no executável.
/// </summary>
/// <remarks>
/// <para>
/// <b>Por que embutir é aceitável aqui.</b> Este é um cliente OAuth do tipo
/// "aplicativo instalado", e para esse tipo o próprio Google documenta que o
/// segredo <b>não é tratado como segredo</b>: ele viaja dentro de todo binário
/// distribuído e não autoriza nada sozinho. Quem protege o fluxo é o PKCE, que
/// esta implementação já usa, e o consentimento do usuário no navegador.
/// </para>
/// <para>
/// <b>Por que o arquivo continua tendo precedência.</b> Quem já configurou o
/// próprio cliente — inclusive quem migrou do gravador Python — não pode ter o
/// comportamento trocado por baixo por uma atualização do app.
/// </para>
/// <para>
/// <b>O que embutir NÃO resolve.</b> Um app em modo "Testing" no Google Cloud
/// só autoriza contas cadastradas como testadoras, e expira todo refresh token
/// em 7 dias. Distribuir o binário para outra pessoa exige, além disto, que o
/// e-mail dela esteja na lista de testadores ou que o app seja publicado.
/// </para>
/// </remarks>
public static class FonteDoSegredo
{
    internal const string RecursoEmbutido = "MeetingRecorder.Agenda.google_client_secret.json";

    /// <summary>O JSON do cliente, ou <c>null</c> se não há nenhuma fonte.</summary>
    public static string? Ler()
    {
        try
        {
            if (File.Exists(Caminhos.SegredoDoCliente))
                return File.ReadAllText(Caminhos.SegredoDoCliente);
        }
        catch (IOException)
        {
            // Arquivo ilegível não pode impedir o embutido de funcionar.
        }
        return Embutido();
    }

    /// <summary>Existe alguma fonte de credencial, arquivo ou embutida.</summary>
    public static bool Existe() => File.Exists(Caminhos.SegredoDoCliente) || Embutido() is not null;

    /// <summary>O app foi publicado com credenciais próprias.</summary>
    public static bool TemEmbutido => Embutido() is not null;

    private static string? Embutido()
    {
        using var fluxo = Assembly.GetExecutingAssembly()
            .GetManifestResourceStream(RecursoEmbutido);
        if (fluxo is null) return null;

        using var leitor = new StreamReader(fluxo);
        return leitor.ReadToEnd();
    }
}
