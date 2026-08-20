using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json.Serialization;

namespace MeetingApp.Nucleo;

/// <summary>
/// O estado desta instalação, num bloco que se copia e se cola numa mensagem.
/// </summary>
/// <remarks>
/// <para>
/// <b>Por que existe.</b> Enquanto o app roda só na máquina de quem o compila,
/// "está dando erro" é diagnosticável olhando em volta. A Fase 4 o entrega a
/// outras pessoas, e a partir daí a mesma frase não diz nada: não se sabe a
/// versão, se a placa foi encontrada, se o modelo chegou a ser baixado, nem para
/// onde as gravações estão indo. Este objeto responde as quatro de uma vez.
/// </para>
/// <para>
/// Ele só <b>lê</b>. Nada aqui conserta nada, e nada aqui pode derrubar a tela
/// de onde é chamado: cada campo que depende do mundo (a placa, o disco) cai
/// para um valor honesto quando o mundo não responde.
/// </para>
/// <para>
/// <b>Nada de identificável vai junto</b> — nem nome de reunião, nem cliente,
/// nem participante. O bloco é feito para ser colado num chat, e um bloco que
/// vaza nome de cliente é um bloco que ninguém pode colar.
/// </para>
/// </remarks>
public sealed class Diagnostico
{
    /// <summary>
    /// O nome do produto, para a tela não precisar saber qual é.
    /// </summary>
    /// <remarks>
    /// Calculado, não recebido: a marca mora no <see cref="Nucleo.Marca"/>, e
    /// mandá-la junto do diagnóstico é o que evita uma segunda cópia dela no
    /// JavaScript. Ver <c>configuracoes.js</c>.
    /// </remarks>
    [JsonPropertyName("marca")] public string Marca => Nucleo.Marca.Nome;

    [JsonPropertyName("versao")] public required string Versao { get; init; }
    [JsonPropertyName("windows")] public required string Windows { get; init; }

    /// <summary>A placa NVIDIA e o driver, ou nulo quando não há.</summary>
    /// <remarks>
    /// Nulo é informação, e é a mais importante deste bloco: a Fase 4 entrega
    /// só o caminho CUDA, então "sem placa" explica sozinho por que uma reunião
    /// de uma hora levou a tarde inteira.
    /// </remarks>
    [JsonPropertyName("placa")] public string? Placa { get; init; }

    /// <summary>O que falta nos motores, ou nulo quando está tudo no lugar.</summary>
    [JsonPropertyName("motores")] public string? Motores { get; init; }

    /// <summary>Os pacotes que estão inteiros em disco, por id.</summary>
    [JsonPropertyName("modelos")] public required List<string> Modelos { get; init; }

    /// <summary>O que está escolhido hoje: ASR, diarização e ata.</summary>
    [JsonPropertyName("escolhidos")] public required List<string> Escolhidos { get; init; }

    [JsonPropertyName("pasta_das_gravacoes")] public required string PastaDasGravacoes { get; init; }

    /// <summary>Espaço livre no disco das gravações, em GB. -1 quando não deu para ler.</summary>
    [JsonPropertyName("disco_livre_gb")] public double DiscoLivreGb { get; init; }

    /// <summary>
    /// O bloco pronto para colar, calculado aqui e não na página.
    /// </summary>
    /// <remarks>
    /// Vai serializado junto dos campos, e não montado em JavaScript, porque é
    /// ele que se cola numa mensagem: montá-lo dos dois lados garantiria que um
    /// dia o texto colado e a tela discordassem.
    /// </remarks>
    [JsonPropertyName("texto")] public string Texto => ComoTexto();

    /// <summary>
    /// A versão publicada, sem os metadados que o SDK pendura atrás de <c>+</c>.
    /// </summary>
    /// <remarks>
    /// O <c>AssemblyInformationalVersion</c> vem como <c>0.1.0+&lt;sha&gt;</c>
    /// quando o SourceLink está ligado. O que se mostra a quem usa é a parte
    /// antes do <c>+</c>; o sha não ajuda quem só quer dizer qual versão tem.
    /// </remarks>
    public static string VersaoDoApp()
    {
        string? informacional = Assembly.GetEntryAssembly()
            ?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;

        if (informacional is { Length: > 0 })
            return informacional.Split('+')[0];

        return Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "?";
    }

    /// <summary>
    /// A placa NVIDIA, perguntada ao <c>nvidia-smi</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// É a única detecção de GPU do app, e é deliberadamente a mais barata: o
    /// <c>nvidia-smi</c> vem com o driver, então achá-lo já responde a pergunta
    /// que importa — <b>tem driver NVIDIA nesta máquina?</b> —, e não achá-lo
    /// responde a mesma coisa pelo outro lado. Nada aqui carrega CUDA nem
    /// reserva VRAM; quem faz isso é o motor, no processo dele.
    /// </para>
    /// <para>
    /// O que ela <b>não</b> prova: que o motor vai conseguir usar a placa. Um
    /// driver velho demais para o build de CUDA embarcado aparece aqui como
    /// placa presente e falha lá dentro — foi o que aconteceu com o llama.cpp
    /// 13.3 (ver FASE3-HANDOFF §4). Por isso o campo diz o que viu, e não
    /// promete o que vai acontecer.
    /// </para>
    /// </remarks>
    public static string? PlacaNvidia()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return null;

