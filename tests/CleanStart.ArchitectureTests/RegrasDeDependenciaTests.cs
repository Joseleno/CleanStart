using System.Reflection;
using NetArchTest.Rules;

// `TestResult` existe no NetArchTest e no Xunit. O alias deixa explícito qual é qual.
using ArchTestResult = NetArchTest.Rules.TestResult;

namespace CleanStart.ArchitectureTests;

/// <summary>
/// Trava as regras de dependência entre camadas declaradas no <c>CLAUDE.md</c>:
/// <c>Api → Application → Domain</c>, <c>Domain → nada</c>, <c>Infrastructure → Domain + Application</c>.
/// </summary>
/// <remarks>
/// Estes testes existem antes do código de domínio de propósito: regra de arquitetura que chega depois
/// da primeira violação não é regra, é pedido de refatoração. Quando um deles reprovar, a resposta é
/// mover o código — nunca afrouxar o teste.
/// </remarks>
public sealed class RegrasDeDependenciaTests
{
    private static readonly Assembly Domain = typeof(CleanStart.Domain.AssemblyMarker).Assembly;
    private static readonly Assembly Application = typeof(CleanStart.Application.AssemblyMarker).Assembly;
    private static readonly Assembly Infrastructure = typeof(CleanStart.Infrastructure.AssemblyMarker).Assembly;

    private const string NamespaceApplication = "CleanStart.Application";
    private const string NamespaceInfrastructure = "CleanStart.Infrastructure";
    private const string NamespaceApi = "CleanStart.Api";
    private const string NamespaceEfCore = "Microsoft.EntityFrameworkCore";

    [Fact]
    public void Domain_NaoDependeDeNenhumaOutraCamada()
    {
        ArchTestResult resultado = Types.InAssembly(Domain)
            .Should()
            .NotHaveDependencyOnAny(NamespaceApplication, NamespaceInfrastructure, NamespaceApi)
            .GetResult();

        resultado.Should().NaoTerViolacao(
            "o Domain é o centro da arquitetura: tudo aponta para ele, ele não aponta para nada");
    }

    [Fact]
    public void Domain_NaoReferenciaEfCore()
    {
        ArchTestResult resultado = Types.InAssembly(Domain)
            .Should()
            .NotHaveDependencyOn(NamespaceEfCore)
            .GetResult();

        resultado.Should().NaoTerViolacao(
            "persistência é detalhe de infraestrutura; o domínio não sabe que existe banco de dados");
    }

    [Fact]
    public void Application_NaoDependeDeInfrastructureNemDeApi()
    {
        ArchTestResult resultado = Types.InAssembly(Application)
            .Should()
            .NotHaveDependencyOnAny(NamespaceInfrastructure, NamespaceApi)
            .GetResult();

        resultado.Should().NaoTerViolacao(
            "a Application declara as interfaces e a Infrastructure as implementa — a dependência aponta para dentro");
    }

    [Fact]
    public void Application_NaoReferenciaEfCore()
    {
        ArchTestResult resultado = Types.InAssembly(Application)
            .Should()
            .NotHaveDependencyOn(NamespaceEfCore)
            .GetResult();

        resultado.Should().NaoTerViolacao(
            "caso de uso fala com IRepository e IUnitOfWork, nunca com DbContext nem com IQueryable do EF");
    }

    [Fact]
    public void Infrastructure_NaoDependeDeApi()
    {
        ArchTestResult resultado = Types.InAssembly(Infrastructure)
            .Should()
            .NotHaveDependencyOn(NamespaceApi)
            .GetResult();

        resultado.Should().NaoTerViolacao(
            "a Infrastructure não conhece quem a consome: trocar a Api por um worker não deve tocá-la");
    }
}
