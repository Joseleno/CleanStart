namespace CleanStart.Application.Common.Abstractions;

/// <summary>
/// Consulta a cotação de uma moeda em um serviço externo.
/// </summary>
/// <remarks>
/// <para>
/// <b>É o exemplo de dependência externa do kit</b> — o lugar onde se vê como chamar alguém de fora sem que a
/// indisponibilidade dele vire a nossa. Nenhum caso de uso a consome hoje: ela existe para mostrar a forma, e o
/// dia em que um pedido precisar converter moeda, o caminho já está montado.
/// </para>
/// <para>
/// <b>A interface fica aqui e a implementação na Infrastructure</b>, como qualquer outra dependência externa. O
/// caso de uso pede uma cotação; que ela venha de HTTP, de cache ou de uma tabela é decisão de outra camada — e
/// é o que permite testar o caso de uso sem rede.
/// </para>
/// <para>
/// <b>Devolve <c>decimal?</c> e não lança em falha de rede.</b> Serviço externo fora do ar é estado previsto, não
/// excepcional: quem chama precisa decidir entre seguir sem a cotação, usar a última conhecida ou recusar a
/// operação — e essa decisão é de negócio. Uma exception forçaria todo chamador a um <c>try/catch</c> para
/// tomá-la.
/// </para>
/// </remarks>
public interface IExchangeRateClient
{
    /// <summary>
    /// Devolve quantas unidades de <paramref name="destino"/> valem uma de <paramref name="origem"/>, ou
    /// <c>null</c> se o serviço não respondeu.
    /// </summary>
    Task<decimal?> ObterCotacaoAsync(string origem, string destino, CancellationToken cancellationToken = default);
}
