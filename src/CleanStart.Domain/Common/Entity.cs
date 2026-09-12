namespace CleanStart.Domain.Common;

/// <summary>
/// Base de entidade: igualdade por identidade, não por conteúdo.
/// </summary>
/// <typeparam name="TId">Tipo da identidade — sempre tipado (<c>OrderId</c>), nunca <c>Guid</c> cru.</typeparam>
/// <remarks>
/// Duas entidades são a mesma se têm o mesmo id, ainda que todos os outros campos difiram: um cliente que
/// trocou de e-mail continua o mesmo cliente. É o oposto de <see cref="ValueObject"/>, onde a identidade
/// é o próprio conteúdo.
/// </remarks>
public abstract class Entity<TId> : IEquatable<Entity<TId>>
    where TId : notnull
{
    protected Entity(TId id)
    {
        ArgumentNullException.ThrowIfNull(id);

        Id = id;
    }

    /// <summary>Identidade da entidade. Imutável por definição.</summary>
    public TId Id { get; }

    public bool Equals(Entity<TId>? other)
    {
        if (other is null)
        {
            return false;
        }

        if (ReferenceEquals(this, other))
        {
            return true;
        }

        // O tipo entra na comparação porque id igual em tipos diferentes não é a mesma entidade —
        // um Order e um Customer podem compartilhar o valor do Guid sem ter relação alguma.
        return GetType() == other.GetType() && EqualityComparer<TId>.Default.Equals(Id, other.Id);
    }

    public override bool Equals(object? obj) => obj is Entity<TId> other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(GetType(), Id);

    public static bool operator ==(Entity<TId>? left, Entity<TId>? right) =>
        left?.Equals(right) ?? right is null;

    public static bool operator !=(Entity<TId>? left, Entity<TId>? right) => !(left == right);
}
