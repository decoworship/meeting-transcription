using System.Text.Json;
using System.Text.Json.Serialization;
using MeetingApp.App.Nativo;
using MeetingApp.Nucleo;
using MeetingApp.Sidecar;

namespace MeetingApp.App;

/// <summary>
/// O que a página pede ao núcleo, e o que o núcleo devolve.
/// </summary>
/// <remarks>
/// Mesma forma do contrato com os motores (docs/SIDECAR.md): JSON com um campo
/// <c>op</c> e um <c>id</c> que volta na resposta. Ter um só formato de
/// mensagem no projeto inteiro significa um só jeito de depurar quando algo não
/// chega do outro lado.
/// </remarks>
internal sealed class Pedido
{
    [JsonPropertyName("id")] public int Id { get; init; }
    [JsonPropertyName("op")] public string? Op { get; init; }
    [JsonPropertyName("gravacao")] public string? Gravacao { get; init; }
    [JsonPropertyName("vocabulario")] public string? Vocabulario { get; init; }
    [JsonPropertyName("idioma")] public string? Idioma { get; init; }
    [JsonPropertyName("modelo")] public string? Modelo { get; init; }
    [JsonPropertyName("cliente")] public string? Cliente { get; init; }
    [JsonPropertyName("projeto")] public string? Projeto { get; init; }
    [JsonPropertyName("data")] public string? Data { get; init; }
    [JsonPropertyName("prefs")] public PreferenciasDoProjeto? Prefs { get; init; }

    /// <summary>Rótulo do falante e o nome dado a ele, para aprender a voz.</summary>
    [JsonPropertyName("falante")] public string? Falante { get; init; }
    [JsonPropertyName("nome")] public string? Nome { get; init; }

    /// <summary>"txt", "srt", "vtt" ou "docx".</summary>
    [JsonPropertyName("formato")] public string? Formato { get; init; }
    [JsonPropertyName("com_falantes")] public bool? ComFalantes { get; init; }

    /// <summary>Também salvar uma cópia numa pasta escolhida pelo usuário.</summary>
    [JsonPropertyName("copiar")] public bool? Copiar { get; init; }

    [JsonPropertyName("config")] public ConfiguracoesDoApp? Config { get; init; }

    /// <summary>A transcrição inteira, como a página a tem depois de editada.</summary>
    [JsonPropertyName("conteudo")] public string? Conteudo { get; init; }

    /// <summary>Qual pessoa e qual amostra dela, na tela de vozes.</summary>
    [JsonPropertyName("pessoa")] public string? Pessoa { get; init; }
    [JsonPropertyName("indice")] public int? Indice { get; init; }

    /// <summary>De onde o diálogo de pasta começa.</summary>
    [JsonPropertyName("pasta")] public string? Pasta { get; init; }
}

internal sealed class Resposta
{
    [JsonPropertyName("id")] public int Id { get; init; }

    /// <summary>
    /// <c>"progresso"</c> numa mensagem intermediária; ausente na final.
    /// </summary>
    /// <remarks>
    /// É o que permite uma operação longa reportar andamento sem inventar um
    /// segundo canal: a página só resolve a promessa quando o tipo não vem.
    /// </remarks>
    [JsonPropertyName("tipo")] public string? Tipo { get; init; }
    [JsonPropertyName("etapa")] public string? Etapa { get; init; }
    [JsonPropertyName("fracao")] public double? Fracao { get; init; }
    [JsonPropertyName("texto")] public string? Texto { get; init; }

    [JsonPropertyName("erro")] public string? Erro { get; init; }
    [JsonPropertyName("gravacoes")] public List<GravacaoResumo>? Gravacoes { get; init; }
    [JsonPropertyName("transcricao")] public string? Transcricao { get; init; }

    /// <summary>Onde o arquivo exportado foi parar.</summary>
    [JsonPropertyName("arquivo")] public string? Arquivo { get; init; }

    /// <summary>A cópia, quando pedida.</summary>
    [JsonPropertyName("copia")] public string? Copia { get; init; }

