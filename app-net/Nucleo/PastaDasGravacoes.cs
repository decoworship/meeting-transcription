using MeetingRecorder.Core;

namespace MeetingApp.Nucleo;

/// <summary>
/// Onde as gravações ficam — uma resposta só para os dois papéis do app.
/// </summary>
/// <remarks>
/// <para>
/// Até a Fase 2 eram dois programas e duas respostas: o gravador escrevia em
/// <c>settings.json / output_dir</c>, e o app lia essa chave à mão porque não
/// podia referenciar o assembly do gravador. A aba Geral ainda oferecia uma
/// terceira, <c>app.json / pasta_das_gravacoes</c>, que <b>nada lia</b> — mexer
/// nela não tinha efeito nenhum.
/// </para>
/// <para>
/// Fundidos, gravar e ler no mesmo lugar deixa de ser coincidência e passa a ser
/// invariante: a autoridade é o <c>output_dir</c> do gravador, porque é onde o
/// áudio de fato cai. O <c>pasta_das_gravacoes</c> sobrevive só como migração —
/// quem tinha escolhido uma pasta ali não deve reconfigurar nada (FASE2.5.md,
/// critério F).
/// </para>
/// </remarks>
public static class PastaDasGravacoes
{
    /// <summary>Onde as gravações caem quando ninguém escolheu nada.</summary>
    public static string Padrao => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        "MeetingRecordings");

    /// <param name="argumento">O <c>--gravacoes</c>, quando dado.</param>
    /// <param name="caminhoDoAppJson">
    /// Só os testes passam: sem isto a migração leria o <c>app.json</c> de quem
    /// está rodando a suíte.
    /// </param>
    /// <remarks>
    /// O argumento vence tudo e não é gravado em lugar nenhum: existe para abrir
    /// um acervo que não é o da máquina — teste, suporte, uma pasta de rede —
    /// sem mexer na configuração de quem grava.
    /// </remarks>
    public static string Resolver(Configuracoes cfg, string? argumento,
                                  string? caminhoDoSettings = null,
                                  string? caminhoDoAppJson = null)
    {
        if (argumento is { Length: > 0 }) return argumento;

        if (cfg.OutputDir is { Length: > 0 } doGravador) return doGravador;

        // A migração acontece uma vez, na primeira abertura do app fundido:
        // adotar em silêncio e não gravar de volta faria a escolha antiga sumir
        // no dia em que alguém mexesse na pasta pelo menu da bandeja.
        var doApp = ConfiguracoesDoApp.Carregar(caminhoDoAppJson);
        if (doApp.PastaDasGravacoes is { Length: > 0 } herdada)
        {
            cfg.OutputDir = herdada;
            try
            {
                cfg.Salvar(caminhoDoSettings);
                doApp.PastaDasGravacoes = null;
                doApp.Salvar(caminhoDoAppJson);
            }
            catch (Exception)
            {
                // Não conseguir gravar a migração não pode impedir o app de
                // abrir: o valor herdado vale para esta execução e a migração
                // tenta de novo na próxima.
            }
            return herdada;
        }

        return Padrao;
    }
}
