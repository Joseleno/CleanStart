using CleanStart.Application.Common.Abstractions;
using CleanStart.Application.Orders.GetOrderById;
using CleanStart.Application.Orders.ListOrders;
using CleanStart.Domain.Customers;
using CleanStart.Domain.Orders;
using CleanStart.Infrastructure.Configuration;
using CleanStart.Infrastructure.Persistence;
using CleanStart.Infrastructure.Persistence.Interceptors;
using CleanStart.Infrastructure.Persistence.Repositories;
using CleanStart.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace CleanStart.Infrastructure;

/// <summary>
/// Registra a infraestrutura no contêiner.
/// </summary>
/// <remarks>
/// Único ponto de entrada da camada: a Api chama <see cref="AddInfrastructure"/> e não conhece
/// <c>AppDbContext</c>, interceptor nem repositório concreto. É o que permite trocar a implementação sem tocar em
/// quem a consome.
/// </remarks>
public static class DependencyInjection
{
    /// <summary>
    /// Acrescenta persistência, cache e os serviços de infraestrutura.
    /// </summary>
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services
            .AddOptionsValidadas(configuration)
            .AddPersistencia()
            .AddCache(configuration)
            .AddServicos();

        return services;
    }

    /// <summary>
    /// Liga as três seções de configuração, validadas no startup.
    /// </summary>
    /// <remarks>
    /// <c>ValidateOnStart</c> é o que importa aqui: sem ele, a validação só roda quando alguém pede o
    /// <c>IOptions</c> pela primeira vez — ou seja, já em produção, na primeira requisição. Com ele, configuração
    /// ausente derruba a aplicação ao subir, com mensagem dizendo qual chave falta.
    /// </remarks>
    private static IServiceCollection AddOptionsValidadas(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<DatabaseOptions>()
            .Bind(configuration.GetSection(DatabaseOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<RedisOptions>()
            .Bind(configuration.GetSection(RedisOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<JwtOptions>()
            .Bind(configuration.GetSection(JwtOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        return services;
    }

    private static IServiceCollection AddPersistencia(this IServiceCollection services)
    {
        // Os interceptors entram no contêiner porque dependem de IDateTimeProvider e ICurrentUser — construí-los
        // à mão aqui significaria resolver essas dependências à mão também.
        services.AddScoped<AuditableInterceptor>();
        services.AddScoped<SoftDeleteInterceptor>();
        services.AddScoped<DomainEventInterceptor>();

        services.AddDbContext<AppDbContext>((provider, options) =>
        {
            DatabaseOptions database = provider.GetRequiredService<IOptions<DatabaseOptions>>().Value;

            options.UseNpgsql(database.ConnectionString, npgsql =>
            {
                npgsql.CommandTimeout(database.CommandTimeoutSeconds);

                // Retry de falha transiente: queda de rede e failover são esperados em nuvem, e repetir é o
                // comportamento correto. Não cobre erro de lógica — só o que é transitório por natureza.
                npgsql.EnableRetryOnFailure(database.MaxRetryCount);
            });

            // Parâmetros de consulta no log carregam PII; a opção existe para desenvolvimento e é falsa por
            // padrão (ver DatabaseOptions).
            options.EnableSensitiveDataLogging(database.EnableSensitiveDataLogging);

            // A ordem dos três é fixada em um lugar só, com o motivo escrito lá — ela importa: o soft delete
            // precisa converter Deleted em Modified antes de a auditoria rodar.
            options.AddCleanStartInterceptors(
                provider.GetRequiredService<SoftDeleteInterceptor>(),
                provider.GetRequiredService<AuditableInterceptor>(),
                provider.GetRequiredService<DomainEventInterceptor>());
        });

        // IUnitOfWork é o próprio contexto — não existe classe UnitOfWork separada, porque ela só delegaria
        // SaveChangesAsync e nada mais. Se um dia o limite transacional precisar de comportamento próprio,
        // a mudança é nesta linha, porque a Application já fala com a interface.
        services.AddScoped<IUnitOfWork>(provider => provider.GetRequiredService<AppDbContext>());

        services.AddScoped<IOrderRepository, OrderRepository>();
        services.AddScoped<ICustomerRepository, CustomerRepository>();

        // Leitura projetada, separada do repositório: o lado de consulta não materializa o agregado.
        services.AddScoped<IOrderReader, OrderReader>();
        services.AddScoped<IOrdersPageReader, OrdersPageReader>();

        return services;
    }

    private static IServiceCollection AddCache(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        RedisOptions redis = configuration
            .GetSection(RedisOptions.SectionName)
            .Get<RedisOptions>() ?? new RedisOptions();

        services.AddHybridCache();

        if (redis.Enabled)
        {
            // O segundo nível é opcional: sem Redis configurado, o HybridCache usa só memória local. A
            // aplicação sobe igual — o que muda é o cache não ser compartilhado entre instâncias.
            services.AddStackExchangeRedisCache(options =>
            {
                options.Configuration = redis.ConnectionString;
                options.InstanceName = redis.InstanceName;
            });
        }

        services.AddScoped<ICacheService, HybridCacheService>();

        return services;
    }

    private static IServiceCollection AddServicos(this IServiceCollection services)
    {
        services.AddSingleton<IDateTimeProvider, SystemDateTimeProvider>();

        // Scoped, não Transient: o correlation id precisa ser o mesmo durante toda a operação, senão a
        // correlação — a única razão de ele existir — não acontece.
        services.AddScoped<ICorrelationIdProvider, ScopedCorrelationIdProvider>();

        // Padrão sem usuário. A Api registra por cima a implementação que lê o HttpContext (Fase 4); job e
        // seed continuam com esta, gravando autoria nula.
        services.AddScoped<ICurrentUser, NoCurrentUser>();

        return services;
    }
}
