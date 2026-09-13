using CleanStart.Application.Common.Messaging;

namespace CleanStart.Application.Orders.PlaceOrder;

/// <summary>
/// Pedido de criação de um pedido.
/// </summary>
/// <remarks>
/// <para>
/// Tipos primitivos na fronteira, de propósito: o command é desserializado do corpo da requisição, e
/// <c>Money</c> ou <c>OrderId</c> não se materializam de JSON sem passar pelas factories que os validam.
/// A conversão para tipo de domínio acontece no handler, onde a falha pode virar <c>Result</c>.
/// </para>
/// <para>
/// <c>record</c> porque mensagem é dado em trânsito — imutável e comparada por valor. Há regra de arquitetura
/// exigindo isso.
/// </para>
/// </remarks>
/// <param name="CustomerId">Cliente que faz o pedido.</param>
/// <param name="Currency">Moeda do pedido, em três letras (ex.: <c>BRL</c>).</param>
/// <param name="Items">Itens do pedido. Precisa de ao menos um.</param>
public sealed record PlaceOrderCommand(
    Guid CustomerId,
    string Currency,
    IReadOnlyList<PlaceOrderItemRequest> Items) : ICommand<PlaceOrderResponse>;

/// <summary>
/// Um item dentro de <see cref="PlaceOrderCommand"/>.
/// </summary>
/// <param name="ProductId">Produto comprado.</param>
/// <param name="Quantity">Quantidade. O domínio exige maior que zero.</param>
/// <param name="UnitPrice">Preço unitário praticado.</param>
public sealed record PlaceOrderItemRequest(Guid ProductId, int Quantity, decimal UnitPrice);
