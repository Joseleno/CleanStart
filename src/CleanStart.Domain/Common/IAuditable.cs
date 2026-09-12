namespace CleanStart.Domain.Common;

/// <summary>
/// Entidade cujas datas de criação e alteração são preenchidas automaticamente.
/// </summary>
/// <remarks>
/// Quem preenche é um interceptor do EF Core, na Infrastructure — não a regra de negócio. Auditoria é
/// preocupação transversal: deixá-la no domínio significaria repetir a mesma atribuição em todo método
/// que altera estado, e esquecer em um deles.
/// <para>
/// <see cref="DateTimeOffset"/> e não <see cref="DateTime"/>: sem o offset, o mesmo instante gravado em
/// dois fusos vira dois valores incomparáveis.
/// </para>
/// </remarks>
public interface IAuditable
{
    /// <summary>Quando a entidade foi criada.</summary>
    DateTimeOffset CreatedAt { get; }

    /// <summary>Quando foi alterada pela última vez, ou nulo se nunca foi.</summary>
    DateTimeOffset? UpdatedAt { get; }
}