    [JsonPropertyName("config")] public ConfiguracoesDoApp? Config { get; init; }

    /// <summary>O que aconteceu ao aprender uma voz, para a UI poder dizer.</summary>
    [JsonPropertyName("voz")] public string? Voz { get; init; }

    /// <summary>Cliente → seus projetos. A UI precisa dos dois para o cadastro.</summary>
    [JsonPropertyName("clientes")] public Dictionary<string, List<string>>? Clientes { get; init; }
    [JsonPropertyName("prefs")] public PreferenciasDoProjeto? Prefs { get; init; }

    /// <summary>Os pacotes de modelo com o estado de cada um.</summary>
    [JsonPropertyName("catalogo")] public List<PacoteComEstado>? Catalogo { get; init; }

    /// <summary>A biblioteca de vozes como a tela precisa vê-la.</summary>
    [JsonPropertyName("vozes")] public List<PessoaResumo>? Vozes { get; init; }

    /// <summary>A pasta escolhida no diálogo, ou nulo se foi cancelado.</summary>
    [JsonPropertyName("pasta")] public string? Pasta { get; init; }
}

/// <summary>Uma pessoa conhecida, e as amostras que o sistema guarda dela.</summary>
/// <remarks>
/// O vetor <b>não</b> vem junto de propósito: são 256 floats por amostra que a
/// tela não tem como usar e que só engordariam cada mensagem. O que a tela
/// precisa é da procedência — é ela que permite tocar o trecho, e é ouvindo
/// quatro segundos que uma pessoa julga o que nenhum número mostra.
/// </remarks>
internal sealed class PessoaResumo
{
    [JsonPropertyName("nome")] public required string Nome { get; init; }
    [JsonPropertyName("amostras")] public List<AmostraResumo> Amostras { get; init; } = [];
}

internal sealed class AmostraResumo
{
    /// <summary>A posição dentro do perfil: é por ela que se aprova ou esquece.</summary>
    [JsonPropertyName("indice")] public int Indice { get; init; }
    [JsonPropertyName("criada_em")] public required string CriadaEm { get; init; }
    [JsonPropertyName("duracao_s")] public double DuracaoS { get; init; }
    [JsonPropertyName("gravacao")] public required string Gravacao { get; init; }
    [JsonPropertyName("faixa")] public required string Faixa { get; init; }
    [JsonPropertyName("t0")] public double T0 { get; init; }
    [JsonPropertyName("t1")] public double T1 { get; init; }
    [JsonPropertyName("dispositivo")] public string? Dispositivo { get; init; }
    [JsonPropertyName("quarentena")] public bool Quarentena { get; init; }

    /// <summary>
    /// O caminho do trecho relativo à pasta de vozes, ou nulo se não houver.
    /// </summary>
    /// <remarks>
    /// Relativo, e não absoluto: a página o concatena em <c>vozes.local</c>,
    /// que é a pasta mapeada. Mandar o caminho absoluto obrigaria a tela a
    /// conhecer a estrutura de disco do app.
    /// </remarks>
    [JsonPropertyName("trecho")] public string? Trecho { get; init; }
}

/// <summary>Uma gravação como a lista precisa mostrá-la.</summary>
/// <remarks>
/// Os avisos vêm prontos do núcleo, e não como campos crus para a página
/// interpretar: decidir que 3% de conteúdo útil é um problema é regra de
/// produto, e regra de produto fica de um lado só.
/// </remarks>
internal sealed class GravacaoResumo
{
    [JsonPropertyName("nome")] public required string Nome { get; init; }
    [JsonPropertyName("caminho")] public required string Caminho { get; init; }
    [JsonPropertyName("duracao_s")] public double DuracaoS { get; init; }
    [JsonPropertyName("titulo")] public string? Titulo { get; init; }
    /// <summary>Quantos a agenda listou — convidados, não presentes.</summary>
    [JsonPropertyName("convidados")] public int Convidados { get; init; }
    [JsonPropertyName("transcrita")] public bool Transcrita { get; init; }
    [JsonPropertyName("avisos")] public List<string> Avisos { get; init; } = [];
}

