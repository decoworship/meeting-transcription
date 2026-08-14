using System.Text.RegularExpressions;

namespace MeetingApp.Nucleo.Atas;

/// <summary>Um fato achado na transcrição, com o pedaço de fala em volta.</summary>
/// <param name="Trecho">O que foi dito, cortado no tamanho que cabe num prompt.</param>
/// <param name="Quando">Segundos desde o início. Serve para ordenar e para citar.</param>
public sealed record Fato(string Chave, string Trecho, double Quando);

/// <summary>
/// O que a reunião disse de concreto: números, compromissos, nomes, datas.
/// </summary>
/// <remarks>
/// <para>
/// <b>Existe porque o modelo pequeno não inventa — ele omite.</b> Na comparação
/// do ATA.md §8, o Qwen3-4B não escreveu um único fato falso e ainda assim
/// deixou de fora metade dos números, incluindo o impacto financeiro da reunião,
/// que era a linha mais importante dela. O verificador do §4 foi desenhado
/// contra invenção e não faz nada contra esquecimento.
/// </para>
/// <para>
/// A resposta é não pedir ao modelo que <em>procure</em>: quem procura é isto
/// aqui, com expressão regular, e o modelo recebe a lista pronta para usar o que
/// for relevante. Regra determinística embaixo, modelo por cima — o mesmo
/// desenho da correção fonética.
/// </para>
/// <para>
/// <b>Errar para mais é barato.</b> Um fato irrelevante na lista custa alguns
/// tokens; um fato ausente custa a linha que faltou na ata.
/// </para>
/// </remarks>
public static class RoteiroDeFatos
{
    /// <summary>Números com três dígitos ou mais, dinheiro, percentual e multiplicador.</summary>
    /// <remarks>
    /// Números de um e dois dígitos ficam de fora: "às 2 horas", "os 3 casos" e
    /// "a v2" enchem a lista sem carregar informação. O que interessa é
    /// <c>27.529</c>, <c>R$ 180 mil</c>, <c>95%</c>, <c>2 milhões</c>.
    /// </remarks>
    private static readonly Regex Numeros = new(
        @"(R\$\s?[\d.,]+(\s*(mil|milh(ão|ões|oes)))?|[\d.,]+\s*(%|por\s?cento)"
        + @"|\b\d{1,3}(\.\d{3})+([,.]\d+)?\b|\b\d{3,}\b"
        + @"|\b[\d.,]+\s*(mil|milh(ão|ões|oes))\b)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Compromisso verbal: o que parece casual na fala e é o que a ata precisa fixar.
    /// </summary>
    /// <remarks>
    /// A frase da própria skill: "'Vamos ver isso até sexta', 'eu te mando os
    /// dados amanhã' — parecem casuais na fala e são exatamente o que a ata
    /// precisa fixar, com nome e data".
    /// </remarks>
    private static readonly Regex Compromissos = new(
        @"\b(eu\s+)?(te\s+)?(mando|envio|passo|gero|fa(ç|c)o|vejo|confirmo|verifico|olho"
        + @"|dou uma olhada|fico de|vou (mandar|enviar|passar|ver|fazer|confirmar|olhar|verificar)"
        + @"|pode (deixar|contar)|combinado|fica(mos)? de)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>Prazos ditos de forma relativa, que somem se ninguém fixar.</summary>
    private static readonly Regex Prazos = new(
        @"\b(hoje|amanh(ã|a)|depois de amanh(ã|a)|ainda hoje|essa semana|esta semana"
        + @"|semana que vem|pr(ó|o)xima semana|segunda|ter(ç|c)a|quarta|quinta|sexta"
        + @"|s(á|a)bado|domingo|at(é|e) o fim do (dia|m(ê|e)s)|no pr(ó|o)ximo ciclo"
        + @"|m(ê|e)s que vem)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Monta o roteiro a partir dos segmentos, sem repetir o mesmo achado.
    /// </summary>
    /// <param name="limite">
    /// Teto de fatos por categoria. Existe porque o roteiro divide o contexto
    /// com a transcrição: numa reunião de duas horas, 400 números empurrariam a
    /// fala para fora da janela — e a fala é a fonte, o roteiro é o índice.
    /// </param>
    public static IReadOnlyList<Fato> De(IEnumerable<SegmentoFinal> segmentos, int limite = 40)
    {
        var numeros = new List<Fato>();
        var compromissos = new List<Fato>();
        var vistos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var s in segmentos)
        {
            string texto = s.Text.Trim();
            if (texto.Length == 0) continue;

            foreach (Match m in Numeros.Matches(texto))
            {
                string chave = Normalizar(m.Value);
                if (chave.Length == 0 || !vistos.Add($"n:{chave}")) continue;
                numeros.Add(new Fato(m.Value.Trim(), Recorte(texto, m.Index), s.Start));
            }

            // Um trecho conta como compromisso quando alguém se compromete E há
            // um prazo dito: "eu te mando" sozinho é conversa; "eu te mando
            // amanhã" é item de ação com data.
            if (Compromissos.IsMatch(texto) && Prazos.Match(texto) is { Success: true } prazo)
            {
                // O corte é sobre o texto JÁ normalizado, e não sobre o
                // original: normalizar tira pontuação e espaço, então o
                // resultado é mais curto — cortar pelo tamanho do original
                // estoura o índice.
                string normalizado = Normalizar(texto);
                string chave = normalizado[..Math.Min(60, normalizado.Length)];
                if (vistos.Add($"c:{chave}"))
                    compromissos.Add(new Fato(prazo.Value, Encurtar(texto), s.Start));
            }
        }

        return [.. numeros.Take(limite), .. compromissos.Take(limite)];
    }

    /// <summary>O roteiro como o prompt o recebe.</summary>
    public static string ParaPrompt(IReadOnlyList<Fato> fatos)
    {
        if (fatos.Count == 0) return "";

        var linhas = new List<string>
        {
            "# Fatos citados na reunião",
            "",
            "Lista levantada automaticamente da transcrição, para conferência. "
            + "**Use o que for relevante para a ata e ignore o resto** — números "
            + "de contexto e valores que não pertencem a nenhuma seção não "
            + "precisam entrar. Não invente nada que não esteja aqui ou na "
            + "transcrição.",
            "",
        };

        foreach (var f in fatos)
            linhas.Add($"- [{Relogio(f.Quando)}] **{f.Chave}** — {f.Trecho}");

        return string.Join("\n", linhas);
    }

    /// <summary>
    /// Os números citados que não entraram na ata.
    /// </summary>
    /// <remarks>
    /// A rede contra omissão, do outro lado: não é o modelo que julga o que
    /// faltou, é uma lista que a pessoa bate o olho. Vai para "Observações",
    /// junto do que o verificador mexeu.
    /// </remarks>
    public static IReadOnlyList<string> NaoIncorporados(
        IReadOnlyList<Fato> roteiro, string ata)
    {
        var faltando = new List<string>();
        foreach (var f in roteiro)
        {
            string chave = Normalizar(f.Chave);
            if (chave.Length < 3) continue;
            // Compara sem pontuação: a transcrição diz "27.529" e a ata pode
            // dizer "27529" ou "27,529" sem que nenhum dos dois esteja errado.
            if (!Normalizar(ata).Contains(chave, StringComparison.OrdinalIgnoreCase))
                faltando.Add(f.Chave);
        }
        return faltando;
    }

    private static string Normalizar(string t) =>
        string.Concat(t.Where(char.IsLetterOrDigit)).ToLowerInvariant();

    private static string Recorte(string texto, int posicao, int janela = 90)
    {
        int inicio = Math.Max(0, posicao - janela / 2);
        int fim = Math.Min(texto.Length, posicao + janela);
        string t = texto[inicio..fim].Trim();
        return (inicio > 0 ? "…" : "") + t + (fim < texto.Length ? "…" : "");
    }

    private static string Encurtar(string texto, int teto = 160) =>
        texto.Length <= teto ? texto : texto[..teto].TrimEnd() + "…";

    private static string Relogio(double segundos)
    {
        int s = (int)Math.Round(segundos);
        return $"{s / 60:00}:{s % 60:00}";
    }
}
