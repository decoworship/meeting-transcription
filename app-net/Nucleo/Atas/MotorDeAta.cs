using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace MeetingApp.Nucleo.Atas;

/// <summary>Onde o motor de ata mora no disco.</summary>
/// <param name="Servidor">O <c>llama-server.exe</c>.</param>
/// <param name="Modelo">O arquivo <c>.gguf</c>.</param>
public sealed record CaminhosDoMotorDeAta(string Servidor, string Modelo)
{
    /// <remarks>
    /// Ao lado do executável, como os motores Python: <c>motores/ata/</c> com o
    /// llama.cpp, e o GGUF na pasta de modelos do usuário, que é de onde o
    /// catálogo baixa.
    /// </remarks>
    public static CaminhosDoMotorDeAta AoLadoDoExecutavel(string? modelo = null)
    {
        // A variável existe para desenvolver e medir sem copiar 3 GB para dentro
        // da instalação — mesmo motivo do HF_TOKEN poder vir do ambiente. Em
        // produção ela não existe, e vale a pasta ao lado do executável.
        string raiz = Environment.GetEnvironmentVariable("MEETINGAPP_MOTOR_ATA")
            is { Length: > 0 } fora
            ? fora
            : Path.Combine(AppContext.BaseDirectory, "motores", "ata");

        string arquivo = modelo is { Length: > 0 } ? modelo : "qwen3-4b-instruct-q4km.gguf";
        string caminhoDoModelo = Path.IsPathRooted(arquivo)
            ? arquivo
            : Path.Combine(raiz, "modelos", arquivo);
        if (!File.Exists(caminhoDoModelo) && File.Exists(Path.Combine(raiz, arquivo)))
            caminhoDoModelo = Path.Combine(raiz, arquivo);

        return new CaminhosDoMotorDeAta(
            Path.Combine(raiz, "bin", "llama-server.exe") is var comBin && File.Exists(comBin)
                ? comBin : Path.Combine(raiz, "llama-server.exe"),
            caminhoDoModelo);
    }

    public string? OQueFalta()
    {
        // Desde a Fase 4 o motor não vem no instalador — são 1,1 GB para uma
        // funcionalidade que nem toda instalação usa. Então "não está lá" é o
        // estado normal de quem acabou de instalar, e a frase tem que dizer o
        // que fazer em vez de só constatar um caminho vazio.
        if (!File.Exists(Servidor))
            return "o motor de ata ainda não foi baixado — "
                 + "abra Ajustes › Modelos e baixe-o (são 641 MB)";
        if (!File.Exists(Modelo))
            return $"o modelo de ata não está em {Modelo} — baixe-o em Ajustes › Modelos";
        return null;
    }
}

/// <summary>Andamento da geração, para a tela desenhar.</summary>
public sealed record ProgressoDaAta(string Etapa, double Fracao, string Texto);

/// <summary>
/// O llama.cpp gerando a ata, com a saída presa ao esquema.
/// </summary>
/// <remarks>
/// <para>
/// <b>Por que HTTP aqui, se o resto do projeto fala por pipe.</b> A doutrina do
/// SIDECAR.md — stdin/stdout, sem porta, sem Firewall — continua valendo para os
/// motores Python. Este é o caso em que ela não se sustenta, e a razão é medida:
/// a saída constrangida por esquema **não funciona pelo <c>llama-cli</c>**. A
/// gramática vale desde o primeiro token e colide com o <c>&lt;|im_start|&gt;</c>
/// do template de chat (<c>Unexpected empty grammar stack</c>), tanto em modo
/// conversa quanto em completação. Pelo <c>llama-server</c>, aplicada só à
/// resposta do assistente, funciona (ATA.md §8).
/// </para>
/// <para>
/// E a saída constrangida não é enfeite: sem ela o modelo escreve o responsável
/// fora do formato, e a ata deixa de ser verificável — que é o que torna um 4B
/// local aceitável.
/// </para>
/// <para>
/// <b>O que se faz para o HTTP custar pouco:</b> escuta só em
/// <c>127.0.0.1</c> (o Windows não pede autorização de Firewall para
/// <em>loopback</em>), porta escolhida pelo sistema a cada execução (nada de
/// porta fixa colidindo com outro app), e o processo é <b>filho</b> — morre com
/// a requisição, e o cancelamento o mata como o <c>MotorSidecar</c> faz.
/// </para>
/// </remarks>
public sealed class MotorDeAta(CaminhosDoMotorDeAta caminhos)
{
    /// <summary>
    /// Caracteres do prompt por token, medido em português com carimbo de tempo.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Medido em 15/08/2026 sobre a reunião que falhou: 48.071 caracteres de
    /// transcrição no formato do prompt viraram ~17.500 tokens, ou
    /// <b>2,74 caracteres por token</b>. É bem menos que os 3,5 a 4 de prosa
    /// corrida, e a razão está no formato: <c>[MM:SS] Fulano:</c> em cada linha
    /// é pontuação e dígito, que tokenizam mal.
    /// </para>
    /// <para>
    /// O valor usado é <b>2,5</b>, e a folga é deliberada: superestimar o
    /// contexto custa VRAM, subestimar custa a ata inteira depois de o usuário
    /// já ter esperado.
    /// </para>
    /// </remarks>
    public const double CaracteresPorToken = 2.5;

