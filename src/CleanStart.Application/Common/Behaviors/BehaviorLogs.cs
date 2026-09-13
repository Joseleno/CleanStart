using Microsoft.Extensions.Logging;

namespace CleanStart.Application.Common.Behaviors;

/// <summary>
/// Mensagens de log dos behaviors, geradas em tempo de compilação.
/// </summary>
/// <remarks>
/// <para>
/// <c>[LoggerMessage]</c> gera um delegate por mensagem, com os parâmetros fortemente tipados. Chamar
/// <c>logger.LogInformation("... {X}", x)</c> direto aloca um <c>object[]</c> e faz boxing de cada valor
/// **ainda que o nível esteja desligado** — num pipeline que intercepta toda mensagem, esse custo aparece.
/// </para>
/// <para>
/// É o que os analyzers CA1848 e CA1873 exigem, e a exigência é justa: com os delegates, a formatação só
/// acontece se o nível estiver habilitado.
/// </para>
/// <para>
/// As mensagens ficam agrupadas aqui, e não espalhadas em cada behavior, para que a lista completa do que o
/// pipeline registra caiba numa tela — inclusive para conferir que nenhuma delas loga conteúdo de mensagem.
/// </para>
/// </remarks>
internal static partial class BehaviorLogs
{
    [LoggerMessage(
        EventId = 1000,
        Level = LogLevel.Information,
        Message = "Processando {MessageName} [{CorrelationId}]")]
    public static partial void Processando(ILogger logger, string messageName, string correlationId);

    [LoggerMessage(
        EventId = 1001,
        Level = LogLevel.Information,
        Message = "Concluído {MessageName} [{CorrelationId}] em {ElapsedMs}ms")]
    public static partial void Concluido(
        ILogger logger,
        string messageName,
        string correlationId,
        double elapsedMs);

    [LoggerMessage(
        EventId = 1002,
        Level = LogLevel.Error,
        Message = "Falhou {MessageName} [{CorrelationId}] após {ElapsedMs}ms")]
    public static partial void Falhou(
        ILogger logger,
        Exception exception,
        string messageName,
        string correlationId,
        double elapsedMs);

    [LoggerMessage(
        EventId = 1003,
        Level = LogLevel.Debug,
        Message = "Consultando cache de {MessageName} com chave {CacheKey}")]
    public static partial void ConsultandoCache(ILogger logger, string messageName, string cacheKey);
}
