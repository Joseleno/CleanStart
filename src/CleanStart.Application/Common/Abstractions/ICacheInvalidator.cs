namespace CleanStart.Application.Common.Abstractions;

/// <summary>
/// Command que torna obsoleto algum resultado guardado em cache.
/// </summary>
/// <remarks>
/// <para>
/// Contraparte do <see cref="ICacheable"/>: aquele diz "este resultado pode ser guardado", este diz "esta
/// operação invalida aqueles resultados". O <c>CacheInvalidationBehavior</c> remove as chaves depois de a
/// operação ter sucesso.
/// </para>
/// <para>
/// <b>Por que um marcador e não uma chamada no handler.</b> Invalidar dentro do caso de uso põe preocupação de
/// infraestrutura no meio da regra de negócio, e faz cada handler novo ter de lembrar de fazê-lo — o tipo de
/// obrigação que alguém esquece, e cujo sintoma é dado velho servido sem erro nenhum. Declarado na mensagem, a
/// intenção fica legível onde a operação é definida e o behavior garante a execução.
/// </para>
/// <para>
/// <b>Só depois do sucesso.</b> Invalidar antes, ou apesar da falha, joga fora cache válido: a operação que
/// falhou não mudou nada, então o que estava guardado continua correto.
/// </para>
/// </remarks>
public interface ICacheInvalidator
{
    /// <summary>
    /// As chaves que esta operação torna obsoletas.
    /// </summary>
    /// <remarks>
    /// A chave precisa ser <b>exatamente</b> a mesma que a query produziu. É por isso que as queries expõem um
    /// método para montá-la (por exemplo <c>GetOrderByIdQuery.ChaveDe</c>) em vez de interpolar a string aqui:
    /// duas interpolações em lugares diferentes divergem por um caractere, e ninguém percebe porque nada falha.
    /// </remarks>
    IReadOnlyList<string> ChavesInvalidadas { get; }
}
