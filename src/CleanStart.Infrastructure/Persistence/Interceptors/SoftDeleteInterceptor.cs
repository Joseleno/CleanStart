using CleanStart.Application.Common.Abstractions;
using CleanStart.Domain.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace CleanStart.Infrastructure.Persistence.Interceptors;

/// <summary>
/// Converte exclusão física em marcação lógica, para quem implementa <see cref="ISoftDeletable"/>.
/// </summary>
/// <remarks>
/// <para>
/// Existe para que <c>Remove()</c> nunca apague a linha de uma entidade referenciada por histórico. Pedido antigo
/// aponta para cliente; apagar o cliente deixaria o pedido referenciando o vazio — ou estouraria a foreign key,
/// que é o melhor dos casos porque pelo menos faz ruído.
/// </para>
/// <para>
/// O par deste interceptor é o filtro global de query do <c>AppDbContext</c>: um marca, o outro esconde. Sem os
/// dois, o registro continuaria visível em toda consulta.
/// </para>
/// <para>
/// <b>Nota de desenho:</b> o caminho recomendado é o método de domínio (<c>Customer.Delete</c>), que além de
/// marcar pode recusar — excluir duas vezes, por exemplo. Este interceptor é a rede de segurança para o
/// <c>Remove()</c> que escapa, não o caminho principal.
/// </para>
/// </remarks>
internal sealed class SoftDeleteInterceptor(IDateTimeProvider clock) : SaveChangesInterceptor
{
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(eventData);

        Converter(eventData.Context);

        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        ArgumentNullException.ThrowIfNull(eventData);

        Converter(eventData.Context);

        return base.SavingChanges(eventData, result);
    }

    private void Converter(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        DateTimeOffset agora = clock.UtcNow;

        foreach (EntityEntry<ISoftDeletable> entrada in context.ChangeTracker.Entries<ISoftDeletable>())
        {
            if (entrada.State != EntityState.Deleted)
            {
                continue;
            }

            // Trocar o estado para Modified é o que transforma o DELETE em UPDATE. Precisa vir antes de
            // atribuir as colunas: no estado Deleted, o EF ignora alteração de propriedade.
            entrada.State = EntityState.Modified;

            entrada.Property(excluivel => excluivel.IsDeleted).CurrentValue = true;
            entrada.Property(excluivel => excluivel.DeletedAt).CurrentValue = agora;
        }
    }
}
