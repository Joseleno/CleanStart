using CleanStart.Application.Common.Messaging;
using CleanStart.Domain.Common;

namespace CleanStart.Application.Orders.ListOrders;

/// <summary>
/// Orquestra a listagem paginada de pedidos.
/// </summary>
public sealed class ListOrdersHandler(IOrdersPageReader reader)
    : IQueryHandler<ListOrdersQuery, OrdersPage>
{
    public async ValueTask<Result<OrdersPage>> Handle(
        ListOrdersQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        OrdersCursor? cursor = null;

        if (query.Cursor is not null)
        {
            Result<OrdersCursor> decodificado = OrdersCursor.Decodificar(query.Cursor);

            if (decodificado.IsFailure)
            {
                return Result.Failure<OrdersPage>(decodificado.Error);
            }

            cursor = decodificado.Value;
        }

        int tamanho = Math.Clamp(query.Tamanho, 1, ListOrdersQuery.TamanhoMaximo);

        // Pede um item a mais do que o cliente quer. É o truque que responde "existe próxima página?" sem um
        // COUNT: se voltaram tamanho+1, há mais; o extra é descartado. A alternativa — contar o total — é a
        // consulta cara que a paginação por cursor existe para evitar.
        IReadOnlyList<OrderSummary> itens = await reader.ListarAsync(
            new OrdersFilter(query.Status, query.De, query.Ate),
            cursor,
            tamanho + 1,
            cancellationToken);

        bool temProxima = itens.Count > tamanho;

        List<OrderSummary> daPagina = temProxima
            ? [.. itens.Take(tamanho)]
            : [.. itens];

        // O cursor aponta para o último item DESTA página — é de onde a próxima continua.
        string? proximoCursor = temProxima && daPagina.Count > 0
            ? new OrdersCursor(daPagina[^1].CreatedAt, daPagina[^1].Id).Codificar()
            : null;

        return Result.Success(new OrdersPage(daPagina, proximoCursor));
    }
}
