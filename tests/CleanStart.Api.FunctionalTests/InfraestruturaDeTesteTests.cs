namespace CleanStart.Api.FunctionalTests;

/// <summary>
/// Prova que a infraestrutura de teste deste projeto está de pé: xUnit v3 descobre e roda,
/// e o AwesomeAssertions está referenciado e funcionando.
/// </summary>
/// <remarks>
/// Existe porque em Microsoft.Testing.Platform um projeto sem nenhum teste sai com código 8
/// ("zero tests ran") e reprova o <c>dotnet test</c> da solução inteira. Em vez de suprimir esse
/// código — que é justamente o que avisa quando um projeto deixa de descobrir testes em silêncio —
/// cada projeto nasce com um teste de fumaça. Ele sai daqui quando chegar o primeiro teste de verdade.
/// </remarks>
public sealed class InfraestruturaDeTesteTests
{
    [Fact]
    public void InfraestruturaDeTeste_EstaOperacional()
    {
        bool infraestruturaNoAr = true;

        infraestruturaNoAr.Should().BeTrue();
    }
}
