using CleanStart.Domain.Common;
using CleanStart.Domain.Customers;
using CleanStart.Domain.Errors;
using CleanStart.Domain.Orders;
using CleanStart.Domain.Orders.Events;
using CleanStart.Domain.ValueObjects;

namespace CleanStart.Domain.UnitTests.Orders;

/// <summary>
/// Invariantes e transições de estado do agregado <see cref="Order"/>.
/// </summary>
public sealed class OrderTests
{
    // Tempo fixo: a asserção sobre CreatedAt/UpdatedAt precisa ser determinística. É o mesmo motivo do
    // IDateTimeProvider nos handlers — teste que depende de DateTimeOffset.UtcNow falha sozinho um dia.
    private static readonly DateTimeOffset Agora = new(2026, 9, 12, 10, 30, 0, TimeSpan.Zero);

    private static Money Reais(decimal valor) => Money.Of(valor, "BRL").Value;

    private static (ProductId, int, Money) Item(decimal preco, int quantidade = 1) =>
        (ProductId.New(), quantidade, Reais(preco));

    [Fact]
    public void Place_SemItens_RetornaFalha()
    {
        Result<Order> resultado = Order.Place(CustomerId.New(), [], Agora);

        resultado.IsFailure.Should().BeTrue();
        resultado.Error.Should().Be(DomainErrors.Order.SemItens());
    }

    [Fact]
    public void Place_ComQuantidadeInvalida_RetornaFalha()
    {
        Result<Order> resultado = Order.Place(
            CustomerId.New(),
            [Item(preco: 10m, quantidade: 0)],
            Agora);

        resultado.IsFailure.Should().BeTrue();
        resultado.Error.Code.Should().Be("Order.QuantidadeInvalida");
    }

    [Fact]
    public void Place_ComItensEmMoedasDiferentes_RetornaFalha()
    {
        Result<Order> resultado = Order.Place(
            CustomerId.New(),
            [
                (ProductId.New(), 1, Money.Of(10m, "BRL").Value),
                (ProductId.New(), 1, Money.Of(5m, "USD").Value),
            ],
            Agora);

        resultado.IsFailure.Should().BeTrue();
        resultado.Error.Should().Be(DomainErrors.Order.ItensEmMoedasDiferentes());
    }

    [Fact]
    public void Place_ComItensValidos_CalculaOTotal()
    {
        Result<Order> resultado = Order.Place(
            CustomerId.New(),
            [
                (ProductId.New(), 2, Reais(10m)),   // 20
                (ProductId.New(), 3, Reais(5m)),    // 15
            ],
            Agora);

        resultado.IsSuccess.Should().BeTrue();
        resultado.Value.Total.Amount.Should().Be(35m);
        resultado.Value.Total.Currency.Should().Be("BRL");
    }

    [Fact]
    public void Place_NasceComoPending()
    {
        Order pedido = Order.Place(CustomerId.New(), [Item(10m)], Agora).Value;

        pedido.Status.Should().Be(OrderStatus.Pending);
        pedido.CreatedAt.Should().Be(Agora);
        pedido.UpdatedAt.Should().BeNull();
    }

    [Fact]
    public void Place_LevantaOrderPlacedEvent()
    {
        Order pedido = Order.Place(CustomerId.New(), [Item(10m, quantidade: 2)], Agora).Value;

        pedido.DomainEvents.Should().HaveCount(1);

        OrderPlacedEvent evento = pedido.DomainEvents.OfType<OrderPlacedEvent>().Single();

        evento.OrderId.Should().Be(pedido.Id);
        evento.CustomerId.Should().Be(pedido.CustomerId);
        evento.Total.Should().Be(20m);
        evento.Currency.Should().Be("BRL");
        evento.OccurredOn.Should().Be(Agora);
    }

    [Fact]
    public void Place_FalhandoNaoLevantaEvento()
    {
        Result<Order> resultado = Order.Place(CustomerId.New(), [], Agora);

        // Não há objeto para carregar evento quando a criação falha — o que importa é que nenhum efeito
        // ficou registrado para despacho.
        resultado.IsFailure.Should().BeTrue();
    }

    [Fact]
    public void Items_NaoPermiteAlteracaoPorFora()
    {
        Order pedido = Order.Place(CustomerId.New(), [Item(10m)], Agora).Value;

        Action mutar = () => ((ICollection<OrderItem>)pedido.Items).Clear();

        mutar.Should().Throw<NotSupportedException>();
        pedido.Items.Should().HaveCount(1);
    }

