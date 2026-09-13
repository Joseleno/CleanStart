# CleanStart

**Starter kit de arquitetura .NET 10 — Clean Architecture, DDD e CQRS prontos para produção.**

CleanStart não é um template vazio com quatro pastas e um `Program.cs`. É uma solução completa, executável, com uma feature de referência implementada de ponta a ponta, testes em cinco níveis, regras de arquitetura validadas automaticamente e as decisões técnicas documentadas em ADRs.

A ideia é simples: você clona, roda `docker compose up`, prepara o banco com uma flag e tem uma API autenticada
respondendo em poucos minutos — e a partir daí só escreve o domínio do seu problema.

Comece por **[docs/getting-started.md](docs/getting-started.md)**, que vai do clone ao primeiro pedido criado.

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
| Logging | Serilog | Log estruturado em JSON; console, mais Seq em desenvolvimento |
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
│   │   │   ├── OrderStatus.cs              # Pending, Paid, Shipped, Cancelled
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
│   │   │   ├── Abstractions/               # O que a Application precisa e não implementa
│   │   │   │   ├── IUnitOfWork.cs
│   │   │   │   ├── ICurrentUser.cs         # Abstração do usuário autenticado
│   │   │   │   ├── IDateTimeProvider.cs    # Tempo injetável = teste determinístico
│   │   │   │   ├── ICacheService.cs
│   │   │   │   ├── ICacheInvalidator.cs
│   │   │   │   ├── IOutboxPublisher.cs     # Fronteira do outbox (ADR 0006)
│   │   │   │   └── IExchangeRateClient.cs  # Exemplo de dependência externa
│   │   │   ├── Messaging/                  # Marcadores próprios (ADR 0009)
│   │   │   │   ├── ICommand.cs             # O único lugar que menciona o Mediator
│   │   │   │   ├── ICommandHandler.cs
│   │   │   │   ├── IQuery.cs
│   │   │   │   └── IQueryHandler.cs
│   │   │   └── Behaviors/
│   │   │       ├── LoggingBehavior.cs
│   │   │       ├── ValidationBehavior.cs
│   │   │       ├── TransactionBehavior.cs
│   │   │       └── CacheInvalidationBehavior.cs
│   │   ├── Orders/
│   │   │   ├── PlaceOrder/                 # Vertical slice: 1 caso de uso = 1 pasta
│   │   │   │   ├── PlaceOrderCommand.cs
│   │   │   │   ├── PlaceOrderHandler.cs
│   │   │   │   ├── PlaceOrderValidator.cs
│   │   │   │   ├── PlaceOrderResponse.cs
│   │   │   │   └── OrderMapper.cs          # Mapperly, dentro do slice que o usa
│   │   │   ├── GetOrderById/               # Query + IOrderReader (porta do slice)
│   │   │   ├── ListOrders/                 # Paginação por cursor
│   │   │   ├── CancelOrder/
│   │   │   └── NotifyOrderPlaced/          # Reação a evento, nomeada pela reação
│   │   └── DependencyInjection.cs
│   │
│   ├── CleanStart.Infrastructure/          # Detalhes. Implementa as interfaces.
│   │   ├── Persistence/
│   │   │   ├── AppDbContext.cs             # Implementa IUnitOfWork (sem classe à parte)
│   │   │   ├── AppDbContextFactory.cs      # Design-time, para o dotnet ef
│   │   │   ├── Configurations/             # IEntityTypeConfiguration por entidade
│   │   │   ├── Repositories/               # Repositórios e readers
│   │   │   ├── Interceptors/
│   │   │   │   ├── AuditableInterceptor.cs
│   │   │   │   ├── SoftDeleteInterceptor.cs
│   │   │   │   ├── DomainEventInterceptor.cs   # Grava o outbox na mesma transação
│   │   │   │   └── InterceptorRegistration.cs  # A ordem, com o motivo escrito
│   │   │   ├── Outbox/                     # Despachante (ADR 0006)
│   │   │   │   ├── OutboxMessage.cs
│   │   │   │   ├── OutboxProcessor.cs      # Reserva, publica, registra
│   │   │   │   ├── OutboxWorker.cs         # Só o laço
│   │   │   │   ├── OutboxEventTypes.cs     # Mapa tipo → nome curto
│   │   │   │   └── LoggingOutboxPublisher.cs   # Troque por um broker real
│   │   │   ├── Seed/                       # Flags --migrate e --seed
│   │   │   └── Migrations/
│   │   ├── Configuration/                  # Options validadas no startup
│   │   ├── Http/ExchangeRateClient.cs      # Cliente com retry e circuit breaker
│   │   ├── Services/                       # Clock, cache, correlation id
│   │   └── DependencyInjection.cs
│   │
│   └── CleanStart.Api/                     # Entrada HTTP. Fina.
│       ├── Modules/
│       │   ├── OrderModule.cs              # Carter: endpoints agrupados
│       │   └── DevTokenModule.cs           # Token de exemplo, só em Development
│       ├── Middlewares/
│       │   ├── CorrelationIdMiddleware.cs
│       │   ├── ExceptionHandlingMiddleware.cs
│       │   ├── RequestLoggingMiddleware.cs
│       │   └── SecurityHeadersMiddleware.cs
│       ├── Security/JwtTokenService.cs     # Emissor de exemplo
│       ├── Services/                       # ICurrentUser e correlation id do HTTP
│       ├── Extensions/ResultExtensions.cs  # Result → HTTP, num lugar só
│       ├── DependencyInjection.cs
│       ├── Program.cs
│       └── appsettings.json
│
├── tests/
│   ├── CleanStart.Domain.UnitTests/
│   ├── CleanStart.Application.UnitTests/
│   ├── CleanStart.Infrastructure.IntegrationTests/
│   ├── CleanStart.Api.FunctionalTests/
│   ├── CleanStart.ArchitectureTests/         # NetArchTest
│   └── load/smoke.js                         # k6
│
├── docs/
│   ├── adr/                                  # 10 ADRs — ver docs/adr/README.md
│   ├── getting-started.md                    # Do clone ao primeiro request
│   └── adding-a-feature.md                   # Um caso de uso, do domínio ao endpoint
│
├── .github/workflows/ci.yml                  # Build, testes e imagem, em jobs separados
├── Dockerfile                                # Multi-stage, usuário sem privilégio
├── docker-compose.yml                        # API, Postgres, Redis, Seq e Jaeger
├── Directory.Build.props                     # Nullable, warnings as errors, analyzers
├── Directory.Packages.props                  # Central Package Management
├── global.json                               # Fixa o SDK e liga o runner MTP
├── .editorconfig                             # Severidade de build, não sugestão
├── .gitattributes                            # Fim de linha por tipo de arquivo
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
public void Domain_NaoDependeDeNenhumaOutraCamada()
{
    // NetArchTest inspeciona o assembly compilado e valida a direção da dependência.
    ArchTestResult resultado = Types.InAssembly(Domain)
        .Should()
        .NotHaveDependencyOnAny(NamespaceApplication, NamespaceInfrastructure, NamespaceApi)
        .GetResult();

    // A asserção própria lista o tipo exato que violou — quem quebrou sabe onde corrigir.
    resultado.Should().NaoTerViolacao(
        "o Domain é o centro da arquitetura: tudo aponta para ele, ele não aponta para nada");
}

