using CleanStart.Domain.Common;

namespace CleanStart.Application.Common.Messaging;

/// <summary>
/// Pergunta que não altera estado, respondida com um <see cref="Result{TResponse}"/>.
/// </summary>
/// <typeparam name="TResponse">O que a consulta devolve em caso de sucesso.</typeparam>
/// <remarks>
/// Separar query de command não é formalidade: é o que permite ao pipeline tratar as duas de modo diferente
/// sem inspecionar o tipo concreto — transação só para comando, cache só para consulta. Ver
/// <see cref="ICommand{TResponse}"/> para o motivo de envolver o Mediator numa abstração própria.
/// </remarks>
public interface IQuery<TResponse> : Mediator.IQuery<Result<TResponse>>;
