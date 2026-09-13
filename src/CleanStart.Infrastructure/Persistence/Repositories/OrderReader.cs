using CleanStart.Application.Common.Abstractions;
using CleanStart.Application.Orders.GetOrderById;
using CleanStart.Domain.Orders;
using Microsoft.EntityFrameworkCore;

namespace CleanStart.Infrastructure.Persistence.Repositories;

/// <summary>
/// Implementa <see cref="IOrderReader"/> com projeção direta para o DTO, servida do cache.
/// </summary>
/// <remarks>
/// <para>
/// <b>`AsNoTracking` e `Select` no lugar de `Include`.</b> A diferença em relação ao <c>OrderRepository</c> não é
/// estilo: aqui o SQL traz apenas as colunas do response e o EF não guarda snapshot de nada. O repositório
/// materializa o agregado porque quem escreve precisa dele; a leitura não precisa, e cobrar o preço de um
/// agregado completo por uma consulta é o que faz CQRS deixar de valer a pena.
/// </para>
/// <para>
/// <b>O cache vive aqui, e não no pipeline.</b> O <c>CachingBehavior</c> guardaria o <c>Result&lt;T&gt;</c>, e o
/// <c>Result</c> não atravessa serialização: o <c>System.Text.Json</c> exige que cada parâmetro do construtor
/// case com uma propriedade <b>do próprio tipo</b>, e <c>IsSuccess</c>/<c>Error</c> vivem na classe base. Fazer o
/// envelope de controle de fluxo serializável exigiria achatar a hierarquia — mudança no tipo central do kit por
/// um detalhe de infraestrutura. Aqui o que se guarda é o DTO, que é dado e serializa sem cerimônia.
/// </para>
/// <para>
/// A chave vem de <c>GetOrderByIdQuery.ChaveDe</c>, e não de uma string montada aqui: quem invalida precisa
/// produzir exatamente a mesma chave, e duas interpolações em lugares diferentes divergem por um caractere sem
/// que nada falhe.
/// </para>
/// </remarks>
internal sealed class OrderReader(AppDbContext context, ICacheService cache) : IOrderReader
{
    /// <inheritdoc />
    public async Task<OrderResponse?> GetByIdAsync(
        Guid orderId,
        CancellationToken cancellationToken = default) =>
        await cache.GetOrCreateAsync(
            GetOrderByIdQuery.ChaveDe(orderId),
            token => ConsultarAsync(orderId, token),
            GetOrderByIdQuery.ValidadeDoCache,
            cancellationToken);

    private async Task<OrderResponse?> ConsultarAsync(Guid orderId, CancellationToken cancellationToken)
    {
        // Compara a identidade tipada inteira, não `o.Id.Value`: OrderId é mapeado por conversor de valor, e
        // navegar até a propriedade interna dentro da expressão compila mas não traduz para SQL.
        OrderId id = new(orderId);

        return await context.Orders
            .AsNoTracking()
            .Where(order => order.Id == id)
            .Select(order => new OrderResponse(
                order.Id.Value,
                order.CustomerId.Value,
                order.Status.ToString(),

                // O total é somado no banco, não em memória: o Order.Total é calculado percorrendo os itens, e
                // usá-lo aqui exigiria materializar o agregado — exatamente o que esta consulta evita.
                order.Items.Sum(item => item.UnitPrice.Amount * item.Quantity),
                order.Currency,
                order.CreatedAt,
                order.UpdatedAt,
                order.Items
                    .Select(item => new OrderItemResponse(
                        item.ProductId.Value,
                        item.Quantity,
                        item.UnitPrice.Amount,
                        item.UnitPrice.Amount * item.Quantity))
                    .ToList()))
            .FirstOrDefaultAsync(cancellationToken);
    }
}
