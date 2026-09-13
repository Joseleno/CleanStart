using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace CleanStart.Infrastructure.Persistence.Interceptors;

/// <summary>
/// Registra os interceptors no <see cref="DbContextOptionsBuilder"/>, na ordem correta.
/// </summary>
/// <remarks>
/// <para>
/// <b>A ordem importa, e é o detalhe que um leitor deve levar daqui.</b> O <c>SoftDeleteInterceptor</c> troca o
/// estado de <c>Deleted</c> para <c>Modified</c>; o <c>AuditableInterceptor</c> preenche <c>UpdatedAt</c> em quem
/// está <c>Modified</c>. Na ordem abaixo, a exclusão lógica é auditada como a alteração que ela de fato é. Ao
/// contrário, a auditoria rodaria antes da conversão e a exclusão ficaria sem registro de quem e quando.
/// </para>
/// <para>
/// O <c>DomainEventInterceptor</c> vai por último porque acrescenta entidades novas ao change tracker — e as
/// mensagens de outbox não são auditáveis nem excluíveis logicamente, então nada depende de vê-las.
/// </para>
/// <para>
/// Existe como extensão separada para que a T3.3 monte a DI sem repetir esta ordem, e para que a ordem tenha um
/// lugar único onde está escrita com o motivo.
/// </para>
/// </remarks>
internal static class InterceptorRegistration
{
    /// <summary>
    /// Acrescenta os três interceptors de persistência.
    /// </summary>
    /// <remarks>
    /// Genérico em <typeparamref name="TContext"/> para preservar o tipo no encadeamento: a sobrecarga não
    /// genérica devolveria <c>DbContextOptionsBuilder</c>, e <c>.Options</c> deixaria de produzir
    /// <c>DbContextOptions&lt;AppDbContext&gt;</c> — obrigando quem chama a um cast.
    /// </remarks>
    public static DbContextOptionsBuilder<TContext> AddCleanStartInterceptors<TContext>(
        this DbContextOptionsBuilder<TContext> builder,
        SoftDeleteInterceptor softDelete,
        AuditableInterceptor auditable,
        DomainEventInterceptor domainEvents)
        where TContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(builder);

        IInterceptor[] naOrdem = [softDelete, auditable, domainEvents];

        return builder.AddInterceptors(naOrdem);
    }

    /// <summary>
    /// Sobrecarga para o <c>AddDbContext</c>, cujo delegate recebe o builder não genérico.
    /// </summary>
    public static DbContextOptionsBuilder AddCleanStartInterceptors(
        this DbContextOptionsBuilder builder,
        SoftDeleteInterceptor softDelete,
        AuditableInterceptor auditable,
        DomainEventInterceptor domainEvents)
    {
        ArgumentNullException.ThrowIfNull(builder);

        IInterceptor[] naOrdem = [softDelete, auditable, domainEvents];

        return builder.AddInterceptors(naOrdem);
    }
}
