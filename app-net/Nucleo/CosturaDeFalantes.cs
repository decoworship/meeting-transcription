using MeetingApp.Sidecar;

namespace MeetingApp.Nucleo;

/// <summary>O que a costura concluiu sobre quem estava na reunião.</summary>
/// <param name="Falantes">
/// A linha do tempo pronta para entrar no lugar da diarização: os mesmos
/// intervalos, com identidade global no lugar do rótulo local do bloco.
/// </param>
/// <param name="RotulosLocais">Quantos pares (bloco, falante) o MOSS produziu.</param>
/// <param name="Identidades">Quantas pessoas a costura concluiu que existem.</param>
/// <param name="SemVoz">
/// Rótulos que não tinham fala limpa bastante para render um vetor, e por isso
/// viraram identidade própria.
/// </param>
public sealed record Costura(
    IReadOnlyList<SegmentoDeFalante> Falantes,
    int RotulosLocais, int Identidades, int SemVoz);

/// <summary>
/// Rótulo local de bloco → identidade global, por vetor de voz.
/// </summary>
/// <remarks>
/// <para>
/// <b>O buraco que ela fecha.</b> O MOSS rotula falante <b>dentro</b> do bloco:
/// o <c>S1</c> do bloco 3 não é o <c>S1</c> do bloco 7, e o modelo não tem como
/// saber que são. Enquanto a Fase 7 media qualidade, a régua casava cada bloco
/// com a referência de forma independente — ou seja, dava de graça exatamente o
/// trabalho difícil (<c>docs/FASE7-RESULTADOS.md</c> §7.4). Esta classe paga
/// essa conta.
/// </para>
/// <para>
/// <b>Em ordem de chegada e sem olhar o futuro.</b> Percorre os rótulos pelo
/// instante em que aparecem; monta o vetor de voz de cada um; compara com as
/// identidades já conhecidas; acima do limiar é a mesma pessoa e o centroide é
/// atualizado; abaixo, é gente nova. Não é preferência estética: <b>ao vivo não
/// há futuro para olhar</b>, e um algoritmo que dependesse da reunião inteira
/// teria de ser reescrito para o produto A da <c>docs/FASE7.md</c>. É o
/// <i>Arrival-Order Speaker Cache</i> do Sortformer feito por fora, sem teto de
/// falantes. O porte fiel é do <c>costurar()</c> do
/// <c>tools/medir_costura.py</c>, que é onde os números da §11 foram medidos.
/// </para>
/// <para>
/// <b>Ela usa o vetor de voz que já existe</b>, o mesmo do
/// <see cref="AprendizadoDeVozes"/> e do banco de vozes — quem extrai é o motor
/// de diarização, pela operação <c>voz</c>. Carregar um modelo próprio aqui
/// criaria um segundo espaço vetorial dentro do mesmo app, e vetores de modelos
/// diferentes não são comparáveis: a comparação não falha, ela passa a errar em
/// silêncio (docs/VOZES.md §7).
/// </para>
/// <para>
/// <b>Custo conhecido, e ele não é conserto desta classe:</b> mesmo no limiar
/// escolhido, ela divide gente demais — 15 identidades para 8 pessoas na reunião
/// de 122 min (§11.4). É o que promove o <c>VOZ-1</c> do
/// <c>docs/BACKLOG.md</c> — sugerir fusão de perfis parecidos — de conveniência
/// a requisito. Errar dividindo é o erro barato: quem lê junta duas linhas;
/// errar fundindo põe a fala de uma pessoa na boca de outra e ninguém percebe.
/// </para>
/// </remarks>
public static class CosturaDeFalantes
{
    /// <summary>
    /// Extrai o vetor de voz de um conjunto de trechos do áudio.
    /// </summary>
    /// <returns>
    /// O vetor, ou <c>null</c> quando não deu para extrair — que a costura trata
    /// como "não sei", nunca como erro.
    /// </returns>
    /// <remarks>
    /// Um delegado, e não um <see cref="MotorSidecar"/>, por duas razões: quem
    /// chama mantém <b>um</b> motor quente para a costura inteira (subir o
    /// pyannote por rótulo pagaria a carga dezenas de vezes), e a costura fica
    /// exercitável sem GPU — que é a única forma de ela ter teste nesta suíte.
    /// </remarks>
    public delegate Task<float[]?> ExtratorDeVoz(
        IReadOnlyList<(double Inicio, double Fim)> trechos, CancellationToken ct);

