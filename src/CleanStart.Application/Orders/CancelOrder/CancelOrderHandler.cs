using CleanStart.Application.Common.Abstractions;
using CleanStart.Application.Common.Messaging;
using CleanStart.Domain.Common;
using CleanStart.Domain.Errors;
using CleanStart.Domain.Orders;

namespace CleanStart.Application.Orders.CancelOrder;

/// <summary>
/// Orquestra o cancelamento de um pedido.
/// </summary>
/// <remarks>
/// <para>
/// <b>Toda a regra está no agregado.</b> O handler carrega, chama <c>Cancel</c> e repassa o que vier: quem sabe
/// que pedido enviado não se cancela — e que cancelar duas vezes é conflito — é o <c>Order</c>, desde a T1.3.
/// </para>
/// <para>
/// Não chama <c>SaveChangesAsync</c>: o commit é do <c>TransactionBehavior</c>, e a invalidação de cache do
/// <c>CacheInvalidationBehavior</c>, que só age depois do sucesso. O handler não sabe que existe nem transação
/// nem cache.
/// </para>
/// <para>
/// Usa o repositório e não o reader: cancelar é escrita, e precisa do agregado rastreado para o EF detectar a
/// mudança de estado. É a distinção que o <c>IOrderReader</c> tornou explícita na T5.1.
/// </para>
/// </remarks>
public sealed class CancelOrderHandler(
    IOrderRepository orders,
    IDateTimeProvider clock)
    : ICommandHandler<CancelOrderCommand>
{
    public async ValueTask<Result> Handle(
        CancelOrderCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        Order? pedido = await orders.GetByIdAsync(new OrderId(command.OrderId), cancellationToken);

        if (pedido is null)
        {
            return Result.Failure(DomainErrors.Order.NaoEncontrado(command.OrderId));
        }

        // A recusa é do domínio; aqui ela só é repassada. Traduzir o erro — "se for este então aquele" —
        // começaria a duplicar a regra fora da entidade.
        return pedido.Cancel(clock.UtcNow);
    }
}
