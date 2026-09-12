using CleanStart.Domain.Customers;

namespace CleanStart.Domain.Orders;

/// <summary>
/// Acesso a pedidos persistidos.
/// </summary>
/// <remarks>
/// <para>
/// A interface vive no Domain e a implementação na Infrastructure — é a inversão de dependência que mantém
/// o domínio ignorante de EF Core, PostgreSQL e SQL. O domínio declara o que precisa; a infraestrutura
/// resolve como.
/// </para>
/// <para>
/// Só existe repositório para a <b>raiz</b> do agregado. Não há <c>IOrderItemRepository</c>: item é
/// alcançado pelo pedido, e carregá-lo isolado permitiria alterá-lo sem passar pelas invariantes do
/// conjunto.
/// </para>
/// <para>
/// Nenhum método devolve <c>IQueryable</c>. Devolver consulta pela metade deixaria a Application montar
/// filtro e projeção, ou seja, decidir detalhe de persistência — e tornaria impossível saber, lendo o
/// handler, quantas idas ao banco a chamada custa.
/// </para>
/// </remarks>
public interface IOrderRepository
{
    /// <summary>Busca um pedido com os seus itens, ou <c>null</c> se não existir.</summary>
    Task<Order?> GetByIdAsync(OrderId id, CancellationToken cancellationToken = default);

    /// <summary>Lista os pedidos de um cliente.</summary>
    Task<IReadOnlyList<Order>> GetByCustomerAsync(
        CustomerId customerId,
        CancellationToken cancellationToken = default);

    /// <summary>Registra um pedido novo.</summary>
    /// <remarks>
    /// Não persiste por conta própria: quem decide o limite da transação é o <c>IUnitOfWork</c>, para que
    /// um caso de uso que altera dois agregados grave os dois de uma vez ou nenhum.
    /// </remarks>
    void Add(Order order);
}
