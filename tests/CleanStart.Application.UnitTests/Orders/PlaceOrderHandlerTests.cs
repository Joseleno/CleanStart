using CleanStart.Application.Common.Abstractions;
using CleanStart.Application.Orders.PlaceOrder;
using CleanStart.Domain.Common;
using CleanStart.Domain.Customers;
using CleanStart.Domain.Errors;
using CleanStart.Domain.Orders;
using CleanStart.Domain.ValueObjects;
using NSubstitute;

namespace CleanStart.Application.UnitTests.Orders;

/// <summary>
/// Orquestração do <see cref="PlaceOrderHandler"/> — com os repositórios substituídos.
/// </summary>
public sealed class PlaceOrderHandlerTests
{
    private static readonly DateTimeOffset Agora = new(2026, 9, 12, 10, 30, 0, TimeSpan.Zero);

    private readonly IOrderRepository _orders = Substitute.For<IOrderRepository>();
    private readonly ICustomerRepository _customers = Substitute.For<ICustomerRepository>();
    private readonly IDateTimeProvider _clock = Substitute.For<IDateTimeProvider>();

    public PlaceOrderHandlerTests() => _clock.UtcNow.Returns(Agora);

    [Fact]
    public async Task ComDadosValidos_CriaOPedidoEDevolveOResponse()
    {
        Customer cliente = ClienteCadastrado();
        PlaceOrderCommand comando = Comando(cliente.Id, (quantidade: 2, preco: 10m), (quantidade: 3, preco: 5m));

        Result<PlaceOrderResponse> resultado = await Handler().Handle(
            comando,
            TestContext.Current.CancellationToken);

        resultado.IsSuccess.Should().BeTrue();
        resultado.Value.CustomerId.Should().Be(cliente.Id.Value);
        resultado.Value.Status.Should().Be("Pending");
        resultado.Value.Total.Should().Be(35m, "2x10 + 3x5");
        resultado.Value.Currency.Should().Be("BRL");
        resultado.Value.CreatedAt.Should().Be(Agora);
        resultado.Value.Items.Should().HaveCount(2);
    }

    [Fact]
    public async Task ComDadosValidos_RegistraNoRepositorioSemPersistir()
    {
        Customer cliente = ClienteCadastrado();

        await Handler().Handle(
            Comando(cliente.Id, (1, 10m)),
            TestContext.Current.CancellationToken);

        // Add registra a intenção; o commit é do TransactionBehavior. Handler que chama SaveChanges por conta
        // própria quebra o caso de uso que altera dois agregados na mesma transação.
        _orders.Received(1).Add(Arg.Any<Order>());
    }

    [Fact]
    public async Task ComClienteInexistente_RetornaNotFound()
    {
        var idInexistente = Guid.CreateVersion7();

        _customers.GetByIdAsync(Arg.Any<CustomerId>(), Arg.Any<CancellationToken>())
            .Returns((Customer?)null);

        Result<PlaceOrderResponse> resultado = await Handler().Handle(
            Comando(new CustomerId(idInexistente), (1, 10m)),
            TestContext.Current.CancellationToken);

        resultado.IsFailure.Should().BeTrue();
        resultado.Error.Should().Be(DomainErrors.Customer.NaoEncontrado(idInexistente));
        resultado.Error.Type.Should().Be(ErrorType.NotFound);

        _orders.DidNotReceive().Add(Arg.Any<Order>());
    }

    [Fact]
    public async Task ComDominioRecusando_PropagaOErroDoDominio()
    {
        // Pedido sem item: a recusa vem do Order.Place, não de um `if` no handler. O teste prova que o handler
        // repassa em vez de decidir.
        Customer cliente = ClienteCadastrado();

        PlaceOrderCommand comando = new(cliente.Id.Value, "BRL", []);

        Result<PlaceOrderResponse> resultado = await Handler().Handle(
            comando,
            TestContext.Current.CancellationToken);

        resultado.IsFailure.Should().BeTrue();
        resultado.Error.Should().Be(DomainErrors.Order.SemItens());
        _orders.DidNotReceive().Add(Arg.Any<Order>());
    }

    [Fact]
    public async Task ComQuantidadeInvalida_PropagaOErroDoDominio()
    {
        Customer cliente = ClienteCadastrado();

        Result<PlaceOrderResponse> resultado = await Handler().Handle(
            Comando(cliente.Id, (quantidade: 0, preco: 10m)),
            TestContext.Current.CancellationToken);

        resultado.IsFailure.Should().BeTrue();
        resultado.Error.Code.Should().Be("Order.QuantidadeInvalida");
    }

    [Fact]
    public async Task ComMoedaInvalida_RetornaFalhaDoValueObject()
    {
        Customer cliente = ClienteCadastrado();

        PlaceOrderCommand comando = new(
            cliente.Id.Value,
            "BRLL",
            [new PlaceOrderItemRequest(Guid.CreateVersion7(), 1, 10m)]);

        Result<PlaceOrderResponse> resultado = await Handler().Handle(
            comando,
            TestContext.Current.CancellationToken);

        resultado.IsFailure.Should().BeTrue();
        resultado.Error.Code.Should().Be("Money.MoedaInvalida");
    }

    [Fact]
    public async Task UsaORelogioInjetado_NaoODoSistema()
    {
        Customer cliente = ClienteCadastrado();

        Result<PlaceOrderResponse> resultado = await Handler().Handle(
            Comando(cliente.Id, (1, 10m)),
            TestContext.Current.CancellationToken);

        // Com DateTimeOffset.UtcNow dentro do handler, esta asserção seria impossível.
        resultado.Value.CreatedAt.Should().Be(Agora);
        _ = _clock.Received().UtcNow;
    }

    [Fact]
    public async Task PropagaOCancellationTokenAoRepositorio()
    {
        Customer cliente = ClienteCadastrado();
        using CancellationTokenSource cts = new();

        await Handler().Handle(Comando(cliente.Id, (1, 10m)), cts.Token);

        await _customers.Received(1).GetByIdAsync(Arg.Any<CustomerId>(), cts.Token);
    }

    private PlaceOrderHandler Handler() => new(_orders, _customers, _clock);

    private Customer ClienteCadastrado()
    {
        Customer cliente = Customer.Register(
            "João da Silva",
            Email.Of("joao@example.com").Value,
            Document.Of("529.982.247-25").Value,
            Agora).Value;

        _customers.GetByIdAsync(cliente.Id, Arg.Any<CancellationToken>()).Returns(cliente);

        return cliente;
    }

    private static PlaceOrderCommand Comando(
        CustomerId customerId,
        params (int quantidade, decimal preco)[] itens) =>
        new(
            customerId.Value,
            "BRL",
            [.. itens.Select(item =>
                new PlaceOrderItemRequest(Guid.CreateVersion7(), item.quantidade, item.preco))]);
}
