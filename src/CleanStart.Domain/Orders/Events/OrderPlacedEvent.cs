using CleanStart.Domain.Common;
using CleanStart.Domain.Customers;

namespace CleanStart.Domain.Orders.Events;

/// <summary>
/// Um pedido foi criado.
/// </summary>
/// <remarks>
/// Carrega os dados que quem reage precisa, não a entidade inteira: o handler que manda o e-mail de
/// confirmação não deve receber um <c>Order</c> mutável, e o evento precisa sobreviver a serialização se um
/// dia sair do processo.
/// </remarks>
public sealed record OrderPlacedEvent(
    OrderId OrderId,
    CustomerId CustomerId,
    decimal Total,
    string Currency,
    DateTimeOffset OccurredOn) : IDomainEvent;