    /// <summary>
    /// Costura os rótulos locais dos segmentos numa identidade por pessoa.
    /// </summary>
    /// <param name="segmentos">
    /// A saída do <see cref="MossEmBlocos"/>: o rótulo local vem em
    /// <see cref="SegmentoFinal.Speaker"/>. Segmento sem rótulo é ignorado.
    /// </param>
    public static async Task<Costura> CosturarAsync(
        IReadOnlyList<SegmentoFinal> segmentos, ExtratorDeVoz extrair,
        CancellationToken ct = default)
    {
        var grupos = new Dictionary<string, List<SegmentoFinal>>(StringComparer.Ordinal);
        foreach (var s in segmentos)
        {
            if (s.Speaker is not { Length: > 0 } rotulo) continue;
            if (!grupos.TryGetValue(rotulo, out var lista))
                grupos[rotulo] = lista = [];
            lista.Add(s);
        }

        // A ordem é a de chegada: o primeiro instante em que o rótulo aparece.
        // Ordenar por outra coisa — o nome do rótulo, o tamanho do grupo — daria
        // outro resultado, porque o centroide de cada identidade depende de quem
        // entrou nela antes.
        var ordem = grupos.Keys
            .OrderBy(r => grupos[r].Min(s => s.Start))
            .ThenBy(r => r, StringComparer.Ordinal)
            .ToList();

        var identidades = new List<Identidade>();
        var mapa = new Dictionary<string, string>(StringComparer.Ordinal);
        int semVoz = 0;

        foreach (string rotulo in ordem)
        {
            ct.ThrowIfCancellationRequested();

            // Lista vazia é "não há fala limpa bastante", e ela não vira
            // requisição: o motor levantaria erro, e um erro por rótulo poluiria
            // o registro com falhas que não são falhas.
            var trechos = TrechosDoRotulo(grupos[rotulo]);
            float[]? vetor = trechos.Count == 0 ? null : await extrair(trechos, ct);
            vetor = vetor is null ? null : Normalizado(vetor);

            if (vetor is null)
            {
                // Sem fala limpa suficiente: vira identidade própria. Inventar um
                // vínculo sem vetor é pior que admitir que não se sabe — e a
                // §2.3 mediu que há fala para ancorar em ~88% dos pares, então
                // isto é a exceção, não a regra.
                semVoz++;
                mapa[rotulo] = Rotulo(identidades.Count);
                identidades.Add(new Identidade(null, 0));
                continue;
            }

            int melhor = -1;
            double maior = -1;
            for (int i = 0; i < identidades.Count; i++)
            {
                if (identidades[i].Vetor is not { } conhecido) continue;
                double s = Vozes.Cosseno(vetor, conhecido);
                if (s > maior) { maior = s; melhor = i; }
            }

            if (melhor >= 0 && maior >= Vozes.LimiarDeCosturaNaReuniao)
            {
                // Centroide corrente: a voz da pessoa melhora a cada bloco em que
                // ela aparece, e é isso que segura a costura em reunião longa —
                // o vetor do primeiro bloco sozinho envelhece mal quando a
                // pessoa muda de tom, de distância do microfone ou de sala.
                identidades[melhor] = identidades[melhor].Com(vetor);
                mapa[rotulo] = Rotulo(melhor);
            }
            else
            {
                mapa[rotulo] = Rotulo(identidades.Count);
                identidades.Add(new Identidade(vetor, 1));
            }
        }

        var falantes = new List<SegmentoDeFalante>(segmentos.Count);
        foreach (var s in segmentos)
            if (s.Speaker is { Length: > 0 } rotulo && mapa.TryGetValue(rotulo, out string? quem))
                falantes.Add(new SegmentoDeFalante(s.Start, s.End, quem));

        return new Costura(falantes, ordem.Count, identidades.Count, semVoz);
    }

