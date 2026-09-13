using CleanStart.Application.Common.Behaviors;
using CleanStart.Application.Orders.PlaceOrder;
using FluentValidation;
using Mediator;
using Microsoft.Extensions.DependencyInjection;

namespace CleanStart.Application;

/// <summary>
/// Registra a camada de aplicação no contêiner.
/// </summary>
/// <remarks>
/// Único ponto de entrada da camada, como o <c>AddInfrastructure</c> é da Infrastructure: a Api chama
/// <see cref="AddApplication"/> e não conhece handler, validator nem behavior concreto.
/// </remarks>
public static class DependencyInjection
{
    /// <summary>
    /// Acrescenta o Mediator, os validators e o pipeline de behaviors.
    /// </summary>
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // O source generator do Mediator descobre os handlers em tempo de compilação — não há varredura de
        // assembly em runtime aqui, que é a diferença de desempenho em relação ao MediatR.
        services.AddMediator(options => options.ServiceLifetime = ServiceLifetime.Scoped);

        return services
            .AddValidators()
            .AddBehaviors();
    }

    /// <summary>
    /// Registra os validators, um a um.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Sem varredura de assembly, de propósito. O <c>AddValidatorsFromAssembly</c> do FluentValidation exigiria
    /// o pacote <c>Microsoft.Extensions.DependencyInjection</c> completo aqui — e a Application só deve conhecer
    /// <c>.Abstractions</c>. Trazer a implementação do contêiner para dentro da camada de aplicação por
    /// conveniência de registro é o tipo de dependência que se acumula sem ninguém decidir.
    /// </para>
    /// <para>
    /// O custo é uma linha por validator novo. Em troca, lendo este método se sabe exatamente quais validators
    /// existem — a varredura esconde isso, e um validator mal colocado simplesmente nunca roda.
    /// </para>
    /// </remarks>
    private static IServiceCollection AddValidators(this IServiceCollection services)
    {
        services.AddScoped<IValidator<PlaceOrderCommand>, PlaceOrderValidator>();

        return services;
    }

    /// <summary>
    /// Registra os behaviors na ordem em que eles envolvem o handler.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>A ordem é a parte que importa, e ela é de fora para dentro:</b> o primeiro registrado é o mais externo,
    /// e o handler fica no centro. A sequência abaixo existe por razões concretas:
    /// </para>
    /// <list type="number">
    /// <item><b>Logging</b> primeiro, para que a duração medida inclua tudo o que vem depois — inclusive uma
    /// falha de validação. Registrado por último, ele não veria a requisição rejeitada.</item>
    /// <item><b>Validation</b> antes de transação e cache: mensagem malformada não deve abrir transação nem
    /// consultar cache, e o domínio nem precisa vê-la.</item>
    /// <item><b>Transaction</b> depois da validação e antes do handler, para que a transação exista só quando a
    /// mensagem já é válida.</item>
    /// <item><b>CacheInvalidation</b> logo dentro da transação: a invalidação precisa acontecer com o dado novo
    /// já gravado. Mais externo que o commit, uma leitura concorrente repovoaria o cache com o valor antigo entre
    /// a remoção e a gravação, e a janela ficaria aberta até o TTL expirar.</item>
    /// </list>
    /// <para>
    /// Errar a ordem não quebra o build nem faz teste falhar isoladamente — produz comportamento sutilmente
    /// errado: cache servindo resposta a mensagem inválida, transação aberta para ser descartada, log sem a
    /// requisição que falhou.
    /// </para>
    /// </remarks>
    private static IServiceCollection AddBehaviors(this IServiceCollection services)
    {
        services.AddScoped(typeof(IPipelineBehavior<,>), typeof(LoggingBehavior<,>));
        services.AddScoped(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));
        services.AddScoped(typeof(IPipelineBehavior<,>), typeof(TransactionBehavior<,>));
        services.AddScoped(typeof(IPipelineBehavior<,>), typeof(CacheInvalidationBehavior<,>));

        return services;
    }
}
