namespace CleanStart.Application.Common.Abstractions;

/// <summary>
/// Quem está executando a operação.
/// </summary>
/// <remarks>
/// Abstrai <c>HttpContext.User</c> para que o caso de uso não dependa de ASP.NET Core — é o que permite
/// testá-lo sem subir servidor, e expor o mesmo caso de uso por um worker ou job depois.
/// <para>
/// <see cref="Id"/> é nulo quando não há usuário autenticado (job, requisição anônima). Quem precisa de
/// usuário obrigatório trata o nulo explicitamente, em vez de receber um <c>Guid.Empty</c> que se parece com
/// um id de verdade.
/// </para>
/// </remarks>
public interface ICurrentUser
{
    /// <summary>Identidade do usuário, ou nulo se a operação não tem usuário.</summary>
    Guid? Id { get; }

    /// <summary>Se há usuário autenticado.</summary>
    bool IsAuthenticated { get; }
}
