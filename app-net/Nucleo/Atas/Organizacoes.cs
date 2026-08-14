namespace MeetingApp.Nucleo.Atas;

/// <summary>De que lado da mesa uma pessoa está.</summary>
/// <param name="Nome">Como ela é chamada — o displayName da agenda, ou o e-mail.</param>
/// <param name="Dominio">O domínio do e-mail, quando houve e-mail.</param>
/// <param name="DaCasa">
/// Verdadeiro para quem é da nossa organização; falso para cliente; nulo quando
/// não dá para saber, que é o caso das gravações feitas antes de o domínio ser
/// guardado.
/// </param>
public sealed record Pessoa(string Nome, string? Dominio, bool? DaCasa)
{
    /// <summary>"cliente" ou "nosso", para o campo <c>lado</c> das ações.</summary>
    public string Lado => DaCasa == false ? "cliente" : "nosso";
}

/// <summary>
/// Quem é da casa e quem é do cliente, pelo domínio do e-mail.
/// </summary>
/// <remarks>
/// <para>
/// <b>Nasceu de um erro concreto:</b> a ata atribuiu uma ação a "Andre Monlevade
/// (Vivo)" — Andre é da equipe, e o modelo deduziu a organização pelo contexto
/// da conversa, que fala de Vivo o tempo todo. Deduzir organização de conversa é
/// chute; o domínio do e-mail é fato, e a agenda já o entrega.
/// </para>
/// <para>
/// A configuração é <b>só a nossa lista de domínios</b>. Não se cadastra o
/// domínio de cada cliente: quem não é da casa é cliente, e essa regra não
/// precisa de manutenção quando aparece um cliente novo. Um domínio a mais na
/// lista custa uma linha; um cadastro de clientes custa manutenção para sempre.
/// </para>
/// <para>
/// <b>Sem domínio, não se afirma nada.</b> As gravações anteriores a esta versão
/// guardam só o nome do participante — nelas <c>DaCasa</c> é nulo, e o
/// verificador não corrige o lado que o modelo escolheu. Preferir "não sei" a
/// chutar é a diferença entre uma ata que se pode conferir e uma que inventa
/// com confiança.
/// </para>
/// </remarks>
public static class Organizacoes
{
    /// <summary>
    /// Classifica os convidados da agenda contra os domínios da casa.
    /// </summary>
    /// <param name="convidados">
    /// O que o <c>meta.json</c> guarda: nomes, e-mails, ou uma mistura dos dois.
    /// </param>
    public static IReadOnlyList<Pessoa> Classificar(
        IEnumerable<string> convidados, IEnumerable<string> dominiosDaCasa)
    {
        var daCasa = dominiosDaCasa
            .Select(Normalizar)
            .Where(d => d.Length > 0)
            .ToList();

        var pessoas = new List<Pessoa>();
        foreach (string bruto in convidados)
        {
            string valor = (bruto ?? "").Trim();
            if (valor.Length == 0) continue;

            string? dominio = DominioDe(valor);
            bool? casa = dominio is null || daCasa.Count == 0
                ? null
                : daCasa.Any(d => dominio == d || dominio.EndsWith("." + d, StringComparison.Ordinal));

            pessoas.Add(new Pessoa(NomeLegivel(valor), dominio, casa));
        }
        return pessoas;
    }

    /// <summary>Como a organização aparece para o modelo e para o cabeçalho.</summary>
    /// <remarks>
    /// O nome do cliente vem do vínculo da reunião (<see cref="DadosDaReuniao"/>)
    /// e não do domínio: "telefonica.com" é o domínio, "Vivo" é como as pessoas
    /// chamam. Quem sabe disso é o usuário, que já digitou o cliente na tela.
    /// </remarks>
    public static string Rotulo(Pessoa p, string? cliente) => p.DaCasa switch
    {
        true => "nossa equipe",
        false => cliente is { Length: > 0 } ? cliente : (p.Dominio ?? "cliente"),
        _ => "organização não identificada",
    };

    /// <summary>O bloco de participantes que entra no prompt, agrupado.</summary>
    public static string ParaPrompt(IReadOnlyList<Pessoa> pessoas, string? cliente)
    {
        if (pessoas.Count == 0) return "";

        var casa = pessoas.Where(p => p.DaCasa == true).Select(p => p.Nome).ToList();
        var clientes = pessoas.Where(p => p.DaCasa == false).Select(p => p.Nome).ToList();
        var incertos = pessoas.Where(p => p.DaCasa is null).Select(p => p.Nome).ToList();

        var linhas = new List<string>();
        if (casa.Count > 0)
            linhas.Add($"- Da nossa equipe: {string.Join(", ", casa)}");
        if (clientes.Count > 0)
            linhas.Add($"- Do cliente{(cliente is { Length: > 0 } ? $" ({cliente})" : "")}: "
                       + string.Join(", ", clientes));
        if (incertos.Count > 0)
            linhas.Add($"- Sem organização identificada: {string.Join(", ", incertos)}");

        if (casa.Count > 0 && clientes.Count > 0)
            linhas.Add("Use isto para separar as pendências por lado: `lado` é "
                       + "\"nosso\" quando o responsável é da nossa equipe e "
                       + "\"cliente\" quando é do cliente. **Não deduza a "
                       + "organização de ninguém pelo assunto da conversa.**");

        return string.Join("\n", linhas);
    }

    /// <summary>"dimi.randel@beegol.com" -> "beegol.com". Nulo quando não é e-mail.</summary>
    private static string? DominioDe(string valor)
    {
        int arroba = valor.LastIndexOf('@');
        if (arroba < 0 || arroba == valor.Length - 1) return null;
        string dominio = Normalizar(valor[(arroba + 1)..]);
        return dominio.Contains('.') ? dominio : null;
    }

    /// <summary>
    /// "dimi.randel@beegol.com" vira "Dimi Randel"; um nome já legível não muda.
    /// </summary>
    /// <remarks>
    /// A mesma regra do <c>Evento.DoEmail</c> do gravador, e de propósito: os
    /// dois olham para o mesmo dado e precisam produzir o mesmo nome, senão a
    /// mesma pessoa aparece de dois jeitos na mesma ata.
    /// </remarks>
    private static string NomeLegivel(string valor)
    {
        int arroba = valor.LastIndexOf('@');
        if (arroba < 0) return valor;

        string local = valor[..arroba].Replace('.', ' ').Replace('_', ' ');
        var partes = local.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(p => char.ToUpperInvariant(p[0]) + p[1..].ToLowerInvariant());
        string nome = string.Join(" ", partes);
        return nome.Length > 0 ? nome : valor;
    }

    private static string Normalizar(string d) =>
        d.Trim().TrimStart('@').ToLowerInvariant();
}
