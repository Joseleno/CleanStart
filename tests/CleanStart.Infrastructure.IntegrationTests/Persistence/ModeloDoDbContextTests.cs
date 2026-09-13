using CleanStart.Domain.Customers;
using CleanStart.Domain.Orders;
using CleanStart.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace CleanStart.Infrastructure.IntegrationTests.Persistence;

/// <summary>
/// Verifica que o EF Core consegue <b>construir o modelo</b> a partir das configurações.
/// </summary>
/// <remarks>
/// <para>
/// Compilar não prova nada aqui: as configurações são Fluent API, e erro de mapeamento — identidade tipada sem
/// conversor, owned type mal declarado, propriedade calculada sem <c>Ignore</c> — só aparece quando o EF monta o
/// modelo, em tempo de execução.
/// </para>
/// <para>
/// <b>Não precisa de banco nem de Docker.</b> Construir o modelo é operação em memória; o provider do Npgsql é
/// configurado com uma connection string que nunca é aberta. Os testes que exercitam consulta de verdade usam
/// Testcontainers e são a T3.4 — e o provider InMemory continua proibido (ver CLAUDE.md).
/// </para>
/// </remarks>
public sealed class ModeloDoDbContextTests
{
    private static AppDbContext CriarContexto()
    {
        DbContextOptions<AppDbContext> options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost;Database=cleanstart;Username=postgres;Password=naoconecta")
            .Options;

        return new AppDbContext(options);
    }

    [Fact]
    public void OModelo_EConstruidoSemErro()
    {
        // Se qualquer configuração estiver errada, o acesso ao Model lança aqui — é o teste mais barato que
        // cobre todas as configurações de uma vez.
        using AppDbContext contexto = CriarContexto();

        IModel modelo = contexto.Model;

        modelo.Should().NotBeNull();
    }

    [Theory]
    [InlineData(typeof(Order), "orders")]
    [InlineData(typeof(OrderItem), "order_items")]
    [InlineData(typeof(Customer), "customers")]
    public void AsEntidades_EstaoMapeadasParaAsTabelasEsperadas(Type clrType, string tabela)
    {
        using AppDbContext contexto = CriarContexto();

        IEntityType? entidade = contexto.Model.FindEntityType(clrType);

        entidade.Should().NotBeNull();
        entidade!.GetTableName().Should().Be(tabela);
    }

    [Fact]
    public void IdentidadesTipadas_TemConversorDeValor()
    {
        using AppDbContext contexto = CriarContexto();

        IEntityType pedido = contexto.Model.FindEntityType(typeof(Order))!;

        // Sem conversor, o EF trataria OrderId como tipo complexo e tentaria mapeá-lo como tabela própria.
        pedido.FindProperty(nameof(Order.Id))!.GetValueConverter().Should().NotBeNull();
        pedido.FindProperty(nameof(Order.CustomerId))!.GetValueConverter().Should().NotBeNull();
    }

    [Fact]
    public void Money_EOwnedTypeComDuasColunasNaMesmaTabela()
    {
        using AppDbContext contexto = CriarContexto();

        IEntityType item = contexto.Model.FindEntityType(typeof(OrderItem))!;

        INavigation? preco = item.FindNavigation(nameof(OrderItem.UnitPrice));

        preco.Should().NotBeNull("UnitPrice é owned type, não propriedade simples");
        preco!.TargetEntityType.IsOwned().Should().BeTrue();

        // O ponto do owned type: as colunas ficam na tabela do dono, não numa tabela separada — value object
        // não tem identidade e não existe sem o dono.
        preco.TargetEntityType.GetTableName().Should().Be("order_items");
    }

    [Fact]
    public void PropriedadesCalculadas_NaoSaoPersistidas()
    {
        using AppDbContext contexto = CriarContexto();

        IEntityType pedido = contexto.Model.FindEntityType(typeof(Order))!;

        // Total é a soma dos itens e DomainEvents é estado em memória. Persistir o Total criaria uma segunda
        // fonte de verdade que pode divergir dos itens.
        pedido.FindProperty(nameof(Order.Total)).Should().BeNull();
        pedido.FindProperty(nameof(Order.DomainEvents)).Should().BeNull();
    }

    [Fact]
    public void Itens_SaoAcessadosPeloCampoPrivado()
    {
        using AppDbContext contexto = CriarContexto();

        IEntityType pedido = contexto.Model.FindEntityType(typeof(Order))!;
        INavigation itens = pedido.FindNavigation(nameof(Order.Items))!;

        // A propriedade é IReadOnlyCollection; o EF precisa do campo para materializar. É o que permite manter
        // a coleção fechada para o resto do código.
        itens.GetPropertyAccessMode().Should().Be(PropertyAccessMode.Field);
    }

    [Fact]
    public void Customer_TemFiltroGlobalDeSoftDelete()
    {
        using AppDbContext contexto = CriarContexto();

        IEntityType cliente = contexto.Model.FindEntityType(typeof(Customer))!;

        // Sem o filtro global, cada consulta precisaria repetir `WHERE is_deleted = false` — e quem esquecer
        // não erra por pouco: mostra dado que deveria estar invisível.
        cliente.GetDeclaredQueryFilters().Should().NotBeEmpty();
    }

    [Fact]
    public void Order_NaoTemFiltroDeSoftDelete()
    {
        using AppDbContext contexto = CriarContexto();

        IEntityType pedido = contexto.Model.FindEntityType(typeof(Order))!;

        // Order não implementa ISoftDeletable — o filtro é aplicado por varredura, então só alcança quem
        // implementa. Confirma que a varredura não é indiscriminada.
        pedido.GetDeclaredQueryFilters().Should().BeEmpty();
    }

    [Fact]
    public void Document_TemIndiceUnicoFiltrado()
    {
        using AppDbContext contexto = CriarContexto();

        IEntityType cliente = contexto.Model.FindEntityType(typeof(Customer))!;

        IIndex? indice = cliente.GetIndexes()
            .FirstOrDefault(i => i.GetDatabaseName() == "ux_customers_document");

        indice.Should().NotBeNull();
        indice!.IsUnique.Should().BeTrue();

        // O filtro parcial existe por causa do soft delete: sem ele, um cliente excluído continuaria
        // bloqueando o recadastro do mesmo documento.
        indice.GetFilter().Should().Contain("is_deleted");
    }
}
