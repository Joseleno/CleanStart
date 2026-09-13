namespace CleanStart.Application.Orders.ListOrders;

/// <summary>
/// Uma página de pedidos.
/// </summary>
/// <remarks>
/// <para>
/// <b>Não traz total de registros</b>, e é consequência direta do keyset: contar exigiria um
/// <c>SELECT COUNT(*)</c> sobre o filtro inteiro a cada página — a consulta cara que a paginação por cursor
/// existe para evitar. Quem precisa de "1.200 resultados" paga esse custo numa chamada própria e consciente.
/// </para>
/// <para>
/// <c>ProximoCursor</c> nulo significa fim da lista. É o sinal que o cliente usa para parar de pedir.
/// </para>
/// </remarks>
/// <param name="Itens">Os pedidos da página.</param>
/// <param name="ProximoCursor">Onde continuar, ou nulo se acabou.</param>
public sealed record OrdersPage(
    IReadOnlyList<OrderSummary> Itens,
    string? ProximoCursor);

/// <summary>
/// Um pedido na listagem.
/// </summary>
/// <remarks>
/// Resumo, não o pedido inteiro: a listagem não traz os itens de cada pedido. Trazê-los faria uma página de 20
/// pedidos virar dezenas de linhas materializadas, e quem quer o detalhe chama
/// <c>GET /api/v1/orders/{id}</c> — que é cacheado, ao contrário da listagem.
/// </remarks>
/// <param name="Id">Identidade do pedido.</param>
/// <param name="CustomerId">Cliente do pedido.</param>
/// <param name="Status">Situação atual.</param>
/// <param name="Total">Soma dos itens.</param>
/// <param name="Currency">Moeda do total.</param>
/// <param name="CreatedAt">Quando foi criado.</param>
public sealed record OrderSummary(
    Guid Id,
    Guid CustomerId,
    string Status,
    decimal Total,
    string Currency,
    DateTimeOffset CreatedAt);
