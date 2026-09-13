using CleanStart.Domain.Customers;
using CleanStart.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;

namespace CleanStart.Infrastructure.Persistence.Repositories;

/// <summary>
/// Implementa <see cref="ICustomerRepository"/> sobre o EF Core.
/// </summary>
/// <remarks>
/// As consultas não precisam filtrar por <c>IsDeleted</c>: o filtro global do <see cref="AppDbContext"/> já
/// esconde os excluídos. É o ganho de ter o filtro no modelo em vez de em cada consulta — nenhum método aqui
/// pode esquecê-lo.
/// </remarks>
internal sealed class CustomerRepository(AppDbContext context) : ICustomerRepository
{
    /// <inheritdoc />
    public async Task<Customer?> GetByIdAsync(
        CustomerId id,
        CancellationToken cancellationToken = default) =>
        await context.Customers
            .FirstOrDefaultAsync(customer => customer.Id == id, cancellationToken);

    /// <inheritdoc />
    public async Task<Customer?> GetByDocumentAsync(
        Document document,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);

        // Compara o value object inteiro, não `customer.Document.Value`: Document é mapeado por conversor de
        // valor, então a coluna guarda a string e o EF converte os dois lados da comparação. Navegar até `.Value`
        // dentro da expressão não traduz — compila e estoura em runtime.
        return await context.Customers
            .FirstOrDefaultAsync(customer => customer.Document == document, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<bool> ExistsWithDocumentAsync(
        Document document,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);

        // AnyAsync e não GetByDocument != null: traz um booleano do banco em vez de materializar a entidade
        // inteira só para descartá-la.
        return await context.Customers
            .AnyAsync(customer => customer.Document == document, cancellationToken);
    }

    /// <inheritdoc />
    public void Add(Customer customer) => context.Customers.Add(customer);
}
