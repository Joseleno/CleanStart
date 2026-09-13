using System.Globalization;
using System.Security.Claims;
using System.Text;
using System.Threading.RateLimiting;
using Carter;
using CleanStart.Api.Security;
using CleanStart.Api.Services;
using CleanStart.Application.Common.Abstractions;
using CleanStart.Infrastructure.Configuration;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.Tokens;
using Npgsql;
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
            .AddAutenticacao(configuration)
            .AddLimiteDeRequisicoes()
            .AddHealthChecksDaApi(configuration)
            .AddTelemetria();
    }

    /// <summary>
    /// Validação de token JWT e autorização por policy.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Os quatro parâmetros de validação estão ligados de propósito, e cada um cobre um ataque diferente:</b>
    /// sem <c>ValidateIssuer</c>, um token emitido por outro sistema com a mesma chave é aceito; sem
    /// <c>ValidateAudience</c>, um token legítimo emitido para outro serviço vale aqui; sem
    /// <c>ValidateLifetime</c>, token nenhum expira; sem <c>ValidateIssuerSigningKey</c>, qualquer um assina.
    /// Todos são <c>true</c> por padrão — estão escritos assim mesmo, porque num kit de referência o leitor
    /// precisa ver que a decisão foi tomada, e não herdada.
    /// </para>
    /// <para>
    /// <b><c>ClockSkew</c> reduzido para 30 segundos.</b> O padrão da biblioteca é de <b>cinco minutos</b>, o que
    /// significa que um token expirado continua sendo aceito por todo esse tempo. A tolerância existe para
    /// relógios dessincronizados entre emissor e validador; cinco minutos é generoso demais para infraestrutura
    /// com NTP.
    /// </para>
    /// <para>
    /// <b>Para trocar por um provedor de identidade real</b>, substitua <c>IssuerSigningKey</c> por
    /// <c>options.Authority</c> — o handler passa a buscar as chaves públicas pelo JWKS do provedor e a rotação
    /// de chave deixa de ser problema seu. É a única mudança necessária aqui, e o <c>JwtTokenService</c> some
    /// junto com o endpoint que o expõe.
    /// </para>
    /// </remarks>
    private static IServiceCollection AddAutenticacao(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        JwtOptions jwt = configuration
            .GetSection(JwtOptions.SectionName)
            .Get<JwtOptions>() ?? new JwtOptions();

        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = jwt.Issuer,
                    ValidateAudience = true,
                    ValidAudience = jwt.Audience,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey)),
                    ClockSkew = TimeSpan.FromSeconds(30),
                };
            });

        services.AddAuthorization();

        // Emissor de token para desenvolvimento e testes. Registrado sempre, mas exposto por HTTP só em
        // Development — ver DevTokenModule.
        services.AddScoped<JwtTokenService>();

        return services;
    }

    /// <summary>
    /// Limite de requisições por janela, particionado por usuário autenticado ou por IP.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>A partição é o que importa aqui.</b> Um limitador global protege o servidor mas pune todo mundo junto:
    /// um cliente abusivo consumiria a cota de todos os outros. Particionando, cada usuário tem a sua — e quem
    /// ainda não se autenticou é particionado por IP, que é o melhor identificador disponível antes do login.
    /// </para>
    /// <para>
    /// <b>Responde 429, não 503</b>, e com <c>Retry-After</c>: o cliente precisa saber que o pedido foi recusado
    /// por excesso e quando pode tentar de novo. Sem o cabeçalho, um cliente bem-intencionado tenta em laço
    /// apertado e piora exatamente o que o limite existe para conter.
    /// </para>
    /// <para>
    /// <b>Janela fixa, e não deslizante ou token bucket</b>, porque é a que se explica em uma frase: são N
    /// requisições a cada M segundos. Ela tem um defeito conhecido — o dobro do limite na virada entre duas
    /// janelas — e para um exemplo isso é aceitável; para um limite rígido, a janela deslizante é a escolha.
    /// </para>
    /// <para>
    /// <b>Não precisa de pacote:</b> o limitador faz parte do SDK Web desde o .NET 7.
    /// </para>
    /// </remarks>
    private static IServiceCollection AddLimiteDeRequisicoes(this IServiceCollection services)
    {
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            options.OnRejected = async (contexto, cancellationToken) =>
            {
                if (contexto.Lease.TryGetMetadata(MetadataName.RetryAfter, out TimeSpan retryAfter))
                {
                    contexto.HttpContext.Response.Headers.RetryAfter =
                        ((int)retryAfter.TotalSeconds).ToString(CultureInfo.InvariantCulture);
                }

                await contexto.HttpContext.Response.WriteAsync(
                    "Requisições demais. Tente novamente em instantes.",
                    cancellationToken);
            };

            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(contexto =>
            {
                string particao =
                    contexto.User.FindFirstValue(ClaimTypes.NameIdentifier)
                    ?? contexto.Connection.RemoteIpAddress?.ToString()
                    ?? "desconhecido";

                return RateLimitPartition.GetFixedWindowLimiter(particao, _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 100,
                    Window = TimeSpan.FromMinutes(1),

                    // Sem fila: quem passou do limite recebe 429 na hora. Enfileirar aumentaria a latência de
                    // quem já está sendo limitado e seguraria conexões do servidor — o oposto do objetivo.
                    QueueLimit = 0,
                });
            });
        });

        return services;
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

    /// <summary>
    /// Traces e métricas via OpenTelemetry, exportados por OTLP.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>O protocolo é OTLP e o destino é configuração, não código.</b> Jaeger, Tempo, Honeycomb, Datadog e o
    /// coletor da OpenTelemetry falam todos OTLP: trocar de ferramenta é mudar a variável
    /// <c>OTEL_EXPORTER_OTLP_ENDPOINT</c>, sem recompilar. É o que separa instrumentar de casar com um fornecedor.
    /// </para>
    /// <para>
    /// <b>O que cada instrumentação responde:</b> AspNetCore, "quanto demorou a requisição"; Npgsql, "quanto
    /// disso foi o banco, e em qual comando"; HttpClient, "quanto foi esperando um serviço externo"; Runtime,
    /// "houve pausa de GC ou fome de thread pool". Juntas, cobrem as perguntas que se faz às três da manhã.
    /// </para>
    /// </remarks>
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

                // Cada comando SQL vira um span filho, com duração e erro: é o que responde "a requisição
                // demorou — foi o banco?" sem instrumentar consulta a consulta. Vem do Npgsql porque é ele que
                // executa o comando; o instrumentador de EF Core só existe em beta, e o kit não fixa instável.
                .AddNpgsql()

                // Chamada a serviço externo vira span, e o cabeçalho de contexto viaja junto — é o que faz o
                // trace continuar do outro lado, em vez de terminar na fronteira do processo.
                .AddHttpClientInstrumentation()
                .AddOtlpExporter())
            .WithMetrics(metrics => metrics
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()

                // GC, thread pool, exceptions e uso de memória. É a instrumentação que distingue "o código está
                // lento" de "o processo está sufocado" — sintomas parecidos e causas opostas.
                .AddRuntimeInstrumentation()
                .AddOtlpExporter());

        return services;
    }
}