    /// <summary>
    /// Os trechos que representam um rótulo, do mais longo para o mais curto.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Aqui não vale a regra de vizinhança do
    /// <see cref="AprendizadoDeVozes.TrechosDe"/></b>, e a diferença é
    /// deliberada. Lá se está inscrevendo alguém no banco de vozes, que
    /// atravessa reuniões: um vetor contaminado envenena em silêncio e para
    /// sempre, então descartar tudo o que tem vizinho perto é barato. Aqui o
    /// vetor vive uma reunião, e os segmentos já vêm de um modelo que decidiu
    /// que aquela fala é daquela pessoa — refiltrar por vizinho descartaria
    /// justamente os blocos de conversa rápida, que são os que mais precisam de
    /// costura.
    /// </para>
    /// <para>
    /// O truncamento em <see cref="AprendizadoDeVozes.SegundosDoTrecho"/> é o
    /// mesmo, e pela mesma razão: a janela que o vetor enxerga tem de ser a
    /// janela que alguém conseguiria auditar.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<(double Inicio, double Fim)> TrechosDoRotulo(
        IReadOnlyList<SegmentoFinal> doRotulo)
    {
        var trechos = doRotulo
            .Where(s => s.End > s.Start)
            .Select(s => (Inicio: s.Start,
                          Fim: Math.Min(s.End, s.Start + AprendizadoDeVozes.SegundosDoTrecho)))
            .OrderByDescending(t => t.Fim - t.Inicio)
            .ToList();

        // Só o suficiente para passar do piso, como no AprenderAsync: quanto
        // mais trechos entram, maior a chance de um deles estar sujo — e o piso
        // é sobre o total de fala, não sobre um trecho.
        var usados = new List<(double, double)>();
        double soma = 0;
        foreach (var t in trechos)
        {
            usados.Add(t);
            soma += t.Fim - t.Inicio;
            if (soma >= Vozes.SegundosMinimos * 1.5) break;
        }

        // Abaixo do piso não se manda nada: o motor levantaria erro ou devolveria
        // um vetor ruidoso, e um vetor ruidoso casa com qualquer um.
        return soma < Vozes.SegundosMinimos ? [] : usados;
    }

    /// <summary>
    /// O nome de uma identidade, no formato que o resto do pipeline já espera.
    /// </summary>
    /// <remarks>
    /// <c>SPEAKER_00</c>, e não <c>P1</c>, porque nada abaixo da bifurcação pode
    /// mudar: o <see cref="Montagem.AtribuirFalantes"/> numera os falantes pela
    /// <b>ordem alfabética do rótulo cru</b>, e <c>P1, P10, P11, P2</c> faria a
    /// décima pessoa a falar virar "Speaker 2". Com dois dígitos, a ordem
    /// alfabética é a ordem de chegada, que é a mesma leitura que o pyannote
    /// entrega hoje.
    /// </remarks>
    private static string Rotulo(int indice) => $"SPEAKER_{indice:00}";

    /// <summary>Uma pessoa em construção: o centroide corrente e quantos entraram nele.</summary>
    private readonly record struct Identidade(float[]? Vetor, int Quantos)
    {
        public Identidade Com(float[] novo)
        {
            var soma = new float[novo.Length];
            for (int i = 0; i < soma.Length; i++)
                soma[i] = (Vetor![i] * Quantos + novo[i]) / (Quantos + 1);
            return new Identidade(Normalizado(soma), Quantos + 1);
        }
    }

    /// <summary>
    /// O vetor com norma 1.
    /// </summary>
    /// <remarks>
    /// A média corrente só é média de direções se as parcelas tiverem o mesmo
    /// comprimento; sem isto, um vetor de norma maior puxaria o centroide mais
    /// que os outros por um motivo que nada tem a ver com a voz. É a mesma conta
    /// do <see cref="Vozes.Centroide"/>, feita incrementalmente porque a costura
    /// não pode guardar todos os vetores de uma reunião de duas horas.
    /// </remarks>
    private static float[] Normalizado(float[] v)
    {
        double norma = Math.Sqrt(v.Sum(x => (double)x * x));
        if (norma == 0) return v;

        var saida = new float[v.Length];
        for (int i = 0; i < v.Length; i++) saida[i] = (float)(v[i] / norma);
        return saida;
    }
}
