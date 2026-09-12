using CleanStart.Domain.Common;

namespace CleanStart.Domain.Orders.Events;

/// <summary>
/// Um pedido foi cancelado.
/// </summary>
/// <remarks>
/// Guarda o estado anterior ao cancelamento: quem reage costuma precisar dele — estornar um pedido que
/// estava <c>Paid</c> é diferente de descartar um que estava apenas <c>Pending</c>.
/// </remarks>
public sealed record OrderCancelledEvent(
    OrderId OrderId,
    OrderStatus PreviousStatus,
    DateTimeOffset OccurredOn) : IDomainEvent;
