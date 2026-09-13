using CleanStart.Application.Common.Abstractions;
using CleanStart.Domain.Customers;
using CleanStart.Domain.Orders;
using CleanStart.Infrastructure;
using CleanStart.Infrastructure.Configuration;
using CleanStart.Infrastructure.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace CleanStart.Infrastructure.IntegrationTests;

/// <summary>
/// Verifica que o contêiner resolve tudo o que a Application declara, e que a configuração inválida é barrada.
/// </summary>
/// <remarks>
/// <para>
/// Registro errado de DI não quebra o build: ele quebra ao <b>resolver</b> — uma dependência faltando, um tempo
/// de vida incompatível, um tipo não registrado. Estes testes resolvem de verdade, com escopo, que é o que prova
/// o registro.
/// </para>
/// <para>
/// <b>Sem banco.</b> Resolver o <c>AppDbContext</c> não abre conexão — isso só acontece na primeira consulta.
/// </para>
/// </remarks>
public sealed class DependencyInjectionTests
{
    private static IConfiguration ConfiguracaoValida(
        string? connectionString = "Host=localhost;Database=cleanstart;Username=postgres;Password=x") =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Database:ConnectionString"] = connectionString,
                ["Jwt:Issuer"] = "cleanstart",
                ["Jwt:Audience"] = "cleanstart-api",
                ["Jwt:SigningKey"] = new string('k', 32),
            })
            .Build();

    private static ServiceProvider Construir(IConfiguration configuration)
    {
        ServiceCollection services = new();
        services.AddLogging();
        services.AddInfrastructure(configuration);

        return services.BuildServiceProvider(validateScopes: true);
    }

    [Theory]
    [InlineData(typeof(IUnitOfWork))]
    [InlineData(typeof(IOrderRepository))]
    [InlineData(typeof(ICustomerRepository))]
    [InlineData(typeof(ICacheService))]
    [InlineData(typeof(IDateTimeProvider))]
    [InlineData(typeof(ICorrelationIdProvider))]
    [InlineData(typeof(ICurrentUser))]
    public void TodasAsAbstracoesDaApplication_SaoResolviveis(Type servico)
    {
        // Se a Application declara uma interface que ninguém registrou, o erro aparece aqui — não na primeira
        // requisição que precisar dela.
        using ServiceProvider provider = Construir(ConfiguracaoValida());
        using IServiceScope scope = provider.CreateScope();

        object? resolvido = scope.ServiceProvider.GetService(servico);

        resolvido.Should().NotBeNull($"{servico.Name} é declarado pela Application e precisa de implementação");
    }

    [Fact]
    public void IUnitOfWork_EOMesmoAppDbContextDoEscopo()
    {
        // É a decisão de não ter classe UnitOfWork separada: a mesma instância serve as duas pontas. Se fossem
        // objetos diferentes, o repositório gravaria num contexto e o commit aconteceria em outro — e nada
        // seria persistido, sem erro nenhum.
        using ServiceProvider provider = Construir(ConfiguracaoValida());
        using IServiceScope scope = provider.CreateScope();

        IUnitOfWork unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        AppDbContext contexto = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        unitOfWork.Should().BeSameAs(contexto);
    }

    [Fact]
    public void CorrelationId_EOMesmoDentroDoEscopoEDiferenteEntreEscopos()
    {
        using ServiceProvider provider = Construir(ConfiguracaoValida());

        string primeiro;
        string segundo;

        using (IServiceScope escopo = provider.CreateScope())
        {
            // Duas resoluções no mesmo escopo têm de dar o mesmo id, senão a correlação não acontece.
            primeiro = escopo.ServiceProvider.GetRequiredService<ICorrelationIdProvider>().CorrelationId;
            string mesmoEscopo = escopo.ServiceProvider.GetRequiredService<ICorrelationIdProvider>().CorrelationId;

            mesmoEscopo.Should().Be(primeiro, "Scoped, não Transient");
        }

        using (IServiceScope outroEscopo = provider.CreateScope())
        {
            segundo = outroEscopo.ServiceProvider.GetRequiredService<ICorrelationIdProvider>().CorrelationId;
        }

        segundo.Should().NotBe(primeiro, "operações distintas não compartilham correlation id");
    }

    [Fact]
    public void ConfiguracaoSemConnectionString_FalhaAoValidar()
    {
        // ValidateOnStart transforma configuração ausente em falha de startup. Sem isso, a aplicação subiria e
        // quebraria na primeira requisição, em produção.
        using ServiceProvider provider = Construir(ConfiguracaoValida(connectionString: null));

        Action validar = () => _ = provider.GetRequiredService<IOptions<DatabaseOptions>>().Value;

        validar.Should().Throw<OptionsValidationException>()
            .WithMessage("*connection string*");
    }

    [Fact]
    public void ChaveJwtCurta_FalhaAoValidar()
    {
        IConfiguration configuracao = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Database:ConnectionString"] = "Host=localhost;Database=x;Username=u;Password=p",
                ["Jwt:Issuer"] = "cleanstart",
                ["Jwt:Audience"] = "cleanstart-api",
                ["Jwt:SigningKey"] = "curta",
            })
            .Build();

        using ServiceProvider provider = Construir(configuracao);

        // Chave menor que 256 bits é preenchida ou rejeitada conforme a biblioteca — nos dois casos, a
        // segurança que se acredita ter não existe. Melhor falhar ao subir.
        Action validar = () => _ = provider.GetRequiredService<IOptions<JwtOptions>>().Value;

        validar.Should().Throw<OptionsValidationException>()
            .WithMessage("*32 caracteres*");
    }

    [Fact]
    public void SemRedisConfigurado_OCacheAindaFunciona()
    {
        // O segundo nível é opcional: a aplicação sobe sem Redis e usa cache local. O que muda é ele não ser
        // compartilhado entre instâncias — decisão registrada na configuração, não surpresa em produção.
        using ServiceProvider provider = Construir(ConfiguracaoValida());
        using IServiceScope scope = provider.CreateScope();

        ICacheService cache = scope.ServiceProvider.GetRequiredService<ICacheService>();

        cache.Should().NotBeNull();
        provider.GetRequiredService<IOptions<RedisOptions>>().Value.Enabled.Should().BeFalse();
    }
}
