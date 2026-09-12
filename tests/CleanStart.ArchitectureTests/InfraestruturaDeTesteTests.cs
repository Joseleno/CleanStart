using System.Reflection;
using NetArchTest.Rules;

// `TestResult` existe no NetArchTest e no Xunit. O alias deixa explícito qual é qual.
using ArchTestResult = NetArchTest.Rules.TestResult;

namespace CleanStart.ArchitectureTests;

/// <summary>
/// Prova que a infraestrutura deste projeto está de pé — xUnit v3, AwesomeAssertions e NetArchTest —
/// exercitando a regra mais importante da solução em vez de uma asserção trivial.
/// </summary>
/// <remarks>
/// O conjunto completo de regras de arquitetura é a T0.3. Este arquivo sai de cena quando ela chegar.
/// </remarks>
public sealed class InfraestruturaDeTesteTests
{
    private static readonly Assembly Domain = typeof(CleanStart.Domain.AssemblyMarker).Assembly;

    [Fact]
    public void Domain_NaoDependeDeNenhumaOutraCamada()
    {
        ArchTestResult resultado = Types.InAssembly(Domain)
            .Should()
            .NotHaveDependencyOnAny(
                "CleanStart.Application",
                "CleanStart.Infrastructure",
                "CleanStart.Api")
            .GetResult();

        resultado.IsSuccessful.Should().BeTrue();
    }
}
