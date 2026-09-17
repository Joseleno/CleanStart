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

    /// <summary>
    /// Famílias de biblioteca que o Domain não pode alcançar.
    /// </summary>
    /// <remarks>
    /// A lista é por prefixo, então <c>Microsoft</c> cobre EF Core, <c>Microsoft.Extensions.*</c> e o resto.
    /// Isso inclui abstrações aparentemente inofensivas como <c>ILogger</c> e <c>IOptions</c> — deliberado:
    /// domínio que loga ou lê configuração deixou de ser domínio.
    /// <para>
    /// <b>Acrescente aqui</b> ao adotar uma biblioteca nova que não deva chegar ao Domain. É o custo de a
    /// regra ser blocklist, e o motivo de a allowlist ser preferível — ver o comentário da regra.
    /// </para>
    /// </remarks>
    private static readonly string[] NamespacesProibidosNoDomain =
    [
        "Microsoft",
        "Npgsql",
        "Dapper",
        "Newtonsoft",
        "StackExchange",
        "FluentValidation",
        "Mediator",
        "Riok",
        "Carter",
        "Serilog",
        "OpenTelemetry",
        "Polly",
    ];

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

    /// <summary>
    /// O Domain não alcança nenhuma biblioteca de infraestrutura.
    /// </summary>
    /// <remarks>
    /// <b>Era uma blocklist de um item só</b> (<c>NotHaveDependencyOn(EfCore)</c>): Dapper, Newtonsoft ou o
    /// pacote da vez entrariam no Domain sem que nada acusasse. A T9.2 previa inverter para allowlist, que é
    /// a forma certa — o que não foi autorizado reprova por omissão, em vez de passar por omissão.
    /// <para>
    /// <b>A allowlist não é implementável com o NetArchTest 1.3.2, e isso foi verificado.</b> O
    /// <c>OnlyHaveDependenciesOn</c> conta a closure que o compilador gera dentro da classe para cada lambda
    /// de LINQ, e ela não pertence a namespace nenhum: nenhuma lista a alcança. O controle foi <c>Money</c>,
    /// que passa com <c>["System", "CleanStart.Domain"]</c>, contra <c>Document</c> — que usa
    /// <c>.Where()</c> e <c>.All()</c> — e reprova até com a lista estendida com <c>System.Linq</c>,
    /// <c>System.Collections</c> e o blob do compilador.
    /// </para>
    /// <para>
    /// O que ficou no lugar é a blocklist <b>ampliada</b>: em vez de um item, todas as famílias que na
    /// prática alguém importaria por engano. Não cobre um pacote novo imprevisto, e essa é a diferença
    /// honesta entre ela e a allowlist. Fica registrado como pendência no HANDOFF.
    /// </para>
    /// </remarks>
    [Fact]
    public void Domain_NaoDependeDeBibliotecaDeInfraestrutura()
    {
        ArchTestResult resultado = Types.InAssembly(Domain)
            .Should()
            .NotHaveDependencyOnAny(NamespacesProibidosNoDomain)
            .GetResult();

        resultado.Should().NaoTerViolacao(
            "o domínio é a camada que sobrevive à troca de qualquer biblioteca: se ele depende de uma delas, "
            + "a próxima migração passa por dentro das regras de negócio");
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
