using System.Text.Json;
using System.Text.Json.Serialization;
using MeetingApp.App.Nativo;
using MeetingApp.Nucleo;
using MeetingApp.Nucleo.Atas;
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

    /// <summary>Separar quem falou. Ausente equivale a sim.</summary>
    [JsonPropertyName("diarizar")] public bool? Diarizar { get; init; }
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

    /// <summary>"mic" ou "loopback", nas operações do gravador.</summary>
    [JsonPropertyName("faixa")] public string? Faixa { get; init; }

    /// <summary>O id WASAPI escolhido, ou nulo para o padrão do Windows.</summary>
    [JsonPropertyName("dispositivo")] public string? Dispositivo { get; init; }

    /// <summary>Liga/desliga, nas opções do gravador.</summary>
    [JsonPropertyName("ligado")] public bool? Ligado { get; init; }
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

    /// <summary>O vínculo desta gravação, na resposta a <c>reuniao</c>.</summary>
    [JsonPropertyName("cliente")] public string? Cliente { get; init; }
    [JsonPropertyName("projeto")] public string? Projeto { get; init; }

    /// <summary>O que está escrito em notas.md.</summary>
    [JsonPropertyName("notas")] public string? Notas { get; init; }

    /// <summary>Nomes e siglas achados nas notas, para sugerir como vocabulário.</summary>
    [JsonPropertyName("termos")] public List<string>? Termos { get; init; }

    /// <summary>Os tipos de ata que a tela pode oferecer.</summary>
    [JsonPropertyName("tipos")] public List<TipoDeAtaResumo>? Tipos { get; init; }

    /// <summary>A ata em Markdown, ou nulo quando ainda não existe.</summary>
    [JsonPropertyName("ata")] public string? Ata { get; init; }

    /// <summary>A transcrição mudou depois de a ata ter sido escrita.</summary>
    [JsonPropertyName("ata_velha")] public bool AtaVelha { get; init; }
    [JsonPropertyName("prefs")] public PreferenciasDoProjeto? Prefs { get; init; }

    /// <summary>Os pacotes de modelo com o estado de cada um.</summary>
    [JsonPropertyName("catalogo")] public List<PacoteComEstado>? Catalogo { get; init; }

    /// <summary>A biblioteca de vozes como a tela precisa vê-la.</summary>
    [JsonPropertyName("vozes")] public List<PessoaResumo>? Vozes { get; init; }

    /// <summary>A pasta escolhida no diálogo, ou nulo se foi cancelado.</summary>
    [JsonPropertyName("pasta")] public string? Pasta { get; init; }

    /// <summary>O gravador como a tela precisa vê-lo.</summary>
    [JsonPropertyName("gravador")] public EstadoDoGravador? Gravador { get; init; }

    /// <summary>O que está sendo transcrito, e o que acabou de terminar.</summary>
    [JsonPropertyName("transcricoes")] public EstadoDasTranscricoes? Transcricoes { get; init; }

    /// <summary>Os dispositivos de áudio, para a tela poder escolher.</summary>
    [JsonPropertyName("dispositivos")] public DispositivosDisponiveis? Dispositivos { get; init; }
}

/// <summary>
/// O gravador num instante: o que a bandeja diz num tooltip, aberto em campos.
/// </summary>
/// <remarks>
/// Chega à página de dois jeitos — como resposta a <c>gravador</c> e empurrado a
/// cada 200 ms enquanto a janela está aberta e gravando. É o mesmo objeto nos
/// dois casos de propósito: a tela desenha do estado que recebeu, sem precisar
/// saber se pediu ou se foi avisada.
/// </remarks>
internal sealed class EstadoDoGravador
{
    [JsonPropertyName("gravando")] public bool Gravando { get; init; }
    [JsonPropertyName("mudo")] public bool Mudo { get; init; }

    /// <summary>Há quanto tempo está mudo. Mute esquecido é o modo de falha mais provável.</summary>
    [JsonPropertyName("mudo_ha_s")] public double MudoHaS { get; init; }

