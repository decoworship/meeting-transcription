using System.Text.Json;
using MeetingApp.Nucleo;
using Xunit;

namespace MeetingApp.Tests;

/// <summary>
/// O contrato entre a página e o núcleo.
/// </summary>
/// <remarks>
/// A <c>Ponte</c> é interna ao executável do app, então o que dá para exercitar
/// daqui é o que ela escreve em disco — e é justamente aí que mora o risco:
/// gravar por cima da transcrição é a operação que, se sair errada, apaga a
/// revisão inteira de uma reunião.
/// </remarks>
public sealed class PonteTests : IDisposable
{
    private readonly string _pasta =
        Directory.CreateTempSubdirectory("ponte-testes").FullName;

    public void Dispose() => Directory.Delete(_pasta, recursive: true);

    private static ResultadoDaTranscricao Exemplo() => new()
    {
        Language = "pt",
        Duration = 12.5,
        Segments =
        [
            new SegmentoFinal { Start = 0, End = 2, Text = " bom dia", Speaker = "Speaker 1" },
            new SegmentoFinal { Start = 2, End = 4, Text = " tudo bem?", Speaker = "You" },
        ],
    };

    [Fact]
    public void OJsonSalvoVoltaIgualAoQueFoiEscrito()
    {
        // O ciclo que a revisão faz: grava editado, relê na próxima abertura.
        string caminho = Path.Combine(_pasta, "transcricao.json");
        File.WriteAllText(caminho, Exemplo().ParaJson());

        using var doc = JsonDocument.Parse(File.ReadAllText(caminho));
        var segs = doc.RootElement.GetProperty("segments");

        Assert.Equal(2, segs.GetArrayLength());
        Assert.Equal("Speaker 1", segs[0].GetProperty("speaker").GetString());
        Assert.Equal("pt", doc.RootElement.GetProperty("language").GetString());
    }

    [Fact]
    public void NomearOFalanteTrocaORotuloEmTodosOsTrechosDele()
    {
        // É o que a gaveta faz ao gravar: os nomes vivem à parte durante a
        // revisão e entram nos segmentos na hora de salvar.
        var r = Exemplo();
        var nomes = new Dictionary<string, string> { ["Speaker 1"] = "Vanessa" };

        foreach (var s in r.Segments)
            if (s.Speaker is { } atual && nomes.TryGetValue(atual, out string? novo))
                s.Speaker = novo;

        Assert.Equal("Vanessa", r.Segments[0].Speaker);
        Assert.Equal("You", r.Segments[1].Speaker);   // o dono não é renomeado junto
    }

    [Fact]
    public void FundirDoisFalantesReescreveORotuloENaoCriaApelido()
    {
        // Fundir precisa fazer o falante sumir de verdade — inclusive do
        // filtro. Um apelido deixaria os dois na lista, que é o oposto do
        // pedido de quem funde.
        var r = Exemplo();
        foreach (var s in r.Segments)
            if (s.Speaker == "You") s.Speaker = "Speaker 1";

        Assert.All(r.Segments, s => Assert.Equal("Speaker 1", s.Speaker));
        Assert.Single(r.Segments.Select(s => s.Speaker).Distinct());
    }

    [Fact]
    public void TextoEditadoSobreviveAoIdaEVolta()
    {
        var r = Exemplo();
        r.Segments[0].Text = " Bom dia, Júri.";

        string json = r.ParaJson();
        using var doc = JsonDocument.Parse(json);

        Assert.Equal(" Bom dia, Júri.",
            doc.RootElement.GetProperty("segments")[0].GetProperty("text").GetString());
        // Acento literal, como o Python com ensure_ascii=False.
        Assert.Contains("Júri", json);
    }
}
