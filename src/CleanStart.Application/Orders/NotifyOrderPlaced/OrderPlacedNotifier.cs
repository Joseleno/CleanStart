using CleanStart.Application.Common.Abstractions;
using CleanStart.Domain.Common;
using CleanStart.Domain.Orders.Events;
using Microsoft.Extensions.Logging;

namespace CleanStart.Application.Orders.NotifyOrderPlaced;

/// <summary>
/// Exemplo de reação a um pedido criado — o que um consumidor do outbox faz do outro lado.
/// </summary>
/// <remarks>
/// <para>
/// <b>A pasta tem o nome da reação, não "EventHandlers".</b> É a mesma convenção dos outros slices: cada pasta
/// responde "o que este código faz para o negócio?". Uma pasta por papel técnico é o caminho de volta ao
/// agrupamento por camada que a organização vertical existe para evitar — com o tempo ela junta reações que não
/// têm nada a ver umas com as outras, só porque todas são "handlers".
/// </para>
/// <para>
/// <b>A entrega é at-least-once, e é por isso que este método é idempotente.</b> Se o despachante cair entre
/// publicar e marcar a mensagem, o mesmo evento chega de novo. Nada no transporte impede isso — a garantia tem
/// de vir de quem reage. Aqui a proteção é a verificação em <see cref="JaNotificado"/>, e num caso real ela seria
/// o mesmo: uma consulta a "isto já foi feito?" antes de agir, ou uma chave única que faça a segunda tentativa
/// falhar sem efeito.
/// </para>
/// <para>
/// O efeito colateral real — enviar e-mail, chamar um serviço, gravar uma leitura — entra onde está o log. O que
/// o kit demonstra é a forma: verificar, agir, registrar que agiu.
/// </para>
/// </remarks>
public sealed partial class OrderPlacedNotifier(ILogger<OrderPlacedNotifier> logger)
{
    /// <remarks>
    /// Em memória só porque é exemplo. Num sistema de verdade isto é uma tabela — o processo reinicia, e a
    /// memória não sobrevive: depois do reinício o mesmo evento seria tratado como novo.
    /// </remarks>
    private readonly HashSet<Guid> _jaNotificados = [];

    /// <summary>Os pedidos já notificados — é o efeito observável deste exemplo.</summary>
    /// <remarks>
    /// Exposto como leitura porque, sem efeito externo de verdade, é a única maneira de o teste provar que a
    /// entrega repetida não produziu o efeito duas vezes. Numa implementação real o teste olharia a tabela, o
    /// e-mail enviado ou a chamada feita.
    /// </remarks>
    public IReadOnlyCollection<Guid> Notificados => _jaNotificados;

    public Task HandleAsync(OrderPlacedEvent evento, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(evento);

        if (JaNotificado(evento.OrderId.Value))
        {
            // Não é erro: é o caso normal de uma entrega repetida. Tratar como falha faria o despachante
            // reagendar uma mensagem que já cumpriu seu papel.
            EntregaRepetida(logger, evento.OrderId.Value);
            return Task.CompletedTask;
        }

        _jaNotificados.Add(evento.OrderId.Value);
        PedidoNotificado(logger, evento.OrderId.Value, evento.Total, evento.Currency);

        return Task.CompletedTask;
    }

    private bool JaNotificado(Guid pedidoId) => _jaNotificados.Contains(pedidoId);

    [LoggerMessage(
        EventId = 3000,
        Level = LogLevel.Information,
        Message = "Pedido {PedidoId} criado no valor de {Total} {Moeda} — notificação de exemplo")]
    private static partial void PedidoNotificado(ILogger logger, Guid pedidoId, decimal total, string moeda);

    [LoggerMessage(
        EventId = 3001,
        Level = LogLevel.Debug,
        Message = "Pedido {PedidoId} já havia sido notificado; entrega repetida ignorada")]
    private static partial void EntregaRepetida(ILogger logger, Guid pedidoId);
}
