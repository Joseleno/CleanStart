using CleanStart.Api.Services;
using CleanStart.Application.Common.Abstractions;

namespace CleanStart.Api.Middlewares;

/// <summary>
/// Estabelece o correlation id da requisição e o devolve no cabeçalho da resposta.
/// </summary>
/// <remarks>
/// <para>
/// Aceita um id vindo de fora (<c>X-Correlation-Id</c>) e gera um quando não vem. Aceitar o de fora é o que faz a
/// correlação atravessar serviços: uma chamada que passa por gateway, api e worker aparece no log dos três com o
/// mesmo identificador.
/// </para>
/// <para>
/// <b>Devolve no cabeçalho da resposta</b> porque é isso que permite ao usuário relatar o problema com o id em
/// mãos — sem isso, ele diria "deu erro às 14h" e alguém procuraria no log por horário.
/// </para>
/// <para>
/// Precisa rodar <b>antes</b> do middleware de exception: se ele lançar, o handler de erro já terá um id para pôr
/// na resposta.
/// </para>
/// </remarks>
internal sealed class CorrelationIdMiddleware(RequestDelegate next)
{
    /// <summary>Cabeçalho de entrada e de saída.</summary>
    internal const string HeaderName = "X-Correlation-Id";

    public async Task InvokeAsync(HttpContext context, ICorrelationIdProvider correlationIdProvider)
    {
        ArgumentNullException.ThrowIfNull(context);

        string correlationId = LerOuGerar(context);

        // O provider da Api é mutável justamente para isto: o valor é definido uma vez por requisição e lido
        // pelo resto do escopo — behaviors, handlers, middleware de erro.
        if (correlationIdProvider is HttpCorrelationIdProvider mutavel)
        {
            mutavel.Definir(correlationId);
        }

        // Registrado antes de `next` e num callback: escrever no cabeçalho depois que a resposta começou lança,
        // e OnStarting garante que a escrita aconteça no último instante em que ainda é permitida.
        context.Response.OnStarting(() =>
        {
            context.Response.Headers[HeaderName] = correlationId;
            return Task.CompletedTask;
        });

        await next(context);
    }

    private static string LerOuGerar(HttpContext context)
    {
        if (context.Request.Headers.TryGetValue(HeaderName, out Microsoft.Extensions.Primitives.StringValues valor)
            && !string.IsNullOrWhiteSpace(valor))
        {
            string recebido = valor.ToString();

            // Limite de tamanho porque o valor vai para o log: um cabeçalho de 10 KB repetido em toda linha é
            // um vetor de inflar log a custo zero para quem chama.
            return recebido.Length <= 128 ? recebido : recebido[..128];
        }

        return Guid.CreateVersion7().ToString();
    }
}
