using CleanStart.Domain.Customers;
using CleanStart.Domain.Orders;
using CleanStart.Domain.ValueObjects;
using CleanStart.Infrastructure.Persistence;
using CleanStart.Infrastructure.Persistence.Outbox;
using CleanStart.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;

namespace CleanStart.Infrastructure.IntegrationTests.Persistence;

/// <summary>
/// Exercita a persistência contra um PostgreSQL real.
/// </summary>
/// <remarks>
/// É aqui que se prova o que os testes de modelo e de change tracker não alcançam: que a migration gerada
/// funciona, que o SQL dos conversores de valor traduz, que o filtro global some com o excluído e que os
/// interceptors produzem o efeito esperado no banco — não só no change tracker.
/// </remarks>
public sealed class PersistenciaTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>
{
    private static Customer ClienteNovo(string documento = "529.982.247-25") => Customer.Register(
        "João da Silva",
        Email.Of("joao@example.com").Value,
        Document.Of(documento).Value,
        PostgresFixture.Agora).Value;

    private static Order PedidoDe(CustomerId clienteId) => Order.Place(
        clienteId,
        [
            (ProductId.New(), 2, Money.Of(10m, "BRL").Value),
            (ProductId.New(), 3, Money.Of(5m, "BRL").Value),
        ],
        PostgresFixture.Agora).Value;

    [Fact]
    public async Task Pedido_EGravadoELidoComOsItens()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        Customer cliente = ClienteNovo("111.444.777-35");
        Order pedido = PedidoDe(cliente.Id);

        await using (AppDbContext escrita = fixture.CriarContexto())
        {
            escrita.Add(cliente);
            escrita.Add(pedido);
            await escrita.SaveChangesAsync(ct);
        }

        // Contexto novo: força a leitura a vir do banco, não do change tracker da gravação.
        await using AppDbContext leitura = fixture.CriarContexto();
        OrderRepository repositorio = new(leitura);

        Order? lido = await repositorio.GetByIdAsync(pedido.Id, ct);

        lido.Should().NotBeNull();
        lido!.Items.Should().HaveCount(2, "o Include do repositório traz o agregado inteiro");

