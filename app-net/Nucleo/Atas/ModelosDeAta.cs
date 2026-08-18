using System.Reflection;

namespace MeetingApp.Nucleo.Atas;

/// <summary>
/// Um tipo de reunião: as instruções que definem como a ata dele é escrita.
/// </summary>
/// <param name="Id">"sprint", "cliente-update" — o nome do arquivo, sem extensão.</param>
/// <param name="Nome">Como a tela chama: "Sprint", "Update com cliente".</param>
/// <param name="Texto">O Markdown inteiro da referência, como escrito.</param>
/// <param name="DoUsuario">Veio da pasta do perfil, e não de dentro do executável.</param>
public sealed record ModeloDeAta(string Id, string Nome, string Texto, bool DoUsuario)
{
    /// <summary>
    /// As seções que o esqueleto da referência pede, na ordem.
    /// </summary>
    /// <remarks>
    /// Sai do bloco <c>```markdown</c> da referência, que é onde cada tipo
    /// desenha a ata dele. Serve para pedir ao modelo e para conferir o que
    /// voltou — não para gerar um esquema por tipo, que é o que faria
    /// customizar exigir escrever JSON Schema (ATA.md §1).
    /// </remarks>
    public IReadOnlyList<string> Secoes
    {
        get
        {
            int inicio = Texto.IndexOf("```markdown", StringComparison.Ordinal);
            if (inicio < 0) return [];
            int fim = Texto.IndexOf("```", inicio + 11, StringComparison.Ordinal);
            if (fim < 0) return [];

            var titulos = new List<string>();
            foreach (string linha in Texto[(inicio + 11)..fim].Split('\n'))
            {
                string t = linha.TrimEnd();
                if (t.StartsWith("## ", StringComparison.Ordinal))
                    titulos.Add(t[3..].Trim());
            }
            return titulos;
        }
    }
}

/// <summary>
/// Os tipos de reunião disponíveis: os embutidos e os do usuário.
/// </summary>
/// <remarks>
/// <para>
/// <b>Embutido é a base; o do usuário ganha.</b> As seis referências da skill
/// vão dentro do executável e definem os tipos que o app conhece de fábrica. Um
/// arquivo <c>.md</c> em <c>%USERPROFILE%\.meeting-transcription\atas\</c>
/// substitui o embutido de mesmo nome, e um nome novo vira um tipo novo na tela
/// — sem recompilar nada (ATA.md §1).
/// </para>
/// <para>
/// <b>Nunca se edita o embutido no lugar.</b> Customizar copia para a pasta do
/// perfil; "voltar ao original" apaga a cópia. Editar o recurso embutido faria a
/// próxima versão do app apagar o trabalho do usuário sem avisar.
/// </para>
/// </remarks>
public static class ModelosDeAta
{
    /// <summary>A ordem em que a tela oferece. Não é alfabética: é de uso.</summary>
    private static readonly string[] Embutidos =
        ["cliente-update", "sprint", "trabalho", "kickoff", "resultados", "daily"];

    private static readonly Dictionary<string, string> NomesBonitos = new()
    {
        ["cliente-update"] = "Update com cliente",
        ["sprint"] = "Sprint",
        ["trabalho"] = "Sessão de trabalho",
        ["kickoff"] = "Kickoff",
        ["resultados"] = "Apresentação de resultados",
        ["daily"] = "Daily / status curto",
    };

