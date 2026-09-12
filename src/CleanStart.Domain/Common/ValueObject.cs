namespace CleanStart.Domain.Common;

/// <summary>
/// Base de value object: igualdade estrutural, definida pelos componentes que a subclasse declara.
/// </summary>
/// <remarks>
/// <para>
/// Value object não tem identidade — dois <c>Money(10, "BRL")</c> são o mesmo valor, como dois <c>10</c>
/// são o mesmo número. É o oposto de <see cref="Entity{TId}"/>, onde dois objetos com os mesmos dados e
/// ids diferentes são coisas diferentes.
/// </para>
/// <para>
/// A igualdade usa os componentes <b>declarados</b> em <see cref="GetEqualityComponents"/>, e não
/// reflexão sobre as propriedades. A escolha é deliberada: com reflexão, acrescentar um campo mudaria a
/// semântica de igualdade em silêncio, sem ninguém decidir — além de custar caro em comparação repetida.
/// Aqui, o que entra na igualdade é uma decisão visível no código.
/// </para>
/// </remarks>
public abstract class ValueObject : IEquatable<ValueObject>
{
    /// <summary>
    /// Os valores que definem a igualdade deste value object, na ordem.
    /// </summary>
    /// <example>
    /// <code>
    /// protected override IEnumerable&lt;object?&gt; GetEqualityComponents()
    /// {
    ///     yield return Amount;
    ///     yield return Currency;
    /// }
    /// </code>
    /// </example>
    protected abstract IEnumerable<object?> GetEqualityComponents();

    public bool Equals(ValueObject? other)
    {
        if (other is null)
        {
            return false;
        }

        if (ReferenceEquals(this, other))
        {
            return true;
        }

        // Tipos diferentes nunca são iguais, ainda que os componentes coincidam: um Money(1,"BRL") não
        // é um Quantity(1,"BRL"). Sem esta checagem, dois value objects distintos com a mesma forma
        // passariam por iguais.
        return GetType() == other.GetType()
            && GetEqualityComponents().SequenceEqual(other.GetEqualityComponents());
    }

    public override bool Equals(object? obj) => obj is ValueObject other && Equals(other);

    public override int GetHashCode()
    {
        HashCode hash = default;

        foreach (object? component in GetEqualityComponents())
        {
            hash.Add(component);
        }

        return hash.ToHashCode();
    }

    public static bool operator ==(ValueObject? left, ValueObject? right) =>
        left?.Equals(right) ?? right is null;

    public static bool operator !=(ValueObject? left, ValueObject? right) => !(left == right);
}
