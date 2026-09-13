using CleanStart.Application.Common.Messaging;

namespace CleanStart.Application.Orders.GetOrderById;

/// <summary>
/// Busca um pedido pela identidade.
/// </summary>
/// <remarks>
/// <para>
/// <b>Não implementa <c>ICacheable</c>, e vale saber por quê.</b> O <c>CachingBehavior</c> guardaria o
/// <c>Result&lt;OrderResponse&gt;</c>, e o <c>Result</c> não atravessa serialização: o <c>System.Text.Json</c>
/// exige que cada parâmetro do construtor case com uma propriedade <b>do próprio tipo</b>, e
/// <c>IsSuccess</c>/<c>Error</c> vivem na classe base. Tornar o envelope serializável exigiria achatar a
/// hierarquia do tipo central do kit por causa de um detalhe de infraestrutura.
/// </para>
/// <para>
/// O cache ficou no <c>OrderReader</c>, que trabalha com o DTO — dado, não envelope de controle de fluxo. As
/// constantes de chave e validade vivem aqui porque é a query que define a identidade do resultado, e tanto o
/// reader quanto o invalidador precisam produzir exatamente a mesma chave.
/// </para>
/// </remarks>
/// <param name="OrderId">Identidade do pedido.</param>
public sealed record GetOrderByIdQuery(Guid OrderId) : IQuery<OrderResponse>
{
    /// <summary>
    /// Prefixo das chaves de pedido, para que a invalidação saiba montar a mesma chave.
    /// </summary>
    public const string PrefixoDaChave = "order:";

    /// <summary>
    /// Por quanto tempo o pedido fica em cache.
    /// </summary>
    /// <remarks>
    /// Cinco minutos: curto o bastante para que uma invalidação perdida não sirva dado velho por muito tempo, e
    /// longo o bastante para absorver a rajada de leituras que segue uma criação.
    /// </remarks>
    public static readonly TimeSpan ValidadeDoCache = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Monta a chave de cache de um pedido.
    /// </summary>
    /// <remarks>
    /// Um método, e não interpolação no ponto de uso: quem invalida precisa produzir exatamente a chave que quem
    /// lê produziu. Duas interpolações em lugares diferentes divergem por um caractere, e ninguém percebe —
    /// porque nada falha, o cache apenas passa a servir dado velho.
    /// </remarks>
    public static string ChaveDe(Guid orderId) => $"{PrefixoDaChave}{orderId}";
}