[JsonSourceGenerationOptions(DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(Pedido))]
[JsonSerializable(typeof(Resposta))]
[JsonSerializable(typeof(PacoteComEstado))]
[JsonSerializable(typeof(PessoaResumo))]
[JsonSerializable(typeof(PreferenciasDoProjeto))]
[JsonSerializable(typeof(ConfiguracoesDoApp))]
internal sealed partial class PonteJsonBase : JsonSerializerContext;

internal static class PonteJson
{
    // Acento literal, como em todo JSON deste projeto: o nome do dispositivo e o
    // título da reunião passam por aqui.
    public static readonly PonteJsonBase Default = new(new JsonSerializerOptions
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    });
}

/// <summary>Atende os pedidos da página.</summary>
/// <param name="responder">
/// Envia uma mensagem à página. Chamado mais de uma vez por pedido quando há
/// progresso, e sempre na thread da UI — quem passa o delegate garante isso.
/// </param>
internal sealed class Ponte(string pastaDasGravacoes, Action<string> responder)
{
    private readonly Transcritor _transcritor = new(Motores.AoLadoDoExecutavel());
    private readonly Projetos _projetos = new();

    public async Task AtenderAsync(string mensagem)
    {
        Pedido? p;
        try
        {
            p = JsonSerializer.Deserialize(mensagem, PonteJson.Default.Pedido);
        }
        catch (JsonException e)
        {
            Responder(new Resposta { Id = 0, Erro = $"pedido ilegível: {e.Message}" });
            return;
        }
        if (p is null)
        {
            Responder(new Resposta { Id = 0, Erro = "pedido vazio" });
            return;
        }

        try
        {
            switch (p.Op)
            {
                case "gravacoes":
                    Responder(new Resposta { Id = p.Id, Gravacoes = Listar() });
                    break;

                case "clientes":
                    Responder(new Resposta { Id = p.Id, Clientes = MapaDeClientes() });
                    break;

                case "prefs":
                    Responder(new Resposta
                    {
                        Id = p.Id,
                        Prefs = _projetos.Preferencias(p.Cliente ?? "", p.Projeto ?? ""),
                    });
                    break;

                case "salvar-projeto":
                    // Cliente e projeto novos nascem aqui: digitar um nome
                    // inédito e transcrever é o fluxo do app Python que o
                    // usuário pediu para manter.
                    _projetos.Salvar(p.Cliente ?? "", p.Projeto ?? "",
                                     p.Prefs ?? new PreferenciasDoProjeto());
                    Responder(new Resposta { Id = p.Id, Clientes = MapaDeClientes() });
                    break;

                case "config":
                    Responder(new Resposta { Id = p.Id, Config = ConfiguracoesDoApp.Carregar() });
                    break;

                case "escolher-pasta":
                    Responder(new Resposta { Id = p.Id, Pasta = EscolherPasta(p.Pasta) });
                    break;

                case "renomear-cliente":
                    _projetos.RenomearCliente(p.Cliente ?? "", p.Nome ?? "");
                    Responder(new Resposta { Id = p.Id, Clientes = MapaDeClientes() });
                    break;

                case "renomear-projeto":
                    _projetos.RenomearProjeto(p.Cliente ?? "", p.Projeto ?? "", p.Nome ?? "");
                    Responder(new Resposta { Id = p.Id, Clientes = MapaDeClientes() });
                    break;

                case "apagar-cliente":
                    _projetos.ApagarCliente(p.Cliente ?? "");
                    Responder(new Resposta { Id = p.Id, Clientes = MapaDeClientes() });
                    break;

                case "apagar-projeto":
                    _projetos.ApagarProjeto(p.Cliente ?? "", p.Projeto ?? "");
                    Responder(new Resposta { Id = p.Id, Clientes = MapaDeClientes() });
                    break;

                case "apagar-gravacao":
                    ApagarGravacao(p.Gravacao);
                    Responder(new Resposta { Id = p.Id, Gravacoes = Listar() });
                    break;

                case "catalogo":
                    Responder(new Resposta
                    {
                        Id = p.Id,
                        Catalogo = Catalogo.Listar(ConfiguracoesDoApp.Carregar()),
                    });
                    break;

                case "baixar-pacote":
                    await BaixarPacoteAsync(p);
                    break;

                case "remover-pacote":
                    RemoverPacote(p.Modelo);
                    Responder(new Resposta
                    {
                        Id = p.Id,
                        Catalogo = Catalogo.Listar(ConfiguracoesDoApp.Carregar()),
                    });
                    break;

                case "vozes":
                    Responder(new Resposta { Id = p.Id, Vozes = VozesConhecidas() });
                    break;

                case "aprovar-voz":
                    new Vozes().Aprovar(p.Pessoa ?? "", p.Indice ?? -1);
                    Responder(new Resposta { Id = p.Id, Vozes = VozesConhecidas() });
                    break;

                case "esquecer-voz":
                    new Vozes().Esquecer(p.Pessoa ?? "", p.Indice ?? -1);
                    Responder(new Resposta { Id = p.Id, Vozes = VozesConhecidas() });
                    break;

                case "salvar-config":
                    p.Config?.Salvar();
                    Responder(new Resposta { Id = p.Id, Config = ConfiguracoesDoApp.Carregar() });
                    break;

                case "exportar":
                {
                    var (arquivo, copia) = Exportar(p);
                    Responder(new Resposta { Id = p.Id, Arquivo = arquivo, Copia = copia });
                    break;
                }

                case "aprender-voz":
                    await AprenderVozAsync(p);
                    break;

                case "salvar-transcricao":
                    SalvarTranscricao(p.Gravacao, p.Conteudo);
                    Responder(new Resposta { Id = p.Id });
                    break;

                case "transcricao":
                    Responder(new Resposta { Id = p.Id, Transcricao = LerTranscricao(p.Gravacao) });
                    break;

                case "transcrever":
                    await TranscreverAsync(p);
                    break;

                default:
                    Responder(new Resposta { Id = p.Id, Erro = $"operação desconhecida: {p.Op}" });
                    break;
            }
        }
        catch (Exception e)
        {
            // A página precisa poder mostrar o erro; derrubar a janela por causa
            // de uma pasta ilegível seria pior que a falha original.
            Responder(new Resposta { Id = p.Id, Erro = e.Message });
        }
    }

