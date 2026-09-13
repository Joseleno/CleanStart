using CleanStart.Domain.Common;
using CleanStart.Domain.Customers;
using CleanStart.Domain.Errors;
using CleanStart.Domain.Orders.Events;
using CleanStart.Domain.ValueObjects;

namespace CleanStart.Domain.Orders;

/// <summary>
/// Pedido: a raiz do agregado que governa os itens e a transição de situação.
/// </summary>
/// <remarks>
/// <para>
/// Toda alteração no pedido ou nos seus itens passa por um método daqui. É o que permite garantir as
/// invariantes do conjunto — "tem ao menos um item", "todos na mesma moeda", "não se cancela o que foi
/// enviado" — em vez de depender de cada chamador lembrar delas.
/// </para>
/// <para>
/// Não há setter público nem construtor público: o estado muda por método de domínio, com nome que diz o
/// que aconteceu no negócio (<see cref="Pay"/>, <see cref="Cancel"/>), não por atribuição de propriedade.
/// O construtor privado com itens existe para o EF Core materializar a entidade na leitura.
/// </para>
/// </remarks>
public sealed class Order : AggregateRoot<OrderId>, IAuditable
{
    private readonly List<OrderItem> _items = [];

    private Order(OrderId id, CustomerId customerId, string currency, DateTimeOffset createdAt)
        : base(id)
    {
        CustomerId = customerId;
        Currency = currency;
        Status = OrderStatus.Pending;
        CreatedAt = createdAt;
    }

    /// <summary>Cliente que fez o pedido.</summary>
    public CustomerId CustomerId { get; }

    /// <summary>Situação atual.</summary>
    public OrderStatus Status { get; private set; }

    /// <summary>
    /// Moeda do pedido, fixada pelo primeiro item.
    /// </summary>
    /// <remarks>
    /// Guardada na raiz para que a invariante "todos os itens na mesma moeda" tenha onde ser verificada sem
    /// depender da ordem em que os itens foram adicionados.
    /// </remarks>
    public string Currency { get; }

    /// <summary>Itens do pedido, somente leitura.</summary>
    /// <remarks>
    /// Exposto como <see cref="IReadOnlyCollection{T}"/> porque acrescentar item é decisão do pedido, que
    /// precisa validar quantidade e moeda. Devolver a <c>List</c> interna permitiria a qualquer chamador
    /// inserir um item inválido e furar a invariante por fora.
    /// </remarks>
    public IReadOnlyCollection<OrderItem> Items => _items.AsReadOnly();

    /// <inheritdoc />
    public DateTimeOffset CreatedAt { get; }

    /// <inheritdoc />
    public DateTimeOffset? UpdatedAt { get; private set; }

    /// <inheritdoc />
    /// <remarks>Preenchido pelo interceptor de auditoria, na Infrastructure — não pela regra de negócio.</remarks>
    public Guid? CreatedBy { get; private set; }

    /// <inheritdoc />
    public Guid? UpdatedBy { get; private set; }

    /// <summary>
    /// Soma dos totais dos itens.
    /// </summary>
    /// <remarks>
    /// Calculado, não armazenado: total guardado pode divergir dos itens, e então existem duas respostas
    /// para a mesma pergunta. Como a invariante garante moeda única, a soma não pode falhar aqui.
    /// </remarks>
    public Money Total => _items.Count == 0
        ? Money.Of(0m, Currency).Value
        : _items.Aggregate(
            Money.Of(0m, Currency).Value,
            (acumulado, item) => acumulado.Add(item.Total).Value);