    /// <summary>
    /// Tokens reservados para a ata que o modelo vai escrever.
    /// </summary>
    /// <remarks>
    /// O mesmo número vai para o <c>max_tokens</c> da requisição e para a
    /// reserva no dimensionamento do contexto — eram dois números diferentes
    /// (3.072 e 4.096) e a divergência é justamente o que produz o pior
    /// desfecho: o contexto reservado é menor do que o modelo tem permissão para
    /// escrever, e a ata sai pela metade.
    /// </remarks>
    public const int TokensDeSaida = 8192;

    /// <summary>Sobra para os buffers de computação do llama.cpp.</summary>
    private const long FolgaDeVram = 600L * 1024 * 1024;

    /// <summary>O contexto é arredondado para cima neste múltiplo.</summary>
    /// <remarks>
    /// <para>
    /// Havia uma escada de degraus (8k, 16k, 24k, 32k, 48k, 64k…) e ela se
    /// mostrou grosseira demais na primeira medição: uma reunião que precisava
    /// de 51k tokens caía no degrau de 64k, e 64k **não cabem** com a chave em
    /// q8_0 — então o cache inteiro desabava para q4_0 por causa de 13k tokens
    /// que ninguém ia usar.
    /// </para>
    /// <para>
    /// Arredondar de 4k em 4k pede o que se precisa, e deixa a chave em q8_0 nos
    /// casos em que a escada a derrubava sem necessidade.
    /// </para>
    /// </remarks>
    private const int Granularidade = 4096;

    /// <summary>Teto prático: acima disto o cache não cabe em placa alguma.</summary>
    private const int ContextoMaximoPratico = 131072;