    /// <summary>Roda o pipeline, reportando andamento pelo mesmo id.</summary>
    private async Task TranscreverAsync(Pedido p)
    {
        if (p.Gravacao is not { Length: > 0 } pasta)
        {
            Responder(new Resposta { Id = p.Id, Erro = "sem gravação" });
            return;
        }

        // O pipeline é pesado e bloquearia a thread da UI, que é a mesma que
        // desenha a janela: sem isto a barra de progresso congelaria justamente
        // enquanto há progresso a mostrar.
        var resultado = await Task.Run(() => _transcritor.ExecutarAsync(
            pasta, p.Vocabulario, p.Idioma,
            modelo: p.Modelo, cliente: p.Cliente, projeto: p.Projeto,
            progresso: e => Responder(new Resposta
            {
                Id = p.Id,
                Tipo = "progresso",
                Etapa = e.Etapa,
                Fracao = e.Fracao,
                Texto = e.Texto,
            })));

        Responder(new Resposta { Id = p.Id, Transcricao = resultado.ParaJson() });
    }

    private Dictionary<string, List<string>> MapaDeClientes()
    {
        var mapa = new Dictionary<string, List<string>>();
        foreach (string c in _projetos.ListarClientes()) mapa[c] = _projetos.ListarProjetos(c);
        return mapa;
    }

