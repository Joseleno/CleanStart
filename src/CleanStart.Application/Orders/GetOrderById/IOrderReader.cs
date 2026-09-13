namespace CleanStart.Application.Orders.GetOrderById;

/// <summary>
/// Leitura projetada de pedidos, para consultas.
/// </summary>
/// <remarks>
/// <para>
/// <b>Separado do <c>IOrderRepository</c> de propósito, e é a decisão que dá sentido ao CQRS aqui.</b> O
/// repositório serve ao lado de escrita: devolve o agregado inteiro, rastreado pelo contexto, com os itens
/// carregados, porque quem vai alterá-lo precisa das invariantes e do change tracking. Uma consulta não precisa
/// de nada disso — e paga caro por tudo.
/// </para>
/// <para>
/// A leitura projeta direto para o DTO, sem rastreamento: a consulta traz só as colunas do response, e o EF não
/// guarda snapshot de nada. Em listagem isso é a diferença entre uma consulta e N.
/// </para>
/// <para>
/// Vive na pasta do slice, não em <c>Common</c>: cada caso de uso declara a leitura de que precisa. Uma interface
/// de leitura compartilhada cresce até virar o repositório genérico que o CQRS existe para evitar.
/// </para>
/// </remarks>
public interface IOrderReader
{
    /// <summary>
    /// Busca um pedido projetado, ou <c>null</c> se não existir.
    /// </summary>
    Task<OrderResponse?> GetByIdAsync(Guid orderId, CancellationToken cancellationToken = default);
}
