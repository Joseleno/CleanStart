namespace CleanStart.Domain.Customers;

/// <summary>
/// Identidade de um cliente.
/// </summary>
/// <remarks>
/// Vive em <c>Customers</c> e não em <c>Orders</c> porque pertence ao agregado de clientes — o pedido
/// apenas a referencia. Ver <c>OrderId</c> para o motivo de identidade tipada.
/// </remarks>
/// <param name="Value">O valor subjacente.</param>
public readonly record struct CustomerId(Guid Value)
{
    /// <summary>Gera uma identidade nova (UUID v7, ordenado no tempo).</summary>
    public static CustomerId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString();
}
