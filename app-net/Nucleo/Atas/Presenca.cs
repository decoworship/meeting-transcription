namespace MeetingApp.Nucleo.Atas;

/// <summary>
/// Quem a agenda convidou, e quem de fato abriu a boca.
/// </summary>
/// <remarks>
/// <para>
/// <b>O defeito:</b> a ata listava como "Participantes" a lista inteira da
/// agenda. Numa reunião com onze convidados e seis falantes, cinco pessoas que
/// nunca entraram na chamada apareciam como se tivessem participado — e depois
/// contavam como presentes para quem lê a ata meses depois.
/// </para>
/// <para>
/// <b>O que se sabe e o que não se sabe.</b> A transcrição diz quem falou; ela
/// não diz quem entrou. Então "não falou" é a afirmação honesta, e é a que esta
/// classe faz — não "não participou", que seria afirmar mais do que se mediu.
/// Quem entrou e ficou calado cai no mesmo balde de quem não entrou, e essa
/// imprecisão é conhecida e aceita: ela erra para o lado de não inventar
/// presença.
/// </para>
/// <para>
/// <b>Falante sem nome continua contando.</b> "Speaker 3" é alguém que falou e
/// que a diarização não soube nomear — some da lista de nomes, mas entra na
/// contagem, porque desaparecer com ele faria a ata dizer que menos gente falou
/// do que falou.
/// </para>
/// </remarks>
public static class Presenca
{
    /// <param name="Falaram">Convidados que a transcrição pegou falando.</param>
    /// <param name="SoConvidados">Convidados sem nenhuma fala atribuída.</param>
    /// <param name="NaoIdentificados">
    /// Quantos falantes sobraram sem casar com convidado nenhum — "Speaker 3",
    /// "Unknown", ou alguém que entrou sem estar no convite.
    /// </param>
    public sealed record Quadro(
        IReadOnlyList<Pessoa> Falaram,
        IReadOnlyList<Pessoa> SoConvidados,
        int NaoIdentificados)
    {
        /// <summary>
        /// Falso quando não dá para separar — sem falantes, ou com falantes que
        /// não casam com ninguém.
        /// </summary>
        /// <remarks>
        /// Neste caso o cabeçalho volta a listar todo mundo junto, que é o
        /// comportamento de antes. Uma separação em que o lado "falaram" está
        /// vazio seria pior que não separar: diria que a reunião não teve fala.
        /// </remarks>
        public bool DaParaSeparar => Falaram.Count > 0;
    }

    /// <summary>
    /// Cruza a lista da agenda com os falantes da transcrição.
    /// </summary>
    /// <param name="rotuloDoDono">
    /// Como o dono do microfone aparece. Ele é da casa por construção — é a
    /// máquina dele que grava —, então casa com o primeiro da casa.
    /// </param>
    public static Quadro Cruzar(IReadOnlyList<Pessoa> pessoas,
                               IReadOnlyList<string> falantes,
                               string rotuloDoDono = "You")
    {
        var falaram = new List<Pessoa>();
        var restantes = pessoas.ToList();
        int semDono = 0;

        foreach (string falante in falantes.Where(f => f is { Length: > 0 }))
        {
            var quem = Casar(falante, restantes, rotuloDoDono);
            if (quem is null)
            {
                // Rótulo genérico e falante fora do convite contam igual: alguém
                // falou e a agenda não explica quem.
                semDono++;
                continue;
            }
            falaram.Add(quem);
            restantes.Remove(quem);
        }

        return new Quadro(falaram, restantes, semDono);
    }

    /// <summary>
    /// O convidado por trás de um rótulo de falante.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Casa por quantidade de partes em comum, e não por primeiro nome.</b>
    /// A régua do primeiro nome, que serve ao <see cref="DonoPelaFala"/>, foi
    /// medida aqui e reprovou: a agenda às vezes guarda o local-part do e-mail
    /// como se fosse nome — <c>andre.monlevade</c> —, e aí "Andre Monlevade"
    /// não casava e a ata afirmava que ele <b>não falou</b> numa reunião em que
    /// ele falou 47 vezes.
    /// </para>
    /// <para>
    /// Afirmar que alguém não falou é mais forte do que a lista genérica de
    /// antes, e por isso exige um casamento mais tolerante: o ponto e o
    /// sublinhado viram espaço, e vence quem compartilha mais partes.
    /// "Vanessa" casa com "Vanessa Levorato Sao Bernado" por uma parte;
    /// "andre.monlevade" casa com "Andre Monlevade" por duas, e por isso ganha
    /// de "Andre Yuri", que também compartilha "andre".
    /// </para>
    /// </remarks>
    private static Pessoa? Casar(string falante, List<Pessoa> pessoas, string rotuloDoDono)
    {
        if (string.Equals(falante, rotuloDoDono, StringComparison.OrdinalIgnoreCase))
            return pessoas.FirstOrDefault(p => p.DaCasa == true);

        if (PromptDeAta.EhRotuloGenerico(falante)) return null;

        var doFalante = Partes(falante);
        if (doFalante.Count == 0) return null;

        Pessoa? melhor = null;
        int maisPartes = 0;
        foreach (var p in pessoas)
        {
            int comuns = Partes(p.Nome).Count(doFalante.Contains);
            if (comuns > maisPartes) { maisPartes = comuns; melhor = p; }
        }
        return melhor;
    }

    /// <summary>
    /// As partes de um nome, com o e-mail desmontado.
    /// </summary>
    /// <remarks>
    /// <c>andre.monlevade</c> e <c>Andre Monlevade</c> são a mesma pessoa
    /// escrita de dois jeitos, e a agenda entrega os dois — às vezes na mesma
    /// reunião. Ver <c>Organizacoes.NomeLegivel</c>, que resolve o mesmo
    /// problema do outro lado.
    /// </remarks>
    private static HashSet<string> Partes(string nome) =>
        [.. (nome ?? "").Replace('.', ' ').Replace('_', ' ').Replace('-', ' ')
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.ToLowerInvariant())];
}
