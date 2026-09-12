namespace CleanStart.Domain.Errors;

/// <summary>
/// Natureza de um <see cref="Error"/>.
/// </summary>
/// <remarks>
/// Este enum é a fronteira entre o domínio e o protocolo: a Api o traduz em status HTTP
/// (400, 404, 409, 500) num único lugar. O domínio classifica a falha pela natureza dela e
/// permanece ignorante de HTTP — é o que permite expor o mesmo caso de uso por outro transporte
/// sem reescrever a regra.
/// </remarks>
public enum ErrorType
{
    /// <summary>Entrada inválida ou invariante de domínio violada. → 400.</summary>
    Validation = 1,

    /// <summary>O recurso pedido não existe. → 404.</summary>
    NotFound = 2,

    /// <summary>O estado atual não permite a operação. → 409.</summary>
    Conflict = 3,

    /// <summary>Falha inesperada. → 500.</summary>
    Failure = 4,
}
