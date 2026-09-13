using CleanStart.Application.Common.Abstractions;
using CleanStart.Application.Common.Behaviors;
using CleanStart.Application.Common.Messaging;
using CleanStart.Domain.Common;
using CleanStart.Domain.Errors;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace CleanStart.Application.UnitTests.Common.Behaviors;

/// <summary>
/// Quando o <see cref="CacheInvalidationBehavior{TMessage,TResponse}"/> remove chaves — e quando não remove.
/// </summary>
public sealed class CacheInvalidationBehaviorTests
{
    private const string Chave = "order:123";

    private readonly ICacheService _cache = Substitute.For<ICacheService>();

    private sealed record AlterarCommand : ICommand<int>, ICacheInvalidator
    {
        public IReadOnlyList<string> ChavesInvalidadas => [Chave, "order:456"];
    }

    private sealed record CommandSemInvalidacao : ICommand<int>;

    private CacheInvalidationBehavior<TMessage, Result<int>> Criar<TMessage>()
        where TMessage : Mediator.IMessage =>
        new(_cache, NullLogger<CacheInvalidationBehavior<TMessage, Result<int>>>.Instance);

    [Fact]
    public async Task ComSucesso_RemoveTodasAsChaves()
    {
        CacheInvalidationBehavior<AlterarCommand, Result<int>> behavior = Criar<AlterarCommand>();

        await behavior.Handle(
            new AlterarCommand(),
            (_, _) => ValueTask.FromResult(Result.Success(1)),
            TestContext.Current.CancellationToken);

        await _cache.Received(1).RemoveAsync(Chave, Arg.Any<CancellationToken>());
        await _cache.Received(1).RemoveAsync("order:456", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ComResultadoDeFalha_NaoRemoveNada()
    {
        // A operação que falhou não mudou nada, então o que está guardado continua correto. Invalidar aqui
        // jogaria fora cache válido e forçaria uma consulta ao banco sem motivo.
        CacheInvalidationBehavior<AlterarCommand, Result<int>> behavior = Criar<AlterarCommand>();

        await behavior.Handle(
            new AlterarCommand(),
            (_, _) => ValueTask.FromResult(Result.Failure<int>(DomainErrors.Order.SemItens())),
            TestContext.Current.CancellationToken);

        await _cache.DidNotReceiveWithAnyArgs().RemoveAsync(default!, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task SemOMarcador_PassaDireto()
    {
        CacheInvalidationBehavior<CommandSemInvalidacao, Result<int>> behavior = Criar<CommandSemInvalidacao>();

        await behavior.Handle(
            new CommandSemInvalidacao(),
            (_, _) => ValueTask.FromResult(Result.Success(1)),
            TestContext.Current.CancellationToken);

        await _cache.DidNotReceiveWithAnyArgs().RemoveAsync(default!, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task ComFalhaAoRemover_NaoDerrubaAOperacao()
    {
        // O dado já foi gravado. Propagar a exception faria o cliente receber erro numa operação que de fato
        // aconteceu — e provavelmente tentar de novo, duplicando o efeito. Servir valor velho até o TTL é ruim,
        // mas recuperável.
        _cache.RemoveAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("redis fora do ar"));

        CacheInvalidationBehavior<AlterarCommand, Result<int>> behavior = Criar<AlterarCommand>();

        Result<int> resposta = await behavior.Handle(
            new AlterarCommand(),
            (_, _) => ValueTask.FromResult(Result.Success(7)),
            TestContext.Current.CancellationToken);

        resposta.IsSuccess.Should().BeTrue();
        resposta.Value.Should().Be(7);
    }

    [Fact]
    public async Task ComFalhaNaPrimeiraChave_AindaTentaAsDemais()
    {
        // Uma chave que falha não pode impedir as outras: cada uma é independente, e parar na primeira deixaria
        // o resto do cache obsoleto sem necessidade.
        _cache.RemoveAsync(Chave, Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("falha só nesta"));

        CacheInvalidationBehavior<AlterarCommand, Result<int>> behavior = Criar<AlterarCommand>();

        await behavior.Handle(
            new AlterarCommand(),
            (_, _) => ValueTask.FromResult(Result.Success(1)),
            TestContext.Current.CancellationToken);

        await _cache.Received(1).RemoveAsync("order:456", Arg.Any<CancellationToken>());
    }
}
