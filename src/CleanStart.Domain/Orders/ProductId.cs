namespace CleanStart.Domain.Orders;

/// <summary>
/// Identidade de um produto.
/// </summary>
/// <remarks>
/// O catálogo de produtos não é modelado neste kit — o item de pedido guarda a referência, a descrição e o
/// preço praticados no momento da compra. Ver <c>OrderId</c> para o motivo de identidade tipada.
/// </remarks>
/// <param name="Value">O valor subjacente.</param>
public readonly record struct ProductId(Guid Value)
{
    /// <summary>Gera uma identidade nova (UUID v7, ordenado no tempo).</summary>
    public static ProductId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString();
}
