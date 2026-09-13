using CleanStart.Domain.Customers;
using CleanStart.Domain.Orders;
using Microsoft.EntityFrameworkCore;

namespace CleanStart.Infrastructure.Persistence.Repositories;

/// <summary>
/// Implementa <see cref="IOrderRepository"/> sobre o EF Core.
/// </summary>
/// <remarks>
/// <para>
/// Nenhum método devolve <c>IQueryable</c>: a consulta é montada e concluída aqui, de modo que dá para saber
/// lendo o handler quantas idas ao banco a chamada custa. Devolver consulta pela metade deixaria a Application
/// decidir filtro e projeção, ou seja, decidir detalhe de persistência.
/// </para>
/// <para>
/// <b>Nenhuma regra de negócio aqui.</b> O repositório carrega e registra; quem decide é o agregado.
/// </para>
/// </remarks>
internal sealed class OrderRepository(AppDbContext context) : IOrderRepository
{
    /// <inheritdoc />
    public async Task<Order?> GetByIdAsync(OrderId id, CancellationToken cancellationToken = default) =>
        await context.Orders
            // Include explícito: sem ele, Items viria vazia e o Total sairia zero — sem erro nenhum, o que é
            // pior que uma exception. O agregado só é válido carregado por inteiro.
            .Include(order => order.Items)
            .FirstOrDefaultAsync(order => order.Id == id, cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<Order>> GetByCustomerAsync(
        CustomerId customerId,
        CancellationToken cancellationToken = default) =>
        await context.Orders
            .Include(order => order.Items)
            .Where(order => order.CustomerId == customerId)
            .OrderByDescending(order => order.CreatedAt)
            .ToListAsync(cancellationToken);

    /// <inheritdoc />
    public void Add(Order order) => context.Orders.Add(order);
}
