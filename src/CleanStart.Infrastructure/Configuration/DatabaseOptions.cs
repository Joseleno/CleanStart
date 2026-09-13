using System.ComponentModel.DataAnnotations;

namespace CleanStart.Infrastructure.Configuration;

/// <summary>
/// Configuração do banco de dados.
/// </summary>
/// <remarks>
/// <para>
/// Validada no startup (<c>ValidateOnStart</c>), não na primeira vez que alguém a usa. A diferença importa:
/// connection string ausente derruba a aplicação ao subir, com mensagem dizendo qual chave falta — em vez de
/// produzir um erro de conexão na primeira requisição de um usuário, em produção, às três da manhã.
/// </para>
/// <para>
/// A connection string **não** tem valor padrão de propósito. Um default plausível
/// (<c>Host=localhost;Database=cleanstart</c>) faria a aplicação subir apontando para lugar errado em vez de
/// reclamar, e o sintoma seria "o dado não aparece" em vez de "falta configurar".
/// </para>
/// </remarks>
public sealed class DatabaseOptions
{
    /// <summary>Seção correspondente no arquivo de configuração.</summary>
    public const string SectionName = "Database";

    /// <summary>Connection string do PostgreSQL.</summary>
    /// <remarks>
    /// Nunca versionada com senha real: em desenvolvimento vai em User Secrets, e em produção em variável de
    /// ambiente ou cofre. O `appsettings.json` do repositório carrega só a forma.
    /// </remarks>
    [Required(AllowEmptyStrings = false, ErrorMessage = "A connection string do banco é obrigatória.")]
    public string ConnectionString { get; init; } = string.Empty;

    /// <summary>Quantos segundos uma consulta pode levar antes de ser abortada.</summary>
    /// <remarks>
    /// Existe para que uma consulta degenerada não segure a conexão indefinidamente. O limite baixo é
    /// deliberado: é melhor falhar rápido e visivelmente que acumular conexões presas até o pool esgotar.
    /// </remarks>
    [Range(1, 300, ErrorMessage = "O timeout deve estar entre 1 e 300 segundos.")]
    public int CommandTimeoutSeconds { get; init; } = 30;

    /// <summary>Quantas vezes repetir uma falha transiente de conexão.</summary>
    [Range(0, 10)]
    public int MaxRetryCount { get; init; } = 3;

    /// <summary>
    /// Se o EF deve registrar os parâmetros das consultas no log.
    /// </summary>
    /// <remarks>
    /// <b>Falso por padrão, e é importante que seja.</b> Os parâmetros carregam dado de cliente — documento,
    /// e-mail, endereço — e ligá-los manda PII para o log. Serve em desenvolvimento, nunca em produção.
    /// </remarks>
    public bool EnableSensitiveDataLogging { get; init; }
}
