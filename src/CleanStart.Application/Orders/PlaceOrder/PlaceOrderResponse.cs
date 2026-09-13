namespace CleanStart.Application.Orders.PlaceOrder;

/// <summary>
/// O pedido criado, como o cliente da Api o vê.
/// </summary>
/// <remarks>
/// <para>
/// Existe para que a entidade <c>Order</c> não seja serializada direto. Três razões, todas práticas: expor a
/// entidade publica os domain events e a coleção interna; acopla o contrato HTTP ao modelo de domínio, de modo
/// que renomear uma propriedade interna quebra o cliente; e obriga a entidade a carregar atributos de
/// serialização que nada têm a ver com regra de negócio.
/// </para>
/// <para>
/// <c>Total</c> é <c>decimal</c> + <c>Currency</c> separados, e não um <c>Money</c>: o response é JSON, e o
/// value object não atravessa a fronteira — ele existe para proteger a regra dentro do domínio.
/// </para>
/// </remarks>
/// <param name="Id">Identidade do pedido criado.</param>
/// <param name="CustomerId">Cliente do pedido.</param>
/// <param name="Status">Situação, sempre <c>Pending</c> na criação.</param>
/// <param name="Total">Soma dos itens.</param>
/// <param name="Currency">Moeda do total.</param>
/// <param name="CreatedAt">Quando foi criado.</param>
/// <param name="Items">Itens do pedido.</param>
public sealed record PlaceOrderResponse(
    Guid Id,
    Guid CustomerId,
    string Status,
    decimal Total,
    string Currency,
    DateTimeOffset CreatedAt,
    IReadOnlyList<PlaceOrderItemResponse> Items);

/// <summary>
/// Um item do pedido criado.
/// </summary>
/// <param name="ProductId">Produto.</param>
/// <param name="Quantity">Quantidade.</param>
/// <param name="UnitPrice">Preço unitário praticado.</param>
/// <param name="Total">Preço unitário multiplicado pela quantidade.</param>
public sealed record PlaceOrderItemResponse(
    Guid ProductId,
    int Quantity,
    decimal UnitPrice,
    decimal Total);
