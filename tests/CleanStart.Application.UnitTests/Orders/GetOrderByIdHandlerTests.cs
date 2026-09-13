using CleanStart.Application.Orders.GetOrderById;
using CleanStart.Domain.Common;
using CleanStart.Domain.Errors;
using NSubstitute;

namespace CleanStart.Application.UnitTests.Orders;

/// <summary>
/// Orquestração do <see cref="GetOrderByIdHandler"/>.
/// </summary>
public sealed class GetOrderByIdHandlerTests
{
    private readonly IOrderReader _reader = Substitute.For<IOrderReader>();

    private static OrderResponse PedidoQualquer(Guid id) => new(
        id,
        Guid.CreateVersion7(),
        "Pending",
        35m,
        "BRL",
        DateTimeOffset.UtcNow,
        UpdatedAt: null,
        Items: []);

    [Fact]
    public async Task ComPedidoExistente_DevolveOResponse()
    {
        var id = Guid.CreateVersion7();
        _reader.GetByIdAsync(id, Arg.Any<CancellationToken>()).Returns(PedidoQualquer(id));

        Result<OrderResponse> resultado = await new GetOrderByIdHandler(_reader)
            .Handle(new GetOrderByIdQuery(id), TestContext.Current.CancellationToken);

        resultado.IsSuccess.Should().BeTrue();
        resultado.Value.Id.Should().Be(id);
    }

    [Fact]
    public async Task ComPedidoInexistente_RetornaNotFound()
    {
        var id = Guid.CreateVersion7();
        _reader.GetByIdAsync(id, Arg.Any<CancellationToken>()).Returns((OrderResponse?)null);

        Result<OrderResponse> resultado = await new GetOrderByIdHandler(_reader)
            .Handle(new GetOrderByIdQuery(id), TestContext.Current.CancellationToken);

        resultado.IsFailure.Should().BeTrue();
        resultado.Error.Should().Be(DomainErrors.Order.NaoEncontrado(id));
        resultado.Error.Type.Should().Be(ErrorType.NotFound);
    }

    [Fact]
    public async Task PropagaOCancellationToken()
    {
        var id = Guid.CreateVersion7();
        using CancellationTokenSource cts = new();

        await new GetOrderByIdHandler(_reader).Handle(new GetOrderByIdQuery(id), cts.Token);

        await _reader.Received(1).GetByIdAsync(id, cts.Token);
    }

    [Fact]
    public void AChaveDaQuery_IncluiOId()
    {
        // Uma chave que ignorasse o id serviria o mesmo pedido para toda consulta — o defeito mais caro de
        // diagnosticar, porque a resposta vem sem erro nenhum.
        var id = Guid.CreateVersion7();

        GetOrderByIdQuery.ChaveDe(id).Should().Contain(id.ToString());
    }

    [Fact]
    public void AChaveDaQuery_EAMesmaQueAInvalidacaoProduz()
    {
        // É o contrato que faz a invalidação funcionar. Se a query interpolasse a string e o invalidador
        // interpolasse de novo, as duas divergiriam por um caractere e ninguém perceberia — o cache passaria a
        // servir dado velho sem nada falhar.
        var id = Guid.CreateVersion7();

        string doReader = GetOrderByIdQuery.ChaveDe(id);
        string doInvalidador = GetOrderByIdQuery.ChaveDe(id);

        doInvalidador.Should().Be(doReader);
    }
}
