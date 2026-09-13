# Acrescentando uma funcionalidade

Este guia percorre a criação de um caso de uso completo, do domínio ao endpoint. O exemplo é real: o
**cancelamento de pedido**, que existe no repositório e pode ser lido inteiro enquanto se acompanha o texto.

A ordem importa. Ela vai **de dentro para fora** — domínio primeiro, endpoint por último — pela mesma razão que
as dependências apontam para dentro (ver [ADR 0001](adr/0001-clean-architecture.md)). Começar pelo endpoint
costuma levar a regra de negócio para o lugar errado.

---

## 1. A regra vive no domínio

Antes de qualquer camada de aplicação, pergunte: **o que o negócio decide aqui?** No cancelamento, três coisas:
pedido enviado não pode ser cancelado, cancelar duas vezes não é permitido, e o cancelamento levanta um evento.

Tudo isso é do agregado, não do caso de uso:

```csharp
// Domain/Orders/Order.cs
public Result Cancel(DateTimeOffset agora)
{
    if (Status is OrderStatus.Shipped or OrderStatus.Cancelled)
    {
        return Result.Failure(DomainErrors.Order.TransicaoInvalida(Status, OrderStatus.Cancelled));
    }

    OrderStatus anterior = Status;

    Status = OrderStatus.Cancelled;
    UpdatedAt = agora;

    RaiseDomainEvent(new OrderCancelledEvent(Id, anterior, agora));

    return Result.Success();
}
```

Repare em três coisas:

- **Erro de negócio retorna `Result`**, não lança ([ADR 0004](adr/0004-result-em-vez-de-exception.md)).
- **O tempo entra por parâmetro**, nunca `DateTimeOffset.UtcNow` — é o que torna o teste determinístico.
- **O evento é levantado aqui**, e o domínio não sabe que existe fila
  ([ADR 0006](adr/0006-outbox-para-eventos.md)).

**O teste desta regra é de domínio**, roda em milissegundos e não precisa de banco:

```csharp
[Fact]
public void Cancel_ComPedidoEnviado_Falha()
{
    Order pedido = PedidoEnviado();

    Result resultado = pedido.Cancel(Agora);

    resultado.IsFailure.Should().BeTrue();
    pedido.Status.Should().Be(OrderStatus.Shipped, "a falha não pode ter mudado o estado");
}
```

---

## 2. O caso de uso é uma pasta

Crie `Application/Orders/CancelOrder/`. Uma pasta por caso de uso, nunca pastas por papel técnico
([ADR 0005](adr/0005-vertical-slices-na-application.md)).

**O comando** é um `record` — dado em trânsito, imutável:

```csharp
public sealed record CancelOrderCommand(Guid OrderId) : ICommand, ICacheInvalidator
{
    public IReadOnlyList<string> ChavesInvalidadas => [GetOrderByIdQuery.ChaveDe(OrderId)];
}
```

`ICommand` sem parâmetro genérico significa "não devolve valor" — o endpoint responderá 204. E `ICacheInvalidator`
declara que este comando torna obsoleta uma leitura guardada: sem isso, quem cancelasse continuaria vendo
"Pending" por até cinco minutos.

**O handler orquestra e não decide:**

```csharp
internal sealed class CancelOrderHandler(
    IOrderRepository repositorio,
    IUnitOfWork unitOfWork,
    IDateTimeProvider clock) : ICommandHandler<CancelOrderCommand>
{
    public async ValueTask<Result> Handle(CancelOrderCommand command, CancellationToken cancellationToken)
    {
        Order? pedido = await repositorio.GetByIdAsync(new OrderId(command.OrderId), cancellationToken);

        if (pedido is null)
        {
            return Result.Failure(DomainErrors.Order.NaoEncontrado(command.OrderId));
        }

        Result resultado = pedido.Cancel(clock.UtcNow);

        return resultado.IsFailure ? resultado : Result.Success();
    }
}
```

Quatro linhas de orquestração: carrega, delega, repassa. **Se o handler estiver decidindo alguma coisa, a regra
está no lugar errado** — volte ao passo 1.

Note o que ele **não** faz: não chama `SaveChangesAsync`. Isso é do `TransactionBehavior`, que grava quando o
comando termina em sucesso.

> **Precisa de validação de formato?** Acrescente um `CancelOrderValidator : AbstractValidator<CancelOrderCommand>`
> na mesma pasta e registre-o em `AddApplication` — uma linha, explícita. Validação de *formato* (campo
> obrigatório, tamanho, faixa) é do validator; validação de *regra* é do domínio.

**O teste do handler usa dublês**, sem banco:

