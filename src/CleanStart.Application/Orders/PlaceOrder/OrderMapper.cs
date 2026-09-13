using CleanStart.Domain.Customers;
using CleanStart.Domain.Orders;
using CleanStart.Domain.ValueObjects;
using Riok.Mapperly.Abstractions;

namespace CleanStart.Application.Orders.PlaceOrder;

/// <summary>
/// Converte o agregado <see cref="Order"/> no response da Api.
/// </summary>
/// <remarks>
/// <para>
/// Mapperly gera o código em tempo de compilação: mapeamento que não fecha é <b>erro de build</b>, não exception
/// na primeira requisição em produção. É o que o AutoMapper não dá — além da licença, que é critério de bloqueio
/// neste kit.
/// </para>
/// <para>
/// Vale ver a prova disso: antes dos conversores abaixo existirem, o build reprovou com sete diagnósticos
/// nomeando cada membro que não fechava (<c>RMG007</c>, <c>RMG012</c>, <c>RMG020</c>). Um mapeador por reflexão
/// aceitaria o mesmo código e falharia em runtime.
/// </para>
/// <para>
/// Os identificadores tipados e o <c>Money</c> precisam de conversor explícito, porque o domínio os embrulha de
/// propósito e a fronteira HTTP os desembrulha. Para ver o código gerado, olhe <c>obj/Generated/</c>.
/// </para>
/// </remarks>
[Mapper]
public static partial class OrderMapper
{
    /// <summary>
    /// Converte o pedido no response.
    /// </summary>
    [MapProperty(nameof(Order.Total), nameof(PlaceOrderResponse.Total), Use = nameof(ToAmount))]
    [MapProperty(nameof(Order.Total), nameof(PlaceOrderResponse.Currency), Use = nameof(ToCurrency))]
    [MapperIgnoreSource(nameof(Order.DomainEvents))]
    [MapperIgnoreSource(nameof(Order.UpdatedAt))]
    [MapperIgnoreSource(nameof(Order.Currency))]
    public static partial PlaceOrderResponse ToResponse(Order order);

    [MapProperty(nameof(OrderItem.UnitPrice), nameof(PlaceOrderItemResponse.UnitPrice), Use = nameof(ToAmount))]
    [MapProperty(nameof(OrderItem.Total), nameof(PlaceOrderItemResponse.Total), Use = nameof(ToAmount))]
    [MapperIgnoreSource(nameof(OrderItem.Id))]
    private static partial PlaceOrderItemResponse ToItemResponse(OrderItem item);

    /// <summary>A quantia de dentro do <see cref="Money"/>.</summary>
    private static decimal ToAmount(Money money) => money.Amount;

    /// <summary>A moeda de dentro do <see cref="Money"/>.</summary>
    private static string ToCurrency(Money money) => money.Currency;

    private static Guid ToGuid(OrderId id) => id.Value;

    private static Guid ToGuid(CustomerId id) => id.Value;

    private static Guid ToGuid(ProductId id) => id.Value;

    private static string ToName(OrderStatus status) => status.ToString();
}