    /// <summary>"cinza", "vermelho", "laranja" ou "amarelo" — a cor da bandeja.</summary>
    /// <remarks>
    /// A tela usa a <b>mesma</b> escala de cor do ícone, e não uma própria:
    /// laranja é você tendo mutado, amarelo é um canal sem áudio sem ninguém ter
    /// pedido. Inventar outra linguagem na janela obrigaria a traduzir de cabeça
    /// entre dois lugares que mostram a mesma coisa.
    /// </remarks>
    [JsonPropertyName("cor")] public required string Cor { get; init; }

    /// <summary>O texto de status pronto, do EstadoDaBandeja.</summary>
    [JsonPropertyName("status")] public required string Status { get; init; }

    [JsonPropertyName("duracao_s")] public double DuracaoS { get; init; }
    [JsonPropertyName("pasta")] public required string Pasta { get; init; }

    /// <summary>
    /// A pasta <b>desta</b> gravação, e não a raiz onde elas moram.
    /// </summary>
    /// <remarks>
    /// É o endereço para onde as notas escritas durante a reunião vão. Sem ele
    /// a tela do Gravador saberia que está gravando e não saberia onde — a raiz
    /// não serve, porque nota pertence a uma reunião, não à coleção delas.
    /// </remarks>
    [JsonPropertyName("gravacao")] public string? Gravacao { get; init; }

    /// <summary>A reunião da agenda que está sendo gravada, quando há uma.</summary>
    [JsonPropertyName("titulo")] public string? Titulo { get; init; }
    [JsonPropertyName("participantes")] public List<string>? Participantes { get; init; }

    [JsonPropertyName("notificacoes")] public bool Notificacoes { get; init; }
    [JsonPropertyName("usar_agenda")] public bool UsarAgenda { get; init; }
    [JsonPropertyName("conta")] public string? Conta { get; init; }
    [JsonPropertyName("agenda_configurada")] public bool AgendaConfigurada { get; init; }

    [JsonPropertyName("faixas")] public List<FaixaAoVivo> Faixas { get; init; } = [];
}

/// <summary>Uma faixa enquanto grava — é daqui que sai o medidor de nível.</summary>
internal sealed class FaixaAoVivo
{
    [JsonPropertyName("nome")] public required string Nome { get; init; }
    [JsonPropertyName("dispositivo")] public required string Dispositivo { get; init; }

    /// <summary>RMS instantâneo, 0 a 1. Ver WasapiTrackCapture.Nivel.</summary>
    [JsonPropertyName("nivel")] public double Nivel { get; init; }
    [JsonPropertyName("ja_ouviu")] public bool JaOuviu { get; init; }
    [JsonPropertyName("mudo")] public bool Mudo { get; init; }
    [JsonPropertyName("silencio_s")] public double SilencioS { get; init; }
    [JsonPropertyName("desconectado")] public bool Desconectado { get; init; }
    [JsonPropertyName("falha")] public string? Falha { get; init; }
}

/// <summary>
/// As transcrições como a página precisa vê-las.
/// </summary>
/// <remarks>
/// Chega de dois jeitos, como o gravador: como resposta a <c>transcricoes</c> e
/// empurrado a cada aviso de andamento do pipeline. É o mesmo objeto nos dois
/// casos, para a tela desenhar do que recebeu sem saber se pediu ou foi avisada.
/// </remarks>
internal sealed class EstadoDasTranscricoes
{
    /// <summary>A que está rodando agora, ou nulo. É ela que acende a bolinha.</summary>
    [JsonPropertyName("atual")] public TranscricaoResumo? Atual { get; init; }

    /// <summary>A última que terminou, para a tela poder mostrar como acabou.</summary>
    [JsonPropertyName("ultimo")] public TranscricaoResumo? Ultimo { get; init; }
}

internal sealed class TranscricaoResumo
{
    /// <summary>A pasta da gravação: é por ela que a tela sabe se é a sua.</summary>
    [JsonPropertyName("gravacao")] public required string Gravacao { get; init; }
    [JsonPropertyName("nome")] public required string Nome { get; init; }
    [JsonPropertyName("etapa")] public required string Etapa { get; init; }
    [JsonPropertyName("fracao")] public double Fracao { get; init; }
    [JsonPropertyName("texto")] public required string Texto { get; init; }
    [JsonPropertyName("comecou_em")] public required string ComecouEm { get; init; }
    [JsonPropertyName("terminou")] public bool Terminou { get; init; }
    [JsonPropertyName("erro")] public string? Erro { get; init; }

