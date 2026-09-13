using CleanStart.Domain.Common;
using CleanStart.Domain.Errors;
using CleanStart.Domain.ValueObjects;

namespace CleanStart.Domain.Orders;

/// <summary>
/// Um item de pedido: produto, quantidade e o preço praticado na compra.
/// </summary>
/// <remarks>
/// <para>
/// Entidade, não value object: dois itens do mesmo produto com a mesma quantidade são linhas distintas do
/// pedido, e precisam de identidade própria para serem alteradas ou removidas individualmente.
/// </para>
/// <para>
/// Guarda <see cref="UnitPrice"/> em vez de consultar o preço do produto ao calcular: preço de catálogo
/// muda, e um pedido de seis meses atrás precisa continuar valendo o que valia. Copiar o preço no momento
/// da compra é o que torna o histórico estável.
/// </para>
/// <para>
/// Não é acessível de fora do agregado — criação e alteração passam pelo <see cref="Order"/>, que é quem
/// pode garantir a invariante do conjunto.
/// </para>
/// </remarks>
public sealed class OrderItem : Entity<Guid>
{
    private OrderItem(Guid id, ProductId productId, int quantity, Money unitPrice)
        : base(id)
    {
        ProductId = productId;
        Quantity = quantity;
        UnitPrice = unitPrice;
    }

    /// <summary>
    /// Construtor de materialização, usado apenas pelo ORM.
    /// </summary>
    /// <remarks>
    /// O EF Core precisa criar a instância antes de preencher um owned type (o <see cref="UnitPrice"/>), e
    /// por isso não consegue usar o construtor acima — ele exige o <c>Money</c> pronto. É a única concessão
    /// do domínio à persistência, e ela é privada: ninguém fora do ORM alcança este caminho, e nenhum atributo
    /// de EF entra na classe.
    /// <para>
    /// Descoberto por teste que constrói o modelo do EF — o código compilava, e a falha só aparecia ao montar
    /// o <c>IModel</c>.
    /// </para>
    /// </remarks>
    private OrderItem()
        : base(Guid.Empty)
    {
        UnitPrice = null!;
    }

    /// <summary>Produto comprado.</summary>
    public ProductId ProductId { get; }

    /// <summary>Quantidade. Sempre maior que zero.</summary>
    public int Quantity { get; private set; }

    /// <summary>Preço unitário praticado no momento da compra.</summary>
    /// <remarks>
    /// O setter é privado porque o EF precisa atribuí-lo ao materializar o owned type. Privado, ele continua
    /// imutável para todo o resto — e a regra de arquitetura que proíbe setter público segue valendo.
    /// </remarks>
    public Money UnitPrice { get; private set; }

    /// <summary>Preço unitário multiplicado pela quantidade.</summary>
    public Money Total => UnitPrice.Multiply(Quantity);

    /// <summary>
    /// Cria um item válido.
    /// </summary>
    /// <remarks>
    /// <c>internal</c> de propósito: só o <see cref="Order"/> cria item. Deixar público permitiria montar
    /// um item solto, fora de qualquer pedido, e a invariante "todo item pertence a um pedido" deixaria de
    /// ser garantida pelo tipo.
    /// </remarks>
    internal static Result<OrderItem> Create(ProductId productId, int quantity, Money unitPrice)
    {
        ArgumentNullException.ThrowIfNull(unitPrice);

        if (quantity <= 0)
        {
            return Result.Failure<OrderItem>(DomainErrors.Order.QuantidadeInvalida(quantity));
        }

        return new OrderItem(Guid.CreateVersion7(), productId, quantity, unitPrice);
    }

    /// <summary>
    /// Soma uma quantidade à existente.
    /// </summary>
    /// <remarks>
    /// Usado quando o mesmo produto é adicionado duas vezes ao pedido: em vez de criar uma segunda linha,
    /// o pedido acumula na linha que já existe.
    /// </remarks>
    internal Result IncreaseQuantity(int amount)
    {
        if (amount <= 0)
        {
            return Result.Failure(DomainErrors.Order.QuantidadeInvalida(amount));
        }

        Quantity += amount;

        return Result.Success();
    }
}
