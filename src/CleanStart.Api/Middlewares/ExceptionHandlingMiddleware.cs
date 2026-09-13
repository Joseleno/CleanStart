using CleanStart.Application.Common.Abstractions;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;

namespace CleanStart.Api.Middlewares;

/// <summary>
/// Traduz exception em <see cref="ProblemDetails"/> (RFC 9457).
/// </summary>
/// <remarks>
/// <para>
/// Existe para que nenhuma exception escape como página de erro do servidor. Duas responsabilidades: dar ao
/// cliente uma resposta com forma previsível, e **não** contar a ele como o sistema é feito por dentro.
/// </para>
/// <para>
/// <b>Stack trace nunca vai para a resposta em produção.</b> Ele revela caminho de arquivo, nome de biblioteca e
/// estrutura interna — material de reconhecimento para quem procura vulnerabilidade. Em desenvolvimento ele
/// aparece, porque ali o custo é zero e a conveniência é real.
/// </para>
/// <para>
/// O <c>CorrelationId</c> entra em toda resposta de erro: é o que permite ao usuário relatar "deu erro" e alguém
/// encontrar exatamente aquela requisição no log, sem precisar caçar por horário.
/// </para>
/// </remarks>
internal sealed class ExceptionHandlingMiddleware(
    RequestDelegate next,
    ILogger<ExceptionHandlingMiddleware> logger,
    IHostEnvironment environment)
{
    public async Task InvokeAsync(HttpContext context, ICorrelationIdProvider correlationIdProvider)
    {
        ArgumentNullException.ThrowIfNull(context);

        try
        {
            await next(context);
        }
        catch (ValidationException excecao)
        {
            // Validação do pipeline: mensagem malformada, não falha do servidor. É o único caso em que o
            // ValidationBehavior lança de propósito, e aqui ele vira 400 com os campos que falharam.
            await EscreverProblemDetails(
                context,
                correlationIdProvider.CorrelationId,
                StatusCodes.Status400BadRequest,
                "Requisição inválida",
                "Um ou mais campos não passaram na validação.",
                erros: excecao.Errors
                    .GroupBy(falha => falha.PropertyName)
                    .ToDictionary(
                        grupo => grupo.Key,
                        grupo => grupo.Select(falha => falha.ErrorMessage).ToArray()));
        }
        catch (BadHttpRequestException excecao)
        {
            // Corpo ausente, JSON malformado ou tipo incompatível: o binding do ASP.NET lança antes de o
            // endpoint ser chamado, então a checagem de null dentro do handler nunca é alcançada.
            //
            // É **erro do cliente**, não do servidor: sem este catch, um `{` a mais no payload viraria 500 —
            // dizendo a quem chamou que o problema é nosso, e poluindo o log de erro com requisição malformada.
            await EscreverProblemDetails(
                context,
                correlationIdProvider.CorrelationId,
                StatusCodes.Status400BadRequest,
                "Requisição inválida",
                environment.IsDevelopment()
                    ? excecao.Message
                    : "O corpo da requisição não pôde ser lido. Verifique se é um JSON válido.");
        }
        catch (Exception excecao)
        {
            // Log com a exception inteira (fica no servidor); resposta sem detalhe (vai para o cliente).
            ApiLogs.ErroNaoTratado(
                logger,
                excecao,
                context.Request.Method,
                context.Request.Path,
                correlationIdProvider.CorrelationId);

            string detalhe = environment.IsDevelopment()
                ? excecao.ToString()
                : "Ocorreu um erro inesperado. Informe o identificador da requisição ao suporte.";

            await EscreverProblemDetails(
                context,
                correlationIdProvider.CorrelationId,
                StatusCodes.Status500InternalServerError,
                "Erro interno",
                detalhe);
        }
    }

    private static async Task EscreverProblemDetails(
        HttpContext context,
        string correlationId,
        int status,
        string titulo,
        string detalhe,
        IDictionary<string, string[]>? erros = null)
    {
        // Se a resposta já começou a ser escrita, trocar o status quebraria o protocolo — o máximo que se pode
        // fazer é abortar e deixar o log registrar.
        if (context.Response.HasStarted)
        {
            return;
        }

        ProblemDetails problema = new()
        {
            Status = status,
            Title = titulo,
            Detail = detalhe,
            Instance = $"{context.Request.Method} {context.Request.Path}",
        };

        problema.Extensions["correlationId"] = correlationId;

        if (erros is not null)
        {
            problema.Extensions["errors"] = erros;
        }

        context.Response.StatusCode = status;

        // application/problem+json é o content type que a RFC 9457 exige; sem ele o cliente não sabe que o corpo
        // segue o formato padronizado.
        context.Response.ContentType = "application/problem+json";

        await context.Response.WriteAsJsonAsync(problema, context.RequestAborted);
    }
}
