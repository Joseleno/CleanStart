namespace CleanStart.Application.Orders.GetOrderById;

/// <summary>
/// Um pedido, como a leitura o devolve.
/// </summary>
/// <remarks>
/// <para>
/// Tipo próprio da query, e não o mesmo do <c>PlaceOrder</c>: o response de criação e o de leitura coincidem hoje
/// e divergem amanhã — a leitura tende a ganhar campo derivado, o de criação não. Compartilhá-los faria a
/// primeira divergência virar mudança em dois casos de uso.
/// </para>
/// <para>
/// Preenchido por **projeção direta no banco**, sem materializar o agregado: a consulta traz exatamente estas
/// colunas. Carregar o <c>Order</c> inteiro para depois descartar metade é trabalho que o banco não precisava
/// fazer.
/// </para>
/// </remarks>
/// <param name="Id">Identidade do pedido.</param>
/// <param name="CustomerId">Cliente do pedido.</param>
/// <param name="Status">Situação atual.</param>
/// <param name="Total">Soma dos itens.</param>
/// <param name="Currency">Moeda do total.</param>
/// <param name="CreatedAt">Quando foi criado.</param>
/// <param name="UpdatedAt">Quando foi alterado, ou nulo.</param>
/// <param name="Items">Itens do pedido.</param>
public sealed record OrderResponse(
    Guid Id,
    Guid CustomerId,
    string Status,
    decimal Total,
    string Currency,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt,
    IReadOnlyList<OrderItemResponse> Items);

/// <summary>
/// Um item do pedido lido.
/// </summary>
/// <param name="ProductId">Produto.</param>
/// <param name="Quantity">Quantidade.</param>
/// <param name="UnitPrice">Preço unitário praticado.</param>
/// <param name="Total">Preço unitário multiplicado pela quantidade.</param>
public sealed record OrderItemResponse(
    Guid ProductId,
    int Quantity,
    decimal UnitPrice,
    decimal Total);
