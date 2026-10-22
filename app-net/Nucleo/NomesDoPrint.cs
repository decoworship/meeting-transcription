using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using MeetingApp.Sidecar;

namespace MeetingApp.Nucleo;

/// <summary>
/// Os nomes de gente nas linhas que o OCR leu de um print da reunião (UI-12).
/// </summary>
/// <remarks>
/// <para>
/// <b>O que um print do Teams tem</b> (medido em 21/10/2026 em quatro prints
/// reais do dono): a barra de botões ("Chat", "People", "Leave"…), o
/// cronômetro, o título da reunião, o fundo de marca de quem usa câmera
/// ("tamojunto"), e uma etiqueta por pessoa — com o ícone de sinal grudado no
/// nome de quem fala ("Il Fulano"), o sufixo da empresa ("Fulano- Acme"), o
/// nome em maiúsculas e o usuário com ponto ("fulano.tal").
/// </para>
/// <para>
/// <b>O erro que custa é inventar um participante.</b> Um print de slide com
/// uma tabela de vendedores tem vinte nomes em maiúsculas, e nenhuma regra os
/// distingue de etiquetas do Teams olhando só o texto. Por isso esta classe
/// <em>sugere</em>, a tela pergunta, e <see cref="Leitura.PareceReuniao"/> só
/// decide se as caixinhas já vêm marcadas.
/// </para>
/// </remarks>
public static class NomesDoPrint
{
    /// <param name="Nome">Como sugerir: sem o sufixo da empresa, sem as maiúsculas gritadas.</param>
    /// <param name="Original">Como estava no print, para a tela mostrar de onde veio.</param>
    /// <param name="Convidado">O nome da agenda que casou com este, quando um só casou.</param>
    public sealed record NomeLido(string Nome, string Original, string? Convidado);

    /// <param name="PareceReuniao">
    /// O print tem cara de tela de reunião: a barra do Teams/Meet, ou quase só
    /// nomes. Falso num slide, numa planilha, num painel.
    /// </param>
    public sealed record Leitura(IReadOnlyList<NomeLido> Nomes, bool PareceReuniao);

    /// <summary>Abaixo disto o OCR está chutando — "Qitar Rirvays" no lugar de "Qatar Airways".</summary>
    public const double ConfiancaMinima = 0.8;

    public static Leitura Ler(IReadOnlyList<LinhaLida> linhas, IReadOnlyList<string> convidados)
    {
        var limpas = linhas
            .Where(l => l.Confianca >= ConfiancaMinima)
            .Select(l => Limpar(l.Texto))
            .Where(t => t.Count(char.IsLetter) >= 3)
            .ToList();

        int daInterface = limpas.Select(Chave).Where(TextoDaInterface.Contains).Distinct().Count();

        var candidatos = limpas
            .Where(t => !TextoDaInterface.Contains(Chave(t)))
            .Select(Candidato)
            .Where(c => c.HasValue)
            .Select(c => c!.Value)
            .ToList();

        // A empresa é o sufixo que se repete, ou o que vem depois de " - ". Um
        // hífen sem espaço, num nome só, pode ser sobrenome composto.
        var empresas = candidatos
            .Select(c => c.Empresa).OfType<string>()
            .Concat(candidatos.Select(c => SufixoColado(c.NomeCru)).OfType<string>()
                              .GroupBy(Chave).Where(g => g.Count() >= 2).Select(g => g.First()))
            .Select(Chave).ToHashSet();

        var nomes = new List<NomeLido>();
        var vistos = new HashSet<string>();
        foreach (var (original, cru, _) in candidatos)
        {
            string nome = cru;
            if (SufixoColado(cru) is { } sufixo && empresas.Contains(Chave(sufixo)))
                nome = cru[..cru.LastIndexOf('-')].TrimEnd();
            nome = Capitalizar(nome);

            if (!vistos.Add(Chave(nome))) continue;
            nomes.Add(new NomeLido(nome, original, CasarComAgenda(nome, convidados)));
        }

        // Sem a barra do app, um print de reunião é uma lista de pessoas: a
        // maioria das linhas com texto são nomes. Num slide, são uma minoria.
        bool parece = daInterface >= 2
                      || (nomes.Count >= 2 && nomes.Count * 2 >= limpas.Count - daInterface);
        return new Leitura(nomes, parece);
    }