```csharp
[Fact]
public async Task ComPedidoInexistente_DevolveNaoEncontrado()
{
    IOrderRepository repositorio = Substitute.For<IOrderRepository>();
    repositorio.GetByIdAsync(Arg.Any<OrderId>(), Arg.Any<CancellationToken>()).Returns((Order?)null);

    Result resultado = await Criar(repositorio).Handle(new CancelOrderCommand(Guid.CreateVersion7()), default);

    resultado.Error.Code.Should().Be("Order.NaoEncontrado");
}
```

---

## 3. Precisa de algo da infraestrutura?

Se o caso de uso precisar de algo que a Application não pode fazer — falar com banco, chamar serviço externo,
ler cache — **declare a interface na Application e implemente na Infrastructure**. A dependência aponta para
dentro.

Para leitura, prefira uma interface própria do slice em vez de reusar o repositório:

```csharp
// Application/Orders/GetOrderById/IOrderReader.cs
internal interface IOrderReader
{
    Task<OrderResponse?> ObterAsync(Guid id, CancellationToken cancellationToken);
}
```

O repositório devolve o **agregado rastreado**, porque quem escreve precisa das invariantes; a leitura projeta
direto para o DTO, sem rastreamento. Uma interface de leitura compartilhada cresce até virar o repositório
genérico que o CQRS existe para evitar.

**Teste de infraestrutura roda contra PostgreSQL real** ([ADR 0007](adr/0007-testcontainers-para-integracao.md)).

> ⚠️ Dentro de expressão LINQ, **compare o value object inteiro**: `o.Id == id`, nunca `o.Id.Value == id`. O
> segundo compila e **não traduz** para SQL. Este projeto cometeu o erro três vezes.

---

## 4. O endpoint traduz, e só

```csharp
// Api/Modules/OrderModule.cs
grupo.MapPost("/{id:guid}/cancel", CancelarPedido)
    .WithName("CancelarPedido")
    .WithSummary("Cancela um pedido")
    .Produces(StatusCodes.Status204NoContent)
    .ProducesProblem(StatusCodes.Status404NotFound)
    .ProducesProblem(StatusCodes.Status409Conflict);

private static async Task<IResult> CancelarPedido(
    Guid id,
    ISender sender,
    ICorrelationIdProvider correlationId,
    CancellationToken cancellationToken)
{
    Result resultado = await sender.Send(new CancelOrderCommand(id), cancellationToken);

    return resultado.ParaNoContent(correlationId.CorrelationId);
}
```

O endpoint faz três coisas: converte a requisição em comando, envia, traduz o `Result` em HTTP. **Nenhuma regra
de negócio, nenhum `DbContext`, nenhum `if` de status** — a tradução do `ErrorType` para 400/404/409/500 acontece
num lugar só, em `ResultExtensions`.

O grupo `/api/v1/orders` já exige autenticação, então o endpoint novo nasce protegido.

**O teste funcional exercita a API inteira**, por HTTP, contra Postgres e Redis reais — caminho feliz e pelo
menos um erro:

```csharp
[Fact]
public async Task ComPedidoEnviado_Retorna409()
{
    Guid pedidoId = await SemearAsync(ct, OrderStatus.Shipped);
    using HttpClient client = factory.CreateClientAutenticado();

    HttpResponseMessage resposta = await client.PostAsync($"/api/v1/orders/{pedidoId}/cancel", null, ct);

    resposta.StatusCode.Should().Be(HttpStatusCode.Conflict);
}
```

---

## 5. Precisa reagir ao evento?

O evento levantado no passo 1 foi gravado no outbox, na mesma transação. Para reagir a ele, crie **outra pasta,
nomeada pela reação** — `Orders/NotifyOrderPlaced/`, nunca `EventHandlers/` — e ligue-a no `IOutboxPublisher`.

> **A entrega é at-least-once.** O mesmo evento pode chegar duas vezes, e quem reage precisa tolerar isso: o
> efeito deve acontecer uma vez, não a entrega. Veja o `OrderPlacedNotifier` e o teste que entrega o evento
> duas vezes exigindo um efeito só.

---

## Checklist

- [ ] A regra de negócio está na **entidade**, não no handler
- [ ] Erro de negócio retorna **`Result`**; exception só para falha de infraestrutura
- [ ] O caso de uso é **uma pasta** com tudo dentro
- [ ] Handler é `sealed`; comando/consulta é `record`
- [ ] Tempo vem de `IDateTimeProvider`; `CancellationToken` propagado em toda chamada
- [ ] Identidade tipada na assinatura, nunca `Guid` cru
- [ ] **Teste no nível certo**: domínio para regra, aplicação para handler, integração para persistência,
      funcional para endpoint
- [ ] `dotnet build` e `dotnet test` verdes

Se alguma regra de arquitetura reprovar, **não relaxe o teste** — quase sempre a resposta é mover o código.