[Fact]
public void Handlers_SaoSealed()
{
    // Reflexão direta, e não NetArchTest: a pergunta é sobre o MEMBRO, e a API do NetArchTest opera
    // sobre tipos. Forçá-la aqui renderia um predicado menos legível que o foreach explícito.
    List<Type> handlers = [.. Application.GetTypes().Where(EhHandler)];

    // Guarda contra vacuidade: sem isto, a regra passaria sem inspecionar nada no dia em que o filtro
    // parasse de encontrar handlers — e continuaria verde se o primeiro violador nascesse.
    handlers.Should().NotBeEmpty("o teste precisa de handlers para inspecionar");

    List<string> violacoes = [.. handlers
        .Where(handler => !handler.IsSealed)
        .Select(handler => handler.FullName ?? handler.Name)];

    violacoes.Should().BeEmpty(
        "handler é ponto final de orquestração, não ponto de extensão. Tipos violadores: "
        + string.Join(", ", violacoes));
}
```

São **dez regras** no total: cinco de dependência entre camadas, duas sobre como o domínio é escrito (setter
público, coleção mutável) e três de mensageria (handler `sealed`, mensagem `record`, e nada referenciando o
Mediator fora dos marcadores).

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
    public string Currency { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    // Construtor sem parâmetros só para o EF Core materializar. Privado.
    private Order() { }

    private Order(OrderId id, CustomerId customerId, string currency, DateTimeOffset createdAt) : base(id)
    {
        CustomerId = customerId;
        Currency = currency;
        Status = OrderStatus.Pending;
        CreatedAt = createdAt;
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

    public Result Cancel(DateTimeOffset agora)
    {
        // A regra de transição de estado vive na entidade, não no handler.
        if (Status is OrderStatus.Shipped or OrderStatus.Cancelled)
        {
            return Result.Failure(DomainErrors.Order.TransicaoInvalida(Status, OrderStatus.Cancelled));
        }

        OrderStatus anterior = Status;

        Status = OrderStatus.Cancelled;
        UpdatedAt = agora;

        // O evento carrega o estado anterior: quem reage precisa saber de onde veio.
        RaiseDomainEvent(new OrderCancelledEvent(Id, anterior, agora));

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
    // O marcador próprio já embute Result no retorno — ver ADR 0009.
    : ICommandHandler<PlaceOrderCommand, PlaceOrderResponse>
{
    public async ValueTask<Result<PlaceOrderResponse>> Handle(
        PlaceOrderCommand command, CancellationToken ct)
    {
        // 1. Carrega o que precisa pela abstração. Nenhum EF Core aqui.
        Customer? customer = await customers.GetByIdAsync(new CustomerId(command.CustomerId), ct);

        if (customer is null)
            return Result.Failure<PlaceOrderResponse>(DomainErrors.Customer.NaoEncontrado(command.CustomerId));

        // 2. Delega a decisão de negócio para o domínio. O handler orquestra, não decide.
        Result<Order> resultado = Order.Place(
            customer.Id,
            command.Items.Select(i =>
                (new ProductId(i.ProductId), i.Quantity, Money.Of(i.UnitPrice, command.Currency).Value)),
            clock.UtcNow);

        if (resultado.IsFailure)
            return Result.Failure<PlaceOrderResponse>(resultado.Error);

        // 3. Rastreia. Quem grava é o TransactionBehavior, quando o comando termina em sucesso — e o
        //    SaveChanges dispara os interceptors: auditoria, soft delete e o outbox de eventos.
        orders.Add(resultado.Value);

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

Behaviors incluídos, na ordem em que envolvem a mensagem: **logging** com correlation id, **validação**,
**transação** (só para commands, gravando quando o comando termina em sucesso) e **invalidação de cache** (para
commands que declaram quais chaves tornaram obsoletas).

> Não há behavior de *leitura* de cache, e é decisão registrada: o `Result` não atravessa serialização — o
> `System.Text.Json` exige que cada parâmetro do construtor case com uma propriedade do próprio tipo, e
> `IsSuccess` e `Error` vivem na classe base. O cache de consulta vive no **reader**, guardando o DTO, que é
> dado e serializa sem cerimônia.

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

O `DomainEventInterceptor` coleta os eventos das entidades **durante** o `SaveChanges`, **antes do commit**, e os
grava na tabela de outbox. Isso não é detalhe: como o `INSERT` do evento entra na mesma unidade de trabalho do
dado, ou os dois acontecem ou nenhum. Em `SavedChanges` seria tarde — a transação já teria fechado, e voltaria a
existir a janela em que o dado é gravado e o evento se perde.

Um worker separado relê a tabela e publica. É o que evita o clássico "salvou no banco mas o evento se perdeu" —
e o [ADR 0006](docs/adr/0006-outbox-para-eventos.md) detalha o resto: reserva concorrente com
`FOR UPDATE SKIP LOCKED`, retry com recuo, dead-letter e por que a publicação **não** passa pelo Mediator.

### Api

Endpoints finos com Carter, agrupados por módulo:

```csharp
public sealed class OrderModule : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        RouteGroupBuilder grupo = app
            .MapGroup("/api/v1/orders")
            .WithTags("Pedidos")

            // Exigido uma vez no grupo: endpoint novo nasce protegido, sem ninguém precisar lembrar.
            .RequireAuthorization();

        grupo.MapPost("/", CriarPedido)
            .WithName("CriarPedido")
            .Produces<PlaceOrderResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);
    }

    private static async Task<IResult> CriarPedido(
        [FromBody] CriarPedidoRequest request,
        ISender sender,
        ICorrelationIdProvider correlationId,
        CancellationToken cancellationToken)
    {
        PlaceOrderCommand comando = new(request.CustomerId, request.Currency, /* itens */ []);

        Result<PlaceOrderResponse> resultado = await sender.Send(comando, cancellationToken);

        // Result → HTTP acontece num lugar só. Nada de if/else de status espalhado por endpoint.
        return resultado.ParaCreated(
            localizacao: pedido => $"/api/v1/orders/{pedido.Id}",
            correlationId: correlationId.CorrelationId);
    }
}
```

> **O request é um tipo próprio da Api, não o command.** São contratos com ciclos de vida diferentes: renomear
> um campo do command é refatoração interna; renomear um campo do request é *breaking change* para quem consome.
> Juntá-los faz toda mudança interna virar risco externo.

---

## Feature de referência: Pedidos

O repositório vem com um domínio de pedidos implementado inteiro, não com um `WeatherForecast`. Ele existe para mostrar como as peças se encaixam num caso com alguma complexidade real:

- **Criar pedido** — validação de entrada, carregamento de agregado relacionado, invariantes no domínio, evento publicado via outbox.
- **Consultar pedido** — query com projeção direta para DTO e cache com invalidação por chave.
- **Listar pedidos** — paginação por keyset (não offset), filtro e ordenação.
- **Cancelar pedido** — regra de transição de estado que depende do estado atual, com erro tipado quando não permitido.

Cada uma cobre um padrão diferente. Juntas, dão o vocabulário para implementar qualquer coisa parecida.

| Endpoint | O que exercita |
|---|---|
| `POST /api/v1/orders` | Validação no pipeline, invariantes no agregado, evento no outbox |
| `GET /api/v1/orders` | Paginação por cursor, filtro por status e período |
| `GET /api/v1/orders/{id}` | Projeção direta para DTO, cache no reader |
| `POST /api/v1/orders/{id}/cancel` | Transição de estado, invalidação de cache, 204 sem corpo |
| `POST /api/v1/dev/token` | Emissor de exemplo — **só em `Development`** |
| `GET /health/live` · `GET /health/ready` | Abertos, por design — o orquestrador não se autentica |

Os quatro primeiros **exigem token**. `POST /{id}/cancel` e não `DELETE /{id}` porque cancelar não remove nada:
muda a situação de um registro que continua existindo, e pedido é histórico.

---

## Decisões técnicas

São **dez ADRs** em [`docs/adr/`](docs/adr/), no formato contexto → decisão → consequências. Eles respondem a
pergunta que o código não responde: **por que não do outro jeito?**

Nenhum deles lista só benefícios. Uma decisão sem custo declarado é propaganda, não registro — e quem vier
depois merece saber o preço antes de pagá-lo.

| # | Decisão | Em uma linha |
|---|---|---|
| [0001](docs/adr/0001-clean-architecture.md) | Clean Architecture | Dependências apontam para dentro, e teste de arquitetura verifica |
| [0002](docs/adr/0002-mediator-em-vez-de-mediatr.md) | Mediator em vez de MediatR | Licença é critério de bloqueio |
| [0003](docs/adr/0003-mapperly-em-vez-de-automapper.md) | Mapperly em vez de AutoMapper | Mapeamento errado vira erro de build |
| [0004](docs/adr/0004-result-em-vez-de-exception.md) | `Result` em vez de exception | Erro de negócio não é falha de sistema |
| [0005](docs/adr/0005-vertical-slices-na-application.md) | Vertical slices | Uma pasta por caso de uso, não por papel técnico |
| [0006](docs/adr/0006-outbox-para-eventos.md) | Outbox para eventos | Gravar e publicar são dois sistemas |
| [0007](docs/adr/0007-testcontainers-para-integracao.md) | Testcontainers | Banco de verdade; o InMemory aprova o que o Postgres reprova |
| [0008](docs/adr/0008-awesomeassertions-em-vez-de-fluentassertions.md) | AwesomeAssertions | Mesmo critério de licença, com o custo de ser um fork |
| [0009](docs/adr/0009-abstracoes-proprias-sobre-o-mediator.md) | Abstrações sobre o Mediator | Seis linhas para que migrar seja mudança de um arquivo |
| [0010](docs/adr/0010-identidade-tipada-e-value-objects.md) | Identidade tipada e value objects | Trocar argumento deixa de compilar |

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
// Teste de domínio: sem dublê, sem setup, sem banco. Só a regra.
[Fact]
public void Cancel_ComPedidoEnviado_Falha()
{
    Order pedido = PedidoNovo();
    pedido.Pay(Agora);
    pedido.Ship(Agora);

    Result resultado = pedido.Cancel(Agora);

    resultado.IsFailure.Should().BeTrue();
    resultado.Error.Code.Should().Be("Order.TransicaoInvalida");
    pedido.Status.Should().Be(OrderStatus.Shipped, "a falha não pode ter mudado o estado");
}
```

