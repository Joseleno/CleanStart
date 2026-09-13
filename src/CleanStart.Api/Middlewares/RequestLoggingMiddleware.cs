using System.Diagnostics;
using CleanStart.Application.Common.Abstractions;

namespace CleanStart.Api.Middlewares;

/// <summary>
/// Registra método, rota, status e duração de cada requisição.
/// </summary>
/// <remarks>
/// <para>
/// Complementa o <c>LoggingBehavior</c> da Application, que mede o caso de uso. Este mede a requisição HTTP
/// inteira: a diferença entre os dois revela o custo de serialização, autenticação e middleware — que é
/// justamente o que o behavior não vê.
/// </para>
/// <para>
/// <b>Loga a rota, não a URL.</b> `/api/v1/orders/{id}` em vez de `/api/v1/orders/3f2a...`: a URL concreta
/// carrega identificador de recurso, e agrupar por rota é o que permite perguntar "quanto custa este endpoint"
/// em vez de ter uma linha distinta por requisição.
/// </para>
/// <para>
/// Não loga corpo de requisição nem de resposta. Corpo carrega dado de cliente, e log não é lugar para PII —
/// a mesma razão pela qual o <c>LoggingBehavior</c> registra o nome do tipo e não o conteúdo da mensagem.
/// </para>
/// </remarks>
internal sealed class RequestLoggingMiddleware(
    RequestDelegate next,
    ILogger<RequestLoggingMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context, ICorrelationIdProvider correlationIdProvider)
    {
        ArgumentNullException.ThrowIfNull(context);

        long inicio = Stopwatch.GetTimestamp();

        try
        {
            await next(context);
        }
        finally
        {
            // `finally` e não depois do `await`: a requisição que lança também precisa registrar duração, e é
            // justamente a que mais interessa medir.
            if (logger.IsEnabled(LogLevel.Information))
            {
                double duracaoMs = Stopwatch.GetElapsedTime(inicio).TotalMilliseconds;
                string rota = RotaOuCaminho(context);
                int status = context.Response.StatusCode;
                string correlationId = correlationIdProvider.CorrelationId;

                ApiLogs.RequisicaoConcluida(
                    logger,
                    context.Request.Method,
                    rota,
                    status,
                    duracaoMs,
                    correlationId);
            }
        }
    }

    /// <summary>
    /// O template da rota quando o roteamento já resolveu; o caminho cru quando não (404, por exemplo).
    /// </summary>
    private static string RotaOuCaminho(HttpContext context) =>
        context.GetEndpoint() is Microsoft.AspNetCore.Routing.RouteEndpoint rota
            ? rota.RoutePattern.RawText ?? context.Request.Path.ToString()
            : context.Request.Path.ToString();
}