    /// <summary>
    /// Quanto contexto pedir, a partir do prompt e da placa.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>A versão anterior estimava pela duração da reunião, e isso quebrou em
    /// campo.</b> A escada dava 16k para qualquer reunião de até 45 min; uma
    /// sessão de trabalho de 39 min produziu <b>19.935 tokens</b> — 508 por
    /// minuto, contra os ~360 que a escada pressupunha — e o
    /// <c>llama-server</c> recusou com <c>exceed_context_size_error</c> depois
    /// de o modelo já ter carregado.
    /// </para>
    /// <para>
    /// O erro não era o número: era medir a coisa errada. <b>Tokens não saem do
    /// relógio, saem da fala</b> — duas pessoas discutindo sem pausa produzem o
    /// dobro de uma apresentação com silêncio. E o prompt já existe, inteiro, na
    /// memória, antes de o motor subir: dá para contar em vez de adivinhar.
    /// </para>
    /// <para>
    /// A quantização do cache segue o que couber, nesta ordem: <c>q8_0</c> nos
    /// dois (praticamente sem perda), depois <c>q8_0</c> na chave e <c>q4_0</c>
    /// no valor, e por fim <c>q4_0</c> nos dois. A chave é a última a ceder
    /// porque é ela que decide onde o modelo presta atenção.
    /// </para>
    /// </remarks>
    /// <param name="caracteresDoPrompt">O prompt montado, em caracteres.</param>
    /// <param name="modelo">Lido do próprio <c>.gguf</c>.</param>
    /// <param name="vramBytes">Total da placa. Zero quando não se sabe.</param>
    public static (int Contexto, string Ctk, string Ctv) Dimensionar(
        int caracteresDoPrompt, MetadadosDoGguf modelo, long vramBytes)
    {
        int precisa = (int)(caracteresDoPrompt / CaracteresPorToken) + TokensDeSaida;

        int teto = modelo.ContextoMaximo > 0
            ? Math.Min(modelo.ContextoMaximo, ContextoMaximoPratico)
            : ContextoMaximoPratico;

        // Arredondado para cima, e nunca menor que um contexto de trabalho
        // mínimo: pedir 5k porque a reunião foi curta economiza VRAM que
        // ninguém estava disputando.
        int pedido = Math.Max(
            Granularidade * 2,
            (precisa + Granularidade - 1) / Granularidade * Granularidade);

        // Sem saber a VRAM não se inventa limite: pede o que precisa e deixa o
        // llama.cpp reclamar, que é melhor que recusar por uma conta chutada.
        long paraCache = vramBytes > 0
            ? vramBytes - modelo.BytesDoArquivo - FolgaDeVram
            : long.MaxValue;

        foreach (var (ctk, ctv) in new[] { ("q8_0", "q8_0"), ("q8_0", "q4_0"), ("q4_0", "q4_0") })
        {
            long porToken = modelo.BytesDeCachePorToken(ctk, ctv);
            long cabeNaPlaca = porToken > 0 && paraCache != long.MaxValue
                ? paraCache / porToken
                : teto;

            if (pedido <= teto && pedido <= cabeNaPlaca) return (pedido, ctk, ctv);
        }

        // Nada coube **pela conta**. Isso não é o mesmo que não caber.
        //
        // Medido em 17/08/2026 com o Gemma 4 E4B: a conta disse que só cabiam
        // 17.281 tokens, e o llama.cpp carregou 32.768 sem reclamar. O motivo é
        // que a fórmula trata todas as camadas como iguais, e o Gemma usa janela
        // deslizante — 512 tokens de cache na maioria das camadas, dimensões
        // menores nelas, e 18 camadas compartilhando KV. A conta errou para mais
        // em cerca de cinco vezes.
        //
        // Modelar isso arquitetura por arquitetura é uma corrida que se perde: a
        // próxima família traz outro truque. **Quem conhece a arquitetura é o
        // llama.cpp.** Então a estimativa fica com o papel que ela faz bem —
        // escolher a quantização do cache — e perde o papel de porteiro: se ela
        // acha que não cabe, tenta assim mesmo, com o cache mais apertado, e
        // quem decide é a alocação de verdade.
        if (pedido <= teto) return (pedido, "q4_0", "q4_0");

        // O único limite que é mesmo nosso: o contexto que o modelo foi treinado
        // para ter. Passar dele não dá erro — dá saída ruim, em silêncio.
        throw new InvalidOperationException(
            $"esta reunião precisa de ~{precisa:N0} tokens de contexto, e o modelo "
            + $"{modelo.Nome} vai até {teto:N0}. Gere a ata de um trecho menor, ou "
            + $"escolha um modelo de contexto maior em Ajustes › Modelos.");

        // Chegar aqui é não caber. As duas causas são diferentes e pedem coisas
        // diferentes de quem lê, então a mensagem separa as duas — dizer "não
        // cabe na placa" quando o limite é do modelo manda a pessoa comprar
        // memória que não vai resolver.
        //
        // E dizer isso **agora** é o ponto: antes vinha um JSON de erro do
        // servidor depois de o modelo já ter carregado e o usuário já ter
        // esperado.
    }

    public async Task<AtaGerada> GerarAsync(
        string prompt, double duracaoS,
        Action<ProgressoDaAta>? progresso = null, CancellationToken ct = default)
    {
        if (caminhos.OQueFalta() is { } falta) throw new InvalidOperationException(falta);

        // O modelo diz de quantas camadas e cabeças ele é feito, e a placa diz
        // quanta memória tem. Com os dois, o contexto é conta e não chute — e a
        // recusa, quando vier, vem antes de carregar 2,5 GB.
        var modelo = MetadadosDoGguf.Ler(caminhos.Modelo);
        var (contexto, ctk, ctv) = Dimensionar(prompt.Length, modelo, VramDaPlaca());
        int porta = PortaLivre();

        progresso?.Invoke(new ProgressoDaAta(
            "modelo", 0.05,
            $"carregando o modelo ({contexto / 1024}k de contexto)"));

        using var processo = Subir(porta, contexto, ctk, ctv);
        try
        {
            using var registroDeMorte = ct.Register(() => Matar(processo));
            await EsperarSubirAsync(processo, porta, ct);

            progresso?.Invoke(new ProgressoDaAta("lendo", 0.2, "lendo a reunião"));
            string json = await PedirAsync(porta, prompt, ct);

            progresso?.Invoke(new ProgressoDaAta("montagem", 0.95, "montando a ata"));
            return AtaGerada.DeJson(json)
                ?? throw new InvalidOperationException("o motor devolveu um JSON ilegível");
        }
        finally
        {
            Matar(processo);
        }
    }

