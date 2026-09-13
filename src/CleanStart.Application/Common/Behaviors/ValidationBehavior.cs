using FluentValidation;
using FluentValidation.Results;
using Mediator;

namespace CleanStart.Application.Common.Behaviors;

/// <summary>
/// Valida a mensagem antes de o handler rodar, agregando todas as falhas.
/// </summary>
/// <typeparam name="TMessage">Command ou query.</typeparam>
/// <typeparam name="TResponse">Resposta do handler.</typeparam>
/// <remarks>
/// <para>
/// Fica no pipeline e não dentro do handler para que validação seja garantida por estrutura, não por disciplina:
/// não existe caminho em que um handler rode sem passar por aqui, e nenhum handler precisa lembrar de chamar o
/// validator.
/// </para>
/// <para>
/// <b>Agrega</b> as falhas em vez de parar na primeira. É a razão de o <c>Result</c> do domínio carregar um
/// <c>Error</c> único (decisão 18 do HANDOFF): quem preenche um formulário precisa ver os cinco campos errados
/// de uma vez, e esse trabalho é daqui — não do domínio, que valida uma invariante por vez.
/// </para>
/// <para>
/// <b>Lança em vez de devolver <c>Result</c>.</b> Parece contradizer "erro de negócio é resultado", mas não é
/// erro de negócio: é mensagem malformada, que o domínio nem deveria ver. A Api traduz a exception em 400 num
/// único lugar. O <c>Result</c> fica reservado à regra que o domínio avalia com a mensagem já válida.
/// </para>
/// </remarks>
public sealed class ValidationBehavior<TMessage, TResponse>(
    IEnumerable<IValidator<TMessage>> validators)
    : IPipelineBehavior<TMessage, TResponse>
    where TMessage : IMessage
{
    public async ValueTask<TResponse> Handle(
        TMessage message,
        MessageHandlerDelegate<TMessage, TResponse> next,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(next);

        // Materializa porque a coleção injetada pode ser uma consulta preguiçosa do contêiner, e ela é
        // percorrida duas vezes abaixo.
        List<IValidator<TMessage>> aplicaveis = [.. validators];

        if (aplicaveis.Count == 0)
        {
            // Mensagem sem validator passa direto: validação é opt-in, por existir um validator para ela.
            return await next(message, cancellationToken);
        }

        // Um ValidationContext POR validator, nunca um compartilhado.
        //
        // O contexto acumula as falhas dentro dele, então reaproveitá-lo entre validators que rodam em
        // paralelo faz cada um enxergar os erros do outro — o sintoma é a mesma falha repetida N vezes na
        // resposta. Foi exatamente o que um teste pegou aqui: duas regras violadas produziram quatro
        // mensagens.
        ValidationResult[] resultados = await Task.WhenAll(
            aplicaveis.Select(validator =>
                validator.ValidateAsync(new ValidationContext<TMessage>(message), cancellationToken)));

        List<ValidationFailure> falhas = [.. resultados
            .Where(resultado => !resultado.IsValid)
            .SelectMany(resultado => resultado.Errors)];

        if (falhas.Count > 0)
        {
            throw new ValidationException(falhas);
        }

        return await next(message, cancellationToken);
    }
}