    /// <summary>
    /// Escreve a transcrição no formato pedido, ao lado da gravação.
    /// </summary>
    /// <remarks>
    /// Ao lado da gravação, e não em Downloads: o arquivo pertence àquela
    /// reunião, e quem procurar por ele daqui a um mês vai procurar na pasta
    /// dela. O caminho volta para a UI poder mostrar onde ficou.
    /// </remarks>
    private static (string Arquivo, string? Copia) Exportar(Pedido p)
    {
        if (p.Gravacao is not { Length: > 0 } pasta)
            throw new InvalidOperationException("sem gravação");

        string json = LerTranscricao(pasta)
            ?? throw new InvalidOperationException("esta gravação ainda não foi transcrita");
        var dados = ResultadoDaTranscricao.DeJson(json)
            ?? throw new InvalidOperationException("transcrição ilegível");

        bool comFalantes = p.ComFalantes ?? true;
        string titulo = p.Nome is { Length: > 0 } ? p.Nome : Path.GetFileName(pasta);
        string formato = p.Formato ?? "txt";

        // O que a tela mandou tem precedência: ela mostra o que estava
        // guardado e deixa corrigir, então o valor que chega aqui é o que a
        // pessoa acabou de confirmar. E o que ela preencher volta para o
        // arquivo, senão precisaria digitar de novo na próxima exportação.
        string? cliente = p.Cliente is { Length: > 0 } ? p.Cliente : dados.Client;
        string? projeto = p.Projeto is { Length: > 0 } ? p.Projeto : dados.Project;
        string? data = dados.Date ?? Transcritor.DataDaReuniao(pasta);

        if (cliente != dados.Client || projeto != dados.Project || data != dados.Date)
        {
            dados.Client = cliente;
            dados.Project = projeto;
            dados.Date = data;
            SalvarTranscricao(pasta, dados.ParaJson());
        }

        var cabecalho = Cabecalho.De(dados, titulo, cliente, projeto, data);

        string destino = Path.Combine(pasta,
            Exportacao.NomeDeArquivo(titulo, formato, cabecalho.Data));

        switch (formato)
        {
            case "txt": File.WriteAllText(destino, Exportacao.Txt(dados, comFalantes, cabecalho)); break;
            case "srt": File.WriteAllText(destino, Exportacao.Srt(dados, comFalantes, cabecalho)); break;
            case "vtt": File.WriteAllText(destino, Exportacao.Vtt(dados, comFalantes, cabecalho)); break;
            case "docx": Exportacao.Docx(dados, destino, titulo, comFalantes, cabecalho); break;
            default: throw new InvalidOperationException($"formato desconhecido: {formato}");
        }

        // A cópia é secundária de propósito: o original fica sempre junto da
        // gravação, e a pasta escolhida é para levar o arquivo a outro lugar —
        // rede, nuvem, Downloads. Se a cópia falhar, a exportação já aconteceu.
        string? copia = null;
        if (p.Copiar == true)
        {
            var cfg = ConfiguracoesDoApp.Carregar();
            string? escolhida = cfg.PastaDeExportacao;

            if (escolhida is not { Length: > 0 } || !Directory.Exists(escolhida))
                escolhida = SeletorDePasta.Escolher(IntPtr.Zero,
                    Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                    "Onde salvar a cópia");

            if (escolhida is { Length: > 0 })
            {
                copia = Path.Combine(escolhida, Path.GetFileName(destino));
                File.Copy(destino, copia, overwrite: true);

                // Lembrar a escolha: quem exporta uma vez para a pasta do
                // cliente costuma exportar as próximas para lá também.
                cfg.PastaDeExportacao = escolhida;
                cfg.Salvar();
            }
        }
        return (destino, copia);
    }

