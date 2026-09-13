using CleanStart.Application.Common.Messaging;
using CleanStart.Domain.Common;
using CleanStart.Domain.Orders;

namespace CleanStart.Application.Orders.ListOrders;

/// <summary>
/// Leitura paginada de pedidos.
/// </summary>
/// <remarks>
/// Declarada na pasta do slice, como o <c>IOrderReader</c> do GetOrderById: cada caso de uso define a leitura de
/// que precisa. Uma interface compartilhada cresceria até virar o repositório genérico que o CQRS evita.
/// </remarks>
public interface IOrdersPageReader
{
    /// <summary>
    /// Traz uma página de pedidos a partir do cursor.
    /// </summary>
    /// <param name="filtro">Status e período, ambos opcionais.</param>
    /// <param name="cursor">Onde continuar, ou nulo para a primeira página.</param>
    /// <param name="tamanho">Quantos itens trazer.</param>
    Task<IReadOnlyList<OrderSummary>> ListarAsync(
        OrdersFilter filtro,
        OrdersCursor? cursor,
        int tamanho,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Os filtros da listagem.
/// </summary>
/// <remarks>
/// Um tipo em vez de três parâmetros soltos: acrescentar um filtro novo passa a ser mudança num lugar, e não na
/// assinatura de toda a cadeia.
/// </remarks>
/// <param name="Status">Situação, ou nulo para todas.</param>
/// <param name="De">Início do período por data de criação, ou nulo.</param>
/// <param name="Ate">Fim do período, ou nulo.</param>
public sealed record OrdersFilter(OrderStatus? Status, DateTimeOffset? De, DateTimeOffset? Ate);
