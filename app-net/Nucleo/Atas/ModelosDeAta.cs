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
    /// <summary>A linha que liga a ata por tema num tipo (ATA-5).</summary>
    /// <remarks>
    /// É texto do arquivo, e não uma lista no código, porque o tipo é
    /// personalizável (ATA.md §1): um tipo do usuário liga o agrupamento
    /// escrevendo a linha. O modelo nunca a vê — <see cref="TextoParaPrompt"/>
    /// a tira.
    /// </remarks>
    public const string Marca = "<!-- ata: itens-por-secao -->";

    /// <summary>Os itens moram dentro das seções, quando o formato é por tema.</summary>
    public bool ItensPorSecao => Texto.Contains(Marca, StringComparison.Ordinal);

    /// <summary>O texto sem a linha da marca, para tudo o que vai ao modelo.</summary>
    private string SemMarca =>
        Texto.Replace(Marca + "\r\n", "").Replace(Marca + "\n", "").Replace(Marca, "");

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

    /// <summary>
    /// As seções que o <b>app</b> escreve a partir dos campos do JSON.
    /// </summary>
    /// <remarks>
    /// Toda referência de tipo lista algumas delas no esqueleto, porque foi
    /// escrita para o Claude num chat, onde quem escreve o documento inteiro é o
    /// modelo. Aqui não: quem escreve é o <see cref="RedatorDeAta"/>, a partir
    /// de <c>decisoes</c>, <c>acoes</c>, <c>pontos_em_aberto</c>, <c>riscos</c>
    /// e <c>observacoes</c>.
    /// </remarks>
    private static readonly HashSet<string> DoRedator = new(StringComparer.OrdinalIgnoreCase)
    {
        "Resumo", "Decisões", "Pendências", "Ações", "Ações imediatas",
        "Próximos passos", "Pontos em aberto", "Riscos e alertas",
        "Riscos identificados", "Observações sobre a transcrição",
    };

    /// <summary>
    /// O esqueleto como ele deve chegar ao modelo: só o corpo específico do tipo.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Medido em 17/08/2026, depois de consertar a ata duplicada e ela voltar
    /// menor: o modelo continuava escrevendo uma seção <c>Pendências</c> dentro
    /// de <c>secoes</c>, <b>além</b> de preencher <c>acoes</c> — e o redator
    /// escrevia a dele em seguida. Duas listas de pendências na mesma ata.
    /// </para>
    /// <para>
    /// A causa era a mesma de antes: o esqueleto mostrava a seção, e o modelo
    /// obedecia. Regra em texto ("não repita") não vence exemplo em estrutura —
    /// então a seção sai do exemplo.
    /// </para>
    /// <para>
    /// <b>"Decisões técnicas" fica</b>, e a distinção importa: ela não é a lista
    /// de decisões, é o raciocínio por trás delas — o "por quê", as alternativas
    /// descartadas e as implicações, que é justamente o que dá valor à ata de
    /// sessão de trabalho.
    /// </para>
    /// </remarks>
    public string TextoParaPrompt()
    {
        string texto = SemMarca;
        int inicio = texto.IndexOf("```markdown", StringComparison.Ordinal);
        if (inicio < 0) return texto;
        int fim = texto.IndexOf("```", inicio + 11, StringComparison.Ordinal);
        if (fim < 0) return texto;

        var mantidas = new List<string>();
        bool copiando = true;

        foreach (string linha in texto[(inicio + 11)..fim].Split('\n'))
        {
            string t = linha.TrimEnd();

            // O cabeçalho do documento e a linha de participantes saem sempre:
            // quem os escreve é o redator, e mostrá-los convidava o modelo a
            // escrever uma ata inteira dentro de um campo.
            if (t.StartsWith("# Ata", StringComparison.Ordinal)) { copiando = false; continue; }
            if (t.StartsWith("**Participantes:**", StringComparison.Ordinal)) continue;

            if (t.StartsWith("## ", StringComparison.Ordinal))
                copiando = !DoRedator.Contains(t[3..].Trim());

            if (copiando) mantidas.Add(linha);
        }

        string corpo = string.Join('\n', mantidas).Trim('\n');
        return texto[..(inicio + 11)] + "\n" + corpo + "\n" + texto[fim..];
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
    /// <param name="pasta">A pasta dos modelos do usuário; nula, a do perfil.</param>
    public static IReadOnlyList<ModeloDeAta> Todos(string? pasta = null)
    {
        var achados = new Dictionary<string, ModeloDeAta>(StringComparer.OrdinalIgnoreCase);

        foreach (string id in Embutidos)
            if (LerEmbutido(id) is { } texto)
                achados[id] = new ModeloDeAta(id, NomeDe(id), texto, DoUsuario: false);

        foreach (var arquivo in ArquivosDoUsuario(pasta ?? PastaDoUsuario))
        {
            string id = Path.GetFileNameWithoutExtension(arquivo);
            try
            {
                string texto = File.ReadAllText(arquivo);
                achados[id] = new ModeloDeAta(id, NomeDe(id, texto), texto, DoUsuario: true);
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

    public static ModeloDeAta? Buscar(string? id, string? pasta = null) =>
        id is { Length: > 0 } ? Todos(pasta).FirstOrDefault(
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

    private static IEnumerable<string> ArquivosDoUsuario(string pasta)
    {
        try
        {
            return Directory.Exists(pasta)
                ? Directory.EnumerateFiles(pasta, "*.md").Order()
                : [];
        }
        catch (IOException)
        {
            return [];
        }
    }

    /// <remarks>
    /// Os do app têm nome fixo. O tipo criado pelo usuário tira o nome do título
    /// do arquivo ("# Ata — Comitê de Dados"), que é onde a tela o escreve — o
    /// nome do arquivo perdeu os acentos e as maiúsculas.
    /// </remarks>
    private static string NomeDe(string id, string? texto = null)
    {
        if (NomesBonitos.TryGetValue(id, out string? nome)) return nome;
        if (texto is not null && Titulo(texto) is { Length: > 0 } titulo) return titulo;
        return id.Replace('-', ' ');
    }

    /// <summary>O "# Ata — X" do topo do arquivo, antes do bloco do esqueleto.</summary>
    private static string? Titulo(string texto)
    {
        int bloco = texto.IndexOf("```markdown", StringComparison.Ordinal);
        foreach (string cru in (bloco < 0 ? texto : texto[..bloco]).Split('\n'))
        {
            string linha = cru.Trim();
            if (!linha.StartsWith("# Ata", StringComparison.Ordinal)) continue;
            string resto = linha[5..].TrimStart(' ', '—', '-', ':').Trim();
            return resto.Length > 0 ? resto : null;
        }
        return null;
    }

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

    /// <summary>
    /// Apaga a versão do usuário: volta ao do app, ou apaga o tipo que ele criou.
    /// </summary>
    public static void VoltarAoOriginal(string id, string? pasta = null)
    {
        pasta ??= PastaDoUsuario;
        string arquivo = Path.Combine(pasta, $"{id}.md");
        if (File.Exists(arquivo)) File.Delete(arquivo);
        var marcas = LerMarcas(pasta);
        if (marcas.Remove(id)) GravarMarcas(pasta, marcas);
    }

    // ───────────────────────────── editar em Ajustes › Atas (parte 2 da ATA-5)

    /// <summary>O tipo vem de dentro do app (e não só da pasta do usuário).</summary>
    public static bool EhDoApp(string id) =>
        Embutidos.Contains(id, StringComparer.OrdinalIgnoreCase);

    /// <summary>O texto do tipo como o app o traz, sem a cópia do usuário.</summary>
    public static string? Original(string id) => EhDoApp(id) ? LerEmbutido(id) : null;

    /// <summary>Uma seção do esqueleto, e quem a escreve.</summary>
    /// <param name="DoApp">
    /// O app a escreve a partir de um campo (Resumo, Decisões, Pendências…); a
    /// IA não a vê como seção. Ver <see cref="ModeloDeAta.TextoParaPrompt"/>.
    /// </param>
    public sealed record SecaoDoModelo(string Titulo, bool DoApp);

    /// <summary>As seções que a ata do tipo terá, na ordem, e quem escreve cada uma.</summary>
    public static IReadOnlyList<SecaoDoModelo> Resumo(ModeloDeAta modelo) =>
        [.. modelo.Secoes.Select(t => new SecaoDoModelo(t, RedatorDeAta.NomeCanonico(t) is not null))];

    /// <summary>
    /// O que falta no texto para ele servir de modelo, ou nulo se não falta nada.
    /// </summary>
    /// <remarks>
    /// Sem o bloco do esqueleto a IA não sabe que seções escrever, e a ata sai
    /// só com o que o app escreve sozinho — em silêncio. Melhor recusar ao
    /// salvar, quando quem escreveu ainda está olhando.
    /// </remarks>
    public static string? OQueFalta(string texto)
    {
        const string falta = "o modelo precisa do bloco ```markdown com o esqueleto da ata, "
                             + "fechado com ```, e pelo menos uma seção \"## \" dentro dele";
        int inicio = texto.IndexOf("```markdown", StringComparison.Ordinal);
        if (inicio < 0 || texto.IndexOf("```", inicio + 11, StringComparison.Ordinal) < 0)
            return falta;
        return new ModeloDeAta("x", "x", texto, DoUsuario: true).Secoes.Count == 0 ? falta : null;
    }

    /// <summary>Grava o modelo na pasta do usuário — personalizando, se for do app.</summary>
    /// <exception cref="InvalidOperationException">O texto não tem a estrutura.</exception>
    public static void Salvar(string id, string texto, string? pasta = null)
    {
        if (OQueFalta(texto) is { } falta) throw new InvalidOperationException(falta);
        pasta ??= PastaDoUsuario;
        Directory.CreateDirectory(pasta);
        string arquivo = Path.Combine(pasta, $"{id}.md");

        // A marca do original é a da hora de personalizar, e não a de cada
        // salvar: a cópia continua sendo daquela versão do app.
        if (EhDoApp(id) && !File.Exists(arquivo) && Original(id) is { } original)
        {
            var marcas = LerMarcas(pasta);
            marcas[id] = Marca(original);
            GravarMarcas(pasta, marcas);
        }

        string tmp = arquivo + ".tmp";
        File.WriteAllText(tmp, texto);
        File.Move(tmp, arquivo, overwrite: true);
    }

    /// <summary>Um tipo novo, copiado de outro, com o nome no título e no arquivo.</summary>
    /// <returns>O id do tipo novo — o nome do arquivo.</returns>
    public static string Criar(string baseId, string nome, string? pasta = null)
    {
        pasta ??= PastaDoUsuario;
        string limpo = nome.Trim();
        string id = Identificador(limpo);
        if (id.Length == 0)
            throw new InvalidOperationException("dê ao tipo um nome com letras ou números");
        if (Buscar(id, pasta) is not null)
            throw new InvalidOperationException($"já existe um tipo chamado \"{limpo}\"");
        var base_ = Buscar(baseId, pasta)
            ?? throw new InvalidOperationException($"tipo desconhecido: {baseId}");

        Salvar(id, ComTitulo(base_.Texto, limpo), pasta);
        return id;
    }

    /// <summary>
    /// A cópia personalizada é de um original que o app já trocou.
    /// </summary>
    /// <remarks>
    /// Sem isto, uma versão nova do app muda o modelo — como a ata por tema
    /// mudou o update com cliente em 08/10/2026 — e quem personalizou fica na
    /// versão velha sem saber. Sem marca guardada (personalizado antes desta
    /// versão) não se afirma nada.
    /// </remarks>
    public static bool OriginalMudou(string id, string? pasta = null)
    {
        pasta ??= PastaDoUsuario;
        if (!EhDoApp(id) || !File.Exists(Path.Combine(pasta, $"{id}.md"))) return false;
        if (Original(id) is not { } original) return false;
        return LerMarcas(pasta).TryGetValue(id, out string? marca) && marca != Marca(original);
    }

    /// <summary>"Comitê de Dados" → "comite-de-dados".</summary>
    private static string Identificador(string nome)
    {
        string sem = new string(nome.ToLowerInvariant().Normalize(System.Text.NormalizationForm.FormD)
            .Where(c => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c)
                        != System.Globalization.UnicodeCategory.NonSpacingMark).ToArray());
        var partes = System.Text.RegularExpressions.Regex.Split(sem, "[^a-z0-9]+")
            .Where(x => x.Length > 0);
        return string.Join('-', partes);
    }

    /// <summary>Troca o "# Ata — …" do topo, antes do bloco do esqueleto.</summary>
    private static string ComTitulo(string texto, string nome)
    {
        int bloco = texto.IndexOf("```markdown", StringComparison.Ordinal);
        var linhas = texto.Split('\n');
        int fimDoTopo = bloco < 0 ? linhas.Length : texto[..bloco].Split('\n').Length;
        for (int i = 0; i < fimDoTopo && i < linhas.Length; i++)
        {
            if (!linhas[i].TrimStart().StartsWith("# Ata", StringComparison.Ordinal)) continue;
            linhas[i] = $"# Ata — {nome}" + (linhas[i].EndsWith('\r') ? "\r" : "");
            return string.Join('\n', linhas);
        }
        return $"# Ata — {nome}\n\n" + texto;
    }

    private static string Marca(string texto) => Convert.ToHexString(
        System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(texto.Replace("\r\n", "\n")))).ToLowerInvariant();

    /// <remarks>
    /// Texto, uma linha por tipo ("id\tmarca"), e não JSON: não precisa de
    /// contexto de serialização sob o PublishTrimmed, e não aparece como tipo
    /// porque a lista só lê *.md.
    /// </remarks>
    private static Dictionary<string, string> LerMarcas(string pasta)
    {
        var marcas = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string arquivo = Path.Combine(pasta, ".originais");
        try
        {
            if (!File.Exists(arquivo)) return marcas;
            foreach (string linha in File.ReadAllLines(arquivo))
                if (linha.Split('\t') is [var id, var marca] && id.Length > 0)
                    marcas[id] = marca.Trim();
        }
        catch (IOException) { }
        return marcas;
    }

    private static void GravarMarcas(string pasta, Dictionary<string, string> marcas)
    {
        Directory.CreateDirectory(pasta);
        File.WriteAllLines(Path.Combine(pasta, ".originais"),
                           marcas.OrderBy(m => m.Key).Select(m => $"{m.Key}\t{m.Value}"));
    }
}
