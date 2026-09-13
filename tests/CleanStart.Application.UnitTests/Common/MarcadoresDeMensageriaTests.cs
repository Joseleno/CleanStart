using CleanStart.Application.Common.Messaging;
using CleanStart.Domain.Common;

namespace CleanStart.Application.UnitTests.Common;

/// <summary>
/// Prova que os marcadores próprios são usáveis de ponta a ponta: um comando e uma consulta escritos só
/// contra <c>Application.Common.Messaging</c>, sem mencionar o namespace <c>Mediator</c>.
/// </summary>
/// <remarks>
/// Serve de exemplo mínimo da forma que a T2.3 vai seguir, e de contraprova do critério de aceite da T2.1:
/// se a abstração exigisse vazar o Mediator, este arquivo não compilaria sem um using dele.
/// </remarks>
public sealed class MarcadoresDeMensageriaTests
{
    private sealed record SomarCommand(int A, int B) : ICommand<int>;

    private sealed class SomarHandler : ICommandHandler<SomarCommand, int>
    {
        public ValueTask<Result<int>> Handle(SomarCommand command, CancellationToken cancellationToken) =>
            ValueTask.FromResult(Result.Success(command.A + command.B));
    }

    private sealed record BuscarQuery(string Termo) : IQuery<string>;

    private sealed class BuscarHandler : IQueryHandler<BuscarQuery, string>
    {
        public ValueTask<Result<string>> Handle(BuscarQuery query, CancellationToken cancellationToken) =>
            ValueTask.FromResult(Result.Success($"achou {query.Termo}"));
    }

    private sealed record ExcluirCommand(Guid Id) : ICommand;

    private sealed class ExcluirHandler : ICommandHandler<ExcluirCommand>
    {
        public ValueTask<Result> Handle(ExcluirCommand command, CancellationToken cancellationToken) =>
            ValueTask.FromResult(Result.Success());
    }

    [Fact]
    public async Task Command_ComResposta_DevolveResultDeSucesso()
    {
        SomarHandler handler = new();

        Result<int> resultado = await handler.Handle(new SomarCommand(2, 3), CancellationToken.None);

        resultado.IsSuccess.Should().BeTrue();
        resultado.Value.Should().Be(5);
    }

    [Fact]
    public async Task Query_DevolveResultDeSucesso()
    {
        BuscarHandler handler = new();

        Result<string> resultado = await handler.Handle(new BuscarQuery("x"), CancellationToken.None);

        resultado.Value.Should().Be("achou x");
    }

    [Fact]
    public async Task Command_SemResposta_DevolveResultSimples()
    {
        ExcluirHandler handler = new();

        Result resultado = await handler.Handle(new ExcluirCommand(Guid.NewGuid()), CancellationToken.None);

        resultado.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void Command_ERequestDoMediator_ParaOSourceGeneratorEnxergar()
    {
        // O marcador precisa continuar sendo um IBaseCommand do Mediator: é assim que o source generator
        // descobre o handler. Se a herança se perder numa refatoração, o pipeline para de funcionar em
        // silêncio — nada quebra em tempo de compilação, as mensagens só deixam de ser roteadas.
        typeof(SomarCommand).Should().BeAssignableTo<Mediator.IBaseCommand>();
        typeof(BuscarQuery).Should().BeAssignableTo<Mediator.IBaseQuery>();
    }
}
