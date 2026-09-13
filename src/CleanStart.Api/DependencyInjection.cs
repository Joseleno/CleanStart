using Carter;
using CleanStart.Api.Services;
using CleanStart.Application.Common.Abstractions;
using CleanStart.Infrastructure.Configuration;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace CleanStart.Api;

/// <summary>
/// Registra o que é específico da camada de apresentação.
/// </summary>
public static class DependencyInjection
{
    /// <summary>
    /// Acrescenta Carter, OpenAPI, health checks, telemetria e os serviços que dependem de HTTP.
    /// </summary>
    public static IServiceCollection AddApiServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        // Carter descobre os módulos por varredura do assembly; os endpoints são mapeados em MapCarter().
        services.AddCarter();

        // Geração do documento OpenAPI embutida no .NET 10 — não precisa de Swashbuckle.
        services.AddOpenApi();

        services.AddHttpContextAccessor();

        return services
            .AddServicosDeRequisicao()
            .AddHealthChecksDaApi(configuration)
            .AddTelemetria();
    }

    /// <summary>
    /// Sobrescreve os padrões da Infrastructure pelas implementações que leem o <c>HttpContext</c>.
    /// </summary>
    /// <remarks>
    /// Chamado **depois** de <c>AddInfrastructure</c> de propósito: no contêiner da Microsoft, o último registro
    /// do mesmo serviço é o que vence. A Infrastructure registra padrões que funcionam fora de HTTP (job, seed), e
    /// a Api os substitui onde há requisição — sem que nenhuma das duas camadas conheça a outra.
    /// </remarks>
    private static IServiceCollection AddServicosDeRequisicao(this IServiceCollection services)
    {
        services.AddScoped<HttpCorrelationIdProvider>();

        // A mesma instância serve as duas pontas: o middleware precisa do tipo concreto para definir o valor, e
        // o resto do código consome a interface. Sem isto, seriam dois objetos e o valor definido se perderia.
        services.AddScoped<ICorrelationIdProvider>(provider =>
            provider.GetRequiredService<HttpCorrelationIdProvider>());

        services.AddScoped<ICurrentUser, HttpCurrentUser>();

        return services;
    }

    /// <summary>
    /// Health checks separados em <c>live</c> e <c>ready</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A distinção é o que importa aqui, e ela existe por causa do orquestrador:
    /// </para>
    /// <list type="bullet">
    /// <item><b>live</b> responde "o processo está de pé". Sem dependência externa — se o banco cair, o
    /// Kubernetes <b>não</b> deve reiniciar o pod, porque reiniciar não conserta banco fora do ar e só remove
    /// capacidade de servir o que ainda funciona.</item>
    /// <item><b>ready</b> responde "posso receber tráfego", e aí sim checa Postgres e Redis: sem banco, a
    /// instância deve sair do balanceador até voltar.</item>
    /// </list>
    /// <para>
    /// Apontar os dois para o mesmo lugar é o erro comum, e ele transforma indisponibilidade de banco em
    /// reinício em massa de pods.
    /// </para>
    /// </remarks>
    private static IServiceCollection AddHealthChecksDaApi(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        DatabaseOptions database = configuration
            .GetSection(DatabaseOptions.SectionName)
            .Get<DatabaseOptions>() ?? new DatabaseOptions();

        RedisOptions redis = configuration
            .GetSection(RedisOptions.SectionName)
            .Get<RedisOptions>() ?? new RedisOptions();

        IHealthChecksBuilder builder = services.AddHealthChecks();

        if (!string.IsNullOrWhiteSpace(database.ConnectionString))
        {
            builder.AddNpgSql(database.ConnectionString, name: "postgres", tags: ["ready"]);
        }

        if (redis.Enabled)
        {
            builder.AddRedis(redis.ConnectionString, name: "redis", tags: ["ready"]);
        }

        return services;
    }

    private static IServiceCollection AddTelemetria(this IServiceCollection services)
    {
        services
            .AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService("cleanstart-api"))
            .WithTracing(tracing => tracing
                .AddAspNetCoreInstrumentation(options =>
                {
                    // Health check em loop de orquestrador geraria um trace por segundo, por instância, sem
                    // informação nenhuma — e o custo aparece na fatura do coletor.
                    options.Filter = context =>
                        !context.Request.Path.StartsWithSegments("/health", StringComparison.Ordinal);
                })
                .AddOtlpExporter())
            .WithMetrics(metrics => metrics
                .AddAspNetCoreInstrumentation()
                .AddOtlpExporter());

        return services;
    }
}