    /// <summary>
    /// Aprende a voz de um falante recém-nomeado.
    /// </summary>
    /// <remarks>
    /// Roda fora da thread da UI e nunca lança para fora: o nome já foi
    /// aplicado à transcrição, e falhar em aprender a voz não pode desfazer
    /// isso nem travar a janela.
    /// </remarks>
    private async Task AprenderVozAsync(Pedido p)
    {
        if (p.Gravacao is not { Length: > 0 } pasta
            || p.Falante is not { Length: > 0 } falante
            || p.Nome is not { Length: > 0 } nome)
        {
            Responder(new Resposta { Id = p.Id, Voz = "" });
            return;
        }

        string? json = LerTranscricao(pasta);
        if (json is null)
        {
            Responder(new Resposta { Id = p.Id, Voz = "" });
            return;
        }

        var dados = ResultadoDaTranscricao.DeJson(json);
        if (dados is null)
        {
            Responder(new Resposta { Id = p.Id, Voz = "" });
            return;
        }

        var amostra = await Task.Run(() => new AprendizadoDeVozes(
            Motores.AoLadoDoExecutavel(), new Vozes())
            .AprenderAsync(pasta, dados.Segments, falante, nome));

        Responder(new Resposta
        {
            Id = p.Id,
            Voz = amostra is null ? "pouca fala limpa para aprender a voz"
                : amostra.Quarentena ? $"voz de {nome} guardada, aguardando revisão"
                : $"voz de {nome} aprendida",
        });
    }

    /// <summary>Abre o diálogo de pasta do Windows e devolve o que foi escolhido.</summary>
    /// <remarks>
    /// Digitar caminho à mão é onde os erros moram — barra invertida trocada,
    /// espaço no fim, pasta que não existe. O diálogo do sistema não erra
    /// nenhum dos três, e é o mesmo que a exportação já usava.
    /// </remarks>
    private static string? EscolherPasta(string? inicial)
    {
        string ponto = inicial is { Length: > 0 } && Directory.Exists(inicial)
            ? inicial
            : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        return SeletorDePasta.Escolher(IntPtr.Zero, ponto, "Escolher pasta");
    }

