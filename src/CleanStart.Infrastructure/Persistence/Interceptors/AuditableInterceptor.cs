using CleanStart.Application.Common.Abstractions;
using CleanStart.Domain.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace CleanStart.Infrastructure.Persistence.Interceptors;

/// <summary>
/// Preenche as colunas de auditoria no insert e no update.
/// </summary>
/// <remarks>
/// <para>
/// Auditoria é preocupação transversal: se cada método de domínio tivesse de atribuir <c>UpdatedAt</c>, a mesma
/// linha se repetiria em toda mudança de estado — e em algum método alguém esqueceria. O interceptor a aplica no
/// único ponto por onde tudo passa.
/// </para>
/// <para>
/// As propriedades são escritas por <c>CurrentValue</c> do change tracker, não por setter: elas são
/// <c>private set</c> no domínio, e é assim que a auditoria funciona sem abrir a entidade para o mundo.
/// </para>
/// </remarks>
internal sealed class AuditableInterceptor(
    IDateTimeProvider clock,
    ICurrentUser currentUser)
    : SaveChangesInterceptor
{
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(eventData);

        Preencher(eventData.Context);

        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    /// <summary>
    /// Sobrescrito também na versão síncrona: o <c>SaveChanges()</c> sem <c>Async</c> não passa pelo método
    /// assíncrono, e um interceptor que cobre só um dos dois deixa metade das gravações sem auditoria.
    /// </summary>
    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        ArgumentNullException.ThrowIfNull(eventData);

        Preencher(eventData.Context);

        return base.SavingChanges(eventData, result);
    }

    private void Preencher(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        DateTimeOffset agora = clock.UtcNow;
        Guid? usuario = currentUser.Id;

        foreach (EntityEntry<IAuditable> entrada in context.ChangeTracker.Entries<IAuditable>())
        {
            switch (entrada.State)
            {
                case EntityState.Added:
                    entrada.Property(auditavel => auditavel.CreatedAt).CurrentValue = agora;
                    entrada.Property(auditavel => auditavel.CreatedBy).CurrentValue = usuario;
                    break;

                case EntityState.Modified:
                    // CreatedAt e CreatedBy ficam de fora: marcá-los como não modificados impede que uma
                    // entidade recarregada e salva sobrescreva a autoria original.
                    entrada.Property(auditavel => auditavel.CreatedAt).IsModified = false;
                    entrada.Property(auditavel => auditavel.CreatedBy).IsModified = false;

                    entrada.Property(auditavel => auditavel.UpdatedAt).CurrentValue = agora;
                    entrada.Property(auditavel => auditavel.UpdatedBy).CurrentValue = usuario;
                    break;

                default:
                    break;
            }
        }
    }
}
