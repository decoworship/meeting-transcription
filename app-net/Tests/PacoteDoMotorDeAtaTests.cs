using MeetingApp.Nucleo.Atas;
using Xunit;

namespace MeetingApp.Tests;

/// <summary>
/// O motor de ata como pacote baixável (Fase 4).
/// </summary>
/// <remarks>
/// Estes testes não baixam nada — 641 MB numa suíte que roda a cada commit é o
/// oposto de uma rede de proteção. O que eles protegem é o que uma suíte
/// portátil consegue proteger: que o estado seja lido do disco e não presumido,
/// e que a frase de "falta o motor" continue dizendo o que fazer. O download em
/// si se verifica clicando no botão uma vez, e o resultado dele tem régua
/// própria dentro do <see cref="PacoteDoMotorDeAta"/>.
/// </remarks>
public sealed class PacoteDoMotorDeAtaTests
{
    [Fact]
    public void OTamanhoDoDownloadEAsomaDosDoisArquivos()
    {
        // Ele vai para a tela ("são 641 MB de download") e para a frase de erro.
        // Um número inventado aqui vira uma promessa quebrada lá.
        Assert.InRange(PacoteDoMotorDeAta.BytesDoDownload, 600_000_000, 700_000_000);
    }

    [Fact]
    public void SemOMotorOEstadoDizAusenteENaoInventaTamanho()
    {
        // A suíte roda em Linux, onde motores/ata/bin não existe. É o mesmo
        // estado de uma instalação recém-feita, que é o caso que importa.
        var estado = PacoteDoMotorDeAta.Estado();

        Assert.False(estado.Instalado);
        Assert.Equal(0, estado.BytesEmDisco);
        Assert.Equal(PacoteDoMotorDeAta.BytesDoDownload, estado.BytesDoDownload);
        Assert.NotEmpty(estado.Pasta);
    }

    [Fact]
    public void AFraseDeMotorAusenteDizOndeIrEQuantoCusta()
    {
        // "o motor de ata não está em C:\...\llama-server.exe" é uma constatação
        // de caminho, e desde a Fase 4 a ausência é o estado NORMAL de quem
        // acabou de instalar. A frase tem que ser uma instrução.
        string? falta = CaminhosDoMotorDeAta.AoLadoDoExecutavel().OQueFalta();

        Assert.NotNull(falta);
        Assert.Contains("Modelos", falta);
        Assert.Contains("641 MB", falta);
    }

    [Fact]
    public void APastaDoMotorFicaAoLadoDosOutrosMotores()
    {
        // O download extrai aqui, e o MotorDeAta procura aqui. São dois códigos
        // diferentes com que concordar; discordar significaria baixar 641 MB
        // para uma pasta que ninguém lê.
        Assert.Equal(
            Path.GetDirectoryName(CaminhosDoMotorDeAta.AoLadoDoExecutavel().Servidor),
            PacoteDoMotorDeAta.Pasta);
    }
}
