using Microsoft.Extensions.Logging;

namespace CleanStart.Infrastructure.Persistence.Outbox;

/// <summary>
/// Mensagens de log do despachante, geradas em tempo de compilação.
/// </summary>
/// <remarks>
/// <para>
/// Mesmo motivo dos logs do pipeline: <c>[LoggerMessage]</c> evita o boxing que o <c>logger.LogX(...)</c> direto
/// faz ainda que o nível esteja desligado — e aqui o laço roda a cada poucos segundos, indefinidamente.
/// </para>
/// <para>
/// <b>Nenhuma delas registra o conteúdo da mensagem.</b> O <c>Content</c> é o evento serializado e carrega dado
/// de negócio — identificador de cliente, valor, às vezes documento. Log é indexado, retido e lido por muita
/// gente; o corpo do evento fica na tabela, onde o acesso é controlado.
/// </para>
/// </remarks>
internal static partial class OutboxLogs
{
    [LoggerMessage(
        EventId = 2000,
        Level = LogLevel.Debug,
        Message = "Outbox: {Quantidade} mensagem(ns) reservada(s) para despacho")]
    public static partial void LoteReservado(ILogger logger, int quantidade);

    [LoggerMessage(
        EventId = 2001,
        Level = LogLevel.Information,
        Message = "Outbox: {Quantidade} mensagem(ns) despachada(s)")]
    public static partial void LoteDespachado(ILogger logger, int quantidade);

    [LoggerMessage(
        EventId = 2002,
        Level = LogLevel.Warning,
        Message = "Outbox: falha ao despachar {MessageId} ({Tipo}), tentativa {Tentativa} de {Maximo}")]
    public static partial void FalhaAoDespachar(
        ILogger logger,
        Guid messageId,
        string tipo,
        int tentativa,
        int maximo,
        Exception excecao);

    [LoggerMessage(
        EventId = 2003,
        Level = LogLevel.Error,
        Message = "Outbox: {MessageId} ({Tipo}) esgotou as {Maximo} tentativas e não será mais despachada")]
    public static partial void TentativasEsgotadas(ILogger logger, Guid messageId, string tipo, int maximo);

    [LoggerMessage(
        EventId = 2004,
        Level = LogLevel.Error,
        Message = "Outbox: {MessageId} tem o tipo desconhecido '{Tipo}' e será ignorada")]
    public static partial void TipoDesconhecido(ILogger logger, Guid messageId, string tipo);

    [LoggerMessage(
        EventId = 2005,
        Level = LogLevel.Information,
        Message = "Outbox: {Quantidade} mensagem(ns) processada(s) removida(s) pela retenção")]
    public static partial void RetencaoAplicada(ILogger logger, int quantidade);

    [LoggerMessage(
        EventId = 2006,
        Level = LogLevel.Error,
        Message = "Outbox: o ciclo falhou; o despachante continua e tentará de novo no próximo intervalo")]
    public static partial void CicloFalhou(ILogger logger, Exception excecao);
}
