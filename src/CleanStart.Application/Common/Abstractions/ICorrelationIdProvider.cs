namespace CleanStart.Application.Common.Abstractions;

/// <summary>
/// Identificador que amarra todos os registros de uma mesma operação.
/// </summary>
/// <remarks>
/// Existe para que uma falha relatada pelo usuário seja localizável: com o id em mãos, uma consulta no
/// agregador traz a requisição HTTP, os logs do pipeline, a query no banco e a chamada ao serviço externo —
/// em vez de uma busca por horário em meio a tudo o que aconteceu naquele segundo.
/// <para>
/// A implementação vive na Infrastructure e costuma ler o cabeçalho da requisição, gerando um novo quando ele
/// não vem. A Application só consome.
/// </para>
/// </remarks>
public interface ICorrelationIdProvider
{
    /// <summary>O identificador da operação em curso.</summary>
    string CorrelationId { get; }
}
