using CleanStart.Application.Orders.ListOrders;
using CleanStart.Domain.Customers;
using CleanStart.Domain.Orders;
using CleanStart.Domain.ValueObjects;
using CleanStart.Infrastructure.Persistence;
using CleanStart.Infrastructure.Persistence.Repositories;

namespace CleanStart.Infrastructure.IntegrationTests.Persistence;

/// <summary>
/// Paginação por keyset contra PostgreSQL real.
/// </summary>
/// <remarks>
/// <para>
/// É o único nível que prova a consulta: o keyset compara e ordena por identidade tipada, e expressão que o EF
/// não sabe traduzir **compila e estoura em runtime**. Estes testes pegaram exatamente isso — a primeira versão
/// usava <c>order.Id.Value</c> no <c>OrderBy</c> e não traduzia; o <c>OrderId</c> ganhou <c>IComparable</c> para
/// que a comparação acontecesse sobre o tipo inteiro.
/// </para>
/// <para>
/// Cada teste usa o seu próprio cliente e o seu próprio conjunto de pedidos, porque a base é compartilhada entre
/// os testes da classe.
/// </para>
/// </remarks>
public sealed class OrdersPageReaderTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>
{
    /// <summary>
    /// O mesmo instante que a fixture injeta no relógio.
    /// </summary>
    /// <remarks>
    /// <b>O `CreatedAt` gravado é o do interceptor de auditoria, não o que se passa ao `Order.Place`.</b> Usar
    /// uma data diferente aqui faz o filtro de período procurar numa janela onde não há nada — e as consultas
    /// voltam vazias sem erro nenhum. Custou uma rodada de testes vermelhos para perceber.
    /// </remarks>
    private static readonly DateTimeOffset Base = PostgresFixture.Agora;

    /// <summary>
    /// Cria um cliente e <paramref name="quantidade"/> pedidos, um por minuto, do mais antigo ao mais novo.
    /// </summary>
    private async Task<(CustomerId Cliente, List<Order> Pedidos)> SemearAsync(
        int quantidade,
        CancellationToken ct,
        OrderStatus status = OrderStatus.Pending)
    {
        Customer cliente = Customer.Register(
            "João da Silva",
            Email.Of($"joao.{Guid.CreateVersion7():N}@example.com").Value,
            Document.Of(DocumentoNovo()).Value,
            Base).Value;

        List<Order> pedidos = [];

        await using AppDbContext contexto = fixture.CriarContexto();
        contexto.Add(cliente);

        for (int i = 0; i < quantidade; i++)
        {
            Order pedido = Order.Place(
                cliente.Id,
                [(ProductId.New(), 1, Money.Of(10m, "BRL").Value)],
                Base.AddMinutes(i)).Value;

            if (status == OrderStatus.Paid)
            {
                pedido.Pay(Base.AddMinutes(i));
            }

            contexto.Add(pedido);
            pedidos.Add(pedido);
        }

        await contexto.SaveChangesAsync(ct);

        return (cliente.Id, pedidos);
    }

    private static OrdersFilter Periodo() => new(null, Base.AddDays(-1), Base.AddDays(1));

    [Fact]
    public async Task PrimeiraPagina_TrazOsMaisRecentesPrimeiro()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        (CustomerId cliente, List<Order> pedidos) = await SemearAsync(5, ct);

        await using AppDbContext leitura = fixture.CriarContexto();

        IReadOnlyList<OrderSummary> pagina = await new OrdersPageReader(leitura)
            .ListarAsync(Periodo(), cursor: null, tamanho: 3, ct);

        List<OrderSummary> doCliente = [.. pagina.Where(p => p.CustomerId == cliente.Value)];

        doCliente.Should().NotBeEmpty();

