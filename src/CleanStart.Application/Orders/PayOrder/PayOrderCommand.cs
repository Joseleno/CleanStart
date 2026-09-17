using CleanStart.Application.Common.Abstractions;
using CleanStart.Application.Common.Messaging;
using CleanStart.Application.Orders.GetOrderById;

namespace CleanStart.Application.Orders.PayOrder;

/// <summary>
/// Marca um pedido como pago.
/// </summary>
/// <remarks>
/// <para>
/// Não devolve valor — o pagamento é um fato, não uma consulta. Quem precisar do estado resultante lê o pedido
/// depois; o endpoint responde 204. É a mesma escolha do cancelamento, pelo mesmo motivo.
/// </para>
/// <para>
/// <b>Este caso de uso não cobra nada.</b> Ele registra que o pagamento foi confirmado — a captura no gateway,
/// o webhook do banco e a conciliação ficam fora do kit de propósito, porque cada um deles arrastaria uma
/// integração e uma dependência que não ensinam arquitetura. O que existe aqui é a transição de estado, que é
/// o que o domínio governa.
/// </para>
/// <para>
/// Invalida o cache pelo mesmo motivo do cancelamento: o pedido já pode ter sido lido e guardado pelo
/// <c>GET /orders/{id}</c>, e sem invalidar o cliente continuaria vendo "Pending" depois de pagar.
/// </para>
/// </remarks>
/// <param name="OrderId">Identidade do pedido a pagar.</param>
public sealed record PayOrderCommand(Guid OrderId) : ICommand, ICacheInvalidator
{
    /// <inheritdoc />
    /// <remarks>
    /// A chave vem de <c>GetOrderByIdQuery.ChaveDe</c>, nunca interpolada aqui: ela precisa ser exatamente a
    /// que o reader produziu, e duas interpolações divergem por um caractere sem que nada falhe.
    /// </remarks>
    public IReadOnlyList<string> ChavesInvalidadas => [GetOrderByIdQuery.ChaveDe(OrderId)];
}
