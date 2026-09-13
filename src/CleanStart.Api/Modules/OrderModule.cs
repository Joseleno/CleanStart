using Carter;
using CleanStart.Api.Extensions;
using CleanStart.Application.Common.Abstractions;
using CleanStart.Application.Orders.CancelOrder;
using CleanStart.Application.Orders.GetOrderById;
using CleanStart.Application.Orders.ListOrders;
using CleanStart.Application.Orders.PlaceOrder;
using CleanStart.Domain.Common;
using CleanStart.Domain.Orders;
using Mediator;
using Microsoft.AspNetCore.Mvc;

namespace CleanStart.Api.Modules;

/// <summary>
/// Endpoints de pedidos.
/// </summary>
/// <remarks>
/// <para>
/// Módulo Carter em vez de controller: o endpoint é uma função, não um método de uma classe com estado e
/// atributos. O agrupamento por recurso fica explícito no <c>MapGroup</c>, e não implícito num nome de classe.
/// </para>
/// <para>
/// <b>O endpoint faz três coisas e nada mais:</b> converte o request em command, envia pelo <see cref="ISender"/> e
/// traduz o <c>Result</c> em HTTP pelo helper único. Não há regra de negócio aqui — nem validação, que é do
/// pipeline, nem decisão, que é do domínio.
/// </para>
/// <para>
/// <b>Público, não internal:</b> o Carter descobre os módulos por varredura de tipos públicos do assembly, e um
/// módulo internal é silenciosamente ignorado — a rota simplesmente não existe e o endpoint responde 404, sem
/// erro de build nem aviso no log. Descoberto da maneira difícil.
/// </para>
/// </remarks>
public sealed class OrderModule : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        // Versão no caminho desde o primeiro endpoint: acrescentar /v2 depois é trivial, mas mover de
        // /api/orders para /api/v1/orders quebra todo cliente existente.
        RouteGroupBuilder grupo = app
            .MapGroup("/api/v1/orders")
            .WithTags("Pedidos");

        grupo.MapPost("/", CriarPedido)
            .WithName("CriarPedido")
            .WithSummary("Cria um pedido")
            .WithDescription(
                "Cria um pedido para um cliente existente. Todos os itens devem usar a mesma moeda.")
            .Produces<PlaceOrderResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        grupo.MapGet("/", ListarPedidos)
            .WithName("ListarPedidos")
            .WithSummary("Lista pedidos")
            .WithDescription(
                "Lista pedidos com filtro por status e período. A paginação é por cursor: use o "
                + "`proximoCursor` da resposta para pedir a página seguinte. Cursor nulo significa fim.")
            .Produces<OrdersPage>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest);

        grupo.MapGet("/{id:guid}", BuscarPedido)
            .WithName("BuscarPedido")
            .WithSummary("Busca um pedido pela identidade")
            .WithDescription(
                "Devolve o pedido com os seus itens. O resultado é cacheado por cinco minutos e invalidado "
                + "quando o pedido é alterado.")
            .Produces<OrderResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

        grupo.MapPost("/{id:guid}/cancel", CancelarPedido)
            .WithName("CancelarPedido")
            .WithSummary("Cancela um pedido")
            .WithDescription(
                "Cancela um pedido pendente ou pago. Pedido já enviado não pode ser cancelado — o que existe "
                + "a partir daí é devolução, que é outro processo.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);
    }

    /// <summary>
    /// <c>POST /api/v1/orders/{id}/cancel</c>
    /// </summary>
    /// <remarks>
    /// <c>POST</c> num sub-recurso, e não <c>DELETE</c> no pedido: cancelar não remove nada, muda a situação de
    /// um registro que continua existindo. <c>DELETE</c> sugeriria que o pedido some — e ele é histórico.
    /// </remarks>
    private static async Task<IResult> CancelarPedido(
        Guid id,
        ISender sender,
        ICorrelationIdProvider correlationId,
        CancellationToken cancellationToken)
    {
        Result resultado = await sender.Send(new CancelOrderCommand(id), cancellationToken);

        return resultado.ParaNoContent(correlationId.CorrelationId);
    }

    /// <summary>
    /// <c>GET /api/v1/orders</c>
    /// </summary>
    /// <remarks>
    /// Os filtros vêm da query string, e o binding os converte. <c>status</c> inválido vira 400 do próprio
    /// binding — antes do handler, como o <c>:guid</c> faz na rota do item.
    /// </remarks>
    private static async Task<IResult> ListarPedidos(
        ISender sender,
        ICorrelationIdProvider correlationId,
        CancellationToken cancellationToken,
        OrderStatus? status = null,
        DateTimeOffset? de = null,
        DateTimeOffset? ate = null,
        string? cursor = null,
        int tamanho = ListOrdersQuery.TamanhoPadrao)
    {
        Result<OrdersPage> resultado = await sender.Send(
            new ListOrdersQuery(status, de, ate, cursor, tamanho),
            cancellationToken);

        return resultado.ParaOk(correlationId.CorrelationId);
    }

    /// <summary>
    /// <c>GET /api/v1/orders/{id}</c>
    /// </summary>
    /// <remarks>
    /// A restrição <c>:guid</c> na rota faz um id malformado virar <b>404 do roteamento</b>, antes de qualquer
    /// código rodar — em vez de chegar ao handler e falhar na conversão.
    /// </remarks>
    private static async Task<IResult> BuscarPedido(
        Guid id,
        ISender sender,
        ICorrelationIdProvider correlationId,
        CancellationToken cancellationToken)
    {
        Result<OrderResponse> resultado = await sender.Send(
            new GetOrderByIdQuery(id),
            cancellationToken);

        return resultado.ParaOk(correlationId.CorrelationId);
    }

    /// <summary>
    /// <c>POST /api/v1/orders</c>
    /// </summary>
    /// <remarks>
    /// O request é um tipo próprio da Api, não o command: são contratos com ciclos de vida diferentes. Renomear
    /// um campo do command é refatoração interna; renomear um campo do request é breaking change para o cliente.
    /// Juntá-los faz com que toda mudança interna vire risco externo.
    /// </remarks>
    private static async Task<IResult> CriarPedido(
        [FromBody] CriarPedidoRequest request,
        ISender sender,
        ICorrelationIdProvider correlationId,
        CancellationToken cancellationToken)
    {
        // Corpo ausente ou `null` literal: o binding entrega null e o tipo não-anulável não protege disso.
        // Devolver 400 aqui, e não deixar estourar, é a diferença entre "requisição malformada" e 500.
        if (request is null)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Requisição inválida",
                detail: "O corpo da requisição é obrigatório.",
                extensions: new Dictionary<string, object?>
                {
                    ["correlationId"] = correlationId.CorrelationId,
                });
        }

        // `Items` chega null quando o JSON omite a propriedade — o tipo não-anulável é promessa do compilador,
        // não do desserializador. Uma lista vazia deixa o domínio recusar com a mensagem certa (Order.SemItens),
        // em vez de estourar NullReferenceException e virar 500.
        IReadOnlyList<CriarPedidoItemRequest> itens = request.Items ?? [];

        PlaceOrderCommand comando = new(
            request.CustomerId,
            request.Currency ?? string.Empty,
            [.. itens.Select(item =>
                new PlaceOrderItemRequest(item.ProductId, item.Quantity, item.UnitPrice))]);

        Result<PlaceOrderResponse> resultado = await sender.Send(comando, cancellationToken);

        return resultado.ParaCreated(
            localizacao: pedido => $"/api/v1/orders/{pedido.Id}",
            correlationId: correlationId.CorrelationId);
    }
}

/// <summary>
/// Corpo da requisição de criação de pedido.
/// </summary>
/// <param name="CustomerId">Cliente que faz o pedido.</param>
/// <param name="Currency">Moeda do pedido, em três letras (ex.: <c>BRL</c>).</param>
/// <param name="Items">Itens do pedido. Precisa de ao menos um.</param>
public sealed record CriarPedidoRequest(
    Guid CustomerId,
    string Currency,
    IReadOnlyList<CriarPedidoItemRequest> Items);

/// <summary>
/// Um item na requisição de criação de pedido.
/// </summary>
/// <param name="ProductId">Produto comprado.</param>
/// <param name="Quantity">Quantidade, maior que zero.</param>
/// <param name="UnitPrice">Preço unitário praticado.</param>
public sealed record CriarPedidoItemRequest(Guid ProductId, int Quantity, decimal UnitPrice);
