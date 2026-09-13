using System.Diagnostics;
using CleanStart.Application.Common.Abstractions;
using Mediator;
using Microsoft.Extensions.Logging;

namespace CleanStart.Application.Common.Behaviors;

/// <summary>
/// Registra início, fim e duração de cada mensagem.
/// </summary>
/// <remarks>
/// <para>
/// Log estruturado, nunca string interpolada: <c>"{MessageName} em {ElapsedMs}ms"</c> com os valores como
/// parâmetros permite filtrar por nome e ordenar por duração no agregador. Interpolar produz uma string opaca,
/// que só serve para ser lida por humano e não se consulta.
/// </para>
/// <para>
/// <b>Não loga o conteúdo da mensagem.</b> Command carrega dado de cliente — e-mail, documento, endereço — e
/// log com PII vaza em lugar que ninguém trata como banco de dados: arquivo, agregador de terceiros, console de
/// container. O nome do tipo identifica a operação; quem precisa correlacionar usa o <c>CorrelationId</c>.
/// </para>
/// <para>
/// O <c>Stopwatch</c> mede o pipeline a partir deste ponto, então inclui os behaviors seguintes. É o que se
/// quer: a pergunta que o log responde é "quanto custou esta operação", não "quanto custou o handler isolado".
/// </para>
/// </remarks>
public sealed class LoggingBehavior<TMessage, TResponse>(
    ILogger<LoggingBehavior<TMessage, TResponse>> logger,
    ICorrelationIdProvider correlationIdProvider)
    : IPipelineBehavior<TMessage, TResponse>
    where TMessage : IMessage
{
    public async ValueTask<TResponse> Handle(
        TMessage message,
        MessageHandlerDelegate<TMessage, TResponse> next,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(next);

        string nome = typeof(TMessage).Name;
        string correlationId = correlationIdProvider.CorrelationId;

        BehaviorLogs.Processando(logger, nome, correlationId);

        long inicio = Stopwatch.GetTimestamp();

        try
        {
            TResponse resposta = await next(message, cancellationToken);

            // O guard de nível evita medir e formatar o tempo em toda mensagem quando Information está
            // desligado — num pipeline que intercepta tudo, isso não é ruído. A duração vai para uma local
            // porque o analyzer CA1873 não reconhece o `IsEnabled` como proteção e reclama de expressão
            // calculada na própria chamada.
            if (logger.IsEnabled(LogLevel.Information))
            {
                double duracaoMs = Stopwatch.GetElapsedTime(inicio).TotalMilliseconds;

                BehaviorLogs.Concluido(logger, nome, correlationId, duracaoMs);
            }

            return resposta;
        }
        catch (Exception excecao)
        {
            // Sem guard de nível aqui, de propósito: log de falha não é opcional, e envolvê-lo num
            // `IsEnabled` sugeriria que pode ser desligado. A duração vai para uma variável local porque o
            // analyzer reclama de expressão calculada na chamada — e a conta roda uma vez por falha, não por
            // mensagem.
            double duracaoMs = Stopwatch.GetElapsedTime(inicio).TotalMilliseconds;

            // Loga e relança: o behavior observa, não decide. Engolir aqui transformaria falha em silêncio,
            // e quem chamou receberia sucesso sem resultado.
            BehaviorLogs.Falhou(logger, excecao, nome, correlationId, duracaoMs);

            throw;
        }
    }
}
