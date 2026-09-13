using CleanStart.Application.Common.Abstractions;

namespace CleanStart.Infrastructure.Services;

/// <summary>
/// Relógio do sistema.
/// </summary>
/// <remarks>
/// A implementação trivial existe para que o resto do código dependa da abstração, e é só no teste que ela se
/// paga: lá, um substituto fixa o tempo e a asserção sobre CreatedAt pode ser exata.
/// </remarks>
internal sealed class SystemDateTimeProvider : IDateTimeProvider
{
    /// <inheritdoc />
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
