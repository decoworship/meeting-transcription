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
        if (!File.Exists(Servidor)) return $"o motor de ata não está em {Servidor}";
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
    /// Quanto contexto pedir, pela duração da reunião.
    /// </summary>
    /// <remarks>
    /// Medido na RTX 2060 de 6 GB: o KV custa ~62 KiB por token em q8_0, e é ele
    /// que decide se cabe — não o modelo. Daí a escada, e daí o q4_0 no degrau
    /// mais alto: uma reunião de 2 h só cabe com o KV mais apertado (ATA.md §8).
    /// </remarks>
    public static (int Contexto, string Kv) Dimensionar(double duracaoS)
    {
        double minutos = duracaoS / 60;
        if (minutos <= 45) return (16384, "q8_0");
        if (minutos <= 75) return (24576, "q8_0");
        if (minutos <= 100) return (32768, "q8_0");
        return (49152, "q4_0");
    }

    public async Task<AtaGerada> GerarAsync(
        string prompt, double duracaoS,
        Action<ProgressoDaAta>? progresso = null, CancellationToken ct = default)
    {
        if (caminhos.OQueFalta() is { } falta) throw new InvalidOperationException(falta);

        var (contexto, kv) = Dimensionar(duracaoS);
        int porta = PortaLivre();

        progresso?.Invoke(new ProgressoDaAta("modelo", 0.05, "carregando o modelo"));

        using var processo = Subir(porta, contexto, kv);
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

    private Process Subir(int porta, int contexto, string kv)
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
            "-c", contexto.ToString(), "-ctk", kv, "-ctv", kv,
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
        string corpo = $$"""
        {
          "messages": [
            {"role": "system", "content": {{Texto(PromptDeAta.Sistema)}}},
            {"role": "user", "content": {{Texto(prompt)}}}
          ],
          "temperature": 0.3,
          "max_tokens": 4096,
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

        return doc.RootElement.GetProperty("choices")[0]
            .GetProperty("message").GetProperty("content").GetString()
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
