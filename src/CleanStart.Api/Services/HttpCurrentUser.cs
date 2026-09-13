using System.Security.Claims;
using CleanStart.Application.Common.Abstractions;

namespace CleanStart.Api.Services;

/// <summary>
/// Usuário autenticado da requisição HTTP.
/// </summary>
/// <remarks>
/// <para>
/// Substitui o <c>NoCurrentUser</c> da Infrastructure. Lê o claim de identidade do <c>HttpContext</c>, que é o
/// único lugar onde essa informação existe — e é por isso que esta implementação pertence à Api, não à
/// Infrastructure.
/// </para>
/// <para>
/// <b>Autenticação ainda não está configurada</b> (não está em nenhuma task da Fase 4), então na prática o claim
/// não vem e o Id é nulo. A classe existe desde já para que o interceptor de auditoria não precise mudar quando
/// a auth entrar: o contrato é o mesmo, só passa a ter valor.
/// </para>
/// </remarks>
internal sealed class HttpCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    /// <inheritdoc />
    public Guid? Id
    {
        get
        {
            string? valor = accessor.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier);

            // Guid.TryParse e não Parse: um claim malformado é dado externo, e derrubar a requisição por causa
            // dele seria pior que tratar a operação como anônima.
            return Guid.TryParse(valor, out Guid id) ? id : null;
        }
    }

    /// <inheritdoc />
    public bool IsAuthenticated => accessor.HttpContext?.User.Identity?.IsAuthenticated ?? false;
}
