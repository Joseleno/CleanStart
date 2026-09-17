using CleanStart.Application.Common.Abstractions;
using CleanStart.Application.Common.Messaging;
using CleanStart.Domain.Common;
using CleanStart.Domain.Customers;
using CleanStart.Domain.Errors;
using CleanStart.Domain.Orders;

namespace CleanStart.Application.Orders.PayOrder;

/// <summary>
/// Orquestra o pagamento de um pedido.
/// </summary>
/// <remarks>
/// <para>
/// <b>Toda a regra de transição está no agregado.</b> O handler carrega, chama <c>Pay</c> e repassa o que vier:
/// quem sabe que só pedido pendente é pago — e que pagar duas vezes é conflito — é o <c>Order</c>.
/// </para>
/// <para>
/// <b>Este é o caso de uso que lê dois agregados.</b> Antes de pagar, ele exige que o cliente do pedido ainda
/// exista. A regra não cabe no <c>Order</c>: o pedido guarda um <c>CustomerId</c>, não o cliente, e um agregado
/// não alcança outro por referência — é justamente a fronteira que a modelagem por agregados define. Validação
/// que depende de dois agregados vive no caso de uso, e é este o lugar.
/// </para>
/// <para>
/// <b>Cliente excluído reprova o pagamento, e é o filtro global que faz isso acontecer.</b> O soft delete do
/// <c>Customer</c> é aplicado por filtro de query na Infrastructure, então um cliente excluído simplesmente não
/// volta do repositório — <c>GetByIdAsync</c> devolve <c>null</c> sem que este handler saiba que o conceito de
/// exclusão existe. É o comportamento correto: a exclusão é decisão de persistência, não regra deste caso de
/// uso. Aqui ela aparece como "não existe", que é o que o domínio precisa saber.
/// </para>
/// <para>
/// Escreve em um agregado só — o pedido. O cliente é lido para validar, nunca alterado: pagar um pedido não
/// muda o cliente, e forçar uma escrita nos dois para "demonstrar transação" inventaria uma regra que o negócio
/// não tem.
/// </para>
/// <para>
/// Não chama <c>SaveChangesAsync</c>: o commit é do <c>TransactionBehavior</c>, e a invalidação de cache do
/// <c>CacheInvalidationBehavior</c>, que só age depois do sucesso.
/// </para>
/// </remarks>
public sealed class PayOrderHandler(
    IOrderRepository orders,
    ICustomerRepository customers,
    IDateTimeProvider clock)
    : ICommandHandler<PayOrderCommand>
{
    public async ValueTask<Result> Handle(
        PayOrderCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        Order? pedido = await orders.GetByIdAsync(new OrderId(command.OrderId), cancellationToken);

        if (pedido is null)
        {
            return Result.Failure(DomainErrors.Order.NaoEncontrado(command.OrderId));
        }

        // O cliente é carregado depois do pedido porque é o pedido que diz qual cliente importa. Um cliente
        // excluído não volta do repositório — o filtro global o esconde — e cai neste mesmo ramo.
        Customer? cliente = await customers.GetByIdAsync(pedido.CustomerId, cancellationToken);

        if (cliente is null)
        {
            return Result.Failure(DomainErrors.Customer.NaoEncontrado(pedido.CustomerId.Value));
        }

        // A recusa é do domínio; aqui ela só é repassada.
        return pedido.Pay(clock.UtcNow);
    }
}