    public static string PastaDoUsuario => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".meeting-transcription", "atas");

    /// <summary>Todos os tipos, com os do usuário na frente dos embutidos.</summary>
    public static IReadOnlyList<ModeloDeAta> Todos()
    {
        var achados = new Dictionary<string, ModeloDeAta>(StringComparer.OrdinalIgnoreCase);

        foreach (string id in Embutidos)
            if (LerEmbutido(id) is { } texto)
                achados[id] = new ModeloDeAta(id, NomeDe(id), texto, DoUsuario: false);

        foreach (var arquivo in ArquivosDoUsuario())
        {
            string id = Path.GetFileNameWithoutExtension(arquivo);
            try
            {
                achados[id] = new ModeloDeAta(
                    id, NomeDe(id), File.ReadAllText(arquivo), DoUsuario: true);
            }
            catch (IOException)
            {
                // Um arquivo ilegível na pasta do usuário não pode esconder os
                // outros tipos: fica valendo o embutido, se houver.
            }
        }

        // Os embutidos primeiro, na ordem de uso; os inventados pelo usuário
        // depois, em ordem alfabética — eles não têm ordem canônica.
        return
        [
            .. Embutidos.Where(achados.ContainsKey).Select(id => achados[id]),
            .. achados.Values.Where(m => !Embutidos.Contains(m.Id)).OrderBy(m => m.Id),
        ];
    }

    public static ModeloDeAta? Buscar(string? id) =>
        id is { Length: > 0 } ? Todos().FirstOrDefault(
            m => string.Equals(m.Id, id, StringComparison.OrdinalIgnoreCase)) : null;

    /// <summary>
    /// As regras comuns a todos os tipos, recortadas do SKILL.md.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Recorta a partir do "Passo 2" e joga fora o Passo 1 — que manda
    /// classificar o tipo e perguntar ao usuário quando estiver ambíguo. Aqui
    /// quem classificou foi o usuário, na tela, e deixar o modelo reclassificar
    /// é dar a ele a chance de contrariar quem sabe (ATA.md §1).
    /// </para>
    /// <para>
    /// <b>O recorte é por título, e um teste o protege.</b> Se alguém reescrever
    /// o SKILL.md e o título sumir, a suíte reprova — em vez de o app mandar ao
    /// modelo um prompt sem as regras que impedem ata errada.
    /// </para>
    /// </remarks>
    public static string RegrasComuns()
    {
        string skill = LerEmbutido("SKILL")
            ?? throw new InvalidOperationException("o SKILL.md não está embutido no app");

        const string marca = "## Passo 2: Regras comuns";
        int corte = skill.IndexOf(marca, StringComparison.Ordinal);
        if (corte < 0)
            throw new InvalidOperationException(
                $"o SKILL.md mudou: não achei \"{marca}\". "
                + "Ver ModelosDeAta.RegrasComuns e ATA.md §1.");

        // O recorte termina no Passo 3, e isso conserta um defeito medido.
        //
        // O SKILL.md é escrito para o Claude num chat: o Passo 3 manda escrever
        // o cabeçalho da ata ("Toda ata começa assim", com um modelo em
        // Markdown) e o Passo 4 manda entregar Markdown na conversa e oferecer
        // conversão para .docx. Nenhum dos dois vale aqui — **quem escreve o
        // cabeçalho é o RedatorDeAta, e a saída é JSON preso a um esquema**.
        //
        // Mandá-los assim mesmo produziu o pior defeito da ata, e nos dois
        // modelos testados em 17/08/2026: o modelo escrevia uma ata inteira em
        // Markdown, com cabeçalho, **dentro de uma seção** — e o redator
        // acrescentava a dele em volta. Saía documento duplicado, seção de 2.500
        // caracteres, e as duas falhas por estourar o limite de saída.
        //
        // Quando dois modelos independentes erram igual, a causa é o prompt.
        const string fim = "## Passo 3";
        int ate = skill.IndexOf(fim, corte, StringComparison.Ordinal);

        string regras = (ate < 0 ? skill[corte..] : skill[corte..ate]).TrimEnd();

        return regras.Replace(
            "## Passo 2: Regras comuns a todos os tipos", "## Regras");
    }

    private static IEnumerable<string> ArquivosDoUsuario()
    {
        try
        {
            return Directory.Exists(PastaDoUsuario)
                ? Directory.EnumerateFiles(PastaDoUsuario, "*.md").Order()
                : [];
        }
        catch (IOException)
        {
            return [];
        }
    }

    private static string NomeDe(string id) =>
        NomesBonitos.TryGetValue(id, out string? nome) ? nome : id.Replace('-', ' ');

    /// <remarks>
    /// Os recursos entram pelo .csproj com o mesmo cuidado dos ícones da
    /// bandeja: barra normal no Include, senão o MSBuild no Linux não expande o
    /// glob e o app publica sem eles — em silêncio.
    /// </remarks>
    private static string? LerEmbutido(string id)
    {
        var assembly = typeof(ModelosDeAta).Assembly;
        using var fluxo = assembly.GetManifestResourceStream($"MeetingApp.atas.{id}.md");
        if (fluxo is null) return null;
        using var leitor = new StreamReader(fluxo);
        return leitor.ReadToEnd();
    }

    /// <summary>Copia um embutido para a pasta do usuário, para ele editar.</summary>
    public static string Customizar(string id)
    {
        var modelo = Buscar(id) ?? throw new InvalidOperationException($"tipo desconhecido: {id}");
        Directory.CreateDirectory(PastaDoUsuario);
        string destino = Path.Combine(PastaDoUsuario, $"{id}.md");
        if (!File.Exists(destino)) File.WriteAllText(destino, modelo.Texto);
        return destino;
    }

    /// <summary>Apaga a versão do usuário, voltando ao embutido.</summary>
    public static void VoltarAoOriginal(string id)
    {
        string arquivo = Path.Combine(PastaDoUsuario, $"{id}.md");
        if (File.Exists(arquivo)) File.Delete(arquivo);
    }
}