> O tempo entra por parâmetro (`Agora` é uma constante do teste), nunca `DateTime.UtcNow`. É o que torna o
> resultado determinístico — e é a razão de `IDateTimeProvider` existir na Application.

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
WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.Services.AddApplication();                      // Mediator, behaviors, validators
builder.Services.AddInfrastructure(builder.Configuration);  // DbContext, repos, cache, outbox, HTTP
builder.Services.AddApiServices(builder.Configuration);     // Carter, auth, rate limiting, OpenAPI, health

WebApplication app = builder.Build();

// --migrate e --seed descrevem uma tarefa, não um modo de servir: executam e encerram.
if (await StartupTasks.ExecutarAsync(app.Services, args)) return;

// O pipeline é montado aqui, com o porquê de cada posição escrito ao lado.
app.UseMiddleware<CorrelationIdMiddleware>();
// ...
```

> **`var` não aparece aí por acaso.** O `.editorconfig` exige tipo explícito quando ele não está aparente no
> lado direito, com severidade de **erro de build** — `var builder = WebApplication.CreateBuilder(args)` não
> compila neste repositório. A razão: o público-alvo lê o código sem IDE para passar o mouse em cima.

**A ordem de `AddApiServices` por último importa:** ela sobrescreve `ICurrentUser` e `ICorrelationIdProvider`
pelas implementações que leem o `HttpContext`. No contêiner da Microsoft, o último registro vence — e é assim
que a Infrastructure mantém padrões que funcionam fora de HTTP (worker, seed) sem conhecer a Api.

Tempo de vida: `Scoped` para tudo que toca a requisição (DbContext, repositórios, `ICurrentUser`), `Singleton`
para stateless puro (`IDateTimeProvider`). A configuração usa o padrão Options com validação no startup —
configuração errada derruba a aplicação na inicialização, não na primeira chamada em produção:

```csharp
services.AddOptions<DatabaseOptions>()
    .Bind(configuration.GetSection(DatabaseOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();
```

---

## Observabilidade

- **Serilog** em JSON, com o **correlation id** em toda linha e em toda resposta de erro. O middleware o aceita
  do cabeçalho `X-Correlation-Id` ou gera um — é o que liga "deu erro às 14h" à requisição exata.
- **OpenTelemetry** com exportador OTLP, instrumentando quatro coisas que juntas cobrem as perguntas das três da
  manhã: **ASP.NET Core** (quanto demorou a requisição), **Npgsql** (quanto disso foi o banco, e em qual
  comando), **HttpClient** (quanto foi esperando um serviço externo) e **runtime** (houve pausa de GC ou fome
  de thread pool).
- **Health checks** separados: `/health/live` diz "o processo está de pé" e não consulta dependência;
  `/health/ready` checa Postgres e Redis. Apontar os dois para o mesmo lugar transforma banco fora do ar em
  reinício em massa de pods.
- O compose sobe **Seq** (logs, em `localhost:5341`) e **Jaeger** (traces, em `localhost:16686`), e a API já
  envia para os dois — sem configurar nada.

> O rastreamento do banco vem do **Npgsql**, não de um instrumentador de EF Core: o pacote de EF Core só existe
> em beta, e quem executa o comando é o Npgsql de qualquer forma. O que se perde é a correspondência entre o
> LINQ escrito e o SQL gerado, que o log do EF já mostra.

---

## Segurança

**Os endpoints de pedido exigem autenticação.** Sem token, a resposta é 401 — inclusive na leitura.

- **JWT** com os quatro parâmetros de validação ligados explicitamente: issuer, audience, lifetime e assinatura.
  Cada um cobre um ataque diferente, e estão escritos no código mesmo sendo o padrão, porque num kit o leitor
  precisa ver que a decisão foi tomada e não herdada.
- **`ClockSkew` de 30 segundos.** O padrão da biblioteca é de **cinco minutos** — tempo em que um token expirado
  continua sendo aceito.
- **Autorização aplicada no grupo de rotas**, não endpoint a endpoint: quem acrescentar um endpoint amanhã o
  recebe protegido sem precisar lembrar.
- **Rate limiting** nativo, **particionado por usuário** (ou por IP, antes do login) — um cliente abusivo não
  consome a cota dos outros. Responde 429 com `Retry-After`.
- **Security headers** em toda resposta, inclusive nas de erro: `X-Content-Type-Options`, `X-Frame-Options`,
  `Referrer-Policy` e `Content-Security-Policy`.
- **Segredos fora do repositório**: User Secrets em desenvolvimento, variável de ambiente ou cofre em produção.
  A aplicação **recusa subir** com configuração ausente ou chave JWT curta demais.
- **Erro nunca vaza stack trace em produção**; o `ProblemDetails` (RFC 9457) carrega o correlation id.

> **A emissão de token é um exemplo.** `POST /api/v1/dev/token` existe **apenas em `Development`** — em produção
> a rota não é protegida, é **ausente**, que é a única garantia que não depende de configuração correta. Para
> ligar um provedor real (Entra ID, Keycloak, Auth0), troque `IssuerSigningKey` por `options.Authority` e apague
> o `JwtTokenService`: as chaves passam a vir do JWKS do provedor.

---

## Performance

- Source generators no lugar de reflection (Mediator, Mapperly, `System.Text.Json`).
- **Leitura separada da escrita:** o repositório devolve o agregado rastreado, porque quem escreve precisa das
  invariantes; a consulta projeta direto para DTO, sem rastreamento. É o que dá sentido prático ao CQRS aqui.
- Paginação por keyset nas listagens — não degrada com offset alto.
- `HybridCache` com L1 em memória e L2 no Redis. A invalidação é **por chave**, e quem a dispara é o comando que
  altera o dado: `CancelOrderCommand` declara a chave que torna obsoleta. Sem isso, o cliente veria "Pending"
  por até cinco minutos depois de cancelar — sem erro nenhum.
- Resiliência com Polly nas chamadas externas: timeout, retry com jitter, circuit breaker.
- Script k6 de smoke em `tests/load/` para você medir antes de afirmar.

---

## Como rodar

Pré-requisitos: **.NET 10 SDK** e **Docker** — este último também para rodar os testes, porque cerca de 37% da
suíte sobe containers de verdade.

```bash
git clone https://github.com/<seu-usuario>/CleanStart.git
cd CleanStart

# Sobe API, Postgres, Redis, Seq e Jaeger
docker compose up -d --build

# Aplica as migrations e popula dados de exemplo. Executa a tarefa e encerra — não sobe a API.
docker compose run --rm api dotnet CleanStart.Api.dll --migrate --seed
```

| Serviço | URL |
|---|---|
| API | http://localhost:8080 |
| Documentação interativa (Scalar) | http://localhost:8080/scalar/v1 |
| Seq (logs) | http://localhost:5341 |
| Jaeger (traces) | http://localhost:16686 |

**Os endpoints de pedido exigem token.** Em desenvolvimento há um emissor de exemplo:

```bash
# 1. Peça um token
TOKEN=$(curl -s -X POST http://localhost:8080/api/v1/dev/token \
  -H "Content-Type: application/json" -d '{"nome":"eu"}' \
  | grep -oE '"token":"[^"]+' | cut -d'"' -f4)

# 2. Use-o
curl -s http://localhost:8080/api/v1/orders -H "Authorization: Bearer $TOKEN"
```

Criar um pedido, cancelar, e o passo a passo completo estão em
**[docs/getting-started.md](docs/getting-started.md)** — inclusive o caminho alternativo, com a API rodando na
máquina e só as dependências em container.

Testes:

```bash
dotnet test                                                     # tudo (303 testes, 5 níveis)
dotnet test tests/CleanStart.ArchitectureTests/CleanStart.ArchitectureTests.csproj   # só as regras
```

> Os testes **não** dependem do `docker-compose`: eles sobem os próprios containers e os derrubam ao final. Um
> clone novo roda `dotnet test` sem subir nada antes. Se todos os testes de integração falharem, o Docker não
> está rodando — é a primeira coisa a verificar.

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
- [ ] Exemplo de integração com mensageria externa (MassTransit + RabbitMQ) — o ponto de extensão já existe e
      está nomeado: `IOutboxPublisher`, hoje com uma implementação que só registra no log
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