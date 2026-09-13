using CleanStart.Application.Common.Abstractions;
using CleanStart.Application.Orders.CancelOrder;
using CleanStart.Application.Orders.GetOrderById;
using CleanStart.Domain.Common;
using CleanStart.Domain.Customers;
using CleanStart.Domain.Errors;
using CleanStart.Domain.Orders;
using CleanStart.Domain.Orders.Events;
using CleanStart.Domain.ValueObjects;
using NSubstitute;

namespace CleanStart.Application.UnitTests.Orders;

/// <summary>
/// Orquestração do <see cref="CancelOrderHandler"/>.
/// </summary>
public sealed class CancelOrderHandlerTests
{
    private static readonly DateTimeOffset Agora = new(2026, 9, 13, 10, 0, 0, TimeSpan.Zero);

    private readonly IOrderRepository _orders = Substitute.For<IOrderRepository>();
    private readonly IDateTimeProvider _clock = Substitute.For<IDateTimeProvider>();

    public CancelOrderHandlerTests() => _clock.UtcNow.Returns(Agora);

    private static Order PedidoPendente() => Order.Place(
        CustomerId.New(),
        [(ProductId.New(), 1, Money.Of(10m, "BRL").Value)],
        Agora).Value;

    private CancelOrderHandler Handler() => new(_orders, _clock);

    [Fact]
    public async Task ComPedidoPendente_Cancela()
    {
        Order pedido = PedidoPendente();
        _orders.GetByIdAsync(pedido.Id, Arg.Any<CancellationToken>()).Returns(pedido);

        Result resultado = await Handler()
            .Handle(new CancelOrderCommand(pedido.Id.Value), TestContext.Current.CancellationToken);

        resultado.IsSuccess.Should().BeTrue();
        pedido.Status.Should().Be(OrderStatus.Cancelled);
        pedido.UpdatedAt.Should().Be(Agora, "o relógio injetado é o que grava a data");
    }

    [Fact]
    public async Task ComPedidoPago_Cancela()
    {
        Order pedido = PedidoPendente();
        pedido.Pay(Agora);
        _orders.GetByIdAsync(pedido.Id, Arg.Any<CancellationToken>()).Returns(pedido);

        Result resultado = await Handler()
            .Handle(new CancelOrderCommand(pedido.Id.Value), TestContext.Current.CancellationToken);

        resultado.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task ComPedidoEnviado_RetornaConflict()
    {
        // A transição proibida. A regra é do agregado; o handler só repassa.
        Order pedido = PedidoPendente();
        pedido.Pay(Agora);
        pedido.Ship(Agora);
        _orders.GetByIdAsync(pedido.Id, Arg.Any<CancellationToken>()).Returns(pedido);

        Result resultado = await Handler()
            .Handle(new CancelOrderCommand(pedido.Id.Value), TestContext.Current.CancellationToken);

        resultado.IsFailure.Should().BeTrue();
        resultado.Error.Type.Should().Be(ErrorType.Conflict, "o pedido existe; o estado é que impede");
        resultado.Error.Code.Should().Be("Order.TransicaoInvalida");
        pedido.Status.Should().Be(OrderStatus.Shipped, "o estado não muda quando a operação é recusada");
    }

    [Fact]
    public async Task ComPedidoJaCancelado_RetornaConflictENaoLevantaSegundoEvento()
    {
        Order pedido = PedidoPendente();
        pedido.Cancel(Agora);
        _orders.GetByIdAsync(pedido.Id, Arg.Any<CancellationToken>()).Returns(pedido);

        Result resultado = await Handler()
            .Handle(new CancelOrderCommand(pedido.Id.Value), TestContext.Current.CancellationToken);

        resultado.IsFailure.Should().BeTrue();

        // Cancelar duas vezes levantaria o evento duas vezes, e quem reage estornaria em dobro.
        pedido.DomainEvents.OfType<OrderCancelledEvent>().Should().HaveCount(1);
    }

    [Fact]
    public async Task ComPedidoInexistente_RetornaNotFound()
    {
        var id = Guid.CreateVersion7();
        _orders.GetByIdAsync(Arg.Any<OrderId>(), Arg.Any<CancellationToken>()).Returns((Order?)null);

        Result resultado = await Handler()
            .Handle(new CancelOrderCommand(id), TestContext.Current.CancellationToken);

        resultado.IsFailure.Should().BeTrue();
        resultado.Error.Should().Be(DomainErrors.Order.NaoEncontrado(id));
    }

    [Fact]
    public async Task NaoPersisteSozinho()
    {
        // O commit é do TransactionBehavior. Handler que chama SaveChanges por conta própria quebra o caso de
        // uso que altera dois agregados na mesma transação.
        Order pedido = PedidoPendente();
        _orders.GetByIdAsync(pedido.Id, Arg.Any<CancellationToken>()).Returns(pedido);

        await Handler().Handle(new CancelOrderCommand(pedido.Id.Value), TestContext.Current.CancellationToken);

        _orders.DidNotReceive().Add(Arg.Any<Order>());
    }

    [Fact]
    public void OComandoInvalidaAChaveDoPedido()
    {
        // É o que o ICacheInvalidator existe para declarar: o pedido cancelado pode ter sido lido e guardado, e
        // sem invalidar o cliente veria "Pending" por até cinco minutos depois de cancelar.
        var id = Guid.CreateVersion7();

        CancelOrderCommand comando = new(id);

        comando.ChavesInvalidadas.Should().ContainSingle()
            .Which.Should().Be(GetOrderByIdQuery.ChaveDe(id), "a chave é a mesma que o reader produz");
    }
}
