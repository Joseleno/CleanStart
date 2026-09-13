using CleanStart.Domain.Common;

namespace CleanStart.Application.Common.Messaging;

/// <summary>
/// Intenção de alterar estado, que produz um <see cref="Result{TResponse}"/>.
/// </summary>
/// <typeparam name="TResponse">O que o caso de uso devolve em caso de sucesso.</typeparam>
/// <remarks>
/// <para>
/// <b>Por que existe, se o Mediator já tem um <c>ICommand&lt;T&gt;</c>:</b> o estável do Mediator é o 3.0.2,
/// um pacote <c>net8.0</c>, enquanto o 3.1.0 alinhado ao .NET 10 está em RC. Com os handlers escritos contra
/// esta abstração, subir para o 3.1 quando estabilizar é mudança <b>deste arquivo</b>, não dos handlers todos
/// (decisão 2 do HANDOFF e ADR 0009).
/// </para>
/// <para>
/// A herança de <c>Mediator.ICommand&lt;&gt;</c> é o que mantém o source generator funcionando: ele varre os
/// tipos que implementam as interfaces dele. O acoplamento fica aqui, num lugar só.
/// </para>
/// <para>
/// Comando sempre devolve <c>Result</c>: alterar estado é onde a regra de negócio recusa, e recusa previsível
/// se comunica por resultado, não por exception.
/// </para>
/// </remarks>
public interface ICommand<TResponse> : Mediator.ICommand<Result<TResponse>>;

/// <summary>
/// Intenção de alterar estado que não produz valor — só sucesso ou falha.
/// </summary>
/// <remarks>
/// Existe para o caso de uso que não tem o que devolver (cancelar, excluir). Sem esta variante, o handler
/// seria forçado a inventar um tipo de retorno vazio só para satisfazer a assinatura.
/// </remarks>
public interface ICommand : Mediator.ICommand<Result>;
