using CleanStart.Application.Common.Abstractions;
using CleanStart.Domain.Customers;
using CleanStart.Domain.Orders;
using CleanStart.Domain.ValueObjects;
using CleanStart.Infrastructure.Persistence;
using CleanStart.Infrastructure.Persistence.Interceptors;
using CleanStart.Infrastructure.Persistence.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using NSubstitute;

namespace CleanStart.Infrastructure.IntegrationTests.Persistence;

/// <summary>
/// Comportamento dos interceptors de auditoria, soft delete e outbox.
/// </summary>
/// <remarks>
/// <para>
/// <b>Sem banco e sem Docker.</b> Os interceptors agem sobre o change tracker em <c>SavingChanges</c>, antes de
/// qualquer SQL — então chamá-los diretamente com um contexto não conectado exercita toda a lógica deles. O que
/// falta aqui é a prova de que o SQL gerado funciona, e isso é a T3.4 com Testcontainers.
/// </para>
/// <para>
/// A alternativa seria o provider InMemory, que o <c>CLAUDE.md</c> proíbe — e com razão: ele não tem constraint
/// nem transação, então passaria em teste que o PostgreSQL reprovaria.
/// </para>
/// </remarks>
public sealed class InterceptorsTests
{
    private static readonly DateTimeOffset Agora = new(2026, 9, 12, 10, 30, 0, TimeSpan.Zero);
    private static readonly Guid UsuarioLogado = Guid.CreateVersion7();

    private readonly IDateTimeProvider _clock = Substitute.For<IDateTimeProvider>();
    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();

    public InterceptorsTests()
    {
        _clock.UtcNow.Returns(Agora);
        _currentUser.Id.Returns(UsuarioLogado);
    }

    /// <summary>
    /// Cria um contexto com os três interceptors registrados, sem conexão real.
    /// </summary>
    private AppDbContext CriarContexto()
    {
        DbContextOptions<AppDbContext> options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost;Database=cleanstart;Username=postgres;Password=naoconecta")
            .AddInterceptors(
                new SoftDeleteInterceptor(_clock),
                new AuditableInterceptor(_clock, _currentUser),
                new DomainEventInterceptor())
            .Options;

        return new AppDbContext(options);
    }

    /// <summary>
    /// Dispara os interceptors sem tocar o banco.
    /// </summary>
    /// <remarks>
    /// Os interceptors rodam em <c>SavingChanges</c>, que o EF chama antes de abrir conexão. Invocá-los assim é o
    /// que permite testar a lógica deles sem container.
    /// </remarks>
    private static void DispararInterceptors(AppDbContext contexto, params IInterceptor[] interceptors)
    {
        DbContextEventData eventData = new(null!, null!, contexto);

        foreach (SaveChangesInterceptor interceptor in interceptors.OfType<SaveChangesInterceptor>())
        {
            interceptor.SavingChanges(eventData, default);
        }
    }

    private static Customer ClienteNovo() => Customer.Register(
        "João da Silva",
        Email.Of("joao@example.com").Value,
        Document.Of("529.982.247-25").Value,
        Agora).Value;

    [Fact]
    public void Auditoria_NoInsert_PreencheCreatedAtECreatedBy()
    {
        using AppDbContext contexto = CriarContexto();
        Customer cliente = ClienteNovo();
        contexto.Add(cliente);

        DispararInterceptors(contexto, new AuditableInterceptor(_clock, _currentUser));

        cliente.CreatedAt.Should().Be(Agora);
        cliente.CreatedBy.Should().Be(UsuarioLogado);
        cliente.UpdatedAt.Should().BeNull("insert não é alteração");
    }

    [Fact]
    public void Auditoria_NoUpdate_PreencheUpdatedAtENaoMexeEmCreated()
    {
        using AppDbContext contexto = CriarContexto();
        Customer cliente = ClienteNovo();
        contexto.Attach(cliente);
        contexto.Entry(cliente).State = EntityState.Modified;

        DispararInterceptors(contexto, new AuditableInterceptor(_clock, _currentUser));

        cliente.UpdatedAt.Should().Be(Agora);
        cliente.UpdatedBy.Should().Be(UsuarioLogado);

        // CreatedAt e CreatedBy ficam marcados como não modificados: sem isso, uma entidade recarregada e salva
        // sobrescreveria a autoria original.
        contexto.Entry(cliente).Property(nameof(Customer.CreatedAt)).IsModified.Should().BeFalse();
        contexto.Entry(cliente).Property(nameof(Customer.CreatedBy)).IsModified.Should().BeFalse();
    }

