using MeetingApp.Sidecar;

namespace MeetingApp.Nucleo;

/// <summary>Onde o pipeline está, para quem espera.</summary>
/// <param name="Etapa">"mix", "asr", "diarizacao" ou "montagem".</param>
/// <param name="Fracao">0 a 1 dentro da etapa, ou -1 quando não há como saber.</param>
public readonly record struct Progresso(string Etapa, double Fracao, string Texto);

/// <summary>Como achar os motores Python.</summary>
/// <remarks>
/// Caminhos, e não descoberta automática: quando o empacotamento da Fase 2
/// chegar, os motores virão numa pasta conhecida ao lado do executável, e até
/// lá dá para apontar para um ambiente de desenvolvimento sem mudar código.
/// </remarks>
public sealed record Motores(string Python, string ScriptAsr, string ScriptDiarizacao)
{
    /// <summary>O arranjo esperado do app instalado: <c>motores/</c> ao lado do .exe.</summary>
    public static Motores AoLadoDoExecutavel()
    {
        string raiz = Path.Combine(AppContext.BaseDirectory, "motores");
        return new Motores(
            Path.Combine(raiz, "python", "python.exe"),
            Path.Combine(raiz, "asr", "motor.py"),
            Path.Combine(raiz, "diarizacao", "motor.py"));
    }

    /// <summary>Diz o que falta, ou <c>null</c> se está tudo no lugar.</summary>
    /// <remarks>
    /// Checar antes de spawnar é o que transforma "o motor morreu" — mensagem
    /// que não ajuda ninguém — em "faltou este arquivo aqui".
    /// </remarks>
    public string? OQueFalta()
    {
        if (!File.Exists(Python)) return $"o Python dos motores não está em {Python}";
        if (!File.Exists(ScriptAsr)) return $"o motor de transcrição não está em {ScriptAsr}";
        if (!File.Exists(ScriptDiarizacao))
            return $"o motor de diarização não está em {ScriptDiarizacao}";
        return null;
    }
}

/// <summary>
/// O pipeline completo de uma gravação: mix → ASR → diarização → resultado.
/// </summary>
/// <remarks>
/// Vive no núcleo, e não no CLI, porque tem dois consumidores — a linha de
/// comando, que provou o caminho antes de existir UI, e o app. Duplicar a
/// ordem das etapas nos dois seria garantir que um dia divergissem.
/// </remarks>
public sealed class Transcritor(Motores motores)
{
    /// <param name="progresso">Chamado na thread do pipeline, não na da UI.</param>
    public async Task<ResultadoDaTranscricao> ExecutarAsync(
        string pastaDaGravacao, string? vocabulario = null, string? idioma = null,
        bool filtrarSilencio = false, Action<Progresso>? progresso = null,
        CancellationToken ct = default)
    {
        if (motores.OQueFalta() is { } falta) throw new MotorException(falta);

        string mic = Path.Combine(pastaDaGravacao, "mic.wav");
        string sistema = Path.Combine(pastaDaGravacao, "system.wav");
        foreach (string f in new[] { mic, sistema })
            if (!File.Exists(f))
                throw new MotorException($"a gravação não tem {Path.GetFileName(f)}");

        progresso?.Invoke(new Progresso("mix", 0, "somando as duas faixas"));
        var faixas = Faixas.Ler(mic, sistema);

        // O mix vai para junto da gravação: é derivado e refazível, mas enquanto
        // o pipeline roda ele precisa existir num caminho que o motor abra.
        string caminhoDoMix = Path.Combine(pastaDaGravacao, "mix.wav");
        faixas.EscreverMix(caminhoDoMix);

        // ASR primeiro, diarização depois, cada um no seu processo: numa placa
        // de 6 GB os dois modelos não cabem juntos, e processos separados fazem
        // a VRAM do primeiro voltar antes de o segundo subir.
        Transcricao transcricao;
        using (var asr = await MotorSidecar.IniciarAsync(motores.Python, [motores.ScriptAsr], ct))
        {
            transcricao = await asr.TranscreverAsync(caminhoDoMix, vocabulario, idioma,
                (pct, texto) => progresso?.Invoke(new Progresso("asr", pct, texto)), ct);
        }

        // A diarização roda só no system.wav: o que o microfone captou já se sabe
        // de quem é, e dar o mix ao pyannote o faria tentar separar você de você.
        IReadOnlyList<SegmentoDeFalante> diarizacao;
        using (var diar = await MotorSidecar.IniciarAsync(
                   motores.Python, [motores.ScriptDiarizacao], ct))
        {
            diarizacao = await diar.DiarizarAsync(sistema,
                (pct, texto) => progresso?.Invoke(new Progresso("diarizacao", pct, texto)), ct);
        }

        progresso?.Invoke(new Progresso("montagem", 0, "juntando texto e falantes"));

        var segmentos = transcricao.Segmentos
            .Select(s => new SegmentoFinal { Start = s.Inicio, End = s.Fim, Text = s.Texto })
            .ToList();

        if (filtrarSilencio) FiltroDeSilencio.Filtrar(segmentos, faixas.Mix());

        if (vocabulario is { Length: > 0 })
        {
            var termos = vocabulario.Split(',', StringSplitOptions.TrimEntries
                                                | StringSplitOptions.RemoveEmptyEntries);
            foreach (var seg in segmentos)
            {
                var (texto, trocas) = CorrecaoFonetica.Corrigir(seg.Text, termos);
                if (trocas.Count > 0) seg.Text = texto;
            }
        }

        Montagem.AtribuirFalantes(segmentos, diarizacao);
        Montagem.AtribuirDono(segmentos, faixas);

        var resultado = new ResultadoDaTranscricao
        {
            Language = transcricao.Idioma,
            Duration = transcricao.Duracao,
            Segments = segmentos,
        };

        await File.WriteAllTextAsync(
            Path.Combine(pastaDaGravacao, "transcricao.json"), resultado.ParaJson(), ct);

        progresso?.Invoke(new Progresso("montagem", 1, "pronto"));
        return resultado;
    }
}
