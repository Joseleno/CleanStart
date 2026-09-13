namespace CleanStart.Domain.Common;

/// <summary>
/// Algo que acumula domain events — implementado por toda raiz de agregado.
/// </summary>
/// <remarks>
/// Existe porque <see cref="AggregateRoot{TId}"/> é genérico, e quem varre o change tracker não conhece o
/// <c>TId</c> de cada entidade em tempo de compilação. Uma interface não genérica é o que permite perguntar
/// "isto tem eventos?" sem refletir sobre argumentos de tipo.
/// <para>
/// É contrato de domínio, não concessão à infraestrutura: qualquer despachante de eventos precisa da mesma
/// pergunta, venha ele do EF Core ou de outro lugar.
/// </para>
/// </remarks>
public interface IHasDomainEvents
{
    /// <summary>Eventos levantados e ainda não despachados.</summary>
    IReadOnlyCollection<IDomainEvent> DomainEvents { get; }

    /// <summary>Descarta os eventos acumulados, após o despacho.</summary>
    void ClearDomainEvents();
}
