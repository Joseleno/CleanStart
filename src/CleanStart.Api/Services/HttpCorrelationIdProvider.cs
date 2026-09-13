using CleanStart.Application.Common.Abstractions;

namespace CleanStart.Api.Services;

/// <summary>
/// Correlation id da requisição HTTP em curso.
/// </summary>
/// <remarks>
/// <para>
/// Substitui o <c>ScopedCorrelationIdProvider</c> da Infrastructure, que gera um id novo e serve a quem roda fora
/// de HTTP (job, seed). Aqui o valor vem do cabeçalho quando existe, o que permite correlacionar a requisição
/// entre serviços.
/// </para>
/// <para>
/// É mutável por desenho: o <c>CorrelationIdMiddleware</c> define o valor uma vez, no início do escopo, e o resto
/// da requisição só lê. A alternativa — ler o <c>IHttpContextAccessor</c> a cada acesso — acopla quem consome ao
/// pipeline HTTP e devolve nulo em qualquer caminho fora dele.
/// </para>
/// </remarks>
internal sealed class HttpCorrelationIdProvider : ICorrelationIdProvider
{
    private string? _correlationId;

    /// <inheritdoc />
    /// <remarks>
    /// Gera um id se ninguém definiu — acontece fora do pipeline HTTP, e devolver vazio faria o log perder a
    /// correlação em silêncio.
    /// </remarks>
    public string CorrelationId => _correlationId ??= Guid.CreateVersion7().ToString();

    /// <summary>Define o id da requisição. Chamado uma vez pelo middleware.</summary>
    internal void Definir(string correlationId) => _correlationId = correlationId;
}