    /// <summary>
    /// Apaga uma gravação inteira: os WAVs, o meta e a transcrição.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A pasta é conferida contra a raiz das gravações antes de qualquer coisa.
    /// A página manda um caminho, e caminho vindo da tela que chega direto a um
    /// <c>Directory.Delete(recursive)</c> é um apagador de disco controlado pelo
    /// HTML — basta um <c>..\..\</c> para virar outra coisa.
    /// </para>
    /// <para>
    /// É a operação mais destrutiva do app: leva junto o áudio original, que não
    /// se refaz. Quem chama é responsável por confirmar antes.
    /// </para>
    /// </remarks>
    private void ApagarGravacao(string? pasta)
    {
        if (pasta is not { Length: > 0 })
            throw new InvalidOperationException("sem gravação");

        string alvo = Path.GetFullPath(pasta);
        string raiz = Path.GetFullPath(pastaDasGravacoes);

        // O separador no fim impede que "…/gravacoes-antigas" passe por estar
        // sob "…/gravacoes" por prefixo de texto.
        if (!alvo.StartsWith(raiz + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                "esta pasta não está na pasta das gravações");

        if (alvo.TrimEnd(Path.DirectorySeparatorChar)
                .Equals(raiz.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("isso apagaria todas as gravações");

        if (!Directory.Exists(alvo))
            throw new InvalidOperationException("a gravação não está mais lá");

        Directory.Delete(alvo, recursive: true);
    }

    /// <summary>
    /// Baixa um pacote de modelo, relatando andamento à tela.
    /// </summary>
    /// <remarks>
    /// O motor de modelos sobe e cai por download: diferente do ASR e da
    /// diarização, não há nada quente para preservar entre chamadas, e um
    /// processo Python parado à toa é memória sem contrapartida.
    /// </remarks>
    private async Task BaixarPacoteAsync(Pedido p)
    {
        var pacote = Catalogo.Pacotes.FirstOrDefault(x => x.Id == p.Modelo)
            ?? throw new InvalidOperationException($"não conheço o pacote {p.Modelo}");

        var motores = Motores.AoLadoDoExecutavel();
        if (!File.Exists(motores.ScriptModelos))
            throw new MotorException(
                $"o motor de modelos não está em {motores.ScriptModelos}");

        var ambiente = new Dictionary<string, string>();
        if (Motores.TokenDoHuggingFace() is { Length: > 0 } token) ambiente["HF_TOKEN"] = token;

        using (var motor = await MotorSidecar.IniciarAsync(
                   motores.Python, [motores.ScriptModelos], CancellationToken.None, ambiente))
        {
            await motor.BaixarAsync(
                pacote.Repositorio,
                Catalogo.PastaDoPacote(pacote),
                pacote.TamanhoEsperadoBytes,
                (pct, texto) =>
                Responder(new Resposta
                {
                    Id = p.Id,
                    Tipo = "progresso",
                    Etapa = "baixando",
                    Fracao = pct,
                    Texto = texto,
                }));
        }

        // O catálogo relê o disco: a tela nunca acredita no que o download
        // disse ter feito, só no que está lá.
        Responder(new Resposta
        {
            Id = p.Id,
            Catalogo = Catalogo.Listar(ConfiguracoesDoApp.Carregar()),
        });
    }

    /// <summary>Apaga um pacote do cache.</summary>
    /// <remarks>
    /// Só apaga pasta de pacote que está no catálogo, e o caminho é montado
    /// aqui a partir do repositório conhecido — nunca vem da página. Um
    /// caminho vindo da tela seria um apagador recursivo controlado pelo HTML.
    /// </remarks>
    private static void RemoverPacote(string? id)
    {
        var pacote = Catalogo.Pacotes.FirstOrDefault(x => x.Id == id)
            ?? throw new InvalidOperationException($"não conheço o pacote {id}");

        string pasta = Catalogo.PastaDoPacote(pacote);
        if (Directory.Exists(pasta)) Directory.Delete(pasta, recursive: true);
    }

    /// <summary>
    /// A biblioteca de vozes achatada para a tela.
    /// </summary>
    /// <remarks>
    /// A quarentena aparece <b>junto</b> das demais amostras da pessoa, e não
    /// numa lista à parte: quem decide se a amostra estranha é contaminação ou
    /// uma condição nova legítima precisa ver as outras amostras da mesma pessoa
    /// ao lado. Separar em duas telas obrigaria a decidir sem a comparação, que
    /// é justamente o que a decisão exige.
    /// </remarks>
    private static List<PessoaResumo> VozesConhecidas()
    {
        var vozes = new Vozes();
        var lista = new List<PessoaResumo>();

        foreach (string pessoa in vozes.Pessoas())
        {
            var perfil = vozes.Perfil(pessoa);
            if (perfil is null) continue;

            var resumo = new PessoaResumo { Nome = pessoa };
            for (int i = 0; i < perfil.Amostras.Count; i++)
            {
                var a = perfil.Amostras[i];
                resumo.Amostras.Add(new AmostraResumo
                {
                    Indice = i,
                    CriadaEm = a.CriadaEm,
                    DuracaoS = a.DuracaoS,
                    Gravacao = a.Origem.Gravacao,
                    Faixa = a.Origem.Faixa,
                    T0 = a.Origem.T0,
                    T1 = a.Origem.T1,
                    Dispositivo = a.Origem.Dispositivo,
                    Quarentena = a.Quarentena,
                    // Conferir que o arquivo existe, e não só que o campo está
                    // preenchido: amostra antiga pode apontar para um recorte
                    // que foi apagado, e a tela precisa desabilitar o play em
                    // vez de oferecer um som que não vem.
                    Trecho = a.Trecho is { Length: > 0 }
                             && File.Exists(vozes.CaminhoDoTrecho(a.Trecho))
                                 ? a.Trecho.Replace('\\', '/') : null,
                });
            }
            lista.Add(resumo);
        }
        return lista;
    }

    /// <summary>
    /// Grava a transcrição editada por cima da que estava lá.
    /// </summary>
    /// <remarks>
    /// Escrita atômica pelo mesmo motivo do resto do projeto: a alternativa é
    /// um desligamento no meio deixar o arquivo pela metade, e aqui isso
    /// custaria a revisão inteira de uma reunião.
    /// </remarks>
    private static void SalvarTranscricao(string? pasta, string? conteudo)
    {
        if (pasta is not { Length: > 0 } || conteudo is not { Length: > 0 })
            throw new InvalidOperationException("nada para salvar");

        // Conferir que é JSON antes de gravar: escrever lixo aqui apagaria a
        // transcrição, e o erro só apareceria na próxima abertura.
        using (JsonDocument.Parse(conteudo)) { }

        string destino = Path.Combine(pasta, "transcricao.json");
        string tmp = destino + ".tmp";
        File.WriteAllText(tmp, conteudo);
        File.Move(tmp, destino, overwrite: true);
    }

    private static string? LerTranscricao(string? pasta)
    {
        if (pasta is not { Length: > 0 }) return null;
        string caminho = Path.Combine(pasta, "transcricao.json");
        return File.Exists(caminho) ? File.ReadAllText(caminho) : null;
    }

    private void Responder(Resposta r) =>
        responder(JsonSerializer.Serialize(r, PonteJson.Default.Resposta));

    /// <summary>As gravações que o gravador deixou, mais recentes primeiro.</summary>
    private List<GravacaoResumo> Listar()
    {
        if (!Directory.Exists(pastaDasGravacoes)) return [];

        var lista = new List<GravacaoResumo>();
        foreach (string pasta in Directory.EnumerateDirectories(pastaDasGravacoes))
        {
            string meta = Path.Combine(pasta, "meta.json");
            if (!File.Exists(meta)) continue;

            try
            {
                lista.Add(LerResumo(pasta, meta));
            }
            catch (Exception)
            {
                // Uma gravação ilegível não pode esconder as outras da lista.
            }
        }
        return [.. lista.OrderByDescending(g => g.Nome)];
    }

    private static GravacaoResumo LerResumo(string pasta, string caminhoMeta)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(caminhoMeta));
        var raiz = doc.RootElement;

        double duracao = raiz.TryGetProperty("duration_s", out var d) ? d.GetDouble() : 0;
        var avisos = new List<string>();

        if (raiz.TryGetProperty("tracks", out var faixas))
        {
            foreach (var faixa in faixas.EnumerateObject())
            {
                string nome = faixa.Name == "mic" ? "microfone" : "áudio do sistema";
                var t = faixa.Value;

                if (t.TryGetProperty("no_audio", out var sem) && sem.GetBoolean())
                    avisos.Add($"O {nome} não teve áudio nenhum.");
                else if (t.TryGetProperty("usable_pct", out var util) && util.GetDouble() < 20)
                    avisos.Add($"O {nome} tem só {util.GetDouble():F0}% de conteúdo útil.");

                // Campo novo do gravador nativo, que até agora ninguém lia.
                if (t.TryGetProperty("disconnected", out var caiu) && caiu.GetBoolean())
                    avisos.Add($"O dispositivo do {nome} caiu durante a gravação.");
            }
        }

        string? titulo = null;
        int convidados = 0;
        if (raiz.TryGetProperty("meeting", out var reuniao))
        {
            if (reuniao.TryGetProperty("title", out var t) && t.ValueKind == JsonValueKind.String)
                titulo = t.GetString();
            if (reuniao.TryGetProperty("attendees", out var a) && a.ValueKind == JsonValueKind.Array)
                convidados = a.GetArrayLength();
        }

        return new GravacaoResumo
        {
            Nome = Path.GetFileName(pasta),
            Caminho = pasta,
            DuracaoS = duracao,
            Titulo = titulo,
            Convidados = convidados,
            Transcrita = File.Exists(Path.Combine(pasta, "transcricao.json")),
            Avisos = avisos,
        };
    }
}
