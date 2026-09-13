using CleanStart.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;
using Testcontainers.Redis;

namespace CleanStart.Api.FunctionalTests;

/// <summary>
/// Sobe a Api inteira em memória, com PostgreSQL e Redis em container.
/// </summary>
/// <remarks>
/// <para>
/// <b>A Api de verdade, não uma montagem de teste.</b> O <c>WebApplicationFactory</c> executa o <c>Program.cs</c>
/// real — os mesmos middlewares, o mesmo pipeline de behaviors, a mesma DI. O que se substitui é só a
/// configuração: as connection strings apontam para os containers.
/// </para>
/// <para>
/// É o que distingue teste funcional de teste de integração: aqui o exercício entra por HTTP e passa por tudo,
/// inclusive serialização, binding e tradução de erro — exatamente as três coisas que os testes das camadas de
/// baixo não alcançam.
/// </para>
/// <para>
/// <b>Sem depender do docker-compose de desenvolvimento</b>, que é o critério de aceite: os containers sobem e
/// caem com a suíte, e nenhum serviço local precisa estar rodando.
/// </para>
/// </remarks>
public sealed class CleanStartApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine")
        .WithDatabase("cleanstart_functional")
        .WithUsername("postgres")
        .WithPassword("postgres")
        .Build();

    private readonly RedisContainer _redis = new RedisBuilder("redis:7-alpine").Build();

    public async ValueTask InitializeAsync()
    {
        // Em paralelo: são independentes, e subir em série dobra o tempo de arranque da suíte.
        await Task.WhenAll(_postgres.StartAsync(), _redis.StartAsync());

        // Aplica as migrations de verdade — é o mesmo caminho que a aplicação usa em produção. EnsureCreated
        // montaria o schema do modelo e passaria mesmo com a migration quebrada.
        using IServiceScope scope = Services.CreateScope();
        AppDbContext contexto = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await contexto.Database.MigrateAsync();
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        await Task.WhenAll(_postgres.DisposeAsync().AsTask(), _redis.DisposeAsync().AsTask());
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.UseEnvironment("Development");

        // Só a configuração é substituída, não o registro de serviços: trocar implementação aqui faria o teste
        // exercitar uma composição que não existe em produção.
        builder.UseSetting("Database:ConnectionString", _postgres.GetConnectionString());
        builder.UseSetting("Redis:ConnectionString", _redis.GetConnectionString());

        // A chave JWT é validada no startup (ValidateOnStart) — sem ela a aplicação nem sobe. É um valor de
        // teste, com o tamanho mínimo que a validação exige.
        builder.UseSetting("Jwt:SigningKey", new string('t', 32));
    }

    /// <summary>
    /// Executa uma ação com um escopo de DI próprio.
    /// </summary>
    /// <remarks>
    /// Usado para semear dados: não há endpoint de cliente, e inserir por SQL cru deixaria o teste dependente do
    /// nome das colunas em vez do modelo.
    /// </remarks>
    public async Task ComEscopoAsync(Func<AppDbContext, Task> acao)
    {
        ArgumentNullException.ThrowIfNull(acao);

        using IServiceScope scope = Services.CreateScope();
        AppDbContext contexto = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        await acao(contexto);
    }
}
