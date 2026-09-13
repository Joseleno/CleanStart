using CleanStart.Domain.Common;

namespace CleanStart.Application.Common.Messaging;

/// <summary>
/// Trata um <see cref="ICommand{TResponse}"/>.
/// </summary>
/// <remarks>
/// O handler orquestra: carrega o que precisa pelos repositórios, delega a decisão ao domínio, persiste e
/// traduz o resultado. <b>Regra de negócio não mora aqui</b> — se o handler está decidindo, a regra pertence
/// à entidade.
/// <para>
/// Devolve <c>ValueTask</c> e não <c>Task</c> porque é a assinatura do Mediator, cujo source generator evita
/// a alocação quando o handler completa de forma síncrona.
/// </para>
/// </remarks>
public interface ICommandHandler<in TCommand, TResponse>
    : Mediator.ICommandHandler<TCommand, Result<TResponse>>
    where TCommand : ICommand<TResponse>;

/// <summary>
/// Trata um <see cref="ICommand"/> que não produz valor.
/// </summary>
public interface ICommandHandler<in TCommand>
    : Mediator.ICommandHandler<TCommand, Result>
    where TCommand : ICommand;
