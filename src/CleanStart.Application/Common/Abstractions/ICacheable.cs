namespace CleanStart.Application.Common.Abstractions;

/// <summary>
/// Consulta cujo resultado pode ser servido do cache.
/// </summary>
/// <remarks>
/// <para>
/// Marcador opcional: o <c>CachingBehavior</c> (T2.2) só intercepta a query que o implementa. Quem não
/// implementa passa direto, sem cache — o padrão é <b>não</b> cachear, porque cache é decisão consciente sobre
/// um dado específico, não comportamento que se ganha por descuido.
/// </para>
/// <para>
/// Só faz sentido em <c>IQuery</c>. Comando altera estado; servir um comando do cache significaria não
/// executá-lo.
/// </para>
/// </remarks>
public interface ICacheable
{
    /// <summary>
    /// Chave que identifica este resultado no cache.
    /// </summary>
    /// <remarks>
    /// A implementação precisa incluir <b>todos</b> os parâmetros que mudam o resultado. Chave que ignora um
    /// filtro serve a resposta de uma consulta para outra — e o sintoma é dado errado aparecendo sem erro
    /// nenhum no log, que é o defeito mais caro de diagnosticar.
    /// </remarks>
    string CacheKey { get; }

    /// <summary>
    /// Por quanto tempo o valor vale, ou nulo para usar o padrão da configuração.
    /// </summary>
    TimeSpan? Expiration { get; }
}
