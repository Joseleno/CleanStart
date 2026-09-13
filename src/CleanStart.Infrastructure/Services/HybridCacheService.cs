using CleanStart.Application.Common.Abstractions;
using CleanStart.Infrastructure.Configuration;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Options;

namespace CleanStart.Infrastructure.Services;

/// <summary>
/// Implementa <see cref="ICacheService"/> sobre o <see cref="HybridCache"/>.
/// </summary>
/// <remarks>
/// <para>
/// O <c>HybridCache</c> junta dois níveis — memória local e distribuído — e traz <b>stampede protection</b>: se
/// cem requisições pedem a mesma chave ausente ao mesmo tempo, a factory roda uma vez e as outras esperam. Com
/// <c>IDistributedCache</c> cru, as cem iriam ao banco.
/// </para>
/// <para>
/// O prefixo de instância é aplicado aqui, não deixado para quem chama: duas aplicações apontando para o mesmo
/// Redis sem prefixo leem a chave uma da outra, e o sintoma é dado de um ambiente aparecendo no outro.
/// </para>
/// </remarks>
internal sealed class HybridCacheService(
    HybridCache cache,
    IOptions<RedisOptions> options)
    : ICacheService
{
    private readonly RedisOptions _options = options.Value;

    /// <inheritdoc />
    public async Task<T> GetOrCreateAsync<T>(
        string key,
        Func<CancellationToken, Task<T>> factory,
        TimeSpan? expiration = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(factory);

        HybridCacheEntryOptions entryOptions = new()
        {
            Expiration = expiration ?? TimeSpan.FromSeconds(_options.DefaultExpirationSeconds),
        };

        return await cache.GetOrCreateAsync(
            Prefixar(key),
            factory,
            static (state, token) => new ValueTask<T>(state(token)),
            entryOptions,
            cancellationToken: cancellationToken);
    }

    /// <inheritdoc />
    public async Task RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        await cache.RemoveAsync(Prefixar(key), cancellationToken);
    }

    private string Prefixar(string key) => $"{_options.InstanceName}{key}";
}
