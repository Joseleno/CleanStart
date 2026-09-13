using System.Text.Json;
using CleanStart.Domain.Common;
using CleanStart.Infrastructure.Persistence.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace CleanStart.Infrastructure.Persistence.Interceptors;

/// <summary>
/// Coleta os domain events das raízes de agregado e os grava na tabela de outbox, na mesma transação.
/// </summary>
/// <remarks>
/// <para>
/// <b>É o ponto do padrão outbox.</b> Gravar o dado e publicar na fila são dois sistemas, e nenhuma transação
/// cobre os dois: publicar antes do commit anuncia fato que pode ser desfeito; publicar depois perde o evento se
/// o processo cair no meio. Gravando a mensagem junto com o dado, o commit é atômico — ou os dois acontecem, ou
/// nenhum. Um despachante separado relê a tabela e publica.
/// </para>
/// <para>
/// Roda em <c>SavingChanges</c>, antes do commit, justamente para que os <c>INSERT</c> do outbox entrem na mesma
/// unidade de trabalho que o dado. Em <c>SavedChanges</c> seria tarde: a transação já teria fechado.
/// </para>
/// </remarks>
internal sealed class DomainEventInterceptor : SaveChangesInterceptor
{
    /// <remarks>
    /// <c>Web</c> em vez do padrão: camelCase e tolerância a caixa na leitura, que é o que um consumidor em outra
    /// linguagem espera encontrar no JSON.
    /// </remarks>
    private static readonly JsonSerializerOptions OpcoesDeSerializacao = new(JsonSerializerDefaults.Web);

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(eventData);

        Coletar(eventData.Context);

        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        ArgumentNullException.ThrowIfNull(eventData);

        Coletar(eventData.Context);

        return base.SavingChanges(eventData, result);
    }

    private static void Coletar(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        // Materializa antes de iterar: acrescentar OutboxMessage ao contexto altera o change tracker, e
        // percorrer a coleção enquanto ela muda lança InvalidOperationException.
        List<EntityEntry> raizes = [.. context.ChangeTracker
            .Entries()
            .Where(entrada => entrada.Entity is IHasDomainEvents { DomainEvents.Count: > 0 })];

        foreach (EntityEntry entrada in raizes)
        {
            var raiz = (IHasDomainEvents)entrada.Entity;

            List<IDomainEvent> eventos = [.. raiz.DomainEvents];

            // Limpa antes de gravar as mensagens, não depois: se o SaveChanges falhar e alguém tentar de novo
            // com a mesma instância, os eventos já coletados não seriam duplicados.
            raiz.ClearDomainEvents();

            foreach (IDomainEvent evento in eventos)
            {
                context.Set<OutboxMessage>().Add(new OutboxMessage
                {
                    Id = Guid.CreateVersion7(),
                    // O nome do tipo, não o assembly-qualified: amarrar a mensagem à versão do assembly que a
                    // gravou quebraria o consumo depois de um rename ou de uma atualização.
                    Type = evento.GetType().FullName ?? evento.GetType().Name,
                    Content = JsonSerializer.Serialize(evento, evento.GetType(), OpcoesDeSerializacao),
                    OccurredOn = evento.OccurredOn,
                });
            }
        }
    }
}