    [Fact]
    public void Auditoria_SemUsuarioLogado_GravaNulo()
    {
        // Job, seed e migração criam registro sem usuário. Nulo é estado legítimo — um Guid.Empty se pareceria
        // com um id de verdade.
        _currentUser.Id.Returns((Guid?)null);

        using AppDbContext contexto = CriarContexto();
        Customer cliente = ClienteNovo();
        contexto.Add(cliente);

        DispararInterceptors(contexto, new AuditableInterceptor(_clock, _currentUser));

        cliente.CreatedAt.Should().Be(Agora);
        cliente.CreatedBy.Should().BeNull();
    }

    [Fact]
    public void SoftDelete_ConverteDeleteEmUpdate()
    {
        using AppDbContext contexto = CriarContexto();
        Customer cliente = ClienteNovo();
        contexto.Attach(cliente);
        contexto.Remove(cliente);

        contexto.Entry(cliente).State.Should().Be(EntityState.Deleted, "estado antes do interceptor");

        DispararInterceptors(contexto, new SoftDeleteInterceptor(_clock));

        // A linha não é apagada: pedido antigo referencia o cliente, e apagá-la deixaria o histórico apontando
        // para o vazio.
        contexto.Entry(cliente).State.Should().Be(EntityState.Modified);
        cliente.IsDeleted.Should().BeTrue();
        cliente.DeletedAt.Should().Be(Agora);
    }

    [Fact]
    public void SoftDelete_NaoTocaEntidadeQueNaoEhExcluivel()
    {
        using AppDbContext contexto = CriarContexto();
        Order pedido = PedidoNovo();
        contexto.Attach(pedido);
        contexto.Remove(pedido);

        DispararInterceptors(contexto, new SoftDeleteInterceptor(_clock));

        // Order não implementa ISoftDeletable — continua sendo exclusão física. Confirma que o interceptor não
        // age indiscriminadamente.
        contexto.Entry(pedido).State.Should().Be(EntityState.Deleted);
    }

    [Fact]
    public void Outbox_GravaUmaMensagemPorEventoELimpaOAgregado()
    {
        using AppDbContext contexto = CriarContexto();
        Order pedido = PedidoNovo();

        pedido.DomainEvents.Should().HaveCount(1, "Place levanta OrderPlacedEvent");
        contexto.Add(pedido);

        DispararInterceptors(contexto, new DomainEventInterceptor());

        List<OutboxMessage> mensagens = [.. contexto.ChangeTracker
            .Entries<OutboxMessage>()
            .Select(entrada => entrada.Entity)];

        mensagens.Should().HaveCount(1);
        mensagens[0].Type.Should().Be("CleanStart.Domain.Orders.Events.OrderPlacedEvent");
        mensagens[0].Content.Should().Contain("orderId", "serializado em camelCase para o consumidor");
        mensagens[0].ProcessedOn.Should().BeNull("nasce pendente");

        // Limpar evita que um segundo SaveChanges na mesma instância republique o mesmo evento.
        pedido.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void Outbox_SemEventos_NaoGravaNada()
    {
        using AppDbContext contexto = CriarContexto();
        Customer cliente = ClienteNovo();
        contexto.Add(cliente);

        DispararInterceptors(contexto, new DomainEventInterceptor());

        contexto.ChangeTracker.Entries<OutboxMessage>().Should().BeEmpty();
    }

    [Fact]
    public void Ordem_SoftDeleteAntesDeAuditoria_FazAExclusaoSerAuditada()
    {
        // É a razão de a ordem dos interceptors estar fixada em InterceptorRegistration: o soft delete converte
        // Deleted em Modified, e só então a auditoria reconhece a operação como alteração. Na ordem inversa, a
        // exclusão lógica ficaria sem registro de quem e quando.
        using AppDbContext contexto = CriarContexto();
        Customer cliente = ClienteNovo();
        contexto.Attach(cliente);
        contexto.Remove(cliente);

        DispararInterceptors(
            contexto,
            new SoftDeleteInterceptor(_clock),
            new AuditableInterceptor(_clock, _currentUser));

        cliente.IsDeleted.Should().BeTrue();
        cliente.UpdatedAt.Should().Be(Agora, "a exclusão lógica é uma alteração e precisa ser auditada");
        cliente.UpdatedBy.Should().Be(UsuarioLogado);
    }

    private static Order PedidoNovo() => Order.Place(
        CustomerId.New(),
        [(ProductId.New(), 2, Money.Of(10m, "BRL").Value)],
        Agora).Value;
}
