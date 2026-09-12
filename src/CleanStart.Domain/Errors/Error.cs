namespace CleanStart.Domain.Errors;

/// <summary>
/// Falha de negócio: um código estável, uma mensagem legível e a natureza da falha.
/// </summary>
/// <remarks>
/// <para>
/// É <c>record</c> para ganhar igualdade estrutural — dois erros com o mesmo código e tipo são o mesmo
/// erro, o que deixa o teste escrever <c>resultado.Error.Should().Be(DomainErrors.Order.SemItens)</c>
/// em vez de comparar string solta.
/// </para>
/// <para>
/// O <see cref="Code"/> é contrato: ele atravessa a Api e chega ao cliente, que pode ramificar em cima
/// dele. Mudá-lo é breaking change. A <see cref="Message"/> é para humano e pode ser reescrita à vontade.
/// </para>
/// </remarks>
/// <param name="Code">Identificador estável, no formato <c>Agregado.Problema</c> (ex.: <c>Order.SemItens</c>).</param>
/// <param name="Message">Descrição legível da falha.</param>
/// <param name="Type">Natureza da falha, que a Api traduz em status HTTP.</param>
public sealed record Error(string Code, string Message, ErrorType Type)
{
    /// <summary>
    /// Ausência de erro. Acompanha <see cref="Result.Success"/>.
    /// </summary>
    /// <remarks>
    /// Existe para que <c>Result.Error</c> nunca seja nulo, eliminando a checagem de nulo em todo
    /// consumidor. Quem quer saber se houve falha pergunta <c>IsFailure</c>, não se o erro é nulo.
    /// </remarks>
    public static readonly Error None = new(string.Empty, string.Empty, ErrorType.Failure);

    /// <summary>Cria um erro de validação (entrada inválida ou invariante violada).</summary>
    public static Error Validation(string code, string message) => new(code, message, ErrorType.Validation);

    /// <summary>Cria um erro de recurso inexistente.</summary>
    public static Error NotFound(string code, string message) => new(code, message, ErrorType.NotFound);

    /// <summary>Cria um erro de conflito com o estado atual.</summary>
    public static Error Conflict(string code, string message) => new(code, message, ErrorType.Conflict);

    /// <summary>Cria um erro de falha inesperada.</summary>
    public static Error Failure(string code, string message) => new(code, message, ErrorType.Failure);

    public override string ToString() => $"{Code}: {Message}";
}
