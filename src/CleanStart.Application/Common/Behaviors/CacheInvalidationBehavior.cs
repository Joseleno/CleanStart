using CleanStart.Application.Common.Abstractions;
using CleanStart.Domain.Common;
using Mediator;
using Microsoft.Extensions.Logging;

namespace CleanStart.Application.Common.Behaviors;

/// <summary>
/// Remove do cache as chaves que um command tornou obsoletas.
/// </summary>
/// <remarks>
/// <para>
/// Só age sobre quem implementa <see cref="ICacheInvalidator"/>, e **só depois de a operação ter sucesso**:
/// invalidar apesar da falha jogaria fora cache que continua válido, já que a operação que falhou não mudou nada.
/// </para>
/// <para>
/// <b>Posição no pipeline:</b> depois do <c>TransactionBehavior</c>, porque a invalidação precisa acontecer com o
/// dado novo já gravado. Antes do commit, uma leitura concorrente repovoaria o cache com o valor antigo entre a
/// remoção e a gravação — e a janela ficaria aberta até o TTL expirar.
/// </para>
/// <para>
/// <b>Falha ao invalidar não derruba a operação.</b> O dado foi gravado; perder a remoção significa servir valor
/// velho até o TTL, o que é ruim mas recuperável. Propagar a exception faria o cliente receber erro numa operação
/// que de fato aconteceu — e provavelmente tentar de novo, duplicando o efeito.
/// </para>
/// </remarks>
public sealed class CacheInvalidationBehavior<TMessage, TResponse>(
    ICacheService cache,
    ILogger<CacheInvalidationBehavior<TMessage, TResponse>> logger)
    : IPipelineBehavior<TMessage, TResponse>
    where TMessage : IMessage
{
    public async ValueTask<TResponse> Handle(
        TMessage message,
        MessageHandlerDelegate<TMessage, TResponse> next,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(next);

        TResponse resposta = await next(message, cancellationToken);

        if (message is not ICacheInvalidator invalidador)
        {
            return resposta;
        }

        // Resultado de falha não invalida: nada mudou, e o que está guardado continua correto.
        if (resposta is Result { IsFailure: true })
        {
            return resposta;
        }

        foreach (string chave in invalidador.ChavesInvalidadas)
        {
            try
            {
                await cache.RemoveAsync(chave, cancellationToken);
            }
            catch (Exception excecao)
            {
                // Registrado e engolido, de propósito — ver o comentário da classe. É o único lugar do pipeline
                // onde engolir exception é a decisão certa, e por isso está explicado aqui.
                BehaviorLogs.FalhaAoInvalidarCache(logger, excecao, chave);
            }
        }

        return resposta;
    }
}
