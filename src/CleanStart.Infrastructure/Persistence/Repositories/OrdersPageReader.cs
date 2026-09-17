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
            // A condição do keyset na forma lógica: `created_at < X OR (created_at = X AND id < Y)`.
            //
            // O PostgreSQL tem uma forma mais limpa para isto — a comparação de tupla `(created_at, id) <
            // (:data, :id)`, que casa com o índice composto de maneira direta. **O EF Core não a traduz a
            // partir de LINQ:** não existe construção em C# que o tradutor converta em row-value comparison, e
            // a documentação do EF Core diz isso explicitamente, recomendando a expressão lógica manual para
            // chave de ordenação composta. Chegar à tupla exigiria `FromSql`, trocando a consulta tipada por
            // SQL solto para ganhar uma forma que o planejador resolve bem de qualquer jeito.
            //
            // **O custo real, medido com `EXPLAIN (ANALYZE, BUFFERS)`:** o índice é usado — o plano é
            // `Index Only Scan using ix_orders_created_at_id` —, mas a condição entra como `Filter`, não como
            // `Index Cond`. A diferença é que o índice é *percorrido* a partir do topo da ordenação e cada
            // linha é testada e descartada até chegar ao ponto do cursor, em vez de o banco posicionar-se
            // direto nele. É exatamente esse posicionamento que a comparação de tupla daria.
            //
            // Na prática isso só pesa quando a página fica longe do início e muitos pedidos compartilham a
            // mesma data: o trabalho descartado cresce com a distância, enquanto o da tupla seria constante.
            // Ainda assim é incomparavelmente melhor que `OFFSET`, que descarta as linhas no servidor **e**
            // as materializa antes.
            //
            // **O que medir:** `EXPLAIN (ANALYZE, BUFFERS)` numa página avançada. Espera-se
            // `Index Only Scan using ix_orders_created_at_id`. Dois sinais de alarme: `Seq Scan`, que
            // significa que o índice deixou de ser usado; e `Rows Removed by Filter` crescendo com o número
            // da página, que é o custo do `Filter` acima virando problema real. No segundo caso, a saída é a
            // tupla via `FromSql` — trocando a consulta tipada por SQL solto, com a compensação de perder a
            // composição dos filtros opcionais que este método monta acima.
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
