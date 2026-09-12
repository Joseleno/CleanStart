namespace CleanStart.Domain.Common;

/// <summary>
/// Algo relevante que aconteceu no domínio, no passado.
/// </summary>
/// <remarks>
/// O nome do evento é sempre pretérito (<c>OrderPlacedEvent</c>, não <c>PlaceOrderEvent</c>): ele relata
/// um fato consumado, não pede uma ação. Quem reage a ele não pode impedi-lo.
/// </remarks>
public interface IDomainEvent
{
    /// <summary>Quando o fato ocorreu.</summary>
    DateTimeOffset OccurredOn { get; }
}
