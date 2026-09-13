using CleanStart.Application.Orders.NotifyOrderPlaced;
using CleanStart.Domain.Customers;
using CleanStart.Domain.Orders;
using CleanStart.Domain.Orders.Events;
using Microsoft.Extensions.Logging.Abstractions;

namespace CleanStart.Application.UnitTests.Orders;

/// <summary>
/// Verifica a propriedade que o consumo de um outbox exige: o efeito acontece uma vez, ainda que a entrega
/// aconteça duas.
/// </summary>
/// <remarks>
/// É o teste que dá sentido ao at-least-once. O despachante garante que o evento <b>chega</b>; garantir que ele
/// só <b>vale</b> uma vez é trabalho de quem reage — e um kit que ensina outbox sem demonstrar isso ensina
/// metade do padrão.
/// </remarks>
public sealed class OrderPlacedNotifierTests
{
    private static readonly DateTimeOffset Agora = new(2026, 9, 13, 12, 0, 0, TimeSpan.Zero);

    private static OrderPlacedEvent EventoDe(OrderId pedidoId) =>
        new(pedidoId, CustomerId.New(), 99.90m, "BRL", Agora);

    [Fact]
    public async Task MesmoEventoDuasVezes_ProduzEfeitoUmaVezSo()
    {
        OrderPlacedNotifier notificador = new(NullLogger<OrderPlacedNotifier>.Instance);
        OrderPlacedEvent evento = EventoDe(OrderId.New());

        await notificador.HandleAsync(evento, TestContext.Current.CancellationToken);
        await notificador.HandleAsync(evento, TestContext.Current.CancellationToken);

        // A segunda entrega não pode lançar: para o despachante, exception significa "falhou, tente de novo" —
        // e uma mensagem que já cumpriu seu papel voltaria à fila para sempre.
        notificador.Notificados.Should().ContainSingle(id => id == evento.OrderId.Value);
    }

    [Fact]
    public async Task PedidosDiferentes_SaoNotificadosSeparadamente()
    {
        OrderPlacedNotifier notificador = new(NullLogger<OrderPlacedNotifier>.Instance);
        OrderPlacedEvent primeiro = EventoDe(OrderId.New());
        OrderPlacedEvent segundo = EventoDe(OrderId.New());

        await notificador.HandleAsync(primeiro, TestContext.Current.CancellationToken);
        await notificador.HandleAsync(segundo, TestContext.Current.CancellationToken);

        // A proteção contra repetição é por pedido, não um "já rodei alguma vez" global — o erro que faria o
        // segundo pedido ser silenciosamente ignorado.
        notificador.Notificados.Should().HaveCount(2);
    }
}
