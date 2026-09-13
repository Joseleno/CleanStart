using CleanStart.Application.Common.Abstractions;
using CleanStart.Application.Common.Behaviors;
using CleanStart.Application.Common.Messaging;
using CleanStart.Domain.Common;
using CleanStart.Domain.Errors;
using NSubstitute;

namespace CleanStart.Application.UnitTests.Common.Behaviors;

/// <summary>
/// Quando o <see cref="TransactionBehavior{TMessage,TResponse}"/> grava e quando não grava.
/// </summary>
public sealed class TransactionBehaviorTests
{
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();

    private sealed record CriarCommand : ICommand<int>;

    private sealed record ListarQuery : IQuery<int>;

    [Fact]
    public async Task Query_NaoAbreTransacao()
    {
        // Critério de aceite da T2.2. Transação em leitura segura conexão do pool e mantém snapshot aberto
        // no PostgreSQL sem necessidade.
        TransactionBehavior<ListarQuery, Result<int>> behavior = new(_unitOfWork);

        Result<int> resposta = await behavior.Handle(
            new ListarQuery(),
            (_, _) => ValueTask.FromResult(Result.Success(7)),
            TestContext.Current.CancellationToken);

        resposta.Value.Should().Be(7);
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Command_ComSucesso_Grava()
    {
        TransactionBehavior<CriarCommand, Result<int>> behavior = new(_unitOfWork);

        await behavior.Handle(
            new CriarCommand(),
            (_, _) => ValueTask.FromResult(Result.Success(1)),
            TestContext.Current.CancellationToken);

        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Command_ComResultadoDeFalha_NaoGrava()
    {
        // O caso que o desenho óbvio erra: o handler devolveu falha porque o domínio recusou, mas um commit
        // cego persistiria o que ele tocou antes da recusa — a regra diria "não" e o banco gravaria "sim".
        TransactionBehavior<CriarCommand, Result<int>> behavior = new(_unitOfWork);
        Error erro = DomainErrors.Order.SemItens();

        Result<int> resposta = await behavior.Handle(
            new CriarCommand(),
            (_, _) => ValueTask.FromResult(Result.Failure<int>(erro)),
            TestContext.Current.CancellationToken);

        resposta.IsFailure.Should().BeTrue();
        resposta.Error.Should().Be(erro);
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Command_ComExcecaoNoHandler_NaoGrava()
    {
        TransactionBehavior<CriarCommand, Result<int>> behavior = new(_unitOfWork);

        Func<Task> acao = async () => await behavior.Handle(
            new CriarCommand(),
            (_, _) => throw new InvalidOperationException("falha de infraestrutura"),
            TestContext.Current.CancellationToken);

        await acao.Should().ThrowAsync<InvalidOperationException>();
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Command_PropagaOCancellationToken()
    {
        TransactionBehavior<CriarCommand, Result<int>> behavior = new(_unitOfWork);
        using CancellationTokenSource cts = new();

        await behavior.Handle(
            new CriarCommand(),
            (_, _) => ValueTask.FromResult(Result.Success(1)),
            cts.Token);

        await _unitOfWork.Received(1).SaveChangesAsync(cts.Token);
    }
}
