using CleanStart.Application.Common.Abstractions;
using CleanStart.Domain.Common;
using Mediator;
using Microsoft.Extensions.Logging;

namespace CleanStart.Application.Common.Behaviors;

/// <summary>
/// Serve do cache o resultado de consultas que implementam <see cref="ICacheable"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Opt-in:</b> só intercepta quem implementa <see cref="ICacheable"/>. O padrão é não cachear, porque cache é
/// decisão consciente sobre um dado específico — dado cacheado por descuido é dado velho servido sem que ninguém
/// tenha escolhido esse risco.
/// </para>
/// <para>
/// <b>Só consulta.</b> Comando que implementasse <see cref="ICacheable"/> seria servido do cache, ou seja, não
/// executado — e a alteração simplesmente não aconteceria. A checagem de <c>IBaseQuery</c> existe para que esse
/// erro não seja possível, em vez de ser proibido por convenção.
/// </para>
/// </remarks>
public sealed class CachingBehavior<TMessage, TResponse>(
    ICacheService cache,
    ILogger<CachingBehavior<TMessage, TResponse>> logger)
    : IPipelineBehavior<TMessage, TResponse>
    where TMessage : IMessage
{
    public async ValueTask<TResponse> Handle(
        TMessage message,
        MessageHandlerDelegate<TMessage, TResponse> next,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(next);

        if (message is not ICacheable cacheavel || message is not IBaseQuery)
        {
            return await next(message, cancellationToken);
        }

        BehaviorLogs.ConsultandoCache(logger, typeof(TMessage).Name, cacheavel.CacheKey);

        TResponse resposta = await cache.GetOrCreateAsync(
            cacheavel.CacheKey,
            async token => await next(message, token),
            cacheavel.Expiration,
            cancellationToken);

        // Falha não fica no cache. Guardá-la serviria o mesmo erro pelo TTL inteiro — um "não encontrado"
        // causado por indisponibilidade momentânea continuaria sendo respondido depois de o dado já existir.
        // A remoção é explícita porque o GetOrCreate já gravou antes de nós vermos o resultado.
        if (resposta is Result { IsFailure: true })
        {
            await cache.RemoveAsync(cacheavel.CacheKey, cancellationToken);
        }

        return resposta;
    }
}