    // ─────────────────────────────────────────────────────── a linha

    private static readonly Regex Marcador =
        new(@"\s*\((voc[eê]|you|convidad[oa]|guest|extern[oa]|external|organizador|organizer)\)\s*$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>"Nesta reunião (7)", "People 10": a contagem não é parte do rótulo.</summary>
    private static readonly Regex Contagem = new(@"\s*\(\d+\)\s*$", RegexOptions.Compiled);

    private static readonly Regex HifenSolto = new(@"\s*-\s+|\s+-\s*", RegexOptions.Compiled);

    /// <summary>Tira o que o Teams pendura no nome e o lixo do ícone grudado na frente.</summary>
    private static string Limpar(string texto)
    {
        string t = Contagem.Replace(Marcador.Replace(texto.Trim(), ""), "");
        t = HifenSolto.Replace(t, " - ");
        var tokens = t.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
        // "Il Fulano de Tal": o ícone de sinal lido como letra. Só sai se sobrar
        // nome de duas palavras — "Li Wang" é nome inteiro.
        if (tokens.Count >= 3 && tokens[0].Length <= 2 && tokens[0] != "-") tokens.RemoveAt(0);
        return string.Join(' ', tokens);
    }

    private static readonly Regex Usuario = new(@"^[a-z]+(\.[a-z]+)+$", RegexOptions.Compiled);
    private static readonly Regex Palavra = new(@"^\p{L}[\p{L}'’-]*$", RegexOptions.Compiled);

    /// <summary>
    /// O nome que a linha parece ser, e a empresa depois do " - ", quando há;
    /// nulo quando a linha não é nome de gente.
    /// </summary>
    private static (string Original, string NomeCru, string? Empresa)? Candidato(string linha)
    {
        if (Usuario.IsMatch(linha))
            return (linha, string.Join(' ', linha.Split('.').Select(Maiuscula)), null);

        string nome = linha;
        string? empresa = null;
        int separador = linha.IndexOf(" - ", StringComparison.Ordinal);
        if (separador >= 0)
        {
            nome = linha[..separador];
            empresa = linha[(separador + 3)..];
            var daEmpresa = empresa.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (daEmpresa.Length is < 1 or > 2 || !daEmpresa.All(PareceNomeProprio)) return null;
        }

        var tokens = nome.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        // Oito cabe "Antonio de Leva dos Santos e Silva"; mais que isso é frase.
        if (tokens.Length is < 1 or > 8) return null;
        if (!tokens.All(t => Palavra.IsMatch(t))) return null;
        if (tokens.Any(t => NuncaEmNome.Contains(t.ToLowerInvariant()))) return null;

        var principais = tokens.Where(t => !Particulas.Contains(t.ToLowerInvariant())).ToList();
        // Uma palavra só é nome apenas com a empresa ao lado ("Clara - Acme"):
        // sozinha, é "Algar", "Dashboard", "Organizer".
        if (principais.Count < (empresa is null ? 2 : 1)) return null;
        if (!principais.All(PareceNomeProprio)) return null;

        return (linha, nome, empresa);
    }

    /// <summary>Começa com maiúscula — "Silva", "SILVA", "D'Ávila".</summary>
    private static bool PareceNomeProprio(string t) => char.IsUpper(t[0]);

    /// <summary>"Costa-Acme" → "Acme". O hífen tem de ter algo dos dois lados.</summary>
    private static string? SufixoColado(string nome)
    {
        string ultimo = nome.Split(' ').Last();
        int i = ultimo.LastIndexOf('-');
        return i > 0 && i < ultimo.Length - 1 ? ultimo[(i + 1)..] : null;
    }

    // ─────────────────────────────────────────────────────── a forma

    /// <summary>
    /// Nome gritado ("ANTONIO DE LEVA") vira nome escrito; o que já tem
    /// minúscula fica como a pessoa se cadastrou.
    /// </summary>
    private static string Capitalizar(string nome)
    {
        if (nome.Any(char.IsLower)) return nome;
        var tokens = nome.Split(' ');
        for (int i = 0; i < tokens.Length; i++)
        {
            string baixo = tokens[i].ToLowerInvariant();
            tokens[i] = i > 0 && Particulas.Contains(baixo) ? baixo : Maiuscula(baixo);
        }
        return string.Join(' ', tokens);
    }

    private static string Maiuscula(string t) =>
        t.Length == 0 ? t : char.ToUpperInvariant(t[0]) + t[1..].ToLowerInvariant();

    /// <summary>Sem acento, sem caixa e sem espaço repetido: para comparar.</summary>
    private static string Chave(string texto)
    {
        var sb = new StringBuilder();
        foreach (char c in texto.Normalize(NormalizationForm.FormD))
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                sb.Append(char.ToLowerInvariant(c));
        return string.Join(' ', sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    /// <summary>
    /// O convidado que este nome é, quando um só serve: o primeiro nome igual e
    /// as outras palavras contidas nas dele ("Helena Dias" é "Helena Dias
    /// Fonseca"). Dois que servem é ambiguidade, e ambiguidade não se resolve
    /// no palpite.
    /// </summary>
    private static string? CasarComAgenda(string nome, IReadOnlyList<string> convidados)
    {
        var meus = Palavras(nome);
        if (meus.Count == 0) return null;

        var servem = convidados.Where(c =>
        {
            var deles = Palavras(c);
            if (deles.Count == 0 || deles[0] != meus[0]) return false;
            return meus.All(deles.Contains) || deles.All(meus.Contains);
        }).Distinct().ToList();
        return servem.Count == 1 ? servem[0] : null;
    }

    private static List<string> Palavras(string nome) =>
        [.. Chave(nome).Split(' ', StringSplitOptions.RemoveEmptyEntries)
                       .Where(p => !Particulas.Contains(p))];

    // ─────────────────────────────────────────────────────── listas

    /// <summary>"de", "da", "dos"… — cabem num nome, mas não contam como parte dele.</summary>
    private static readonly HashSet<string> Particulas =
        ["de", "da", "do", "das", "dos", "e", "di", "du", "del", "della", "van", "von", "der"];

    /// <summary>
    /// Palavras de frase que nunca estão num nome — é o que separa "Relatório
    /// por Vendedor" de "Fulano de Tal" quando os dois vêm em maiúsculas.
    /// </summary>
    private static readonly HashSet<string> NuncaEmNome =
    [
        "por", "para", "com", "sem", "em", "no", "na", "nos", "nas", "o", "a", "os", "as",
        "um", "uma", "que", "ao", "aos", "pelo", "pela", "x", "×",
        "of", "the", "and", "for", "to", "in", "on", "by", "with", "at", "per", "vs",
    ];

    /// <summary>
    /// A interface do Teams e do Meet, em inglês e em português, como o OCR a lê
    /// (sem acento e em minúsculas). Duas delas num print bastam para dizer que
    /// é tela de reunião.
    /// </summary>
    private static readonly HashSet<string> TextoDaInterface =
    [
        // Teams, inglês
        "chat", "people", "raise", "react", "view", "more", "camera", "mic", "share", "leave",
        "stop sharing", "organizer", "presenter", "attendee", "participants", "in this meeting",
        "microsoft teams meeting", "apps", "rooms", "recording", "mute all",
        // Teams, português
        "bate-papo", "pessoas", "levantar", "reagir", "exibir", "mais", "microfone",
        "compartilhar", "sair", "parar compartilhamento", "organizador", "apresentador",
        "participante", "participantes", "nesta reuniao", "reuniao do microsoft teams",
        "salas", "gravando", "silenciar todos", "convidado", "convidados",
        // Meet
        "present now", "apresentar agora", "you", "voce", "meeting details",
        "detalhes da reuniao", "contributors", "colaboradores", "in call", "na chamada",
    ];
}
