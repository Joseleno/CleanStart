using CleanStart.Application.Common.Abstractions;

namespace CleanStart.Infrastructure.Services;

/// <summary>
/// Gera um correlation id por escopo, quando não vem de fora.
/// </summary>
/// <remarks>
/// <para>
/// Registrado como <c>Scoped</c>: o valor é criado uma vez e reusado por todo o escopo, que é o que faz os
/// registros de uma mesma operação compartilharem o id. Como <c>Transient</c>, cada injeção geraria um id
/// diferente e a correlação — a única razão de isto existir — não aconteceria.
/// </para>
/// <para>
/// A Api substitui esta implementação por uma que lê o cabeçalho da requisição, para que a correlação atravesse
/// serviços. Esta é o padrão para quem roda fora de HTTP.
/// </para>
/// </remarks>
internal sealed class ScopedCorrelationIdProvider : ICorrelationIdProvider
{
    /// <inheritdoc />
    public string CorrelationId { get; } = Guid.CreateVersion7().ToString();
}
