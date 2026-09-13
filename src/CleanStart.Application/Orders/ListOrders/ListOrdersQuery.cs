using CleanStart.Application.Common.Messaging;
using CleanStart.Domain.Orders;

namespace CleanStart.Application.Orders.ListOrders;

/// <summary>
/// Lista pedidos, com filtro e paginação por cursor.
/// </summary>
/// <remarks>
/// <para>
/// <b>Não é cacheada</b>, e a decisão é deliberada: cada combinação de filtro e cursor é uma chave distinta, e um
/// pedido novo torna obsoleto um número indeterminado delas. Sem invalidação confiável, o cache serviria listagem
/// desatualizada — pior que consultar o banco, porque o erro não aparece.
/// </para>
/// </remarks>
/// <param name="Status">Filtra por situação, ou nulo para todas.</param>
/// <param name="De">Início do período (por data de criação), ou nulo.</param>
/// <param name="Ate">Fim do período, ou nulo.</param>
/// <param name="Cursor">Posição de onde continuar, devolvida pela página anterior. Nulo na primeira página.</param>
/// <param name="Tamanho">Quantos itens trazer.</param>
public sealed record ListOrdersQuery(
    OrderStatus? Status = null,
    DateTimeOffset? De = null,
    DateTimeOffset? Ate = null,
    string? Cursor = null,
    int Tamanho = ListOrdersQuery.TamanhoPadrao) : IQuery<OrdersPage>
{
    /// <summary>Tamanho de página quando o cliente não informa.</summary>
    public const int TamanhoPadrao = 20;

    /// <summary>
    /// Teto de itens por página.
    /// </summary>
    /// <remarks>
    /// Existe para que o cliente não peça uma página de 100 mil registros — deliberadamente ou por engano em um
    /// laço. Sem teto, um único parâmetro derruba a memória do servidor.
    /// </remarks>
    public const int TamanhoMaximo = 100;
}
