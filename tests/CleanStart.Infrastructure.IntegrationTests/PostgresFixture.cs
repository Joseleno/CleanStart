using CleanStart.Application.Common.Abstractions;
using CleanStart.Infrastructure.Configuration;
using CleanStart.Infrastructure.Persistence;
using CleanStart.Infrastructure.Persistence.Interceptors;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Testcontainers.PostgreSql;

namespace CleanStart.Infrastructure.IntegrationTests;

/// <summary>
/// Sobe um PostgreSQL em container, aplica as migrations e entrega contextos por teste.
/// </summary>
/// <remarks>
/// <para>
/// <b>PostgreSQL de verdade, nunca o provider InMemory.</b> O InMemory não tem constraint, não tem transação e
/// não fala SQL — um teste que passa nele não diz nada sobre o banco real. É proibido pelo <c>CLAUDE.md</c>, e
/// esta fase é justamente onde a proibição se paga: a migration, o filtro parcial do índice único e a tradução
/// LINQ dos conversores de valor só se provam contra o Postgres.
/// </para>
/// <para>
/// O container sobe uma vez por classe de teste (<c>IAsyncLifetime</c>) e é derrubado no fim — nenhum banco local
/// precisa estar instalado, que é o critério de aceite da T3.4.
/// </para>
/// </remarks>
public sealed class PostgresFixture : IAsyncLifetime
{
    /// <summary>
    /// Tempo fixo, para que as asserções de auditoria sejam exatas.
    /// </summary>
    public static readonly DateTimeOffset Agora = new(2026, 9, 12, 10, 30, 0, TimeSpan.Zero);

    /// <summary>Usuário fictício, para conferir <c>CreatedBy</c>.</summary>
    public static readonly Guid Usuario = Guid.CreateVersion7();

    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:17-alpine")
        .WithDatabase("cleanstart_tests")
        .WithUsername("postgres")
        .WithPassword("postgres")
        .Build();

    public async ValueTask InitializeAsync()
    {
        await _container.StartAsync();

        // Aplica as migrations de verdade, em vez de EnsureCreated: é o que prova que a migration gerada
        // funciona. EnsureCreated monta o schema a partir do modelo e passaria mesmo com migration quebrada.
        await using AppDbContext contexto = CriarContexto();
        await contexto.Database.MigrateAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await _container.DisposeAsync();
    }

    /// <summary>
    /// Cria um contexto novo, com os três interceptors na ordem de produção.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Um contexto por chamada, não um compartilhado: o change tracker guarda as entidades que já viu, e reusar
    /// o contexto faria um teste ler do cache em vez do banco — passando sem provar que a gravação funcionou.
    /// </para>
    /// <para>
    /// <b>O <c>UseNpgsql</c> espelha o de produção, incluindo o <c>EnableRetryOnFailure</c>.</b> A diferença não
    /// é cosmética: com a estratégia de retry ligada, o EF Core recusa transação iniciada pelo usuário
    /// (<c>BeginTransactionAsync</c>) fora de <c>CreateExecutionStrategy().ExecuteAsync(...)</c> — e falha em
    /// runtime, não na compilação. Uma fixture sem retry aprovaria justamente o código que quebra no primeiro
    /// tick em produção. É o mesmo princípio que proíbe o provider InMemory: teste que não reproduz a
    /// configuração real aprova o que o banco real reprova.
    /// </para>
    /// </remarks>
    public AppDbContext CriarContexto()
    {
        IDateTimeProvider clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(Agora);

        ICurrentUser currentUser = Substitute.For<ICurrentUser>();
        currentUser.Id.Returns(Usuario);

        DatabaseOptions padroes = new();

        DbContextOptions<AppDbContext> options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(_container.GetConnectionString(), npgsql =>
            {
                npgsql.CommandTimeout(padroes.CommandTimeoutSeconds);
                npgsql.EnableRetryOnFailure(padroes.MaxRetryCount);
            })
            .AddCleanStartInterceptors(
                new SoftDeleteInterceptor(clock),
                new AuditableInterceptor(clock, currentUser),
                new DomainEventInterceptor())
            .Options;

        return new AppDbContext(options);
    }
}
