using CleanStart.Application.Common.Abstractions;
using CleanStart.Application.Orders.GetOrderById;
using CleanStart.Domain.Customers;
using CleanStart.Domain.Orders;
using CleanStart.Domain.ValueObjects;
using CleanStart.Infrastructure.Persistence;
using CleanStart.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace CleanStart.Infrastructure.IntegrationTests.Persistence;

/// <summary>
/// Projeção do <see cref="OrderReader"/> contra PostgreSQL real.
/// </summary>
/// <remarks>
/// É aqui que a projeção se prova: a consulta usa conversores de valor dentro de um <c>Select</c>, e uma
/// expressão que o EF não sabe traduzir **compila e estoura em runtime**. Sem banco de verdade, o teste passaria
/// e o endpoint quebraria na primeira chamada.
/// </remarks>
public sealed class OrderReaderTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>
{
    /// <summary>
    /// Cache que sempre executa a consulta.
    /// </summary>
    /// <remarks>
    /// Estes testes exercitam a <b>projeção</b>: se o cache guardasse, o segundo teste leria o resultado do
    /// primeiro e a consulta nunca seria verificada contra o banco. O comportamento do cache em si está coberto
    /// nos testes de unidade.
    /// </remarks>
    private static ICacheService CacheQuePassaDireto()
    {
        ICacheService cache = Substitute.For<ICacheService>();

        cache.GetOrCreateAsync(
                Arg.Any<string>(),
                Arg.Any<Func<CancellationToken, Task<OrderResponse?>>>(),
                Arg.Any<TimeSpan?>(),
                Arg.Any<CancellationToken>())
            .Returns(chamada =>
                chamada.ArgAt<Func<CancellationToken, Task<OrderResponse?>>>(1)(CancellationToken.None));

        return cache;
    }

    private async Task<Order> SemearPedidoAsync(string documento, CancellationToken ct)
    {
        Customer cliente = Customer.Register(
            "João da Silva",
            Email.Of($"joao.{Guid.CreateVersion7():N}@example.com").Value,
            Document.Of(documento).Value,
            PostgresFixture.Agora).Value;

        Order pedido = Order.Place(
            cliente.Id,
            [
                (ProductId.New(), 2, Money.Of(10.50m, "BRL").Value),
                (ProductId.New(), 3, Money.Of(5.00m, "BRL").Value),
            ],
            PostgresFixture.Agora).Value;

        await using AppDbContext escrita = fixture.CriarContexto();
        escrita.Add(cliente);
        escrita.Add(pedido);
        await escrita.SaveChangesAsync(ct);

        return pedido;
    }

    [Fact]
    public async Task ComPedidoExistente_ProjetaTodosOsCampos()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        Order pedido = await SemearPedidoAsync("529.982.247-25", ct);

        await using AppDbContext leitura = fixture.CriarContexto();

        OrderResponse? resposta = await new OrderReader(leitura, CacheQuePassaDireto()).GetByIdAsync(pedido.Id.Value, ct);

        resposta.Should().NotBeNull();
        resposta!.Id.Should().Be(pedido.Id.Value);
        resposta.CustomerId.Should().Be(pedido.CustomerId.Value);
        resposta.Status.Should().Be("Pending");
        resposta.Currency.Should().Be("BRL");

        // O total é somado no banco, não em memória: 2×10,50 + 3×5,00.
        resposta.Total.Should().Be(36.00m);
        resposta.CreatedAt.Should().Be(PostgresFixture.Agora);
        resposta.UpdatedAt.Should().BeNull();
    }

    [Fact]
    public async Task ComPedidoExistente_ProjetaOsItensComTotalPorLinha()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        Order pedido = await SemearPedidoAsync("111.444.777-35", ct);

        await using AppDbContext leitura = fixture.CriarContexto();

        OrderResponse resposta = (await new OrderReader(leitura, CacheQuePassaDireto()).GetByIdAsync(pedido.Id.Value, ct))!;

        resposta.Items.Should().HaveCount(2);

        // O Money vem das duas colunas do owned type e o total de cada linha é calculado no SQL.
        resposta.Items.Should().Contain(item => item.Quantity == 2 && item.UnitPrice == 10.50m && item.Total == 21.00m);
        resposta.Items.Should().Contain(item => item.Quantity == 3 && item.UnitPrice == 5.00m && item.Total == 15.00m);
    }

    [Fact]
    public async Task ComPedidoInexistente_DevolveNull()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;

        await using AppDbContext leitura = fixture.CriarContexto();

        OrderResponse? resposta = await new OrderReader(leitura, CacheQuePassaDireto()).GetByIdAsync(Guid.CreateVersion7(), ct);

        // Null, não exception: "não existe" é resposta legítima de uma consulta, e quem traduz isso em NotFound
        // é o handler.
        resposta.Should().BeNull();
    }

    [Fact]
    public async Task NaoRastreiaAEntidade()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        Order pedido = await SemearPedidoAsync("222.333.444-05", ct);

        await using AppDbContext leitura = fixture.CriarContexto();

        _ = await new OrderReader(leitura, CacheQuePassaDireto()).GetByIdAsync(pedido.Id.Value, ct);

        // AsNoTracking + projeção: o change tracker fica vazio. Rastrear numa consulta custa snapshot de cada
        // entidade materializada, sem que ninguém vá alterá-las.
        leitura.ChangeTracker.Entries().Should().BeEmpty();
    }

    [Fact]
    public async Task PedidoDeClienteExcluido_ContinuaLegivel()
    {
        // Decisão registrada: Order não implementa ISoftDeletable, então o filtro global não o alcança. Pedido é
        // registro histórico — escondê-lo junto com o cliente deixaria relatório e contabilidade apontando para
        // o vazio.
        CancellationToken ct = TestContext.Current.CancellationToken;
        Order pedido = await SemearPedidoAsync("444.555.666-19", ct);

        await using (AppDbContext exclusao = fixture.CriarContexto())
        {
            Customer cliente = await exclusao.Customers.FirstAsync(c => c.Id == pedido.CustomerId, ct);
            exclusao.Remove(cliente);
            await exclusao.SaveChangesAsync(ct);
        }

        await using AppDbContext leitura = fixture.CriarContexto();

        OrderResponse? resposta = await new OrderReader(leitura, CacheQuePassaDireto()).GetByIdAsync(pedido.Id.Value, ct);

        resposta.Should().NotBeNull("o pedido sobrevive à exclusão lógica do cliente");
    }
}
