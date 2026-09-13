namespace CleanStart.Application.Common.Abstractions;

/// <summary>
/// Limite transacional de um caso de uso.
/// </summary>
/// <remarks>
/// <para>
/// Existe para que o handler decida <b>quando</b> gravar sem saber <b>como</b>. Os repositórios registram
/// intenção (<c>Add</c>); é o commit daqui que efetiva — e é isso que permite um caso de uso alterar dois
/// agregados e gravar os dois de uma vez ou nenhum.
/// </para>
/// <para>
/// Na Infrastructure, a implementação é o <c>DbContext</c>. A abstração existe para que o handler não o
/// conheça: com <c>DbContext</c> injetado direto, qualquer handler poderia montar consulta e a fronteira entre
/// Application e persistência deixaria de existir.
/// </para>
/// </remarks>
public interface IUnitOfWork
{
    /// <summary>
    /// Persiste as alterações pendentes.
    /// </summary>
    /// <returns>Quantas linhas foram afetadas.</returns>
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