    [Fact]
    public void Cancel_DePedidoEnviado_RetornaFalha()
    {
        Order pedido = Order.Place(CustomerId.New(), [Item(10m)], Agora).Value;
        pedido.Pay(Agora);
        pedido.Ship(Agora);

        Result resultado = pedido.Cancel(Agora);

        // A mercadoria já saiu: o que existe daí em diante é devolução, outro processo.
        resultado.IsFailure.Should().BeTrue();
        resultado.Error.Should().Be(
            DomainErrors.Order.TransicaoInvalida(OrderStatus.Shipped, OrderStatus.Cancelled));
        pedido.Status.Should().Be(OrderStatus.Shipped);
    }

    [Fact]
    public void Cancel_DePedidoPendente_Cancela()
    {
        Order pedido = Order.Place(CustomerId.New(), [Item(10m)], Agora).Value;
        DateTimeOffset depois = Agora.AddHours(1);

        Result resultado = pedido.Cancel(depois);

        resultado.IsSuccess.Should().BeTrue();
        pedido.Status.Should().Be(OrderStatus.Cancelled);
        pedido.UpdatedAt.Should().Be(depois);
    }

    [Fact]
    public void Cancel_LevantaOrderCancelledEventComEstadoAnterior()
    {
        Order pedido = Order.Place(CustomerId.New(), [Item(10m)], Agora).Value;
        pedido.Pay(Agora);

        pedido.Cancel(Agora);

        OrderCancelledEvent evento = pedido.DomainEvents.OfType<OrderCancelledEvent>().Single();

        // Quem reage precisa do estado anterior: estornar um pedido que estava pago é diferente de
        // descartar um que estava apenas pendente.
        evento.PreviousStatus.Should().Be(OrderStatus.Paid);
    }

    [Fact]
    public void Cancel_DuasVezes_RetornaFalhaNaSegunda()
    {
        Order pedido = Order.Place(CustomerId.New(), [Item(10m)], Agora).Value;
        pedido.Cancel(Agora);

        Result segunda = pedido.Cancel(Agora);

        // Cancelar duas vezes levantaria o evento duas vezes, e quem reage estornaria em dobro.
        segunda.IsFailure.Should().BeTrue();
        pedido.DomainEvents.OfType<OrderCancelledEvent>().Should().HaveCount(1);
    }

    [Fact]
    public void Pay_DePedidoJaPago_RetornaFalha()
    {
        Order pedido = Order.Place(CustomerId.New(), [Item(10m)], Agora).Value;
        pedido.Pay(Agora);

        pedido.Pay(Agora).IsFailure.Should().BeTrue();
    }

    [Fact]
    public void Ship_DePedidoPendente_RetornaFalha()
    {
        Order pedido = Order.Place(CustomerId.New(), [Item(10m)], Agora).Value;

        // Pular o pagamento não é permitido: Pending → Shipped não está no fluxo.
        Result resultado = pedido.Ship(Agora);

        resultado.IsFailure.Should().BeTrue();
        pedido.Status.Should().Be(OrderStatus.Pending);
    }

    [Fact]
    public void AddItem_DoMesmoProduto_SomaNaLinhaExistente()
    {
        var produto = ProductId.New();
        Order pedido = Order.Place(CustomerId.New(), [(produto, 2, Reais(10m))], Agora).Value;

        Result resultado = pedido.AddItem(produto, 3, Reais(10m), Agora);

        resultado.IsSuccess.Should().BeTrue();
        pedido.Items.Should().HaveCount(1, "o mesmo produto acumula em vez de criar segunda linha");
        pedido.Total.Amount.Should().Be(50m);
    }

    [Fact]
    public void AddItem_ComMoedaDiferente_RetornaFalha()
    {
        Order pedido = Order.Place(CustomerId.New(), [Item(10m)], Agora).Value;

        Result resultado = pedido.AddItem(ProductId.New(), 1, Money.Of(5m, "USD").Value, Agora);

        resultado.IsFailure.Should().BeTrue();
        pedido.Items.Should().HaveCount(1);
    }

    [Fact]
    public void AddItem_EmPedidoCancelado_RetornaFalha()
    {
        Order pedido = Order.Place(CustomerId.New(), [Item(10m)], Agora).Value;
        pedido.Cancel(Agora);

        pedido.AddItem(ProductId.New(), 1, Reais(5m), Agora).IsFailure.Should().BeTrue();
    }

    [Fact]
    public void Total_DeItemComQuantidade_MultiplicaPeloPreco()
    {
        Order pedido = Order.Place(CustomerId.New(), [Item(preco: 7.50m, quantidade: 4)], Agora).Value;

        pedido.Total.Amount.Should().Be(30m);
    }
}
