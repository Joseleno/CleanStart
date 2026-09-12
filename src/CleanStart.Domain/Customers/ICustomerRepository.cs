using CleanStart.Domain.ValueObjects;

namespace CleanStart.Domain.Customers;

/// <summary>
/// Acesso a clientes persistidos.
/// </summary>
/// <remarks>
/// Mesmo desenho do <c>IOrderRepository</c>: interface no Domain, implementação na Infrastructure, nenhum
/// <c>IQueryable</c> na assinatura e repositório só para a raiz do agregado.
/// </remarks>
public interface ICustomerRepository
{
    /// <summary>Busca um cliente, ou <c>null</c> se não existir.</summary>
    Task<Customer?> GetByIdAsync(CustomerId id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Busca um cliente pelo documento.
    /// </summary>
    /// <remarks>
    /// Existe para o caso de uso de cadastro verificar duplicidade antes de registrar. A unicidade real é
    /// garantida por índice único no banco — a consulta serve para devolver erro de negócio legível em vez
    /// de deixar estourar violação de constraint.
    /// </remarks>
    Task<Customer?> GetByDocumentAsync(Document document, CancellationToken cancellationToken = default);

    /// <summary>Indica se já existe cliente com o documento informado.</summary>
    Task<bool> ExistsWithDocumentAsync(Document document, CancellationToken cancellationToken = default);

    /// <summary>Registra um cliente novo.</summary>
    /// <remarks>Não persiste: o limite da transação é do <c>IUnitOfWork</c>.</remarks>
    void Add(Customer customer);
}
