# CleanStart

**Starter kit de arquitetura .NET 10 — Clean Architecture, DDD e CQRS prontos para produção.**

CleanStart não é um template vazio com quatro pastas e um `Program.cs`. É uma solução completa, executável, com uma feature de referência implementada de ponta a ponta, testes em cinco níveis, regras de arquitetura validadas automaticamente e as decisões técnicas documentadas em ADRs.

A ideia é simples: você clona, roda `docker compose up`, tem uma API funcionando em dois minutos — e a partir daí só escreve o domínio do seu problema.

---

## Sumário

- [Para quem é](#para-quem-é)
- [O problema que ele resolve](#o-problema-que-ele-resolve)
- [Stack](#stack)
- [Estrutura da solução](#estrutura-da-solução)
- [Regras de dependência](#regras-de-dependência)
- [As camadas em detalhe](#as-camadas-em-detalhe)
- [Feature de referência](#feature-de-referência-pedidos)
- [Decisões técnicas](#decisões-técnicas)
- [Estratégia de testes](#estratégia-de-testes)
- [Injeção de dependência](#injeção-de-dependência)
- [Observabilidade](#observabilidade)
- [Segurança](#segurança)
- [Performance](#performance)
- [Como rodar](#como-rodar)
- [Como usar como base do seu projeto](#como-usar-como-base-do-seu-projeto)
- [Roadmap](#roadmap)
- [Contribuindo](#contribuindo)
- [Licença](#licença)

---

## Para quem é

- **Devs .NET pleno/sênior** que já entenderam Clean Architecture no papel e querem uma referência de como ela se sustenta em um projeto real, com testes e CI.
- **Tech leads** que precisam subir um projeto novo sem gastar duas semanas em bootstrap e sem herdar as decisões erradas do último repositório que a equipe copiou.
- **Times** que querem um baseline arquitetural compartilhado — as regras não ficam num documento no Confluence que ninguém lê, ficam em testes que quebram o build.
- **Consultores e freelancers** que começam projetos do zero com frequência e não podem repetir o mesmo setup toda vez.

Não é para quem está aprendendo C# do zero. O kit assume familiaridade com ASP.NET Core, EF Core e injeção de dependência.

---

## O problema que ele resolve

Todo projeto .NET novo passa pelas mesmas duas semanas perdidas:

1. Decidir o que vai em cada camada e onde fica a regra de negócio.
2. Escolher entre MediatR e alternativas depois que o licenciamento mudou.
3. Montar o pipeline de validação, tratamento de erro, logging estruturado e health checks.
4. Configurar testes de integração com banco real sem depender da máquina de quem roda.
5. Descobrir, três meses depois, que a camada de Application está cheia de `using Microsoft.EntityFrameworkCore` e ninguém percebeu.

O CleanStart entrega essas cinco coisas resolvidas — e o item 5 resolvido de forma que **continue** resolvido, porque a regra virou teste automatizado.

---

## Stack

| Área | Escolha | Motivo resumido |
|---|---|---|
| Runtime | .NET 10 / C# 14 | LTS atual, `params` collections, extension members |
| Mensageria in-process | **Mediator** (source generator) | Sem reflection em runtime; licença permissiva |
| Mapeamento | **Mapperly** (source generator) | Mapeamento gerado em tempo de compilação, erro vira erro de build |
| Endpoints | **Carter** | Minimal APIs organizadas por módulo, sem controllers gordos |
| Persistência | EF Core 10 + PostgreSQL (Npgsql) | Migrations, interceptors, LINQ compilado |
| Cache | Redis (StackExchange.Redis) + `HybridCache` | Cache distribuído com camada L1 local |
| Validação | FluentValidation | Integrada ao pipeline do Mediator |
| Resiliência | Polly (via `Microsoft.Extensions.Http.Resilience`) | Retry, circuit breaker e timeout nas integrações |
| Logging | Serilog | Log estruturado, sinks console + arquivo + OTLP |
| Observabilidade | OpenTelemetry | Traces, métricas e logs num padrão só |
| Testes | xUnit v3, AwesomeAssertions, NSubstitute, Testcontainers, Bogus | Unitário, integração e funcional |
| Arquitetura | **NetArchTest** | Regras de camada como teste |
| Carga | k6 | Script de smoke load no repositório |
| CI/CD | GitHub Actions | Build, testes, análise, imagem Docker |
| Container | Docker + Docker Compose | API, Postgres, Redis, Seq e Jaeger prontos |

> **Sem MediatR, sem AutoMapper e sem FluentAssertions.** Os três passaram a exigir licença comercial. No caso dos dois primeiros, a substituição por source generators não é só questão de licença — ela move o custo de reflection de runtime para compile time. No terceiro, AwesomeAssertions é um fork Apache-2.0 com a mesma API.
>
> O critério aqui é explícito: **nenhuma dependência do kit pode cobrar do seu empregador.** Quem clona isto para um projeto de cliente não deveria ter que auditar licença depois.

---

## Estrutura da solução

```
CleanStart/
├── src/
│   ├── CleanStart.Domain/                  # Núcleo. Zero dependências externas.
│   │   ├── Common/
│   │   │   ├── Entity.cs                   # Base com Id e igualdade por identidade
│   │   │   ├── AggregateRoot.cs            # Raiz de agregado + domain events
│   │   │   ├── ValueObject.cs              # Base com igualdade estrutural
│   │   │   ├── IDomainEvent.cs
│   │   │   ├── ISoftDeletable.cs
│   │   │   └── IAuditable.cs
│   │   ├── Orders/
│   │   │   ├── Order.cs                    # Raiz de agregado
│   │   │   ├── OrderItem.cs                # Entidade filha
│   │   │   ├── OrderStatus.cs              # Enum rico / smart enum
│   │   │   ├── Events/OrderPlacedEvent.cs
│   │   │   └── IOrderRepository.cs         # Interface vive no domínio
│   │   ├── Customers/
│   │   ├── ValueObjects/
│   │   │   ├── Money.cs                    # Valor + moeda, operações seguras
│   │   │   ├── Email.cs
│   │   │   └── Document.cs                 # CPF/CNPJ com validação
│   │   └── Errors/
│   │       ├── Error.cs                    # Código + mensagem, sem exception
│   │       └── DomainErrors.cs             # Catálogo central de erros
│   │
│   ├── CleanStart.Application/             # Casos de uso. Depende só do Domain.
│   │   ├── Common/
│   │   │   ├── Result.cs                   # Result<T> — sucesso ou Error
│   │   │   ├── IUnitOfWork.cs
│   │   │   ├── ICurrentUser.cs             # Abstração do usuário autenticado
│   │   │   ├── IDateTimeProvider.cs        # Tempo injetável = teste determinístico
│   │   │   └── Behaviors/
│   │   │       ├── ValidationBehavior.cs
│   │   │       ├── LoggingBehavior.cs
│   │   │       ├── TransactionBehavior.cs
│   │   │       └── CachingBehavior.cs
│   │   ├── Orders/
│   │   │   ├── PlaceOrder/                 # Vertical slice: 1 caso de uso = 1 pasta
│   │   │   │   ├── PlaceOrderCommand.cs
│   │   │   │   ├── PlaceOrderHandler.cs
│   │   │   │   ├── PlaceOrderValidator.cs
│   │   │   │   └── PlaceOrderResponse.cs
│   │   │   ├── GetOrderById/
│   │   │   ├── ListOrders/
│   │   │   └── CancelOrder/
│   │   └── Mapping/OrderMapper.cs          # Mapperly [Mapper] partial class
│   │
│   ├── CleanStart.Infrastructure/          # Detalhes. Implementa as interfaces.
│   │   ├── Persistence/
│   │   │   ├── AppDbContext.cs
│   │   │   ├── Configurations/             # IEntityTypeConfiguration por entidade
│   │   │   ├── Repositories/
│   │   │   ├── Interceptors/
│   │   │   │   ├── AuditableInterceptor.cs
│   │   │   │   ├── SoftDeleteInterceptor.cs
│   │   │   │   └── DomainEventInterceptor.cs
│   │   │   ├── Migrations/
│   │   │   └── UnitOfWork.cs
│   │   ├── Caching/HybridCacheService.cs
│   │   ├── Messaging/                      # Outbox pattern
│   │   ├── Integrations/                   # Clientes HTTP com Polly
│   │   ├── Identity/JwtTokenService.cs
│   │   └── DependencyInjection.cs
│   │
│   └── CleanStart.Api/                     # Entrada HTTP. Fina.
│       ├── Modules/OrderModule.cs          # Carter: endpoints agrupados
│       ├── Middleware/
│       │   ├── ExceptionHandlingMiddleware.cs
│       │   ├── CorrelationIdMiddleware.cs
│       │   └── RequestLoggingMiddleware.cs
│       ├── Extensions/                     # ServiceCollection + WebApplication
│       ├── Program.cs
│       └── appsettings.json
│
├── tests/
│   ├── CleanStart.Domain.UnitTests/
│   ├── CleanStart.Application.UnitTests/
│   ├── CleanStart.Infrastructure.IntegrationTests/
│   ├── CleanStart.Api.FunctionalTests/
│   └── CleanStart.ArchitectureTests/        # NetArchTest
│
├── docs/
│   ├── adr/                                 # Architecture Decision Records
│   │   ├── 0001-clean-architecture.md
│   │   ├── 0002-mediator-em-vez-de-mediatr.md
│   │   ├── 0003-mapperly-em-vez-de-automapper.md
│   │   ├── 0004-result-em-vez-de-exception.md
│   │   ├── 0005-vertical-slices-na-application.md
│   │   ├── 0006-outbox-para-eventos.md
│   │   └── 0007-testcontainers-para-integracao.md
│   ├── getting-started.md
│   └── adding-a-feature.md                  # Passo a passo da primeira feature
│
├── .github/workflows/ci.yml
├── docker-compose.yml
├── Directory.Build.props                    # Nullable, warnings as errors, analyzers
├── Directory.Packages.props                 # Central Package Management
└── CleanStart.slnx                           # Formato XML (.slnx), na raiz
```

---

## Regras de dependência

```
┌──────────────────────────────────────────┐
│  Api (Carter Modules, Middleware, DI)    │  →  depende de Application
├──────────────────────────────────────────┤
│  Application (Commands/Queries/Handlers) │  →  depende de Domain
├──────────────────────────────────────────┤
│  Domain (Entities, VOs, Interfaces)      │  →  não depende de nada
├──────────────────────────────────────────┤
│  Infrastructure (EF, Redis, HTTP)        │  →  implementa interfaces de Domain/Application
└──────────────────────────────────────────┘
```

A dependência sempre aponta para dentro. A Infrastructure é referenciada pela Api **apenas** no registro de DI — em nenhum outro lugar.

O que é proibido em cada camada:

| Camada | Proibido |
|---|---|
| Domain | `Microsoft.EntityFrameworkCore`, qualquer namespace do projeto que não seja o próprio Domain, qualquer framework fora de `System.*` |
| Application | `Microsoft.EntityFrameworkCore`, `DbContext` direto, referência a `Infrastructure` ou `Api` |
| Infrastructure | referência a `Api`, regra de negócio dentro de repositório |
| Api | regra de negócio em endpoint, `DbContext` direto, repositório direto |

Essas regras **não são convenção** — são testes em `CleanStart.ArchitectureTests` que rodam no CI:

```csharp
[Fact]
public void Domain_NaoDeveDependerDeNenhumaOutraCamada()
{
    // NetArchTest inspeciona o assembly compilado e valida a direção da dependência.
    var resultado = Types.InAssembly(DomainAssembly)
        .ShouldNot()
        .HaveDependencyOnAny("CleanStart.Application", "CleanStart.Infrastructure", "CleanStart.Api")
        .GetResult();

    // A mensagem lista o tipo exato que violou — quem quebrou sabe onde corrigir.
    resultado.IsSuccessful.Should().BeTrue(
        $"tipos violando: {string.Join(", ", resultado.FailingTypeNames ?? [])}");
}

[Fact]
public void Handlers_DevemSerSelados()
{
    // Handler não é ponto de extensão. Herança aqui é quase sempre acoplamento acidental.
    Types.InAssembly(ApplicationAssembly)
        .That().ImplementInterface(typeof(IRequestHandler<,>))
        .Should().BeSealed()
        .GetResult().IsSuccessful.Should().BeTrue();
}

[Fact]
public void Entidades_NaoDevemExporSetterPublico()
{
    // Estado do agregado muda por método de domínio, nunca por atribuição externa.
    Types.InAssembly(DomainAssembly)
        .That().Inherit(typeof(Entity))
        .Should().MeetCustomRule(new NaoTerSetterPublicoRule())
        .GetResult().IsSuccessful.Should().BeTrue();
}
```

---

## As camadas em detalhe

### Domain

Só C# puro. Entidades com comportamento, invariantes protegidas no construtor e nos métodos, value objects imutáveis e eventos de domínio.

```csharp
public sealed class Order : AggregateRoot<OrderId>
{
    private readonly List<OrderItem> _items = [];

    // Coleção exposta somente leitura: ninguém adiciona item por fora do agregado.
    public IReadOnlyCollection<OrderItem> Items => _items.AsReadOnly();

    public CustomerId CustomerId { get; private set; }
    public OrderStatus Status { get; private set; }
    public Money Total { get; private set; }
    public DateTime PlacedAt { get; private set; }

    // Construtor sem parâmetros só para o EF Core materializar. Privado.
    private Order() { }

    private Order(OrderId id, CustomerId customerId, DateTime placedAt) : base(id)
    {
        CustomerId = customerId;
        PlacedAt = placedAt;
        Status = OrderStatus.Pending;
        Total = Money.Zero("BRL");
    }

    /// <summary>
    /// Factory method: a única forma de nascer um pedido válido.
    /// Retorna Result em vez de lançar — erro de negócio não é excepcional.
    /// </summary>
    public static Result<Order> Place(
        CustomerId customerId,
        IEnumerable<(ProductId Product, int Quantity, Money UnitPrice)> items,
        DateTime now)
    {
        var lista = items.ToList();

        if (lista.Count == 0)
            return Result.Failure<Order>(DomainErrors.Order.SemItens);

        if (lista.Any(i => i.Quantity <= 0))
            return Result.Failure<Order>(DomainErrors.Order.QuantidadeInvalida);

        var order = new Order(OrderId.New(), customerId, now);

        foreach (var (product, quantity, unitPrice) in lista)
            order.AddItem(product, quantity, unitPrice);

        // Evento registrado, não publicado. Quem publica é a Infrastructure, após o commit.
        order.RaiseDomainEvent(new OrderPlacedEvent(order.Id, order.CustomerId, order.Total));

        return Result.Success(order);
    }

    public Result Cancel(DateTime now)
    {
        // A regra de transição de estado vive na entidade, não no handler.
        if (Status is OrderStatus.Shipped or OrderStatus.Delivered)
            return Result.Failure(DomainErrors.Order.CancelamentoNaoPermitido(Status));

        Status = OrderStatus.Cancelled;
        RaiseDomainEvent(new OrderCancelledEvent(Id, now));
        return Result.Success();
    }

    private void AddItem(ProductId product, int quantity, Money unitPrice)
    {
        _items.Add(OrderItem.Create(Id, product, quantity, unitPrice));
        Total = Total.Add(unitPrice.Multiply(quantity));  // Money protege a aritmética de moeda
    }
}
```

Pontos que o kit demonstra aqui: identidade tipada (`OrderId` em vez de `Guid` solto, elimina a troca acidental de parâmetros), `Money` como value object (nada de `decimal` nu circulando), e `Result` em vez de exception para erro esperado.

### Application

Um caso de uso por pasta — **vertical slice dentro da Clean Architecture**. Command, handler, validator e response ficam juntos, porque é isso que você abre junto quando mexe na feature.

```csharp
// Command: o contrato de entrada do caso de uso. Record = imutável e comparável.
public sealed record PlaceOrderCommand(
    Guid CustomerId,
    IReadOnlyList<PlaceOrderItemDto> Items) : ICommand<Result<PlaceOrderResponse>>;

public sealed class PlaceOrderHandler(
    IOrderRepository orders,
    ICustomerRepository customers,
    IUnitOfWork unitOfWork,
    IDateTimeProvider clock)                       // tempo injetado = teste determinístico
    : ICommandHandler<PlaceOrderCommand, Result<PlaceOrderResponse>>
{
    public async ValueTask<Result<PlaceOrderResponse>> Handle(
        PlaceOrderCommand command, CancellationToken ct)
    {
        // 1. Carrega o que precisa pela abstração. Nenhum EF Core aqui.
        var customer = await customers.GetByIdAsync(new CustomerId(command.CustomerId), ct);
        if (customer is null)
            return Result.Failure<PlaceOrderResponse>(DomainErrors.Customer.NaoEncontrado);

        // 2. Delega a decisão de negócio para o domínio. O handler orquestra, não decide.
        var resultado = Order.Place(
            customer.Id,
            command.Items.Select(i => (new ProductId(i.ProductId), i.Quantity, Money.Of(i.UnitPrice, "BRL"))),
            clock.UtcNow);

        if (resultado.IsFailure)
            return Result.Failure<PlaceOrderResponse>(resultado.Error);

        // 3. Persiste. O commit dispara os interceptors (auditoria, outbox de eventos).
        orders.Add(resultado.Value);
        await unitOfWork.SaveChangesAsync(ct);

        return Result.Success(OrderMapper.ToResponse(resultado.Value));  // Mapperly, gerado
    }
}
```

O pipeline do Mediator cuida do resto de forma transversal:

```csharp
/// <summary>
/// Roda antes de todo handler. Se houver validator para a request, valida.
/// O handler nunca precisa checar entrada — quando ele executa, a entrada já é válida.
/// </summary>
public sealed class ValidationBehavior<TRequest, TResponse>(
    IEnumerable<IValidator<TRequest>> validators)
    : IPipelineBehavior<TRequest, TResponse> where TRequest : notnull
{
    public async ValueTask<TResponse> Handle(
        TRequest request, MessageHandlerDelegate<TRequest, TResponse> next, CancellationToken ct)
    {
        if (!validators.Any()) return await next(request, ct);

        var context = new ValidationContext<TRequest>(request);
        var falhas = validators
            .Select(v => v.Validate(context))
            .SelectMany(r => r.Errors)
            .Where(f => f is not null)
            .ToList();

        if (falhas.Count > 0)
            throw new ValidationException(falhas);   // capturada no middleware → 400 ProblemDetails

        return await next(request, ct);
    }
}
```

Behaviors incluídos: validação, logging com correlation id, transação (só para commands), e cache (só para queries marcadas com `ICacheable`).

### Infrastructure

Implementa as interfaces. Aqui vive o EF Core, o Redis, os clientes HTTP e os interceptors.

```csharp
/// <summary>
/// Interceptor de soft delete: converte Remove() em marcação lógica.
/// Fica na Infrastructure porque é detalhe de persistência — o domínio não sabe que existe.
/// </summary>
public sealed class SoftDeleteInterceptor(IDateTimeProvider clock, ICurrentUser user)
    : SaveChangesInterceptor
{
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken ct = default)
    {
        if (eventData.Context is null) return base.SavingChangesAsync(eventData, result, ct);

        foreach (var entry in eventData.Context.ChangeTracker.Entries<ISoftDeletable>())
        {
            if (entry.State is not EntityState.Deleted) continue;

            entry.State = EntityState.Modified;      // não apaga a linha
            entry.Entity.DeletedAt = clock.UtcNow;
            entry.Entity.DeletedBy = user.Id;
        }

        return base.SavingChangesAsync(eventData, result, ct);
    }
}
```

Complementado por um global query filter no `DbContext`, para que registros deletados sumam de toda consulta sem ninguém precisar lembrar do `Where`.

O `DomainEventInterceptor` coleta os eventos das entidades **depois** do `SaveChanges` bem-sucedido e grava na tabela de outbox — eventos e dados commitam na mesma transação, e um worker publica depois. É o que evita o clássico "salvou no banco mas o evento se perdeu".

### Api

Endpoints finos com Carter, agrupados por módulo:

```csharp
public sealed class OrderModule : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/orders")
            .WithTags("Orders")
            .RequireAuthorization();

        group.MapPost("/", async (PlaceOrderRequest request, ISender sender, CancellationToken ct) =>
        {
            var resultado = await sender.Send(request.ToCommand(), ct);

            // Result → HTTP acontece num único lugar. Nada de if/else espalhado por endpoint.
            return resultado.Match(
                onSuccess: r => Results.Created($"/api/v1/orders/{r.Id}", r),
                onFailure: Results.Problem);
        })
        .WithName("PlaceOrder")
        .Produces<PlaceOrderResponse>(StatusCodes.Status201Created)
        .ProducesProblem(StatusCodes.Status400BadRequest);
    }
}
```

---

## Feature de referência: Pedidos

O repositório vem com um domínio de pedidos implementado inteiro, não com um `WeatherForecast`. Ele existe para mostrar como as peças se encaixam num caso com alguma complexidade real:

- **Criar pedido** — validação de entrada, carregamento de agregado relacionado, invariantes no domínio, evento publicado via outbox.
- **Consultar pedido** — query com projeção direta para DTO e cache com invalidação por chave.
- **Listar pedidos** — paginação por keyset (não offset), filtro e ordenação.
- **Cancelar pedido** — regra de transição de estado que depende do estado atual, com erro tipado quando não permitido.

Cada uma cobre um padrão diferente. Juntas, dão o vocabulário para implementar qualquer coisa parecida.

---

## Decisões técnicas

Todas estão em `docs/adr/`, no formato: contexto → decisão → consequências. As principais:

**1. Result em vez de exception para erro de negócio.** Exception é para o inesperado. "Pedido sem itens" não é inesperado, é um caminho previsto. `Result<T>` torna o erro parte da assinatura do método — o compilador lembra você de tratá-lo. Exceptions continuam existindo para falha real de infraestrutura, capturadas no middleware e traduzidas em `ProblemDetails` (RFC 9457).

**2. Mediator em vez de MediatR.** Source generator resolve os handlers em tempo de compilação. Menos reflection, startup mais rápido, e a licença não é um risco para produto comercial.

**3. Mapperly em vez de AutoMapper.** Mapeamento gerado como código C# legível. Se você renomear uma propriedade, o build quebra — em vez de o campo chegar nulo em produção.

**4. Vertical slices dentro da Clean Architecture.** Organizar a Application por feature (`Orders/PlaceOrder/`) em vez de por tipo técnico (`Commands/`, `Handlers/`, `Validators/`). Alta coesão: a pasta que você abre é a pasta inteira que muda.

**5. Outbox para eventos de domínio.** Evento e dado na mesma transação. Publicação assíncrona por worker, com retry.

**6. Testcontainers para testes de integração.** Postgres e Redis reais, subindo em container, descartados no fim. Sem banco compartilhado, sem `InMemory` provider mentindo sobre o comportamento do SQL.

**7. Identidade tipada e value objects.** `OrderId`, `Money`, `Email`, `Document`. Elimina uma classe inteira de bug — passar o id errado no lugar certo compila quando tudo é `Guid`.

**8. AwesomeAssertions em vez de FluentAssertions.** Fork Apache-2.0 da v7, API idêntica. A v8 da FluentAssertions passou a exigir licença paga para uso comercial — o mesmo critério que baniu MediatR e AutoMapper. O custo: fork acompanha o upstream com atraso.

**9. Abstrações próprias sobre o Mediator.** Handlers implementam `ICommandHandler<,>` nosso, não a interface do pacote. O motivo é datado: o estável do Mediator é `net8.0` e o 3.1 alinhado ao .NET 10 ainda está em RC. Com a abstração, a troca é de uma camada. O custo: uma indireção que só se paga no dia da migração.

---

## Estratégia de testes

Cinco projetos, cada um com um propósito distinto e um custo de execução diferente:

| Projeto | O que testa | Dependências | Velocidade |
|---|---|---|---|
| `Domain.UnitTests` | Invariantes, transições de estado, value objects | nenhuma | ms |
| `Application.UnitTests` | Handlers com repositórios substituídos (NSubstitute) | nenhuma | ms |
| `Infrastructure.IntegrationTests` | Repositórios, interceptors, migrations contra Postgres real | Testcontainers | segundos |
| `Api.FunctionalTests` | Fluxo HTTP completo via `WebApplicationFactory` | Testcontainers | segundos |
| `ArchitectureTests` | Regras de camada, convenções de nomenclatura, selagem | nenhuma | ms |

```csharp
// Teste de domínio: sem mock, sem setup, sem banco. Só a regra.
[Fact]
public void Cancel_DevehFalhar_QuandoPedidoJaFoiEnviado()
{
    var pedido = OrderFactory.Enviado();          // builder do projeto de testes

    var resultado = pedido.Cancel(DateTime.UtcNow);

    resultado.IsFailure.Should().BeTrue();
    resultado.Error.Code.Should().Be("Order.CancelamentoNaoPermitido");
    pedido.Status.Should().Be(OrderStatus.Shipped);   // estado preservado
}
```

```csharp
// Teste de integração: Postgres real via Testcontainers, isolado por teste.
public sealed class OrderRepositoryTests(IntegrationTestFixture fixture)
    : IClassFixture<IntegrationTestFixture>
{
    [Fact]
    public async Task GetByIdAsync_DeveCarregarItensDoPedido()
    {
        await using var scope = fixture.CreateScope();
        var repo = scope.GetRequiredService<IOrderRepository>();
        var pedido = await fixture.SeedOrderAsync(itens: 3);

        var encontrado = await repo.GetByIdAsync(pedido.Id, CancellationToken.None);

        encontrado!.Items.Should().HaveCount(3);
    }
}
```

Não há meta de cobertura percentual no kit. A régua é diferente: **toda regra de negócio tem teste unitário, todo repositório tem teste de integração, todo endpoint tem teste funcional de caminho feliz e de erro.**

---

## Injeção de dependência

Cada camada registra o que é seu, em um método de extensão próprio. `Program.cs` só compõe:

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddApplication()        // Mediator, behaviors, validators, mappers
    .AddInfrastructure(builder.Configuration)   // DbContext, repos, cache, HTTP clients
    .AddApiServices(builder.Configuration);     // Carter, auth, OpenAPI, health checks, CORS

var app = builder.Build();
app.UseApiPipeline();        // ordem do middleware definida num lugar só
app.Run();
```

Tempo de vida: `Scoped` para tudo que toca a requisição (DbContext, repositórios, `ICurrentUser`), `Singleton` para stateless puro (`IDateTimeProvider`, mappers), `Transient` para handlers. A configuração usa o padrão Options com validação no startup — configuração errada derruba a aplicação na inicialização, não na primeira chamada em produção:

```csharp
services.AddOptions<DatabaseOptions>()
    .Bind(configuration.GetSection(DatabaseOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();
```

---

## Observabilidade

- **Serilog** com enrichers de correlation id, usuário e tenant; saída JSON em produção.
- **OpenTelemetry** instrumentando ASP.NET Core, HttpClient, EF Core e Npgsql; exportador OTLP.
- **Health checks** em `/health/live` e `/health/ready`, com verificação de Postgres e Redis no readiness.
- Compose sobe **Seq** (logs) e **Jaeger** (traces) para você ver o resultado sem configurar nada.

---

## Segurança

- Autenticação JWT com validação de issuer, audience, lifetime e chave; suporte a JWKS para provedores externos.
- Autorização baseada em policy, não em role espalhada por atributo.
- Rate limiting nativo do ASP.NET Core, por endpoint.
- Security headers (HSTS, CSP, X-Content-Type-Options) via middleware.
- Toda entrada validada no pipeline, antes do handler.
- Consultas sempre parametrizadas pelo EF Core; não há SQL concatenado no kit.
- Segredos por variável de ambiente / User Secrets — `appsettings.json` não contém credencial.
- Resposta de erro nunca vaza stack trace em produção; o `ProblemDetails` carrega o correlation id para você achar o log correspondente.

---

## Performance

- Source generators no lugar de reflection (Mediator, Mapperly, `System.Text.Json`).
- `AsNoTracking` por padrão nas queries de leitura, projeção direta para DTO.
- Paginação por keyset nas listagens — não degrada com offset alto.
- `HybridCache` com L1 em memória e L2 no Redis, invalidação por tag.
- Resiliência com Polly nas chamadas externas: timeout, retry com jitter, circuit breaker.
- Script k6 de smoke em `tests/load/` para você medir antes de afirmar.

---

## Como rodar

Pré-requisitos: .NET 10 SDK e Docker.

```bash
git clone https://github.com/<seu-usuario>/CleanStart.git
cd CleanStart

# Sobe API, Postgres, Redis, Seq e Jaeger
docker compose up -d

# Aplica migrations e popula dados de exemplo
dotnet run --project src/CleanStart.Api -- --migrate --seed
```

| Serviço | URL |
|---|---|
| API + Scalar (OpenAPI) | http://localhost:8080/scalar |
| Seq (logs) | http://localhost:5341 |
| Jaeger (traces) | http://localhost:16686 |

Testes:

```bash
dotnet test                                             # tudo
dotnet test tests/CleanStart.ArchitectureTests          # só as regras de arquitetura
```

---

## Como usar como base do seu projeto

1. Clone e renomeie a solução e os namespaces (`CleanStart` → o seu nome).
2. Apague a feature `Orders` — ela é referência, não fundação.
3. Mantenha `Domain/Common`, `Application/Common`, os interceptors e os `ArchitectureTests`. É isso que sustenta o resto.
4. Siga `docs/adding-a-feature.md` para a primeira feature do seu domínio.
5. Revise os ADRs: onde a decisão não servir para o seu contexto, escreva um ADR novo substituindo o antigo em vez de apagar o original. O histórico da decisão vale mais do que a decisão.

---

## Roadmap

- [ ] Multi-tenancy opcional (discriminador + Row-Level Security no Postgres)
- [ ] Template `dotnet new` para eliminar o rename manual
- [ ] Variante com MongoDB na Infrastructure, mostrando que a troca não toca Domain nem Application
- [ ] Exemplo de integração com mensageria externa (MassTransit + RabbitMQ)
- [ ] Pipeline de deploy para Azure Container Apps
- [ ] Versão do kit em Minimal API pura, sem Carter, para comparação

---

## Contribuindo

Issues e PRs são bem-vindos. Antes de abrir PR:

1. `dotnet test` precisa passar — incluindo `ArchitectureTests`.
2. Mudança de decisão técnica vem acompanhada de ADR.
3. Feature nova vem com teste no nível apropriado.

Discussões sobre "por que a decisão X" vão na aba Discussions, não em issue.

---

## Licença

**MIT** — veja [LICENSE](LICENSE).

Use em projeto pessoal, comercial ou de cliente, modifique à vontade, redistribua, inclusive em produto fechado. Não precisa pedir autorização nem pagar nada.

A única condição é a da própria MIT: **mantenha o aviso de copyright e o texto da licença** nas cópias ou partes substanciais do código. Na prática, isso significa preservar o arquivo `LICENSE` quando você usar este kit como base — não é preciso creditar na interface do seu produto nem no seu README.

O software é fornecido "como está", sem garantia.

---

## Autor

**Joseleno Santos** — Engenheiro de Software Sênior .NET, ~20 anos de experiência, Aracaju/SE.
Fundador da [CodeProcess Solutions](https://codeprocess.com.br) — consultoria .NET, arquitetura e processos de desenvolvimento.

[LinkedIn](https://linkedin.com/in/joseleno-santos) · [codeprocess.com.br](https://codeprocess.com.br)

> Se o kit te economizou tempo, uma estrela no repositório ajuda mais gente a encontrá-lo.