    /// <summary>
    /// Cria um pedido com os itens informados.
    /// </summary>
    /// <remarks>
    /// É a única porta de entrada: não existe pedido recém-criado sem item, porque a validação acontece
    /// antes de o objeto existir. Um construtor público com <c>AddItem</c> depois permitiria um pedido vazio
    /// circular por aí em estado inválido.
    /// </remarks>
    /// <param name="customerId">Cliente do pedido.</param>
    /// <param name="itens">Produto, quantidade e preço unitário de cada item. Precisa de ao menos um.</param>
    /// <param name="agora">Momento da criação, injetado para manter o teste determinístico.</param>
    public static Result<Order> Place(
        CustomerId customerId,
        IEnumerable<(ProductId ProductId, int Quantity, Money UnitPrice)> itens,
        DateTimeOffset agora)
    {
        ArgumentNullException.ThrowIfNull(itens);

        List<(ProductId ProductId, int Quantity, Money UnitPrice)> pedidos = [.. itens];

        if (pedidos.Count == 0)
        {
            return Result.Failure<Order>(DomainErrors.Order.SemItens());
        }

        string moeda = pedidos[0].UnitPrice.Currency;

        if (pedidos.Any(item => item.UnitPrice.Currency != moeda))
        {
            return Result.Failure<Order>(DomainErrors.Order.ItensEmMoedasDiferentes());
        }

        Order pedido = new(OrderId.New(), customerId, moeda, agora);

        foreach ((ProductId productId, int quantity, Money unitPrice) in pedidos)
        {
            Result<OrderItem> item = OrderItem.Create(productId, quantity, unitPrice);

            if (item.IsFailure)
            {
                // Falha de qualquer item aborta o pedido inteiro: meio pedido criado seria pior que
                // nenhum, porque o cliente receberia parte do que pediu sem saber.
                return Result.Failure<Order>(item.Error);
            }

            pedido._items.Add(item.Value);
        }

        pedido.RaiseDomainEvent(new OrderPlacedEvent(
            pedido.Id,
            pedido.CustomerId,
            pedido.Total.Amount,
            pedido.Currency,
            agora));

        return pedido;
    }

    /// <summary>
    /// Marca o pedido como pago.
    /// </summary>
    public Result Pay(DateTimeOffset agora)
    {
        if (Status != OrderStatus.Pending)
        {
            return Result.Failure(DomainErrors.Order.TransicaoInvalida(Status, OrderStatus.Paid));
        }

        Status = OrderStatus.Paid;
        UpdatedAt = agora;

        return Result.Success();
    }

    /// <summary>
    /// Marca o pedido como enviado.
    /// </summary>
    public Result Ship(DateTimeOffset agora)
    {
        if (Status != OrderStatus.Paid)
        {
            return Result.Failure(DomainErrors.Order.TransicaoInvalida(Status, OrderStatus.Shipped));
        }

        Status = OrderStatus.Shipped;
        UpdatedAt = agora;

        return Result.Success();
    }

    /// <summary>
    /// Cancela o pedido.
    /// </summary>
    /// <remarks>
    /// Pedido <see cref="OrderStatus.Shipped"/> não cancela: a mercadoria já saiu, e o que existe a partir
    /// daí é devolução, que é outro processo. Pedido já cancelado também não — cancelar duas vezes
    /// levantaria o evento duas vezes, e quem reage estornaria em dobro.
    /// </remarks>
    public Result Cancel(DateTimeOffset agora)
    {
        if (Status is OrderStatus.Shipped or OrderStatus.Cancelled)
        {
            return Result.Failure(DomainErrors.Order.TransicaoInvalida(Status, OrderStatus.Cancelled));
        }

        OrderStatus anterior = Status;

        Status = OrderStatus.Cancelled;
        UpdatedAt = agora;

        RaiseDomainEvent(new OrderCancelledEvent(Id, anterior, agora));

        return Result.Success();
    }

    /// <summary>
    /// Acrescenta um item ao pedido, ou soma à linha existente se o produto já estiver nele.
    /// </summary>
    public Result AddItem(ProductId productId, int quantity, Money unitPrice, DateTimeOffset agora)
    {
        ArgumentNullException.ThrowIfNull(unitPrice);

        if (Status != OrderStatus.Pending)
        {
            return Result.Failure(DomainErrors.Order.TransicaoInvalida(Status, OrderStatus.Pending));
        }

        if (unitPrice.Currency != Currency)
        {
            return Result.Failure(DomainErrors.Order.ItensEmMoedasDiferentes());
        }

        OrderItem? existente = _items.FirstOrDefault(item => item.ProductId == productId);

        if (existente is not null)
        {
            Result aumento = existente.IncreaseQuantity(quantity);

            if (aumento.IsFailure)
            {
                return aumento;
            }

            UpdatedAt = agora;

            return Result.Success();
        }

        Result<OrderItem> novo = OrderItem.Create(productId, quantity, unitPrice);

        if (novo.IsFailure)
        {
            return Result.Failure(novo.Error);
        }

        _items.Add(novo.Value);
        UpdatedAt = agora;

        return Result.Success();
    }
}
