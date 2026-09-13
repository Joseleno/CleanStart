using CleanStart.Application.Common.Abstractions;
using CleanStart.Application.Orders.GetOrderById;
using CleanStart.Application.Orders.ListOrders;
using CleanStart.Application.Orders.NotifyOrderPlaced;
using CleanStart.Domain.Customers;
using CleanStart.Domain.Orders;
using CleanStart.Infrastructure.Configuration;
using CleanStart.Infrastructure.Http;
using CleanStart.Infrastructure.Persistence;
using CleanStart.Infrastructure.Persistence.Interceptors;
using CleanStart.Infrastructure.Persistence.Outbox;
using CleanStart.Infrastructure.Persistence.Repositories;
using CleanStart.Infrastructure.Persistence.Seed;
using CleanStart.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Http.Resilience;
using Polly;
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
            .AddServicos()
            .AddOutbox(configuration)
            .AddClientesHttp(configuration);

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

        services.AddOptions<OutboxOptions>()
            .Bind(configuration.GetSection(OutboxOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<HttpResilienceOptions>()
            .Bind(configuration.GetSection(HttpResilienceOptions.SectionName))
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

    /// <summary>
    /// Registra o despachante do outbox e o destino para onde ele publica.
    /// </summary>
    /// <remarks>
    /// <para>
    /// O publisher e o processor são Scoped como o <c>AppDbContext</c> de que dependem; o worker é singleton, por
    /// ser um <c>BackgroundService</c>, e por isso cria um escopo próprio a cada ciclo.
    /// </para>
    /// <para>
    /// <b>Só o worker é condicional.</b> O processor continua registrado mesmo com o despachante desligado —
    /// quem o desligou pode querer chamá-lo à mão, de um comando ou de um teste. O que a flag decide é se
    /// alguém o chama sozinho, de tempos em tempos.
    /// </para>
    /// </remarks>
    private static IServiceCollection AddOutbox(this IServiceCollection services, IConfiguration configuration)
    {
        // Troque esta linha pela sua implementação para ligar um broker de verdade; nada mais muda.
        services.AddScoped<IOutboxPublisher, LoggingOutboxPublisher>();
        services.AddScoped<OutboxProcessor>();

        // Singleton porque a memória do que já foi notificado precisa atravessar os escopos — um por ciclo do
        // despachante. Scoped faria cada ciclo esquecer tudo, e a proteção contra entrega repetida sumiria
        // justamente no caso que ela existe para cobrir. Num sistema real esse estado é uma tabela, e aí o
        // tempo de vida do serviço deixa de importar.
        services.AddSingleton<OrderPlacedNotifier>();

        // Dados de exemplo. Registrado sempre, executado só sob a flag --seed — ver StartupTasks.
        services.AddScoped<DatabaseSeeder>();

        // Lido direto da configuração, e não por IOptions: a decisão é sobre o que REGISTRAR, e acontece antes
        // de existir um provider de onde resolver options.
        bool habilitado = configuration
            .GetSection(OutboxOptions.SectionName)
            .GetValue("Enabled", defaultValue: true);

        if (habilitado)
        {
            services.AddHostedService<OutboxWorker>();
        }

        return services;
    }

    /// <summary>
    /// Clientes HTTP tipados, com retry, circuit breaker e timeout.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>A ordem do pipeline é a que o <c>AddResilienceHandler</c> monta, e ela importa:</b> o timeout total
    /// envolve tudo, o retry vem dentro dele, o circuit breaker dentro do retry, e o timeout por tentativa é o
    /// mais interno. Assim cada tentativa tem prazo próprio, o conjunto tem prazo máximo, e o breaker conta
    /// falhas de tentativas individuais — não do conjunto.
    /// </para>
    /// <para>
    /// <b>⚠️ Retry só vale porque este cliente faz apenas <c>GET</c>.</b> É a decisão mais importante deste
    /// método e a que não se pode copiar sem pensar: repetir uma consulta é inofensivo; repetir um <c>POST</c>
    /// que cobra um cartão cobra duas vezes. Um cliente que escreve precisa de retry só em operação idempotente
    /// — na prática, uma que carregue chave de idempotência — ou de retry nenhum.
    /// </para>
    /// <para>
    /// <b>O <c>HttpClient.Timeout</c> fica desligado</b> (<c>InfiniteTimeSpan</c>) de propósito: ele é um
    /// timeout do cliente inteiro e cancelaria a operação no meio do pipeline, disparando um cancelamento que se
    /// confunde com o do usuário. Quem controla prazo aqui é a política, que sabe distinguir tentativa de
    /// conjunto.
    /// </para>
    /// </remarks>
    private static IServiceCollection AddClientesHttp(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        HttpResilienceOptions resiliencia = configuration
            .GetSection(HttpResilienceOptions.SectionName)
            .Get<HttpResilienceOptions>() ?? new HttpResilienceOptions();

        services
            .AddHttpClient<IExchangeRateClient, ExchangeRateClient>(client =>
            {
                client.BaseAddress = new Uri("https://api.exemplo.invalid/");

                // O prazo é da política, não do HttpClient — ver o comentário acima.
                client.Timeout = Timeout.InfiniteTimeSpan;
            })
            .AddResilienceHandler("externo", pipeline =>
            {
                pipeline.AddTimeout(TimeSpan.FromSeconds(resiliencia.TotalTimeoutSeconds));

                pipeline.AddRetry(new HttpRetryStrategyOptions
                {
                    MaxRetryAttempts = resiliencia.MaxRetryAttempts,
                    Delay = TimeSpan.FromSeconds(resiliencia.BaseDelaySeconds),
                    BackoffType = DelayBackoffType.Exponential,

                    // A variação aleatória evita que instâncias que falharam juntas retentem no mesmo instante,
                    // martelando um serviço que está tentando voltar.
                    UseJitter = true,
                });

                pipeline.AddCircuitBreaker(new HttpCircuitBreakerStrategyOptions
                {
                    FailureRatio = resiliencia.FailureRatio,
                    BreakDuration = TimeSpan.FromSeconds(resiliencia.BreakDurationSeconds),
                });

                pipeline.AddTimeout(TimeSpan.FromSeconds(resiliencia.AttemptTimeoutSeconds));
            });

        return services;
    }
}
