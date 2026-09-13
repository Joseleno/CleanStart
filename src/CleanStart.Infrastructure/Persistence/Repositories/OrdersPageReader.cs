using CleanStart.Application.Orders.ListOrders;
using CleanStart.Domain.Orders;
using Microsoft.EntityFrameworkCore;

namespace CleanStart.Infrastructure.Persistence.Repositories;

/// <summary>
/// Implementa <see cref="IOrdersPageReader"/> com paginação por keyset.
/// </summary>
/// <remarks>
/// <para>
/// <b>Sem cache</b>, ao contrário do <c>OrderReader</c>: cada combinação de filtro e cursor é uma chave distinta,
/// e um pedido novo torna obsoleto um número indeterminado delas. Sem invalidação confiável, o cache serviria
/// listagem desatualizada — e o erro não apareceria em lugar nenhum.
/// </para>
/// <para>
/// A consulta é projetada e sem rastreamento, como a do <c>OrderReader</c>, mas traz **resumo**: a listagem não
/// carrega os itens de cada pedido. Ver <c>OrderSummary</c>.
/// </para>
/// </remarks>
internal sealed class OrdersPageReader(AppDbContext context) : IOrdersPageReader
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<OrderSummary>> ListarAsync(
        OrdersFilter filtro,
        OrdersCursor? cursor,
        int tamanho,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filtro);

        IQueryable<Order> consulta = context.Orders.AsNoTracking();

        if (filtro.Status is not null)
        {
            consulta = consulta.Where(order => order.Status == filtro.Status);
        }

        if (filtro.De is not null)
        {
            consulta = consulta.Where(order => order.CreatedAt >= filtro.De);
        }

        if (filtro.Ate is not null)
        {
            consulta = consulta.Where(order => order.CreatedAt <= filtro.Ate);
        }

        if (cursor is not null)
        {
            // A condição do keyset, escrita como comparação de tupla para o PostgreSQL usar o índice composto:
            // `(created_at, id) < (:data, :id)`. Escrita como `created_at < X OR (created_at = X AND id < Y)`,
            // o planejador frequentemente não aproveita o índice e cai em scan — o oposto do que a paginação
            // por cursor pretende.
            DateTimeOffset data = cursor.CreatedAt;
            OrderId id = new(cursor.Id);

            consulta = consulta.Where(order =>
                order.CreatedAt < data || (order.CreatedAt == data && order.Id < id));
        }

        return await consulta
            // Ordenação estável: a data ordena, o id desempata. Sem o desempate, dois pedidos criados no mesmo
            // instante trocam de posição entre consultas e a fronteira da página fica ambígua — o item repetido
            // que o keyset existe para evitar volta pela porta dos fundos.
            .OrderByDescending(order => order.CreatedAt)
            .ThenByDescending(order => order.Id)
            .Take(tamanho)
            .Select(order => new OrderSummary(
                order.Id.Value,
                order.CustomerId.Value,
                order.Status.ToString(),
                order.Items.Sum(item => item.UnitPrice.Amount * item.Quantity),
                order.Currency,
                order.CreatedAt))
            .ToListAsync(cancellationToken);
    }
}