    /// <summary>Parou a pedido. A tela trata diferente de falha.</summary>
    [JsonPropertyName("cancelada")] public bool Cancelada { get; init; }
}

/// <summary>Um tipo de reunião, como a tela o oferece.</summary>
internal sealed class TipoDeAtaResumo
{
    [JsonPropertyName("id")] public required string Id { get; init; }
    [JsonPropertyName("nome")] public required string Nome { get; init; }

    /// <summary>Veio da pasta do perfil: a tela oferece "voltar ao original".</summary>
    [JsonPropertyName("do_usuario")] public bool DoUsuario { get; init; }
}

internal sealed class DispositivosDisponiveis
{
    [JsonPropertyName("entradas")] public List<DispositivoResumo> Entradas { get; init; } = [];
    [JsonPropertyName("saidas")] public List<DispositivoResumo> Saidas { get; init; } = [];

    /// <summary>Os escolhidos, ou nulo para "padrão do Windows".</summary>
    [JsonPropertyName("mic_id")] public string? MicId { get; init; }
    [JsonPropertyName("loopback_id")] public string? LoopbackId { get; init; }
}

internal sealed class DispositivoResumo
{
    [JsonPropertyName("id")] public required string Id { get; init; }
    [JsonPropertyName("nome")] public required string Nome { get; init; }
    [JsonPropertyName("padrao")] public bool Padrao { get; init; }
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

    /// <summary>O vínculo escolhido na tela de preparo, que sobrevive a ela.</summary>
    [JsonPropertyName("cliente")] public string? Cliente { get; init; }
    [JsonPropertyName("projeto")] public string? Projeto { get; init; }

    /// <summary>Alguém escreveu notas nesta reunião.</summary>
    [JsonPropertyName("com_notas")] public bool ComNotas { get; init; }

    [JsonPropertyName("avisos")] public List<string> Avisos { get; init; } = [];
}