        // Ordem decrescente por data: o mais recente primeiro.
        doCliente.Should().BeInDescendingOrder(p => p.CreatedAt);
    }

    [Fact]
    public async Task NavegacaoEntrePaginas_NaoRepeteNemPerdeItem()
    {
        // É a propriedade que o keyset existe para garantir, e a que o offset quebra quando há inserção
        // concorrente. Aqui a base é estável, então o teste verifica o caso fundamental: percorrer todas as
        // páginas visita cada item exatamente uma vez.
        CancellationToken ct = TestContext.Current.CancellationToken;
        (CustomerId cliente, List<Order> pedidos) = await SemearAsync(7, ct);

        await using AppDbContext leitura = fixture.CriarContexto();
        OrdersPageReader reader = new(leitura);

        HashSet<Guid> vistos = [];
        OrdersCursor? cursor = null;
        const int Tamanho = 3;

        for (int pagina = 0; pagina < 10; pagina++)
        {
            IReadOnlyList<OrderSummary> itens = await reader.ListarAsync(Periodo(), cursor, Tamanho + 1, ct);

            List<OrderSummary> daPagina = [.. itens.Take(Tamanho)];

            if (daPagina.Count == 0)
            {
                break;
            }

            foreach (OrderSummary item in daPagina.Where(i => i.CustomerId == cliente.Value))
            {
                vistos.Add(item.Id).Should().BeTrue($"o pedido {item.Id} não pode aparecer duas vezes");
            }

            if (itens.Count <= Tamanho)
            {
                break;
            }

            cursor = new OrdersCursor(daPagina[^1].CreatedAt, daPagina[^1].Id);
        }

        vistos.Should().HaveCount(7, "todos os pedidos semeados devem ter sido visitados");
    }

    [Fact]
    public async Task FiltroPorStatus_TrazSoOsCorrespondentes()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        (CustomerId cliente, _) = await SemearAsync(3, ct, OrderStatus.Paid);

        await using AppDbContext leitura = fixture.CriarContexto();

        IReadOnlyList<OrderSummary> pagina = await new OrdersPageReader(leitura)
            .ListarAsync(
                new OrdersFilter(OrderStatus.Paid, Base.AddDays(-1), Base.AddDays(1)),
                cursor: null,
                tamanho: 50,
                ct);

        pagina.Should().NotBeEmpty();
        pagina.Should().AllSatisfy(p => p.Status.Should().Be("Paid"));
        pagina.Where(p => p.CustomerId == cliente.Value).Should().HaveCount(3);
    }

    [Fact]
    public async Task FiltroPorPeriodo_ExcluiOQueEstaForA()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        (CustomerId cliente, _) = await SemearAsync(3, ct);

        await using AppDbContext leitura = fixture.CriarContexto();

        // Janela que termina antes do primeiro pedido.
        IReadOnlyList<OrderSummary> pagina = await new OrdersPageReader(leitura)
            .ListarAsync(
                new OrdersFilter(null, Base.AddDays(-10), Base.AddDays(-5)),
                cursor: null,
                tamanho: 50,
                ct);

        pagina.Where(p => p.CustomerId == cliente.Value).Should().BeEmpty();
    }

    [Fact]
    public async Task OTotalVemSomadoDoBanco()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;

        Customer cliente = Customer.Register(
            "Maria Souza",
            Email.Of($"maria.{Guid.CreateVersion7():N}@example.com").Value,
            Document.Of(DocumentoNovo()).Value,
            Base).Value;

        Order pedido = Order.Place(
            cliente.Id,
            [
                (ProductId.New(), 2, Money.Of(10.50m, "BRL").Value),
                (ProductId.New(), 3, Money.Of(5.00m, "BRL").Value),
            ],
            Base).Value;

        await using (AppDbContext escrita = fixture.CriarContexto())
        {
            escrita.Add(cliente);
            escrita.Add(pedido);
            await escrita.SaveChangesAsync(ct);
        }

        await using AppDbContext leitura = fixture.CriarContexto();

        IReadOnlyList<OrderSummary> pagina = await new OrdersPageReader(leitura)
            .ListarAsync(Periodo(), cursor: null, tamanho: 50, ct);

        OrderSummary encontrado = pagina.Single(p => p.Id == pedido.Id.Value);

        encontrado.Total.Should().Be(36.00m, "2×10,50 + 3×5,00, somado no SQL");
    }

    [Fact]
    public async Task NaoRastreiaAsEntidades()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await SemearAsync(2, ct);

        await using AppDbContext leitura = fixture.CriarContexto();

        _ = await new OrdersPageReader(leitura).ListarAsync(Periodo(), cursor: null, tamanho: 10, ct);

        leitura.ChangeTracker.Entries().Should().BeEmpty();
    }

    /// <summary>Gera um CPF válido e distinto — ver o motivo em <c>PersistenciaTests</c>.</summary>
    private static string DocumentoNovo()
    {
        int[] digitos = new int[11];

        for (int i = 0; i < 9; i++)
        {
            digitos[i] = Random.Shared.Next(0, 10);
        }

        digitos[9] = CalcularDigito(digitos, 9, 10);
        digitos[10] = CalcularDigito(digitos, 10, 11);

        return string.Concat(digitos);
    }

    private static int CalcularDigito(int[] digitos, int quantidade, int pesoInicial)
    {
        int soma = 0;

        for (int i = 0; i < quantidade; i++)
        {
            soma += digitos[i] * (pesoInicial - i);
        }

        int resto = soma % 11;

        return resto < 2 ? 0 : 11 - resto;
    }
}
