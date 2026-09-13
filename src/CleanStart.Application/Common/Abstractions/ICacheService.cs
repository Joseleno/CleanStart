namespace CleanStart.Application.Common.Abstractions;

/// <summary>
/// Cache de resultados de consulta.
/// </summary>
/// <remarks>
/// <para>
/// Abstrai o <c>HybridCache</c>, que é implementado na Infrastructure. A Application **não** referencia o
/// pacote <c>Microsoft.Extensions.Caching.Hybrid</c>: cache é detalhe de infraestrutura, e o caso de uso só
/// precisa saber que existe um lugar onde guardar a resposta.
/// </para>
/// <para>
/// A assinatura é <c>GetOrCreate</c> e não um par <c>Get</c>/<c>Set</c> de propósito. Com dois métodos
/// separados, cada chamador reimplementa o mesmo "tentei, não achei, calculei, gravei" — e a janela entre o
/// <c>Get</c> que falha e o <c>Set</c> é exatamente onde N requisições simultâneas calculam a mesma coisa
/// (cache stampede). Aqui a implementação resolve isso uma vez.
/// </para>
/// </remarks>
public interface ICacheService
{
    /// <summary>
    /// Devolve o valor do cache ou, na ausência dele, executa <paramref name="factory"/> e guarda o resultado.
    /// </summary>
    /// <param name="key">Chave do valor. Precisa distinguir todos os parâmetros que mudam a resposta.</param>
    /// <param name="factory">Como obter o valor quando ele não está no cache.</param>
    /// <param name="expiration">Validade, ou nulo para o padrão da configuração.</param>
    Task<T> GetOrCreateAsync<T>(
        string key,
        Func<CancellationToken, Task<T>> factory,
        TimeSpan? expiration = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Remove uma entrada.
    /// </summary>
    /// <remarks>
    /// Usado quando um comando invalida o que uma consulta cacheou. Invalidar é responsabilidade de quem
    /// altera o dado — cache que só expira por tempo serve dado velho entre a alteração e a expiração.
    /// </remarks>
    Task RemoveAsync(string key, CancellationToken cancellationToken = default);
}
