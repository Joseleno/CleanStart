using Carter;
using CleanStart.Api;
using CleanStart.Api.Middlewares;
using CleanStart.Application;
using CleanStart.Infrastructure;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Scalar.AspNetCore;
using Serilog;

// Composition root. A ordem de tudo aqui é deliberada, e os comentários dizem por quê — num kit de referência,
// "funciona" não basta: quem lê precisa poder mudar sem descobrir a razão por tentativa e erro.
WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

// Serilog substitui o logging padrão antes de qualquer outro registro: o que falhar no startup a partir daqui já
// sai no formato estruturado. Lido da configuração para que o ambiente decida sink e nível sem recompilar.
builder.Host.UseSerilog((context, configuration) => configuration
    .ReadFrom.Configuration(context.Configuration)
    .Enrich.FromLogContext());

// As três camadas, de dentro para fora. AddApiServices vem por último porque sobrescreve ICurrentUser e
// ICorrelationIdProvider pelas implementações que leem o HttpContext — no contêiner da Microsoft, o último
// registro vence.
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddApiServices(builder.Configuration);

WebApplication app = builder.Build();

// ───────────────────────────── Pipeline ─────────────────────────────
//
// A ordem dos três primeiros middlewares não é arbitrária:
//
// 1. CorrelationId — primeiro, para que todo log e toda resposta de erro tenham o identificador. Registrado
//    depois do handler de exception, um erro no próprio pipeline sairia sem id.
// 2. ExceptionHandling — envolve tudo o que vem depois. É o que garante que nenhuma exception escape como
//    página de erro do servidor, revelando stack trace.
// 3. RequestLogging — dentro do handler de erro, para que a requisição que falhou também registre duração.

app.UseMiddleware<CorrelationIdMiddleware>();
app.UseMiddleware<ExceptionHandlingMiddleware>();
app.UseMiddleware<RequestLoggingMiddleware>();

if (app.Environment.IsDevelopment())
{
    // OpenAPI só em desenvolvimento: o documento descreve a superfície inteira da API, e publicá-lo em produção
    // entrega o mapa a quem estiver procurando. Quem precisa dele em produção o expõe atrás de autenticação.
    app.MapOpenApi();
    app.MapScalarApiReference();
}

// live: o processo responde. Sem dependência externa — banco fora do ar não deve fazer o orquestrador reiniciar
// o pod, porque reiniciar não conserta banco e só remove capacidade.
app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = _ => false,
});

// ready: posso receber tráfego. Checa Postgres e Redis — sem eles, a instância sai do balanceador.
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready"),
});

// Carter mapeia os módulos descobertos por varredura. Os endpoints de pedidos são a T4.2.
app.MapCarter();

await app.RunAsync();
