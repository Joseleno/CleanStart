using System.Linq.Expressions;
using System.Reflection;
using CleanStart.Application.Common.Abstractions;
using CleanStart.Domain.Common;
using CleanStart.Domain.Customers;
using CleanStart.Domain.Orders;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace CleanStart.Infrastructure.Persistence;

/// <summary>
/// Sessão com o banco de dados.
/// </summary>
/// <remarks>
/// <para>
/// Implementa <see cref="IUnitOfWork"/>, que é a única forma como a Application o alcança. O
/// <c>DbContext</c> em si nunca é injetado num handler: com ele em mãos, qualquer caso de uso poderia montar
/// consulta, e a fronteira entre aplicação e persistência deixaria de existir.
/// </para>
/// <para>
/// Os <c>DbSet</c> são expostos <c>internal</c>, não públicos: quem consulta são os repositórios, que vivem
/// neste assembly. Públicos, eles convidariam a Api a consultar direto.
/// </para>
/// </remarks>
public sealed class AppDbContext(DbContextOptions<AppDbContext> options)
    : DbContext(options), IUnitOfWork
{
    internal DbSet<Order> Orders => Set<Order>();

    internal DbSet<Customer> Customers => Set<Customer>();

    /// <summary>
    /// Mensagens de domain event aguardando despacho.
    /// </summary>
    /// <remarks>
    /// Gravadas pelo <c>DomainEventInterceptor</c> na mesma transação do dado. Ver <c>OutboxMessage</c> para o
    /// motivo de o padrão existir.
    /// </remarks>
    internal DbSet<Outbox.OutboxMessage> OutboxMessages => Set<Outbox.OutboxMessage>();

    /// <inheritdoc />
    Task<int> IUnitOfWork.SaveChangesAsync(CancellationToken cancellationToken) =>
        base.SaveChangesAsync(cancellationToken);

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        // Varre o assembly em vez de listar cada configuração: uma entidade nova com a sua
        // IEntityTypeConfiguration é encontrada sozinha. Listar à mão funciona até alguém esquecer — e o
        // sintoma é uma tabela mapeada por convenção, silenciosamente errada.
        modelBuilder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());

        AplicarFiltroDeSoftDelete(modelBuilder);

        base.OnModelCreating(modelBuilder);
    }

    /// <summary>
    /// Esconde das consultas todo registro marcado como excluído.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Aplicado por varredura do modelo, não entidade por entidade: basta implementar
    /// <see cref="ISoftDeletable"/> para o filtro valer. Escrever o <c>HasQueryFilter</c> à mão em cada
    /// configuração é o tipo de repetição em que alguém esquece uma — e o resultado é dado excluído aparecendo
    /// em relatório, sem erro nenhum no log.
    /// </para>
    /// <para>
    /// O filtro é global: quem precisa ver os excluídos usa <c>IgnoreQueryFilters()</c> explicitamente, que é
    /// visível em revisão.
    /// </para>
    /// </remarks>
    private static void AplicarFiltroDeSoftDelete(ModelBuilder modelBuilder)
    {
        foreach (IMutableEntityType tipo
            in modelBuilder.Model.GetEntityTypes())
        {
            if (!typeof(ISoftDeletable).IsAssignableFrom(tipo.ClrType))
            {
                continue;
            }

            modelBuilder.Entity(tipo.ClrType)
                .HasQueryFilter(ConstruirFiltro(tipo.ClrType));
        }
    }

    /// <summary>
    /// Monta a expressão <c>entidade =&gt; !entidade.IsDeleted</c> para o tipo informado.
    /// </summary>
    /// <remarks>
    /// Construída por árvore de expressão porque o <c>HasQueryFilter</c> não genérico exige um
    /// <c>LambdaExpression</c> — e o tipo só é conhecido em tempo de execução, durante a varredura.
    /// </remarks>
    private static LambdaExpression ConstruirFiltro(Type clrType)
    {
        ParameterExpression entidade =
            Expression.Parameter(clrType, "entidade");

        MemberExpression propriedade =
            Expression.Property(entidade, nameof(ISoftDeletable.IsDeleted));

        return Expression.Lambda(
            Expression.Not(propriedade),
            entidade);
    }
}