    /// <summary>
    /// A memória total da placa, em bytes. Zero quando não há placa ou não deu.
    /// </summary>
    /// <remarks>
    /// Perguntado ao <c>nvidia-smi</c>, como o bloco de diagnóstico já faz. Zero
    /// não é erro: significa "não sei", e o dimensionamento trata "não sei" como
    /// "não imponha limite" — deixar o llama.cpp reclamar é melhor que recusar
    /// uma reunião por causa de uma conta chutada.
    /// </remarks>
    private static long VramDaPlaca()
    {
        try
        {
            var p = Process.Start(new ProcessStartInfo
            {
                FileName = "nvidia-smi",
                Arguments = "--query-gpu=memory.total --format=csv,noheader,nounits",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            });
            if (p is null) return 0;

            string saida = p.StandardOutput.ReadToEnd();
            if (!p.WaitForExit(5_000)) { try { p.Kill(true); } catch { } return 0; }
            if (p.ExitCode != 0) return 0;

            string primeira = saida.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                                   .FirstOrDefault()?.Trim() ?? "";
            return long.TryParse(primeira, out long mib) ? mib * 1024 * 1024 : 0;
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or IOException
                                       or InvalidOperationException)
        {
            return 0;
        }
    }

    private Process Subir(int porta, int contexto, string ctk, string ctv)
    {
        var inicio = new ProcessStartInfo(caminhos.Servidor)
        {
            WorkingDirectory = Path.GetDirectoryName(caminhos.Servidor)!,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            // Sem isto, cada geração pisca um console preto na cara de quem está
            // usando o app. Mesma razão do CREATE_NO_WINDOW dos motores.
            CreateNoWindow = true,
        };

        foreach (string a in new[]
        {
            "-m", caminhos.Modelo, "-ngl", "99",
            "-c", contexto.ToString(), "-ctk", ctk, "-ctv", ctv,
            // Flash attention explícita: o cache de V quantizado depende dela, e
            // o padrão "auto" pode decidir não usá-la e derrubar a alocação de
            // um contexto que a conta dizia caber.
            "-fa", "on",
            "--host", "127.0.0.1", "--port", porta.ToString(),
            "--jinja", "--no-warmup",
            // Um slot só: duas gerações em paralelo dividiriam o contexto ao
            // meio, e não há duas — a ata é uma por vez, como a transcrição.
            "-np", "1",
        }) inicio.ArgumentList.Add(a);

        var p = Process.Start(inicio)
            ?? throw new InvalidOperationException("não consegui iniciar o motor de ata");

        // O servidor escreve bastante em stderr; sem ler, o buffer enche e ele
        // trava. Descartar é aceitável — o que importa é o /health responder.
        p.BeginOutputReadLine();
        p.BeginErrorReadLine();
        return p;
    }

    private static async Task EsperarSubirAsync(Process processo, int porta, CancellationToken ct)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        var limite = DateTime.UtcNow.AddSeconds(120);

