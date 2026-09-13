using CleanStart.Application.Common.Abstractions;
using CleanStart.Domain.Orders;
using CleanStart.Infrastructure.Persistence;
using CleanStart.Infrastructure.Persistence.Seed;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace CleanStart.Infrastructure.IntegrationTests.Persistence;

/// <summary>
/// Exercita o seed contra um PostgreSQL real.
/// </summary>
/// <remarks>
/// <para>
/// <b>Precisa do banco de verdade</b> justamente pelo que o seed pode quebrar: os índices únicos de documento e
/// de e-mail. Um gerador que repita valores passa em memória e falha ao gravar — e falharia pela primeira vez na
/// máquina de quem clonou o repositório.
/// </para>
/// <para>
/// Esta classe tem fixture própria (<c>IAsyncLifetime</c>) e não compartilha a base com as demais: o seed só
/// age em banco vazio, e as outras classes deixam clientes e pedidos gravados. Compartilhar faria o teste
/// principal encontrar dados alheios e ignorar o trabalho — passando sem verificar nada.
/// </para>
/// </remarks>
public sealed class DatabaseSeederTests : IAsyncLifetime
{
    private readonly PostgresFixture _fixture = new();

    public ValueTask InitializeAsync() => _fixture.InitializeAsync();

    public ValueTask DisposeAsync() => _fixture.DisposeAsync();

    private static DatabaseSeeder Construir(AppDbContext contexto)
    {
        IDateTimeProvider clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(PostgresFixture.Agora);

        return new DatabaseSeeder(contexto, clock, NullLogger<DatabaseSeeder>.Instance);
    }

    [Fact]
    public async Task EmBancoVazio_GravaClientesEPedidos()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;

        await using (AppDbContext contexto = _fixture.CriarContexto())
        {
            await Construir(contexto).SemearAsync(ct);
        }

        await using AppDbContext leitura = _fixture.CriarContexto();

        int clientes = await leitura.Customers.CountAsync(ct);
        int pedidos = await leitura.Orders.CountAsync(ct);

        clientes.Should().Be(10);
        pedidos.Should().Be(5, "metade dos clientes fica sem pedido, de propósito");

        // Os pedidos passaram por Order.Place, então têm itens e total calculado — não são linhas cruas.
        Order? primeiro = await leitura.Orders.Include(p => p.Items).FirstOrDefaultAsync(ct);

        primeiro.Should().NotBeNull();
        primeiro.Items.Should().HaveCount(2);
        primeiro.Total.Amount.Should().BeGreaterThan(0);
        primeiro.Status.Should().Be(OrderStatus.Pending);
    }

    [Fact]
    public async Task RodandoDuasVezes_NaoDuplica()
    {
        // É o que permite deixar a flag ligada num ambiente de desenvolvimento sem pensar: reiniciar não
        // multiplica os dados nem falha por violação de índice único.
        CancellationToken ct = TestContext.Current.CancellationToken;

        for (int vez = 0; vez < 2; vez++)
        {
            await using AppDbContext contexto = _fixture.CriarContexto();
            await Construir(contexto).SemearAsync(ct);
        }

        await using AppDbContext leitura = _fixture.CriarContexto();

        (await leitura.Customers.CountAsync(ct)).Should().Be(10, "a segunda execução não acrescenta nada");
    }

    [Fact]
    public async Task OsDocumentosGerados_SaoUnicosEValidos()
    {
        // O índice de documento é único e o Document valida o dígito verificador: se o gerador repetisse ou
        // produzisse número inválido, o SaveChanges acima teria estourado. Este teste torna a causa explícita,
        // em vez de deixá-la aparecer como uma violação de constraint sem explicação.
        CancellationToken ct = TestContext.Current.CancellationToken;

        await using (AppDbContext contexto = _fixture.CriarContexto())
        {
            await Construir(contexto).SemearAsync(ct);
        }

        await using AppDbContext leitura = _fixture.CriarContexto();

        List<string> documentos = await leitura.Customers
            .Select(cliente => cliente.Document.Value)
            .ToListAsync(ct);

        documentos.Should().OnlyHaveUniqueItems();
        documentos.Should().AllSatisfy(documento => documento.Should().HaveLength(11));
    }
}