        try
        {
            var processo = Process.Start(new ProcessStartInfo
            {
                FileName = "nvidia-smi",
                Arguments = "--query-gpu=name,driver_version --format=csv,noheader",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                // Sem isto, um console preto pisca na cara de quem abre os
                // Ajustes. O app já paga essa atenção nos sidecars.
                CreateNoWindow = true,
            });
            if (processo is null) return null;

            string saida = processo.StandardOutput.ReadToEnd();
            // Um nvidia-smi que trava não pode segurar a tela: cinco segundos é
            // muito mais do que ele leva, e desistir é uma resposta melhor do
            // que uma janela parada.
            if (!processo.WaitForExit(5_000))
            {
                try { processo.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
                return null;
            }
            if (processo.ExitCode != 0) return null;

            // "NVIDIA GeForce RTX 2060, 595.97" → "NVIDIA GeForce RTX 2060 (driver 595.97)"
            string primeira = saida.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                                   .FirstOrDefault()?.Trim() ?? "";
            if (primeira.Length == 0) return null;

            var partes = primeira.Split(',', 2);
            return partes.Length == 2
                ? $"{partes[0].Trim()} (driver {partes[1].Trim()})"
                : primeira;
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or IOException
                                       or InvalidOperationException)
        {
            // Sem nvidia-smi no PATH é o caso comum de máquina sem NVIDIA, e não
            // é erro nenhum: é a resposta.
            return null;
        }
    }

    /// <summary>Coleta tudo. Custa uma chamada ao nvidia-smi; não chame em laço.</summary>
    public static Diagnostico Coletar(ConfiguracoesDoApp config, string pastaDasGravacoes)
    {
        var catalogo = Catalogo.Listar(config);

        return new Diagnostico
        {
            Versao = VersaoDoApp(),
            Windows = RuntimeInformation.OSDescription,
            Placa = PlacaNvidia(),
            Motores = Nucleo.Motores.AoLadoDoExecutavel().OQueFalta(),
            Modelos = catalogo.Where(i => i.Estado == "instalado")
                              .Select(i => i.Pacote.Id).ToList(),
            Escolhidos = [config.ModeloPadrao, config.DiarizacaoPadrao, config.ModeloDeAta],
            PastaDasGravacoes = pastaDasGravacoes,
            DiscoLivreGb = LivreEmGb(pastaDasGravacoes),
        };
    }

    /// <summary>O bloco que se cola numa mensagem.</summary>
    /// <remarks>
    /// Texto e não JSON: quem recebe é uma pessoa lendo num chat, e um JSON
    /// colado ali vira uma parede que ninguém lê. Os campos são os mesmos.
    /// </remarks>
    public string ComoTexto()
    {
        var b = new StringBuilder();
        b.Append(Marca).Append(' ').Append(Versao).Append('\n');
        b.Append(Windows).Append('\n');
        // "segundo o Windows" não é preciosismo: esta linha vem do nvidia-smi,
        // que responde pela presença do driver. Quem decide o dispositivo da
        // transcrição é o torch, que precisa também das DLLs de CUDA
        // alcançáveis — e os dois discordaram na máquina de um usuário em
        // 18/08/2026, com a tela dizendo "RTX 4050" enquanto o modelo rodava na
        // CPU. Um bloco de diagnóstico que esconde de onde veio o dado manda
        // quem lê procurar no lugar errado.
        b.Append("placa (segundo o Windows): ")
         .Append(Placa ?? "nenhuma NVIDIA encontrada — vai rodar em CPU").Append('\n');
        b.Append("motores: ").Append(Motores ?? "no lugar").Append('\n');
        b.Append("modelos instalados: ")
         .Append(Modelos.Count > 0 ? string.Join(", ", Modelos) : "nenhum").Append('\n');
        b.Append("em uso: ").Append(string.Join(", ", Escolhidos)).Append('\n');
        // O caminho do log fecha o bloco de propósito: é o que a pessoa abre
        // quando a foto não basta — e a foto não bastou em 18/08/2026.
        b.Append("registro: ").Append(Registro.Caminho).Append('\n');
        b.Append("gravações: ").Append(PastaDasGravacoes);
        if (DiscoLivreGb >= 0) b.Append(" (").Append(DiscoLivreGb.ToString("0.#")).Append(" GB livres)");
        return b.ToString();
    }

    private static double LivreEmGb(string pasta)
    {
        try
        {
            string? raiz = Path.GetPathRoot(Path.GetFullPath(pasta));
            if (raiz is null or "") return -1;
            return new DriveInfo(raiz).AvailableFreeSpace / 1_000_000_000.0;
        }
        catch (Exception e) when (e is IOException or ArgumentException
                                       or UnauthorizedAccessException)
        {
            return -1;
        }
    }
}
