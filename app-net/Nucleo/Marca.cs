namespace MeetingApp.Nucleo;

/// <summary>
/// O nome do produto. É o único lugar do código onde ele é escrito.
/// </summary>
/// <remarks>
/// <para>
/// A marca ainda não está fechada, e o custo de trocá-la não pode ser
/// caçar 107 arquivos. Por isso ela é uma constante só, e o que a acompanha é
/// uma regra: <b>o que a pessoa lê muda; o que o Windows guarda, não.</b>
/// </para>
/// <para>
/// <b>Muda com a marca</b> — título da janela, tooltip e balões da bandeja,
/// bloco de diagnóstico, linha de versão em Ajustes, textos do instalador.
/// Todos passam por aqui ou pelo <c>#define Marca</c> do
/// <c>instalador/MeetingApp.iss</c>, e o <c>MarcaTests</c> falha se os dois
/// discordarem.
/// </para>
/// <para>
/// <b>Não muda nunca</b>, mesmo que o nome mude dez vezes:
/// </para>
/// <list type="bullet">
///   <item><description>o <c>AppId</c> do <c>.iss</c> — é por ele que o Windows
///   sabe que a versão nova é atualização, e não um segundo programa;</description></item>
///   <item><description><c>MeetingApp.exe</c>, a pasta de instalação e o
///   <c>Global\MeetingApp</c> do mutex — renomear o executável deixa o atalho
///   de quem já instalou apontando para o vazio, e o mutex é o que impede o
///   instalador de copiar por cima de um app gravando;</description></item>
///   <item><description>os namespaces, os <c>AssemblyName</c> e os
///   <c>LogicalName</c> dos recursos embutidos — <c>Conteudo.cs</c> monta
///   <c>"MeetingApp.web." + caminho</c> por texto, e um rename silencioso ali
///   devolve página em branco;</description></item>
///   <item><description>o <c>PackageIdentifier</c> do winget, se e quando ele
///   for submetido: trocá-lo cria um pacote novo em vez de uma atualização.</description></item>
/// </list>
/// <para>
/// O símbolo segue o mesmo caminho: <c>assets/logo.svg</c> é a arte, e
/// <c>tools/gerar_icone.py</c> gera dela o ícone do .exe e os quatro da
/// bandeja. Os desenhos recusados ficam em <c>assets/marca-alternativas/</c>.
/// </para>
/// </remarks>
public static class Marca
{
    /// <summary>O nome que a pessoa lê.</summary>
    public const string Nome = "PulseMeet";
}
