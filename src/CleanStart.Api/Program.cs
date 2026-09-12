// Composition root da Api. Por enquanto só o mínimo para a solução compilar e subir:
// Carter, Mediator, EF Core e observabilidade entram nas fases seguintes.
WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

WebApplication app = builder.Build();

app.Run();
