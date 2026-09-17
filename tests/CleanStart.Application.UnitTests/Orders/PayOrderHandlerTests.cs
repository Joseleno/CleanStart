using CleanStart.Application.Common.Abstractions;
using CleanStart.Application.Orders.PayOrder;
using CleanStart.Domain.Common;
using CleanStart.Domain.Customers;
using CleanStart.Domain.Errors;
using CleanStart.Domain.Orders;
using CleanStart.Domain.ValueObjects;
using NSubstitute;

namespace CleanStart.Application.UnitTests.Orders;

/// <summary>
/// Orquestração do <see cref="PayOrderHandler"/>.
/// </summary>
/// <remarks>
/// É o primeiro caso de uso que lê dois agregados, então os testes cobrem dois eixos: a transição de estado
/// (que é do <c>Order</c>) e a exigência de que o cliente exista (que é deste handler, porque nenhum dos dois
/// agregados alcança o outro).
/// </remarks>
public sealed class PayOrderHandlerTests
{
    private static readonly DateTimeOffset Agora = new(2026, 9, 17, 10, 0, 0, TimeSpan.Zero);

    private readonly IOrderRepository _orders = Substitute.For<IOrderRepository>();
    private readonly ICustomerRepository _customers = Substitute.For<ICustomerRepository>();
    private readonly IDateTimeProvider _clock = Substitute.For<IDateTimeProvider>();

    public PayOrderHandlerTests() => _clock.UtcNow.Returns(Agora);

    private static Customer ClienteNovo() => Customer.Register(
        "João da Silva",
        Email.Of("joao@example.com").Value,
        Document.Of("52998224725").Value,
        Agora).Value;

    private static Order PedidoPendenteDe(Customer cliente) => Order.Place(
        cliente.Id,
        [(ProductId.New(), 1, Money.Of(10m, "BRL").Value)],
        Agora).Value;

    /// <summary>
    /// Prepara os dois repositórios para devolverem o par, que é o caminho feliz.
    /// </summary>
    private (Order Pedido, Customer Cliente) Semear()
    {
        Customer cliente = ClienteNovo();
        Order pedido = PedidoPendenteDe(cliente);

        _orders.GetByIdAsync(pedido.Id, Arg.Any<CancellationToken>()).Returns(pedido);
        _customers.GetByIdAsync(cliente.Id, Arg.Any<CancellationToken>()).Returns(cliente);

        return (pedido, cliente);
    }

    private PayOrderHandler Handler() => new(_orders, _customers, _clock);

    [Fact]
    public async Task ComPedidoPendenteEClienteAtivo_Paga()
    {
        (Order pedido, _) = Semear();

        Result resultado = await Handler()
            .Handle(new PayOrderCommand(pedido.Id.Value), TestContext.Current.CancellationToken);

        resultado.IsSuccess.Should().BeTrue();
        pedido.Status.Should().Be(OrderStatus.Paid);
        pedido.UpdatedAt.Should().Be(Agora, "o relógio injetado é o que grava a data");
    }

    [Fact]
    public async Task DepoisDePagar_OPedidoPodeSerEnviado()
    {
        // O que a T9.4 fecha: sem um caso de uso que pague, Ship era inalcançável pela aplicação inteira, e com
        // ele o ramo Shipped de Cancel. Este teste prova que a máquina de estados tem caminho completo.
        (Order pedido, _) = Semear();

        await Handler().Handle(new PayOrderCommand(pedido.Id.Value), TestContext.Current.CancellationToken);

        pedido.Ship(Agora).IsSuccess.Should().BeTrue("pagar é o que habilita o envio");
        pedido.Cancel(Agora).IsFailure.Should().BeTrue("pedido enviado não cancela");
    }

    [Fact]
    public async Task ComPedidoInexistente_RetornaNotFound()
    {
        var desconhecido = Guid.CreateVersion7();
        _orders.GetByIdAsync(new OrderId(desconhecido), Arg.Any<CancellationToken>()).Returns((Order?)null);

        Result resultado = await Handler()
            .Handle(new PayOrderCommand(desconhecido), TestContext.Current.CancellationToken);

        resultado.IsFailure.Should().BeTrue();
        resultado.Error.Type.Should().Be(ErrorType.NotFound);
        resultado.Error.Code.Should().Be("Order.NaoEncontrado");
    }

    [Fact]
    public async Task ComClienteAusente_RetornaNotFoundDoClienteENaoPaga()
    {
        // Cliente excluído cai aqui: o filtro global de soft delete o esconde do repositório, então "excluído" e
        // "nunca existiu" são a mesma resposta para este handler — que é o que o domínio precisa saber.
        Customer cliente = ClienteNovo();
        Order pedido = PedidoPendenteDe(cliente);
        _orders.GetByIdAsync(pedido.Id, Arg.Any<CancellationToken>()).Returns(pedido);
        _customers.GetByIdAsync(cliente.Id, Arg.Any<CancellationToken>()).Returns((Customer?)null);

        Result resultado = await Handler()
            .Handle(new PayOrderCommand(pedido.Id.Value), TestContext.Current.CancellationToken);

        resultado.IsFailure.Should().BeTrue();
        resultado.Error.Type.Should().Be(ErrorType.NotFound);
        resultado.Error.Code.Should().Be("Customer.NaoEncontrado");
        pedido.Status.Should().Be(OrderStatus.Pending, "a recusa não pode deixar o pedido pago");
    }

    [Fact]
    public async Task ComPedidoJaPago_RetornaConflict()
    {
        (Order pedido, _) = Semear();
        pedido.Pay(Agora);

        Result resultado = await Handler()
            .Handle(new PayOrderCommand(pedido.Id.Value), TestContext.Current.CancellationToken);

        resultado.IsFailure.Should().BeTrue();
        resultado.Error.Type.Should().Be(ErrorType.Conflict, "o pedido existe; o estado é que impede");
        resultado.Error.Code.Should().Be("Order.TransicaoInvalida");
    }

    [Fact]
    public async Task ComPedidoCancelado_RetornaConflict()
    {
        (Order pedido, _) = Semear();
        pedido.Cancel(Agora);

        Result resultado = await Handler()
            .Handle(new PayOrderCommand(pedido.Id.Value), TestContext.Current.CancellationToken);

        resultado.IsFailure.Should().BeTrue();
        resultado.Error.Type.Should().Be(ErrorType.Conflict);
        pedido.Status.Should().Be(OrderStatus.Cancelled, "o estado não muda quando a operação é recusada");
    }

    [Fact]
    public async Task NaoConsultaOClienteQuandoOPedidoNaoExiste()
    {
        // A ordem importa: carregar o cliente antes de saber que o pedido existe seria uma consulta a mais em
        // todo 404 — e é o pedido que diz qual cliente interessa.
        var desconhecido = Guid.CreateVersion7();
        _orders.GetByIdAsync(new OrderId(desconhecido), Arg.Any<CancellationToken>()).Returns((Order?)null);

        await Handler().Handle(new PayOrderCommand(desconhecido), TestContext.Current.CancellationToken);

        await _customers.DidNotReceive()
            .GetByIdAsync(Arg.Any<CustomerId>(), Arg.Any<CancellationToken>());
    }
}
