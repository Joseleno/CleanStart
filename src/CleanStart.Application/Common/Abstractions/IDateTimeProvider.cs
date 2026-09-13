namespace CleanStart.Application.Common.Abstractions;

/// <summary>
/// Fonte do instante atual.
/// </summary>
/// <remarks>
/// Existe por causa do teste, não por elegância: <c>DateTimeOffset.UtcNow</c> dentro de um handler torna o
/// teste não determinístico — ele passa hoje e falha na virada do mês, do ano ou do horário de verão. Com a
/// abstração, o teste fixa o tempo e afirma o valor exato.
/// <para>
/// <see cref="DateTimeOffset"/> e não <see cref="DateTime"/>: sem o offset, o mesmo instante em dois fusos vira
/// dois valores incomparáveis.
/// </para>
/// </remarks>
public interface IDateTimeProvider
{
    /// <summary>Agora, em UTC.</summary>
    DateTimeOffset UtcNow { get; }
}