        while (DateTime.UtcNow < limite)
        {
            ct.ThrowIfCancellationRequested();
            if (processo.HasExited)
                throw new InvalidOperationException(
                    $"o motor de ata morreu ao subir (código {processo.ExitCode})");

            try
            {
                var r = await http.GetAsync($"http://127.0.0.1:{porta}/health", ct);
                if (r.IsSuccessStatusCode) return;
            }
            catch (HttpRequestException) { /* ainda não subiu */ }
            catch (TaskCanceledException) when (!ct.IsCancellationRequested) { }

            await Task.Delay(250, ct);
        }
        throw new TimeoutException("o motor de ata não respondeu em 2 minutos");
    }

    private static async Task<string> PedirAsync(int porta, string prompt, CancellationToken ct)
    {
        // Sem timeout no cliente: uma reunião de duas horas leva minutos, e o
        // relógio que interrompe é o do usuário — o cancelamento mata o
        // processo, que é o que devolve a placa.
        using var http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };

        // O corpo é montado como texto, e não por serialização de objeto
        // anônimo: JsonSerializer por reflexão é erro de build sob
        // PublishTrimmed (IL2026) — compila, passa nos testes, e reprova só na
        // publicação. Mesma armadilha que o reuniao.json já tinha dado.
        //
        // Baixa, mas não zero: ata é registro, não criação. Zero deixa o modelo
        // repetitivo em listas longas.
        //
        // **enable_thinking: false** — medido em 17/08/2026. O Qwen3.5 4B é
        // modelo de raciocínio e, com o padrão do template, gastou os 8.192
        // tokens de saída inteiros pensando: a ata saiu pela metade e a falha
        // parecia do tamanho do limite, não do modo do modelo. Para escrever ata
        // o raciocínio é orçamento gasto no lugar errado — o que faz a ata ser
        // verificável é o esquema e o verificador, não a deliberação do modelo.
        //
        // Modelo que não conhece a variável simplesmente a ignora no Jinja, e é
        // por isso que ela pode ir em todos sem um "se".
        string corpo = $$"""
        {
          "messages": [
            {"role": "system", "content": {{Texto(PromptDeAta.Sistema)}}},
            {"role": "user", "content": {{Texto(prompt)}}}
          ],
          "temperature": 0.3,
          "max_tokens": {{TokensDeSaida}},
          "chat_template_kwargs": {"enable_thinking": false},
          "response_format": {
            "type": "json_schema",
            "json_schema": {"name": "ata", "strict": true, "schema": {{AtaGerada.Esquema}}}
          }
        }
        """;

        var conteudo = new StringContent(corpo, Encoding.UTF8, "application/json");

        var resposta = await http.PostAsync(
            $"http://127.0.0.1:{porta}/v1/chat/completions", conteudo, ct);

        string texto = await resposta.Content.ReadAsStringAsync(ct);
        if (!resposta.IsSuccessStatusCode)
            throw new InvalidOperationException($"o motor de ata recusou: {Resumir(texto)}");

        using var doc = JsonDocument.Parse(texto);
        if (doc.RootElement.TryGetProperty("error", out var erro))
            throw new InvalidOperationException($"o motor de ata falhou: {Resumir(erro.ToString())}");

        var escolha = doc.RootElement.GetProperty("choices")[0];

        // Truncar é o modo de falha mais confuso deste motor, e ele já aconteceu
        // em campo: o modelo escreveu uma seção de 14 mil caracteres, bateu no
        // teto de tokens, e o JSON chegou pela metade. O sintoma era um erro de
        // desserialização apontando para uma posição de byte — que não diz nada
        // a quem só queria uma ata.
        //
        // O servidor avisa, e basta perguntar.
        if (escolha.TryGetProperty("finish_reason", out var razao)
            && razao.GetString() == "length")
            throw new InvalidOperationException(
                $"o modelo escreveu mais do que o limite de {TokensDeSaida:N0} tokens e a ata "
                + "saiu pela metade. Isso costuma ser o modelo se alongando numa seção só; "
                + "tente outro tipo de ata, ou um modelo melhor em Ajustes › Modelos.");

        return escolha.GetProperty("message").GetProperty("content").GetString()
            ?? throw new InvalidOperationException("o motor de ata devolveu resposta vazia");
    }

    /// <summary>
    /// Uma porta que o sistema diz estar livre agora.
    /// </summary>
    /// <remarks>
    /// Porta fixa colidiria com outro programa na máquina do usuário — e o
    /// sintoma seria "a ata não gera", sem dizer por quê. Há uma corrida entre
    /// fechar aqui e o servidor abrir; ela é curta e o custo dela é uma
    /// mensagem de erro, não um dado perdido.
    /// </remarks>
    private static int PortaLivre()
    {
        var ouvinte = new TcpListener(IPAddress.Loopback, 0);
        ouvinte.Start();
        int porta = ((IPEndPoint)ouvinte.LocalEndpoint).Port;
        ouvinte.Stop();
        return porta;
    }

    private static void Matar(Process p)
    {
        try
        {
            if (!p.HasExited) p.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException) { /* já saiu */ }
    }

    /// <summary>
    /// Uma string como literal JSON, com as aspas e os escapes.
    /// </summary>
    /// <remarks>
    /// Escrito à mão porque o prompt tem 35 mil caracteres de fala real — com
    /// aspas, barras e quebras de linha — e montar o corpo por concatenação sem
    /// escapar produziria JSON inválido no primeiro "não" entre aspas.
    /// <c>JsonEncodedText</c> faz o escape sem passar por reflexão, que é o que
    /// o <c>PublishTrimmed</c> recusa.
    /// </remarks>
    private static string Texto(string valor) =>
        $"\"{JsonEncodedText.Encode(valor).ToString()}\"";

    private static string Resumir(string texto) =>
        texto.Length <= 300 ? texto : texto[..300] + "…";
}
