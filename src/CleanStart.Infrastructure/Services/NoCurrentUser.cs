using CleanStart.Application.Common.Abstractions;

namespace CleanStart.Infrastructure.Services;

/// <summary>
/// Usuário ausente — para job, seed e processo sem requisição HTTP.
/// </summary>
/// <remarks>
/// <para>
/// A implementação que lê o <c>HttpContext</c> pertence à Api (Fase 4), porque é lá que o contexto existe. Esta é
/// o padrão registrado pela Infrastructure, e o que ela faz é responder honestamente "não há usuário".
/// </para>
/// <para>
/// Sem este registro, a auditoria quebraria em todo processo fora de requisição — um job não tem de ser alterado
/// para funcionar, ele só grava autoria nula.
/// </para>
/// </remarks>
internal sealed class NoCurrentUser : ICurrentUser
{
    /// <inheritdoc />
    public Guid? Id => null;

    /// <inheritdoc />
    public bool IsAuthenticated => false;
}