[JsonSourceGenerationOptions(DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(Pedido))]
[JsonSerializable(typeof(Resposta))]
[JsonSerializable(typeof(PacoteComEstado))]
[JsonSerializable(typeof(EstadoDoGravador))]
[JsonSerializable(typeof(EstadoDasTranscricoes))]
[JsonSerializable(typeof(DispositivosDisponiveis))]
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
/// <param name="gravador">
/// O gravador do mesmo processo. A ponte não o comanda de longe: chama métodos,
/// e o efeito aparece na bandeja e na janela pelo mesmo evento.
/// </param>
/// <param name="avisar">
/// Um balão da bandeja. Serve à transcrição que termina com a janela escondida —
/// que é o caso normal, já que ela passou a rodar sem ninguém olhando.
/// </param>
internal sealed class Ponte(string pastaDasGravacoes, Action<string> responder,
                            Bandeja.Gravador gravador, Action<string> avisar)
{
    private readonly Transcritor _transcritor = new(Motores.AoLadoDoExecutavel());
    private readonly Projetos _projetos = new();

    /// <summary>
    /// O que está sendo transcrito. Vive na ponte, e não na página, porque a
    /// página troca de tela e o pipeline não pode saber disso (FASE3.md §2).
    /// </summary>
    private readonly RegistroDeTranscricoes _transcricoes = new();

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

                // O vínculo da reunião com cliente/projeto, guardado na hora em
                // que se escolhe — e não só quando se transcreve. Ver
                // DadosDaReuniao: o defeito que originou isto era sair da tela
                // de preparo e voltar com os campos em branco.
                case "salvar-reuniao":
                {
                    if (p.Gravacao is not { Length: > 0 } onde)
                        throw new InvalidOperationException("sem gravação");
                    new DadosDaReuniao { Cliente = p.Cliente, Projeto = p.Projeto }
                        .Salvar(onde);
                    Responder(new Resposta { Id = p.Id });
                    break;
                }

                // ─────────────────────────────────────────────── atas

                case "modelos-de-ata":
                    Responder(new Resposta
                    {
                        Id = p.Id,
                        Tipos = [.. ModelosDeAta.Todos().Select(m => new TipoDeAtaResumo
                        {
                            Id = m.Id, Nome = m.Nome, DoUsuario = m.DoUsuario,
                        })],
                    });
                    break;

                case "ata":
                {
                    if (p.Gravacao is not { Length: > 0 } onde)
                        throw new InvalidOperationException("sem gravação");
                    string caminho = Path.Combine(onde, "ata.md");
                    Responder(new Resposta
                    {
                        Id = p.Id,
                        Ata = File.Exists(caminho) ? File.ReadAllText(caminho) : null,
                        // Ata mais velha que a transcrição significa que alguém
                        // corrigiu o texto depois — e a ata ficou desatualizada
                        // sem ninguém avisar.
                        AtaVelha = File.Exists(caminho)
                                   && File.Exists(Path.Combine(onde, "transcricao.json"))
                                   && File.GetLastWriteTimeUtc(caminho)
                                      < File.GetLastWriteTimeUtc(Path.Combine(onde, "transcricao.json")),
                    });
                    break;
                }

                case "gerar-ata":
                    GerarAta(p);
                    break;

                case "customizar-tipo-de-ata":
                    Responder(new Resposta
                    {
                        Id = p.Id,
                        Arquivo = ModelosDeAta.Customizar(p.Modelo ?? ""),
                    });
                    break;

                case "restaurar-tipo-de-ata":
                    ModelosDeAta.VoltarAoOriginal(p.Modelo ?? "");
                    Responder(new Resposta
                    {
                        Id = p.Id,
                        Tipos = [.. ModelosDeAta.Todos().Select(m => new TipoDeAtaResumo
                        {
                            Id = m.Id, Nome = m.Nome, DoUsuario = m.DoUsuario,
                        })],
                    });
                    break;

                // ─────────────────────────────────── notas da reunião

                case "notas":
                {
                    if (p.Gravacao is not { Length: > 0 } onde)
                        throw new InvalidOperationException("sem gravação");
                    Responder(new Resposta
                    {
                        Id = p.Id,
                        Notas = Notas.Ler(onde),
                        Termos = Notas.TermosSugeridos(Notas.Ler(onde)),
                    });
                    break;
                }

                case "salvar-notas":
                {
                    if (p.Gravacao is not { Length: > 0 } onde)
                        throw new InvalidOperationException("sem gravação");
                    Notas.Salvar(onde, p.Conteudo);
                    Responder(new Resposta
                    {
                        Id = p.Id,
                        Termos = Notas.TermosSugeridos(p.Conteudo ?? ""),
                    });
                    break;
                }

                case "reuniao":
                {
                    if (p.Gravacao is not { Length: > 0 } onde)
                        throw new InvalidOperationException("sem gravação");
                    var d = DadosDaReuniao.Ler(onde);
                    Responder(new Resposta
                    {
                        Id = p.Id, Cliente = d.Cliente, Projeto = d.Projeto,
                    });
                    break;
                }

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

                // Não espera o pipeline: responde "aceita" na hora, e o
                // andamento passa a fluir pelo canal de eventos. É o que faz a
                // transcrição sobreviver a trocar de tela — quem desenha a barra
                // deixa de ser o dono da promessa (FASE3.md §2).
                case "transcrever":
                    Transcrever(p);
                    break;

                case "transcricoes":
                    Responder(new Resposta { Id = p.Id, Transcricoes = Instantaneo() });
                    break;

                // Só pede para parar; quem tira do registro é a tarefa que
                // estava rodando, quando os motores de fato morrerem.
                case "cancelar-transcricao":
                    _transcricoes.Cancelar(p.Gravacao);
                    Responder(new Resposta { Id = p.Id, Transcricoes = Instantaneo() });
                    break;

                case "esquecer-transcricao":
                    _transcricoes.EsquecerUltimo();
                    Responder(new Resposta { Id = p.Id, Transcricoes = Instantaneo() });
                    break;

                // ─────────────────────────────────── gravador
                //
                // Todas devolvem o estado inteiro, e não um "ok": a tela desenha
                // do estado que recebeu, e uma resposta vazia a obrigaria a
                // adivinhar o que mudou — ou a pedir de novo logo em seguida.

                case "gravador":
                    Responder(new Resposta { Id = p.Id, Gravador = Instantaneo(gravador) });
                    break;

                case "gravar":
                    gravador.Iniciar();
                    Responder(new Resposta { Id = p.Id, Gravador = Instantaneo(gravador) });
                    break;

                case "parar-gravacao":
                    gravador.Parar();
                    Responder(new Resposta { Id = p.Id, Gravador = Instantaneo(gravador) });
                    break;

                case "mutar":
                    gravador.AlternarMudo();
                    Responder(new Resposta { Id = p.Id, Gravador = Instantaneo(gravador) });
                    break;

                case "dispositivos":
                    Responder(new Resposta { Id = p.Id, Dispositivos = Disponiveis(gravador) });
                    break;

                case "escolher-dispositivo":
                    gravador.EscolherDispositivo(p.Faixa ?? "mic",
                        p.Dispositivo is { Length: > 0 } ? p.Dispositivo : null);
                    Responder(new Resposta
                    {
                        Id = p.Id,
                        Gravador = Instantaneo(gravador),
                        Dispositivos = Disponiveis(gravador),
                    });
                    break;

                case "pasta-das-gravacoes":
                    DefinirPastaDasGravacoes(p.Pasta);
                    Responder(new Resposta { Id = p.Id, Gravador = Instantaneo(gravador) });
                    break;

                case "notificacoes":
                    if ((p.Ligado ?? true) != gravador.Estado.NotificacoesLigadas)
                        gravador.AlternarNotificacoes();
                    Responder(new Resposta { Id = p.Id, Gravador = Instantaneo(gravador) });
                    break;

                case "usar-agenda":
                    gravador.UsarAgenda(p.Ligado ?? true);
                    Responder(new Resposta { Id = p.Id, Gravador = Instantaneo(gravador) });
                    break;

                case "conectar-agenda":
                    // Abre o navegador e volta na hora: a autorização acontece
                    // fora do app, e travar a tela até o usuário terminar de
                    // clicar num site seria travá-la por minutos.
                    gravador.Autorizar();
                    Responder(new Resposta { Id = p.Id, Gravador = Instantaneo(gravador) });
                    break;

                case "desconectar-agenda":
                    MeetingRecorder.Agenda.ClienteDaAgenda.Desconectar();
                    Responder(new Resposta { Id = p.Id, Gravador = Instantaneo(gravador) });
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

    // ───────────────────────────────────────────────────────── gravador

    /// <summary>
    /// O evento empurrado à página: o mesmo estado, com <c>id</c> zero.
    /// </summary>
    /// <remarks>
    /// Zero porque não responde a pedido nenhum, e a página casa pedidos e
    /// respostas pelo id — um id inventado casaria com uma promessa alheia. O
    /// <c>tipo</c> é o que a <c>ponte.js</c> usa para roteá-lo aos assinantes em
    /// vez de a uma promessa.
    /// </remarks>
    public static string EventoDoGravador(Bandeja.Gravador g) =>
        JsonSerializer.Serialize(
            new Resposta { Id = 0, Tipo = "gravador", Gravador = Instantaneo(g) },
            PonteJson.Default.Resposta);

    private static EstadoDoGravador Instantaneo(Bandeja.Gravador g)
    {
        var faixas = new List<FaixaAoVivo>();
        foreach (var c in g.Capturas)
            faixas.Add(new FaixaAoVivo
            {
                Nome = c.Stats.Nome,
                Dispositivo = c.NomeDispositivo,
                Nivel = c.Nivel,
                JaOuviu = c.Stats.JaOuviu,
                Mudo = c.Mudo,
                SilencioS = c.Stats.SilencioAtualS,
                Desconectado = c.Desconectado,
                Falha = c.FalhaDeEscrita ? c.MotivoDaFalha : null,
            });

        return new EstadoDoGravador
        {
            Gravando = g.Estado.Gravando,
            Mudo = g.Estado.Mudo,
            MudoHaS = g.Estado.MudoHaS(DateTime.UtcNow),
            Cor = g.Estado.Cor.ToString().ToLowerInvariant(),
            Status = g.Estado.TextoDeStatus(g.DuracaoAtual, null).Split('\n')[0],
            DuracaoS = g.DuracaoAtual,
            Pasta = g.PastaDeSaida,
            // Só enquanto grava: o PastaAtual guarda também a da última
            // gravação, e oferecer o bloco de notas depois de parar faria
            // escrever numa reunião que já acabou sem a tela dizer qual é.
            Gravacao = g.Estado.Gravando ? g.PastaAtual : null,
            Titulo = g.Evento?.Titulo,
            Participantes = g.Evento?.NomesDosParticipantes().ToList(),
            Notificacoes = g.Estado.NotificacoesLigadas,
            UsarAgenda = g.Cfg.UseCalendar,
            AgendaConfigurada = MeetingRecorder.Agenda.ClienteDaAgenda.EstaConfigurado(),
            Conta = MeetingRecorder.Agenda.ClienteDaAgenda.EstaAutorizado()
                ? MeetingRecorder.Agenda.ClienteDaAgenda.EmailDaConta() : null,
            Faixas = faixas,
        };
    }

    private static DispositivosDisponiveis Disponiveis(Bandeja.Gravador g)
    {
        var cat = g.Dispositivos.Atual;
        static List<DispositivoResumo> Mapear(
            IReadOnlyList<MeetingRecorder.Capture.Dispositivo> lista) =>
            [.. lista.Select(d => new DispositivoResumo
            {
                Id = d.Id, Nome = d.Nome, Padrao = d.EhPadrao,
            })];

        return new DispositivosDisponiveis
        {
            Entradas = Mapear(cat.Entradas),
            Saidas = Mapear(cat.Saidas),
            MicId = g.Cfg.MicId,
            LoopbackId = g.Cfg.LoopbackId,
        };
    }

    /// <summary>
    /// Troca a pasta onde o gravador salva — a mesma que o app lê.
    /// </summary>
    /// <remarks>
    /// Confere a escrita antes de aceitar, como o menu da bandeja: descobrir que
    /// a pasta é somente leitura no meio de uma reunião seria tarde demais. Uma
    /// pasta vazia restaura o padrão.
    /// </remarks>
    private void DefinirPastaDasGravacoes(string? pasta)
    {
        if (gravador.Estado.Gravando)
            throw new InvalidOperationException(
                "não dá para trocar a pasta durante uma gravação");

        if (gravador.PastaForcada is not null)
            throw new InvalidOperationException(
                "esta sessão foi aberta com --gravacoes; a pasta está fixa");

        if (pasta is { Length: > 0 } && !Bandeja.Bandeja.PodeEscrever(pasta, out string? erro))
            throw new InvalidOperationException($"não dá para escrever nessa pasta: {erro}");

        gravador.DefinirPastaDeSaida(pasta);
    }

    // ──────────────────────────────────────────────────── transcrições

    /// <summary>O registro como a página o vê.</summary>
    private EstadoDasTranscricoes Instantaneo() => new()
    {
        Atual = Resumir(_transcricoes.Atual),
        Ultimo = Resumir(_transcricoes.Ultimo),
    };

    private static TranscricaoResumo? Resumir(TrabalhoDeTranscricao? t) => t is null ? null : new()
    {
        Gravacao = t.Gravacao,
        Nome = t.Nome,
        Etapa = t.Etapa,
        Fracao = t.Fracao,
        Texto = t.Texto,
        ComecouEm = t.ComecouEm.ToString("o"),
        Terminou = t.Terminou,
        Erro = t.Erro,
        Cancelada = t.Cancelada,
    };

    /// <summary>Empurra o registro à página, sem ela ter pedido.</summary>
    private void EmpurrarTranscricoes() =>
        Responder(new Resposta { Id = 0, Tipo = "transcricoes", Transcricoes = Instantaneo() });

    /// <summary>
    /// Aceita a transcrição e devolve o controle na hora.
    /// </summary>
    /// <remarks>
    /// <para>
    /// O pipeline roda solto: a resposta a este pedido diz apenas que foi
    /// aceito, e etapa, fração e fim chegam pelo canal de eventos. Foi assim que
    /// a transcrição deixou de morrer ao trocar de tela — antes, quem desenhava
    /// a barra era o dono da promessa, e trocar de tela jogava fora o DOM em que
    /// ela escrevia (FASE3.md §2).
    /// </para>
    /// <para>
    /// <c>Task.Run</c> porque o pipeline bloquearia a thread da UI, que é a
    /// mesma que desenha a janela <b>e</b> a que atende a bandeja: sem isto, a
    /// barra congelaria justamente enquanto há progresso a mostrar, e o menu da
    /// bandeja não abriria durante uma transcrição.
    /// </para>
    /// </remarks>
    private void Transcrever(Pedido p)
    {
        if (p.Gravacao is not { Length: > 0 } pasta)
        {
            Responder(new Resposta { Id = p.Id, Erro = "sem gravação" });
            return;
        }

        // O que a tela mandou tem precedência, mas o silêncio dela não apaga o
        // que já estava: retranscrever com os campos em branco apagava o cliente
        // e o projeto guardados, e essa era a metade invisível do defeito.
        var vinculo = DadosDaReuniao.Ler(pasta);
        string? cliente = p.Cliente is { Length: > 0 } ? p.Cliente : vinculo.Cliente;
        string? projeto = p.Projeto is { Length: > 0 } ? p.Projeto : vinculo.Projeto;
        if (cliente != vinculo.Cliente || projeto != vinculo.Projeto)
            new DadosDaReuniao { Cliente = cliente, Projeto = projeto }.Salvar(pasta);

        // Lança quando já há uma em curso, e a mensagem nomeia qual. O catch do
        // AtenderAsync a transforma na resposta de erro que a tela mostra.
        var trabalho = _transcricoes.Comecar(pasta, NomeDaGravacao(pasta));
        Responder(new Resposta { Id = p.Id, Transcricoes = Instantaneo() });
        EmpurrarTranscricoes();

        _ = Task.Run(async () =>
        {
            try
            {
                await _transcritor.ExecutarAsync(
                    pasta, p.Vocabulario, p.Idioma,
                    modelo: p.Modelo, cliente: cliente, projeto: projeto,
                    diarizar: p.Diarizar ?? true,
                    progresso: e =>
                    {
                        _transcricoes.Progredir(pasta, e.Etapa, e.Fracao, e.Texto);
                        EmpurrarTranscricoes();
                    },
                    ct: trabalho.Token);
                _transcricoes.Terminar(pasta);
                Avisar($"Transcrição pronta: {trabalho.Nome}");
            }
            catch (OperationCanceledException)
            {
                // Parar a pedido não é falha: a tela não mostra alerta vermelho,
                // e a bandeja não avisa — quem clicou em parar sabe que parou.
                _transcricoes.Terminar(pasta, cancelada: true);
            }
            catch (Exception e)
            {
                // O erro vira estado, e não mensagem perdida: quem saiu da tela
                // no meio precisa poder descobrir, ao voltar, que falhou.
                _transcricoes.Terminar(pasta, e.Message);
                Avisar($"A transcrição de {trabalho.Nome} falhou.");
            }
            EmpurrarTranscricoes();
        });
    }

    /// <summary>
    /// Aceita a ata e devolve o controle, como a transcrição faz.
    /// </summary>
    /// <remarks>
    /// Mesmo registro da transcrição, e não um segundo: os dois trabalhos
    /// disputam a mesma placa, e a trava de um por vez é o que garante que o
    /// modelo de ata só carregue com a VRAM do ASR liberada. A bolinha do trilho
    /// acende para os dois pelo mesmo caminho.
    /// </remarks>
    private void GerarAta(Pedido p)
    {
        if (p.Gravacao is not { Length: > 0 } pasta)
        {
            Responder(new Resposta { Id = p.Id, Erro = "sem gravação" });
            return;
        }

        var tipo = ModelosDeAta.Buscar(p.Modelo)
            ?? throw new InvalidOperationException($"tipo de ata desconhecido: {p.Modelo}");

        string json = LerTranscricao(pasta)
            ?? throw new InvalidOperationException("esta reunião ainda não foi transcrita");
        var dados = ResultadoDaTranscricao.DeJson(json)
            ?? throw new InvalidOperationException("transcrição ilegível");

        var trabalho = _transcricoes.Comecar(pasta, NomeDaGravacao(pasta), "ata");
        Responder(new Resposta { Id = p.Id, Transcricoes = Instantaneo() });
        EmpurrarTranscricoes();

        _ = Task.Run(async () =>
        {
            try
            {
                var vinculo = DadosDaReuniao.Ler(pasta);
                var ctx = new ContextoDaReuniao
                {
                    Titulo = Listar().FirstOrDefault(g => g.Caminho == pasta)?.Titulo,
                    Cliente = vinculo.Cliente ?? dados.Client,
                    Projeto = vinculo.Projeto ?? dados.Project,
                    Data = dados.Date ?? Transcritor.DataDaReuniao(pasta),
                    DuracaoS = dados.Duration ?? 0,
                    Falantes = [.. dados.Segments.Select(s => s.Speaker)
                        .Where(s => s is { Length: > 0 }).Distinct()!],
                    Notas = Notas.Ler(pasta),
                    Vocabulario = _projetos.Preferencias(
                        vinculo.Cliente ?? "", vinculo.Projeto ?? "")?.InitialPrompt ?? "",
                };

                var roteiro = RoteiroDeFatos.De(dados.Segments);
                string prompt = PromptDeAta.Montar(tipo, ctx, dados.Segments, roteiro);

                var motor = new MotorDeAta(CaminhosDoMotorDeAta.AoLadoDoExecutavel(
                    ConfiguracoesDoApp.Carregar().ModeloDeAta));

                var ata = await motor.GerarAsync(prompt, ctx.DuracaoS, e =>
                {
                    _transcricoes.Progredir(pasta, e.Etapa, e.Fracao, e.Texto);
                    EmpurrarTranscricoes();
                }, trabalho.Token);

                VerificadorDeAta.Conferir(ata, dados.Segments,
                    [.. ctx.Convidados.Concat(ctx.Falantes)], roteiro);

                File.WriteAllText(Path.Combine(pasta, "ata.md"),
                                  RedatorDeAta.Escrever(ata, tipo, ctx));
                File.WriteAllText(Path.Combine(pasta, "ata.json"), ata.ParaJson());

                _transcricoes.Terminar(pasta);
                Avisar($"Ata pronta: {trabalho.Nome}");
            }
            catch (OperationCanceledException)
            {
                _transcricoes.Terminar(pasta, cancelada: true);
            }
            catch (Exception e)
            {
                _transcricoes.Terminar(pasta, e.Message);
                Avisar($"A ata de {trabalho.Nome} falhou.");
            }
            EmpurrarTranscricoes();
        });
    }

    /// <summary>
    /// Como chamar a reunião numa frase: o título da agenda, ou a pasta.
    /// </summary>
    /// <remarks>
    /// O mesmo nome que a lista mostra, para o aviso de "já estou transcrevendo
    /// X" citar o que a pessoa vê na tela, e não um caminho de disco.
    /// </remarks>
    private string NomeDaGravacao(string pasta)
    {
        string nome = Path.GetFileName(pasta.TrimEnd(Path.DirectorySeparatorChar));
        foreach (var g in Listar())
            if (g.Caminho == pasta) return g.Titulo is { Length: > 0 } t ? t : g.Nome;
        return nome;
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

    /// <summary>
    /// Um balão da bandeja, e nunca uma exceção que suba.
    /// </summary>
    /// <remarks>
    /// O aviso é conveniência; a transcrição já terminou quando ele sai. Deixar
    /// uma falha de Shell_NotifyIcon derrubar a tarefa perderia o
    /// <c>EmpurrarTranscricoes</c> que vem depois — e aí a tela ficaria com a
    /// barra parada para sempre, que é justamente o defeito que esta fase
    /// conserta.
    /// </remarks>
    private void Avisar(string texto)
    {
        try { avisar(texto); }
        catch { /* a bandeja pode estar indo embora; o estado já foi registrado */ }
    }

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

        // O vínculo com cliente/projeto vem junto na lista, e não por pedido
        // separado: são dois campos por gravação, e um pedido por cartão faria a
        // lista piscar preenchendo-se aos poucos.
        var dados = DadosDaReuniao.Ler(pasta);

        return new GravacaoResumo
        {
            Nome = Path.GetFileName(pasta),
            Caminho = pasta,
            DuracaoS = duracao,
            Titulo = titulo,
            Convidados = convidados,
            Transcrita = File.Exists(Path.Combine(pasta, "transcricao.json")),
            Cliente = dados.Cliente,
            Projeto = dados.Projeto,
            ComNotas = Notas.Existem(pasta),
            Avisos = avisos,
        };
    }
}
