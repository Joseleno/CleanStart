namespace CleanStart.Api.Middlewares;

/// <summary>
/// Mensagens de log dos middlewares, geradas em tempo de compilação.
/// </summary>
/// <remarks>
/// Mesmo motivo do <c>BehaviorLogs</c> na Application: chamar <c>logger.LogInformation</c> com parâmetros aloca
/// um <c>object[]</c> e faz boxing ainda que o nível esteja desligado — e um middleware roda em toda requisição.
/// Os analyzers CA1848 e CA1873 exigem isso, com razão.
/// </remarks>
internal static partial class ApiLogs
{
    [LoggerMessage(
        EventId = 2000,
        Level = LogLevel.Information,
        Message = "{Method} {Route} respondeu {StatusCode} em {ElapsedMs}ms [{CorrelationId}]")]
    public static partial void RequisicaoConcluida(
        ILogger logger,
        string method,
        string route,
        int statusCode,
        double elapsedMs,
        string correlationId);

    [LoggerMessage(
        EventId = 2001,
        Level = LogLevel.Error,
        Message = "Erro não tratado em {Method} {Path} [{CorrelationId}]")]
    public static partial void ErroNaoTratado(
        ILogger logger,
        Exception exception,
        string method,
        string path,
        string correlationId);
}
