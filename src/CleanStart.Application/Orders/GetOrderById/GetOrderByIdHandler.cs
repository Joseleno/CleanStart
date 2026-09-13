using CleanStart.Application.Common.Messaging;
using CleanStart.Domain.Common;
using CleanStart.Domain.Errors;

namespace CleanStart.Application.Orders.GetOrderById;

/// <summary>
/// Orquestra a leitura de um pedido.
/// </summary>
/// <remarks>
/// <para>
/// Curto por natureza: consulta não tem regra de negócio para aplicar. Toda a decisão que existe aqui é traduzir
/// "não achei" em <c>NotFound</c> — e mesmo essa vem do catálogo de erros, não de um <c>Error</c> montado à mão.
/// </para>
/// <para>
/// Não sabe que existe cache. O <c>CachingBehavior</c> intercepta antes de chegar aqui, porque a query implementa
/// <c>ICacheable</c>: se o valor estiver guardado, este handler nem roda. É o ganho de a decisão de cachear viver
/// na mensagem, e não no código que a trata.
/// </para>
/// </remarks>
public sealed class GetOrderByIdHandler(IOrderReader reader)
    : IQueryHandler<GetOrderByIdQuery, OrderResponse>
{
    public async ValueTask<Result<OrderResponse>> Handle(
        GetOrderByIdQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        OrderResponse? pedido = await reader.GetByIdAsync(query.OrderId, cancellationToken);

        return pedido is null
            ? Result.Failure<OrderResponse>(DomainErrors.Order.NaoEncontrado(query.OrderId))
            : Result.Success(pedido);
    }
}
