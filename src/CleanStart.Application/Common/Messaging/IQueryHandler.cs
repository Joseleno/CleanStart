using CleanStart.Domain.Common;

namespace CleanStart.Application.Common.Messaging;

/// <summary>
/// Trata um <see cref="IQuery{TResponse}"/>.
/// </summary>
/// <remarks>
/// Consulta não altera estado. Handler de query que grava algo está violando a separação que justifica o
/// CQRS — e o <c>TransactionBehavior</c> não abrirá transação para ele (T2.2).
/// </remarks>
public interface IQueryHandler<in TQuery, TResponse>
    : Mediator.IQueryHandler<TQuery, Result<TResponse>>
    where TQuery : IQuery<TResponse>;
