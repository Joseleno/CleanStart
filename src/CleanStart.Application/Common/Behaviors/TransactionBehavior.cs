using CleanStart.Application.Common.Abstractions;
using CleanStart.Domain.Common;
using Mediator;

namespace CleanStart.Application.Common.Behaviors;

/// <summary>
/// Envolve comandos numa transação. Consultas passam direto.
/// </summary>
/// <remarks>
/// <para>
/// O comando é a unidade atômica do sistema: um caso de uso que altera dois agregados grava os dois ou nenhum.
/// Sem isto, cada handler abriria a sua transação — e o que acontece na prática é que alguém esquece, e a
/// inconsistência aparece semanas depois sem erro nenhum no log.
/// </para>
/// <para>
/// <b>Consulta não abre transação</b>, e isso é critério de aceite da T2.2. Não é economia de linha: transação
/// em leitura segura conexão no pool e mantém snapshot aberto no PostgreSQL sem necessidade nenhuma. A
/// distinção usa <c>IBaseCommand</c>, que é o marcador que o Mediator põe em todo comando.
/// </para>
/// </remarks>
public sealed class TransactionBehavior<TMessage, TResponse>(IUnitOfWork unitOfWork)
    : IPipelineBehavior<TMessage, TResponse>
    where TMessage : IMessage
{
    public async ValueTask<TResponse> Handle(
        TMessage message,
        MessageHandlerDelegate<TMessage, TResponse> next,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(next);

        if (message is not IBaseCommand)
        {
            return await next(message, cancellationToken);
        }

        TResponse resposta = await next(message, cancellationToken);

        // Resultado de falha NÃO grava. Sem esta checagem, um handler que recebe `Result.Failure` do domínio
        // — pedido sem item, moeda incompatível — ainda assim persistiria o que tocou antes da recusa: a regra
        // de negócio diria "não" e o banco gravaria "sim".
        if (resposta is Result { IsFailure: true })
        {
            return resposta;
        }

        // O commit fica fora de try/catch: exception propaga sem gravar, que é o comportamento desejado.
        // Capturar aqui para "tratar" faria o commit rodar sobre estado incompleto.
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return resposta;
    }
}
