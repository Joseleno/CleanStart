using CleanStart.Application.Common.Abstractions;
using CleanStart.Application.Common.Messaging;
using CleanStart.Application.Orders.GetOrderById;

namespace CleanStart.Application.Orders.CancelOrder;

/// <summary>
/// Cancela um pedido.
/// </summary>
/// <remarks>
/// <para>
/// Não devolve valor — o cancelamento é um fato, não uma consulta. Quem precisar do estado resultante lê o
/// pedido depois; o endpoint responde 204.
/// </para>
/// <para>
/// <b>Implementa <c>ICacheInvalidator</c>, e aqui o marcador finalmente se paga:</b> ao contrário da criação, o
/// cancelamento altera um pedido que <b>já pode ter sido lido e guardado</b> pelo <c>GET /orders/{id}</c>. Sem
/// invalidar, o cliente continuaria vendo "Pending" por até cinco minutos depois de cancelar.
/// </para>
/// </remarks>
/// <param name="OrderId">Identidade do pedido a cancelar.</param>
public sealed record CancelOrderCommand(Guid OrderId) : ICommand, ICacheInvalidator
{
    /// <inheritdoc />
    /// <remarks>
    /// A chave vem de <c>GetOrderByIdQuery.ChaveDe</c>, nunca interpolada aqui: ela precisa ser exatamente a
    /// que o reader produziu, e duas interpolações divergem por um caractere sem que nada falhe.
    /// <para>
    /// A listagem não entra: ela não é cacheada (ver T5.2), justamente porque um pedido alterado tornaria
    /// obsoleto um número indeterminado de combinações de filtro e cursor.
    /// </para>
    /// </remarks>
    public IReadOnlyList<string> ChavesInvalidadas => [GetOrderByIdQuery.ChaveDe(OrderId)];
}
