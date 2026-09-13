using CleanStart.Application.Common.Abstractions;
using CleanStart.Application.Common.Messaging;
using CleanStart.Domain.Common;
using CleanStart.Domain.Customers;
using CleanStart.Domain.Errors;
using CleanStart.Domain.Orders;
using CleanStart.Domain.ValueObjects;

namespace CleanStart.Application.Orders.PlaceOrder;

/// <summary>
/// Orquestra a criação de um pedido.
/// </summary>
/// <remarks>
/// <para>
/// <b>Não há regra de negócio aqui</b> — é critério de aceite da T2.3. O handler faz quatro coisas: converte os
/// primitivos do command em tipos de domínio, carrega o que precisa pelos repositórios, <b>delega a decisão ao
/// <see cref="Order.Place"/></b> e registra a intenção de persistir. Toda recusa vem do domínio.
/// </para>
/// <para>
/// O teste de leitura é este: nenhum <c>if</c> abaixo decide sobre pedido. Os que existem propagam falha que
/// outro produziu — a do <c>Money.Of</c>, a do repositório que não achou o cliente, a do <c>Order.Place</c>.
/// Se um dia aparecer aqui um <c>if (itens.Count == 0)</c>, a regra vazou da entidade.
/// </para>
/// <para>
/// Não chama <c>SaveChangesAsync</c>: o commit é do <c>TransactionBehavior</c>, que já sabe não gravar quando o
/// resultado é falha. Handler que commita por conta própria quebra o caso de uso que altera dois agregados.
/// </para>
/// </remarks>
public sealed class PlaceOrderHandler(
    IOrderRepository orders,
    ICustomerRepository customers,
    IDateTimeProvider clock)
    : ICommandHandler<PlaceOrderCommand, PlaceOrderResponse>
{
    public async ValueTask<Result<PlaceOrderResponse>> Handle(
        PlaceOrderCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        CustomerId customerId = new(command.CustomerId);

        Customer? customer = await customers.GetByIdAsync(customerId, cancellationToken);

        if (customer is null)
        {
            // Existência de cliente é pergunta para o repositório, não para o validator: exige I/O, e
            // validator que consulta banco esconde isso onde ninguém espera.
            return Result.Failure<PlaceOrderResponse>(
                DomainErrors.Customer.NaoEncontrado(command.CustomerId));
        }

        Result<List<(ProductId, int, Money)>> itens = ConverterItens(command);

        if (itens.IsFailure)
        {
            return Result.Failure<PlaceOrderResponse>(itens.Error);
        }

        Result<Order> pedido = Order.Place(customerId, itens.Value, clock.UtcNow);

        if (pedido.IsFailure)
        {
            // A recusa é do domínio; aqui ela só é repassada. Nenhuma tradução, nenhum "se for este erro
            // então aquele" — isso começaria a duplicar a regra fora da entidade.
            return Result.Failure<PlaceOrderResponse>(pedido.Error);
        }

        orders.Add(pedido.Value);

        return OrderMapper.ToResponse(pedido.Value);
    }

    /// <summary>
    /// Converte os primitivos do command nos tipos de domínio, propagando a primeira falha.
    /// </summary>
    /// <remarks>
    /// A conversão vive aqui e não no command porque <c>Money.Of</c> devolve <c>Result</c> — e um record
    /// desserializado de JSON não tem como recusar o próprio conteúdo.
    /// </remarks>
    private static Result<List<(ProductId, int, Money)>> ConverterItens(PlaceOrderCommand command)
    {
        List<(ProductId, int, Money)> convertidos = [];

        foreach (PlaceOrderItemRequest item in command.Items)
        {
            Result<Money> preco = Money.Of(item.UnitPrice, command.Currency);

            if (preco.IsFailure)
            {
                return Result.Failure<List<(ProductId, int, Money)>>(preco.Error);
            }

            convertidos.Add((new ProductId(item.ProductId), item.Quantity, preco.Value));
        }

        return convertidos;
    }
}
