using CleanStart.Application.Common.Abstractions;
using CleanStart.Domain.Common;
using CleanStart.Domain.Errors;
using Microsoft.AspNetCore.Mvc;

namespace CleanStart.Api.Extensions;

/// <summary>
/// Traduz <see cref="Result"/> e <see cref="Result{TValue}"/> em resposta HTTP.
/// </summary>
/// <remarks>
/// <para>
/// <b>Ponto único de tradução.</b> Sem isto, cada endpoint decide o seu mapeamento — e em duas semanas o mesmo
/// tipo de falha responde 400 num lugar e 422 noutro, sem que ninguém tenha decidido. O cliente da API é quem
/// paga: ele não consegue tratar erro por categoria.
/// </para>
/// <para>
/// O mapeamento vem do <see cref="ErrorType"/>, que o domínio atribui: <c>Validation</c>→400, <c>NotFound</c>→404,
/// <c>Conflict</c>→409, <c>Failure</c>→500. O domínio classifica pela **natureza** da falha e permanece ignorante
/// de HTTP — é isso que permite expor o mesmo caso de uso por outro transporte sem reescrever regra.
/// </para>
/// </remarks>
internal static class ResultExtensions
{
    /// <summary>
    /// Converte um resultado com valor em <c>201 Created</c> ou na resposta de erro correspondente.
    /// </summary>
    /// <param name="resultado">O resultado do caso de uso.</param>
    /// <param name="localizacao">Onde o recurso criado pode ser lido.</param>
    /// <param name="correlationId">Identificador da requisição, incluído em toda resposta de erro.</param>
    public static IResult ParaCreated<TValue>(
        this Result<TValue> resultado,
        Func<TValue, string> localizacao,
        string correlationId)
    {
        ArgumentNullException.ThrowIfNull(resultado);
        ArgumentNullException.ThrowIfNull(localizacao);

        return resultado.Match(
            onSuccess: valor => Results.Created(localizacao(valor), valor),
            onFailure: erro => ParaProblem(erro, correlationId));
    }

    /// <summary>
    /// Converte um resultado com valor em <c>200 OK</c> ou na resposta de erro correspondente.
    /// </summary>
    public static IResult ParaOk<TValue>(this Result<TValue> resultado, string correlationId)
    {
        ArgumentNullException.ThrowIfNull(resultado);

        return resultado.Match(
            onSuccess: valor => Results.Ok(valor),
            onFailure: erro => ParaProblem(erro, correlationId));
    }

    /// <summary>
    /// Converte um resultado sem valor em <c>204 No Content</c> ou na resposta de erro correspondente.
    /// </summary>
    public static IResult ParaNoContent(this Result resultado, string correlationId)
    {
        ArgumentNullException.ThrowIfNull(resultado);

        return resultado.IsSuccess
            ? Results.NoContent()
            : ParaProblem(resultado.Error, correlationId);
    }

    /// <summary>
    /// Monta o <see cref="ProblemDetails"/> (RFC 9457) de um erro de domínio.
    /// </summary>
    /// <remarks>
    /// O <c>Code</c> do erro vai em <c>extensions.code</c>, não só na mensagem: ele é contrato estável e um cliente
    /// pode ramificar em cima dele, enquanto a mensagem é para humano e muda sem aviso.
    /// </remarks>
    private static IResult ParaProblem(Error erro, string correlationId)
    {
        int status = ParaStatus(erro.Type);

        return Results.Problem(
            statusCode: status,
            title: ParaTitulo(erro.Type),
            detail: erro.Message,
            extensions: new Dictionary<string, object?>
            {
                ["code"] = erro.Code,
                ["correlationId"] = correlationId,
            });
    }

    private static int ParaStatus(ErrorType tipo) => tipo switch
    {
        ErrorType.Validation => StatusCodes.Status400BadRequest,
        ErrorType.NotFound => StatusCodes.Status404NotFound,
        ErrorType.Conflict => StatusCodes.Status409Conflict,
        ErrorType.Failure => StatusCodes.Status500InternalServerError,

        // Um valor novo no enum cai aqui. Deliberadamente 500 e não um default silencioso que pareça certo:
        // responder 400 a um tipo desconhecido esconderia o fato de que alguém estendeu o enum sem mapear.
        _ => StatusCodes.Status500InternalServerError,
    };

    private static string ParaTitulo(ErrorType tipo) => tipo switch
    {
        ErrorType.Validation => "Requisição inválida",
        ErrorType.NotFound => "Recurso não encontrado",
        ErrorType.Conflict => "Conflito com o estado atual",
        _ => "Erro interno",
    };
}
