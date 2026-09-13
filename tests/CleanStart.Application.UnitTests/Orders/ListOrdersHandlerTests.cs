using CleanStart.Application.Orders.ListOrders;
using CleanStart.Domain.Common;
using CleanStart.Domain.Orders;
using NSubstitute;

namespace CleanStart.Application.UnitTests.Orders;

/// <summary>
/// Paginação do <see cref="ListOrdersHandler"/> — em especial o cursor e o "existe próxima página?".
/// </summary>
public sealed class ListOrdersHandlerTests
{
    private static readonly DateTimeOffset Base = new(2026, 9, 13, 12, 0, 0, TimeSpan.Zero);

    private readonly IOrdersPageReader _reader = Substitute.For<IOrdersPageReader>();

    private static OrderSummary Pedido(int ordem) => new(
        Guid.CreateVersion7(),
        Guid.CreateVersion7(),
        "Pending",
        10m,
        "BRL",
        Base.AddMinutes(-ordem));

    private void ReaderDevolve(params OrderSummary[] itens) =>
        _reader.ListarAsync(
                Arg.Any<OrdersFilter>(),
                Arg.Any<OrdersCursor?>(),
                Arg.Any<int>(),
                Arg.Any<CancellationToken>())
            .Returns(itens);

    [Fact]
    public async Task ComMenosItensQueOTamanho_NaoDevolveProximoCursor()
    {
        ReaderDevolve(Pedido(1), Pedido(2));

        Result<OrdersPage> resultado = await new ListOrdersHandler(_reader)
            .Handle(new ListOrdersQuery(Tamanho: 5), TestContext.Current.CancellationToken);

        resultado.Value.Itens.Should().HaveCount(2);
        resultado.Value.ProximoCursor.Should().BeNull("cursor nulo é o sinal de fim da lista");
    }

    [Fact]
    public async Task ComItensAlemDoTamanho_DescartaOExtraEDevolveCursor()
    {
        // O handler pede tamanho+1 justamente para saber se há mais, sem um COUNT. O extra não vai na resposta.
        ReaderDevolve(Pedido(1), Pedido(2), Pedido(3));

        Result<OrdersPage> resultado = await new ListOrdersHandler(_reader)
            .Handle(new ListOrdersQuery(Tamanho: 2), TestContext.Current.CancellationToken);

        resultado.Value.Itens.Should().HaveCount(2, "o item extra é sonda, não conteúdo");
        resultado.Value.ProximoCursor.Should().NotBeNull();
    }

    [Fact]
    public async Task PedeUmItemAMaisQueOTamanho()
    {
        ReaderDevolve(Pedido(1));

        await new ListOrdersHandler(_reader)
            .Handle(new ListOrdersQuery(Tamanho: 10), TestContext.Current.CancellationToken);

        await _reader.Received(1).ListarAsync(
            Arg.Any<OrdersFilter>(),
            Arg.Any<OrdersCursor?>(),
            11,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OCursorApontaParaOUltimoItemDaPagina()
    {
        OrderSummary primeiro = Pedido(1);
        OrderSummary segundo = Pedido(2);

        ReaderDevolve(primeiro, segundo, Pedido(3));

        Result<OrdersPage> resultado = await new ListOrdersHandler(_reader)
            .Handle(new ListOrdersQuery(Tamanho: 2), TestContext.Current.CancellationToken);

        // É de onde a próxima página continua: o último item DESTA, não o extra que foi descartado.
        Result<OrdersCursor> cursor = OrdersCursor.Decodificar(resultado.Value.ProximoCursor!);

        cursor.Value.Id.Should().Be(segundo.Id);
        cursor.Value.CreatedAt.Should().Be(segundo.CreatedAt);
    }

    [Fact]
    public async Task ComCursorValido_RepassaAoReader()
    {
        ReaderDevolve(Pedido(1));
        OrdersCursor cursor = new(Base, Guid.CreateVersion7());

        await new ListOrdersHandler(_reader)
            .Handle(
                new ListOrdersQuery(Cursor: cursor.Codificar()),
                TestContext.Current.CancellationToken);

        await _reader.Received(1).ListarAsync(
            Arg.Any<OrdersFilter>(),
            Arg.Is<OrdersCursor?>(c => c != null && c.Id == cursor.Id),
            Arg.Any<int>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ComCursorMalformado_RetornaValidationSemConsultar()
    {
        Result<OrdersPage> resultado = await new ListOrdersHandler(_reader)
            .Handle(new ListOrdersQuery(Cursor: "isto-nao-e-um-cursor"), TestContext.Current.CancellationToken);

        // Cursor ruim é entrada do usuário — 400, não 500. E não custa uma ida ao banco.
        resultado.IsFailure.Should().BeTrue();
        resultado.Error.Code.Should().Be("Order.CursorInvalido");

        await _reader.DidNotReceiveWithAnyArgs().ListarAsync(
            default!, default, default, TestContext.Current.CancellationToken);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(-5, 1)]
    [InlineData(500, ListOrdersQuery.TamanhoMaximo)]
    public async Task OTamanhoEhLimitado(int pedido, int esperado)
    {
        // Sem teto, um único parâmetro derruba a memória do servidor; sem piso, tamanho zero devolveria página
        // vazia para sempre e o cliente entraria em laço infinito.
        ReaderDevolve(Pedido(1));

        await new ListOrdersHandler(_reader)
            .Handle(new ListOrdersQuery(Tamanho: pedido), TestContext.Current.CancellationToken);

        await _reader.Received(1).ListarAsync(
            Arg.Any<OrdersFilter>(),
            Arg.Any<OrdersCursor?>(),
            esperado + 1,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RepassaOsFiltros()
    {
        ReaderDevolve(Pedido(1));
        DateTimeOffset de = Base.AddDays(-7);

        await new ListOrdersHandler(_reader)
            .Handle(
                new ListOrdersQuery(Status: OrderStatus.Paid, De: de, Ate: Base),
                TestContext.Current.CancellationToken);

        await _reader.Received(1).ListarAsync(
            Arg.Is<OrdersFilter>(f => f.Status == OrderStatus.Paid && f.De == de && f.Ate == Base),
            Arg.Any<OrdersCursor?>(),
            Arg.Any<int>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public void OCursor_SobreviveAoRoundTrip()
    {
        OrdersCursor original = new(Base, Guid.CreateVersion7());

        Result<OrdersCursor> volta = OrdersCursor.Decodificar(original.Codificar());

        volta.IsSuccess.Should().BeTrue();
        volta.Value.Should().Be(original);
    }
}
