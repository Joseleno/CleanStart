using AwesomeAssertions.Execution;
using AwesomeAssertions.Primitives;

using ArchTestResult = NetArchTest.Rules.TestResult;

namespace CleanStart.ArchitectureTests;

/// <summary>
/// Asserção para resultado de regra do NetArchTest que **nomeia os tipos violadores** quando falha.
/// </summary>
/// <remarks>
/// O padrão óbvio — <c>resultado.IsSuccessful.Should().BeTrue()</c> — produz a mensagem inútil
/// "expected true, found false": quem recebe esse build vermelho sabe que violou a arquitetura, mas não
/// onde. O NetArchTest já devolve a lista em <c>FailingTypeNames</c>; isto só a coloca na mensagem,
/// transformando o teste de "algo está errado" em "mova estas classes".
/// </remarks>
internal static class ArchTestResultExtensions
{
    public static ArchTestResultAssertions Should(this ArchTestResult resultado) => new(resultado);
}

internal sealed class ArchTestResultAssertions(ArchTestResult resultado)
    : ReferenceTypeAssertions<ArchTestResult, ArchTestResultAssertions>(resultado, AssertionChain.GetOrCreate())
{
    protected override string Identifier => "regra de arquitetura";

    /// <summary>
    /// Falha listando os tipos violadores, um por linha.
    /// </summary>
    /// <param name="porque">
    /// A razão da regra — entra na mensagem para que quem lê o build saiba *por que* ela existe,
    /// não só que ela existe.
    /// </param>
    public void NaoTerViolacao(string porque)
    {
        if (Subject.IsSuccessful)
        {
            return;
        }

        IEnumerable<string> violadores = Subject.FailingTypeNames ?? [];
        string lista = string.Join(Environment.NewLine, violadores.Select(tipo => $"  - {tipo}"));

        CurrentAssertionChain.FailWith(
            $"Violação de arquitetura — {porque}.{Environment.NewLine}Tipos violadores:{Environment.NewLine}{lista}");
    }
}
