namespace CleanStart.Domain.Common;

/// <summary>
/// Raiz de agregado: a única porta de entrada para alterar o grafo que ela governa, e a origem dos
/// domain events.
/// </summary>
/// <typeparam name="TId">Tipo da identidade.</typeparam>
/// <remarks>
/// <para>
/// Só a raiz tem repositório. Quem quer mudar um <c>OrderItem</c> passa pelo <c>Order</c> — é assim que
/// a invariante do conjunto ("o total é a soma dos itens") fica com um guardião, em vez de depender de
/// todo chamador lembrar dela.
/// </para>
/// <para>
/// Os eventos são acumulados, não publicados: a entidade não conhece mensageria. Quem persiste os
/// despacha <b>depois do commit</b> — evento publicado antes do commit pode anunciar um fato que a
/// transação ainda vai desfazer.
/// </para>
/// </remarks>
public abstract class AggregateRoot<TId> : Entity<TId>, IHasDomainEvents
    where TId : notnull
{
    private readonly List<IDomainEvent> _domainEvents = [];

    protected AggregateRoot(TId id)
        : base(id)
    {
    }

    /// <summary>
    /// Eventos levantados e ainda não despachados.
    /// </summary>
    /// <remarks>
    /// Exposto como <see cref="IReadOnlyCollection{T}"/> para que ninguém de fora acrescente nem limpe:
    /// levantar evento é decisão da regra de negócio, dentro do agregado. Devolver a <c>List</c> aqui
    /// entregaria o controle da invariante a qualquer chamador.
    /// </remarks>
    public IReadOnlyCollection<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    /// <summary>
    /// Registra que algo relevante aconteceu.
    /// </summary>
    protected void RaiseDomainEvent(IDomainEvent domainEvent)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);

        _domainEvents.Add(domainEvent);
    }

    /// <summary>
    /// Descarta os eventos acumulados, após o despacho.
    /// </summary>
    /// <remarks>
    /// Chamado pela infraestrutura depois de publicar. Sem isso, um segundo <c>SaveChanges</c> na mesma
    /// instância republicaria os mesmos eventos.
    /// </remarks>
    public void ClearDomainEvents() => _domainEvents.Clear();
}