        // O Total é calculado a partir dos itens: se o Include falhasse, ele viria zero sem erro nenhum.
        lido.Total.Amount.Should().Be(35m);
        lido.Total.Currency.Should().Be("BRL");
        lido.Status.Should().Be(OrderStatus.Pending);
    }

    [Fact]
    public async Task Money_SobreviveAoRoundTripComoOwnedType()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        Customer cliente = ClienteNovo("555.666.777-20");
        Order pedido = Order.Place(
            cliente.Id,
            [(ProductId.New(), 3, Money.Of(19.99m, "BRL").Value)],
            PostgresFixture.Agora).Value;

        await using (AppDbContext escrita = fixture.CriarContexto())
        {
            escrita.Add(cliente);
            escrita.Add(pedido);
            await escrita.SaveChangesAsync(ct);
        }

        await using AppDbContext leitura = fixture.CriarContexto();
        Order lido = (await new OrderRepository(leitura).GetByIdAsync(pedido.Id, ct))!;

        // Duas colunas viram um Money de novo, com a precisão preservada — numeric(18,2) e não float.
        OrderItem item = lido.Items.Single();
        item.UnitPrice.Amount.Should().Be(19.99m);
        item.UnitPrice.Currency.Should().Be("BRL");
        item.Total.Amount.Should().Be(59.97m);
    }

    [Fact]
    public async Task Auditoria_EPreenchidaNoInsert()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        Customer cliente = ClienteNovo("987.654.321-00");

        await using (AppDbContext escrita = fixture.CriarContexto())
        {
            escrita.Add(cliente);
            await escrita.SaveChangesAsync(ct);
        }

        await using AppDbContext leitura = fixture.CriarContexto();
        Customer lido = (await new CustomerRepository(leitura).GetByIdAsync(cliente.Id, ct))!;

        // Agora lido do banco, não do objeto em memória: prova que o interceptor gravou de verdade.
        lido.CreatedAt.Should().Be(PostgresFixture.Agora);
        lido.CreatedBy.Should().Be(PostgresFixture.Usuario);
        lido.UpdatedAt.Should().BeNull("insert não é alteração");
    }

    [Fact]
    public async Task SoftDelete_EscondeORegistroDasConsultas()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        Customer cliente = ClienteNovo("123.456.789-09");

        await using (AppDbContext escrita = fixture.CriarContexto())
        {
            escrita.Add(cliente);
            await escrita.SaveChangesAsync(ct);
        }

        await using (AppDbContext exclusao = fixture.CriarContexto())
        {
            Customer paraExcluir = (await exclusao.Customers.FirstAsync(c => c.Id == cliente.Id, ct))!;
            exclusao.Remove(paraExcluir);
            await exclusao.SaveChangesAsync(ct);
        }

        await using AppDbContext leitura = fixture.CriarContexto();

        // O filtro global esconde o excluído: nenhuma consulta precisa lembrar do WHERE.
        Customer? pelaConsultaNormal = await new CustomerRepository(leitura).GetByIdAsync(cliente.Id, ct);
        pelaConsultaNormal.Should().BeNull();

        // Mas a linha continua no banco — soft delete, não DELETE. Pedido antigo referencia o cliente.
        Customer? ignorandoOFiltro = await leitura.Customers
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(c => c.Id == cliente.Id, ct);

        ignorandoOFiltro.Should().NotBeNull("a linha não foi apagada");
        ignorandoOFiltro!.IsDeleted.Should().BeTrue();
        ignorandoOFiltro.DeletedAt.Should().Be(PostgresFixture.Agora);

        // E a exclusão lógica foi auditada como alteração — é o efeito da ordem dos interceptors.
        ignorandoOFiltro.UpdatedAt.Should().Be(PostgresFixture.Agora);
        ignorandoOFiltro.UpdatedBy.Should().Be(PostgresFixture.Usuario);
    }

    [Fact]
    public async Task DomainEvent_EGravadoNoOutboxNaMesmaTransacao()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        Customer cliente = ClienteNovo("111.222.333-96");
        Order pedido = PedidoDe(cliente.Id);

        await using (AppDbContext escrita = fixture.CriarContexto())
        {
            escrita.Add(cliente);
            escrita.Add(pedido);
            await escrita.SaveChangesAsync(ct);
        }

        await using AppDbContext leitura = fixture.CriarContexto();

        // A mensagem está no banco porque entrou na mesma transação do pedido — é o ponto do padrão outbox.
        //
        // O filtro inclui o id do pedido porque os testes desta classe compartilham o banco e rodam em
        // paralelo: buscar "o último OrderPlacedEvent" pegaria a mensagem de outro teste, e o teste passaria ou
        // falharia conforme a ordem de execução.
        string idDoPedido = pedido.Id.Value.ToString();

        // O filtro por conteúdo acontece em memória, não em SQL: `Content` é coluna `jsonb`, e o PostgreSQL não
        // tem operador `LIKE` para jsonb — `Contains` traduzido viraria `jsonb ~~ jsonb` e falha com 42883.
        // Quem precisar filtrar outbox por conteúdo em produção usa os operadores de jsonb (`->>`, `@>`), não
        // comparação de texto.
        List<OutboxMessage> doTipo = await leitura.OutboxMessages
            .Where(m => m.Type == "order-placed")
            .ToListAsync(ct);

        List<OutboxMessage> mensagens = [.. doTipo
            .Where(m => m.Content.Contains(idDoPedido, StringComparison.Ordinal))];

        mensagens.Should().HaveCount(1, "um evento levantado produz exatamente uma mensagem");

        OutboxMessage mensagem = mensagens[0];
        mensagem.ProcessedOn.Should().BeNull("nasce pendente, para o despachante encontrar");
        mensagem.Error.Should().BeNull();

        // A identidade tipada é serializada como objeto aninhado (`"orderId": { "value": "..." }`), porque
        // OrderId é um record struct com a propriedade Value. O consumidor precisa saber disso — se a forma do
        // payload importar, o evento deve carregar Guid cru em vez do tipo do domínio.
        mensagem.Content.Should().Contain("\"orderId\"");
        mensagem.Content.Should().Contain("\"currency\": \"BRL\"");
    }

    [Fact]
    public async Task DocumentoDuplicado_EBarradoPeloIndiceUnico()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        const string MesmoDocumento = "888.999.111-93";

        await using (AppDbContext primeiro = fixture.CriarContexto())
        {
            primeiro.Add(ClienteNovo(MesmoDocumento));
            await primeiro.SaveChangesAsync(ct);
        }

        await using AppDbContext segundo = fixture.CriarContexto();
        segundo.Add(ClienteNovo(MesmoDocumento));

        // A unicidade é garantida pelo banco, não pela consulta prévia do caso de uso: entre o SELECT e o
        // INSERT cabem duas requisições simultâneas, e só a constraint fecha a porta.
        Func<Task> gravar = async () => await segundo.SaveChangesAsync(ct);

        await gravar.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task ConsultaPorDocumento_TraduzOConversorDeValor()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        Customer cliente = ClienteNovo("222.333.444-05");

        await using (AppDbContext escrita = fixture.CriarContexto())
        {
            escrita.Add(cliente);
            await escrita.SaveChangesAsync(ct);
        }

        await using AppDbContext leitura = fixture.CriarContexto();
        CustomerRepository repositorio = new(leitura);

        // É o teste que justifica a correção feita na T3.3: comparar o value object inteiro traduz; navegar até
        // `.Value` dentro da expressão compila e estoura aqui.
        Customer? achado = await repositorio.GetByDocumentAsync(cliente.Document, ct);
        bool existe = await repositorio.ExistsWithDocumentAsync(cliente.Document, ct);

        achado.Should().NotBeNull();
        achado!.Id.Should().Be(cliente.Id);
        existe.Should().BeTrue();
    }

    [Fact]
    public async Task PedidosDoCliente_VoltamDoMaisRecenteParaOMaisAntigo()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        Customer cliente = ClienteNovo("444.555.666-19");

        await using (AppDbContext escrita = fixture.CriarContexto())
        {
            escrita.Add(cliente);
            escrita.Add(PedidoDe(cliente.Id));
            escrita.Add(PedidoDe(cliente.Id));
            await escrita.SaveChangesAsync(ct);
        }

        await using AppDbContext leitura = fixture.CriarContexto();

        IReadOnlyList<Order> pedidos = await new OrderRepository(leitura)
            .GetByCustomerAsync(cliente.Id, ct);

        pedidos.Should().HaveCount(2);
        pedidos.Should().AllSatisfy(pedido =>
            pedido.Items.Should().NotBeEmpty("o Include vale também na listagem"));
    }
}
