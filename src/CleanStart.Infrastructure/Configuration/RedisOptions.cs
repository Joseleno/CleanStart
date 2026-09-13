using System.ComponentModel.DataAnnotations;

namespace CleanStart.Infrastructure.Configuration;

/// <summary>
/// Configuração do Redis, usado pelo cache distribuído.
/// </summary>
/// <remarks>
/// O <c>HybridCache</c> funciona sem Redis — cai para cache em memória apenas. Por isso a connection string é
/// opcional aqui: a aplicação sobe sem Redis em desenvolvimento, e quem liga a configuração assume o segundo
/// nível. O que não é opcional é o comportamento ser <b>explícito</b>: ausência de Redis é decisão registrada na
/// configuração, não descoberta em produção.
/// </remarks>
public sealed class RedisOptions
{
    /// <summary>Seção correspondente no arquivo de configuração.</summary>
    public const string SectionName = "Redis";

    /// <summary>Connection string do Redis, ou vazia para usar só cache em memória.</summary>
    public string ConnectionString { get; init; } = string.Empty;

    /// <summary>Se o segundo nível de cache está habilitado.</summary>
    public bool Enabled => !string.IsNullOrWhiteSpace(ConnectionString);

    /// <summary>Validade padrão de uma entrada que não informa a sua.</summary>
    [Range(1, 86_400, ErrorMessage = "A validade padrão deve estar entre 1 segundo e 24 horas.")]
    public int DefaultExpirationSeconds { get; init; } = 300;

    /// <summary>Prefixo das chaves, para separar ambientes que compartilham a mesma instância.</summary>
    /// <remarks>
    /// Sem prefixo, dois ambientes apontando para o mesmo Redis leem a chave um do outro — e o sintoma é dado de
    /// homologação aparecendo em produção, sem erro nenhum.
    /// </remarks>
    public string InstanceName { get; init; } = "cleanstart:";
}
