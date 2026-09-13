using CleanStart.Application.Common.Abstractions;
using CleanStart.Application.Orders.NotifyOrderPlaced;
using CleanStart.Domain.Common;
using CleanStart.Domain.Orders.Events;
using Microsoft.Extensions.Logging;

namespace CleanStart.Infrastructure.Persistence.Outbox;

/// <summary>
/// Implementação padrão que apenas registra a entrega — o ponto onde o broker de verdade entra.
/// </summary>
/// <remarks>
/// <para>
/// <b>É deliberadamente o mínimo, e está marcado como tal.</b> Um kit de referência não pode escolher o broker
/// por quem o usa: RabbitMQ, Kafka, Service Bus e um webhook HTTP têm formas e garantias diferentes, e qualquer
/// escolha aqui seria uma dependência a mais para quem usa outra coisa. O que o kit entrega é tudo que vem
/// <i>antes</i> do broker — a gravação transacional, a reserva concorrente, o retry com recuo, o dead-letter — e
/// um ponto único e nomeado para ligar o destino real.
/// </para>
/// <para>
/// <b>Para ligar um broker de verdade:</b> escreva uma implementação de <see cref="IOutboxPublisher"/> e
/// registre-a no lugar desta. Nada mais no despachante muda — é o que a interface existe para garantir.
/// </para>
/// <para>
/// <b>Lançar é o contrato de falha.</b> Uma implementação que engula o erro e retorne normalmente fará o
/// despachante marcar a mensagem como entregue: ela nunca mais será tentada, e nada acusará que o evento não
/// chegou. É o modo de falha silenciosa que o padrão inteiro existe para evitar.
/// </para>
/// </remarks>
internal sealed partial class LoggingOutboxPublisher(
    OrderPlacedNotifier notificador,
    ILogger<LoggingOutboxPublisher> logger) : IOutboxPublisher
{
    public async Task PublishAsync(IDomainEvent domainEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);

        // Só o nome do tipo: o evento carrega dado de negócio, e log é indexado e lido por muita gente.
        PublicacaoSimulada(logger, domainEvent.GetType().Name, domainEvent.OccurredOn);

        // Despacho por tipo, explícito. Num sistema real o evento iria para um broker e quem reage estaria do
        // outro lado do fio; aqui a reação de exemplo é chamada direto, para que o caminho completo —
        // gravar, despachar, reagir — exista e tenha teste.
        if (domainEvent is OrderPlacedEvent pedidoCriado)
        {
            await notificador.HandleAsync(pedidoCriado, cancellationToken);
        }
    }

    [LoggerMessage(
        EventId = 2100,
        Level = LogLevel.Information,
        Message = "Outbox: {Evento} entregue (ocorrido em {OcorridoEm}) — publisher de exemplo, sem broker")]
    private static partial void PublicacaoSimulada(ILogger logger, string evento, DateTimeOffset ocorridoEm);
}
