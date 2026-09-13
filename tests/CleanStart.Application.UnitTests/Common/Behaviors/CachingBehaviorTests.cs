using CleanStart.Application.Common.Abstractions;
using CleanStart.Application.Common.Behaviors;
using CleanStart.Application.Common.Messaging;
using CleanStart.Domain.Common;
using CleanStart.Domain.Errors;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace CleanStart.Application.UnitTests.Common.Behaviors;

/// <summary>
/// Quando o <see cref="CachingBehavior{TMessage,TResponse}"/> intercepta e quando passa direto.
/// </summary>
public sealed class CachingBehaviorTests
{
    private readonly ICacheService _cache = Substitute.For<ICacheService>();

    private sealed record BuscarCacheavelQuery : IQuery<int>, ICacheable
    {
        public string CacheKey => "buscar:42";

        public TimeSpan? Expiration => TimeSpan.FromMinutes(5);
    }

    private sealed record BuscarSemCacheQuery : IQuery<int>;

    /// <summary>Comando que implementa ICacheable — o erro que a checagem de IBaseQuery impede.</summary>
    private sealed record AlterarCacheavelCommand : ICommand<int>, ICacheable
    {
        public string CacheKey => "alterar:42";

        public TimeSpan? Expiration => null;
    }

    [Fact]
    public async Task QuerySemICacheable_PassaDireto()
    {
        // O padrão é não cachear: dado cacheado por descuido é dado velho servido sem ninguém ter escolhido
        // esse risco.
        CachingBehavior<BuscarSemCacheQuery, Result<int>> behavior = Criar<BuscarSemCacheQuery>();

        Result<int> resposta = await behavior.Handle(
            new BuscarSemCacheQuery(),
            (_, _) => ValueTask.FromResult(Result.Success(7)),
            TestContext.Current.CancellationToken);

        resposta.Value.Should().Be(7);
        await _cache.DidNotReceiveWithAnyArgs()
            .GetOrCreateAsync<Result<int>>(default!, default!, default, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task CommandComICacheable_PassaDireto()
    {
        // Servir um comando do cache significaria não executá-lo — a alteração simplesmente não aconteceria.
        CachingBehavior<AlterarCacheavelCommand, Result<int>> behavior = Criar<AlterarCacheavelCommand>();

        await behavior.Handle(
            new AlterarCacheavelCommand(),
            (_, _) => ValueTask.FromResult(Result.Success(1)),
            TestContext.Current.CancellationToken);

        await _cache.DidNotReceiveWithAnyArgs()
            .GetOrCreateAsync<Result<int>>(default!, default!, default, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task QueryCacheavel_UsaOCacheComAChaveEAExpiracao()
    {
        _cache.GetOrCreateAsync(
                Arg.Any<string>(),
                Arg.Any<Func<CancellationToken, Task<Result<int>>>>(),
                Arg.Any<TimeSpan?>(),
                Arg.Any<CancellationToken>())
            .Returns(Result.Success(99));

        CachingBehavior<BuscarCacheavelQuery, Result<int>> behavior = Criar<BuscarCacheavelQuery>();

        Result<int> resposta = await behavior.Handle(
            new BuscarCacheavelQuery(),
            (_, _) => ValueTask.FromResult(Result.Success(7)),
            TestContext.Current.CancellationToken);

        resposta.Value.Should().Be(99, "o valor vem do cache, não do handler");

        await _cache.Received(1).GetOrCreateAsync(
            "buscar:42",
            Arg.Any<Func<CancellationToken, Task<Result<int>>>>(),
            TimeSpan.FromMinutes(5),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task QueryCacheavel_ComFalha_RemoveDoCache()
    {
        Error erro = DomainErrors.Customer.NaoEncontrado(Guid.NewGuid());

        _cache.GetOrCreateAsync(
                Arg.Any<string>(),
                Arg.Any<Func<CancellationToken, Task<Result<int>>>>(),
                Arg.Any<TimeSpan?>(),
                Arg.Any<CancellationToken>())
            .Returns(Result.Failure<int>(erro));

        CachingBehavior<BuscarCacheavelQuery, Result<int>> behavior = Criar<BuscarCacheavelQuery>();

        Result<int> resposta = await behavior.Handle(
            new BuscarCacheavelQuery(),
            (_, _) => ValueTask.FromResult(Result.Failure<int>(erro)),
            TestContext.Current.CancellationToken);

        // Guardar a falha serviria o mesmo erro pelo TTL inteiro: um "não encontrado" por indisponibilidade
        // momentânea continuaria sendo respondido depois de o dado já existir.
        resposta.IsFailure.Should().BeTrue();
        await _cache.Received(1).RemoveAsync("buscar:42", Arg.Any<CancellationToken>());
    }

    private CachingBehavior<TMessage, Result<int>> Criar<TMessage>()
        where TMessage : Mediator.IMessage =>
        new(_cache, NullLogger<CachingBehavior<TMessage, Result<int>>>.Instance);
}
