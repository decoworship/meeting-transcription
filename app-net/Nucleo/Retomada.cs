namespace MeetingApp.Nucleo;

/// <summary>
/// O ASR que já rodou nesta gravação, recuperado do disco.
/// </summary>
/// <remarks>
/// <para>
/// <b>Por que existe.</b> O ASR é a etapa cara do pipeline e a diarização é a
/// que derruba a máquina do segundo usuário (<c>docs/FASE6.md</c> §3.0). Até a
/// 0.3.0, uma queda na diarização jogava fora o texto que já estava pronto — e
/// a tentativa seguinte pagava o modelo inteiro de novo só para chegar ao mesmo
/// ponto e morrer ali. Duas vezes a mesma conta pelo mesmo resultado.
/// </para>
/// <para>
/// <b>O parcial é o texto cru do ASR</b>, e não o texto já tratado: filtro de
/// silêncio e correção fonética custam segundos de CPU e são refeitos na
/// retomada. Guardar o cru é o que impede o caso que estraga em silêncio —
/// retomar um arquivo já corrigido e corrigi-lo de novo.
/// </para>
/// <para>
/// A única exceção é o dono. <see cref="Montagem.AtribuirDono"/> não depende da
/// diarização, sai das duas faixas e é o que faz o parcial já valer alguma
/// coisa na tela: quem abrir vê o que é seu. Ele roda outra vez na retomada,
/// depois dos falantes, para manter a precedência de sempre.
/// </para>
/// </remarks>
/// <param name="Segmentos">Os segmentos como o ASR os produziu.</param>
public sealed record Retomada(List<SegmentoFinal> Segmentos, string? Idioma, double? Duracao)
{
    /// <summary>A única etapa que hoje se retoma.</summary>
    public const string Diarizacao = "diarizacao";

    /// <summary>
    /// O parcial desta gravação, quando ele serve para o que se vai pedir agora.
    /// </summary>
    /// <remarks>
    /// Devolve <c>null</c> — e o ASR roda de novo — em todo caso duvidoso. Um
    /// parcial recusado custa alguns minutos de GPU; um parcial aceito por
    /// engano devolve o texto de outro modelo sem dizer nada, e o usuário não
    /// tem como perceber.
    /// </remarks>
    public static Retomada? Ler(string pastaDaGravacao, string modelo,
                                string? idioma, string? vocabulario)
    {
        try
        {
            string caminho = Path.Combine(pastaDaGravacao, "transcricao.json");
            if (!File.Exists(caminho)) return null;

            // Mais novo que as faixas. Regravar por cima da mesma pasta é o que
            // torna o parcial mentira, e a data é o jeito barato de perceber.
            var quando = File.GetLastWriteTimeUtc(caminho);
            foreach (string f in new[] { "mic.wav", "system.wav" })
            {
                string faixa = Path.Combine(pastaDaGravacao, f);
                if (File.Exists(faixa) && File.GetLastWriteTimeUtc(faixa) > quando) return null;
            }

            if (ResultadoDaTranscricao.DeJson(File.ReadAllText(caminho)) is not { } lido)
                return null;
            if (lido.Pending is not { } falta) return null;               // já está pronto
            if (!falta.Steps.Contains(Diarizacao)) return null;
            if (lido.Segments.Count == 0) return null;

            // Os três que decidem a saída do ASR. Ver Pendencia.
            if (!Igual(falta.Model, modelo)) return null;
            if (!Igual(falta.Language, idioma)) return null;
            if (!Igual(falta.Vocabulary, vocabulario)) return null;

            return new Retomada(lido.Segments, lido.Language, lido.Duration);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException
                                       or System.Text.Json.JsonException)
        {
            // Parcial ilegível é parcial que não existe: transcreve do começo.
            return null;
        }
    }

    /// <summary>
    /// Grava o texto do ASR antes de a diarização começar.
    /// </summary>
    /// <remarks>
    /// <b>Nunca levanta exceção.</b> Este arquivo é uma rede, e uma rede que
    /// derruba a transcrição bem-sucedida que ela deveria proteger é pior que
    /// rede nenhuma. Mesma regra do <see cref="Registro"/>.
    /// </remarks>
    public static void Escrever(string pastaDaGravacao, ResultadoDaTranscricao parcial,
                                string modelo, string? idioma, string? vocabulario)
    {
        try
        {
            parcial.Pending = new Pendencia
            {
                Steps = [Diarizacao],
                Model = modelo,
                Language = idioma,
                Vocabulary = vocabulario,
            };
            File.WriteAllText(
                Path.Combine(pastaDaGravacao, "transcricao.json"), parcial.ParaJson());
            Registro.Escrever("pipeline",
                $"texto do ASR salvo ({parcial.Segments.Count} segmentos) — falta diarizar");
        }
        catch (Exception)
        {
            Registro.Escrever("pipeline", "não deu para salvar o texto do ASR antes de diarizar");
        }
        finally
        {
            // O objeto segue para o resto do pipeline, que vai completá-lo e
            // regravá-lo. Deixar a pendência colada nele faria o arquivo final
            // sair marcado como parcial.
            parcial.Pending = null;
        }
    }

    /// <summary>
    /// Se esta gravação tem um parcial esperando a diarização.
    /// </summary>
    /// <remarks>
    /// Serve à lista de gravações, que precisa dizer por que a transcrição não
    /// tem falantes — e não pode pagar a leitura dos parâmetros do ASR por
    /// cartão. Só a pergunta, sem a validação que <see cref="Ler"/> faz.
    /// </remarks>
    public static bool EstaPendente(string pastaDaGravacao)
    {
        try
        {
            string caminho = Path.Combine(pastaDaGravacao, "transcricao.json");
            return File.Exists(caminho)
                   && ResultadoDaTranscricao.DeJson(File.ReadAllText(caminho))
                      is { Pending.Steps.Count: > 0 };
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException
                                       or System.Text.Json.JsonException)
        {
            return false;
        }
    }

    /// <summary>Nulo e vazio são a mesma coisa aqui: "não foi pedido".</summary>
    private static bool Igual(string? a, string? b) =>
        string.Equals(
            string.IsNullOrWhiteSpace(a) ? null : a.Trim(),
            string.IsNullOrWhiteSpace(b) ? null : b.Trim(),
            StringComparison.Ordinal);
}
