using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CleanStart.Domain.Customers;
using CleanStart.Domain.Orders;
using CleanStart.Domain.ValueObjects;

namespace CleanStart.Api.FunctionalTests.Orders;

/// <summary>
/// <c>POST /api/v1/orders/{id}/cancel</c> exercitado por HTTP.
/// </summary>
public sealed class CancelarPedidoTests(CleanStartApiFactory factory) : IClassFixture<CleanStartApiFactory>
{
    /// <summary>
    /// Cria um pedido no estado indicado e devolve a identidade dele.
    /// </summary>
    private async Task<Guid> SemearAsync(CancellationToken ct, OrderStatus estado = OrderStatus.Pending)
    {
        Guid pedidoId = Guid.Empty;

        await factory.ComEscopoAsync(async contexto =>
        {
            Customer cliente = Customer.Register(
                "João da Silva",
                Email.Of($"joao.{Guid.CreateVersion7():N}@example.com").Value,
                Document.Of(DocumentoNovo()).Value,
                DateTimeOffset.UtcNow).Value;

            Order pedido = Order.Place(
                cliente.Id,
                [(ProductId.New(), 2, Money.Of(10.50m, "BRL").Value)],
                DateTimeOffset.UtcNow).Value;

            if (estado is OrderStatus.Paid or OrderStatus.Shipped)
            {
                pedido.Pay(DateTimeOffset.UtcNow);
            }

            if (estado == OrderStatus.Shipped)
            {
                pedido.Ship(DateTimeOffset.UtcNow);
            }

            contexto.Add(cliente);
            contexto.Add(pedido);
            await contexto.SaveChangesAsync(ct);

            pedidoId = pedido.Id.Value;
        });

        return pedidoId;
    }

    [Fact]
    public async Task ComPedidoPendente_Retorna204()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        Guid pedidoId = await SemearAsync(ct);
        using HttpClient client = factory.CreateClient();

        HttpResponseMessage resposta = await client.PostAsync($"/api/v1/orders/{pedidoId}/cancel", null, ct);

        resposta.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task DepoisDeCancelar_ALeituraMostraCancelled()
    {
        // O ciclo completo, e o teste mais valioso da task: se a invalidação de cache não funcionasse, a leitura
        // continuaria devolvendo "Pending" por até cinco minutos — sem erro nenhum, o que é o pior tipo de bug.
        CancellationToken ct = TestContext.Current.CancellationToken;
        Guid pedidoId = await SemearAsync(ct);
        using HttpClient client = factory.CreateClient();

        // Lê antes, para popular o cache.
        HttpResponseMessage antes = await client.GetAsync($"/api/v1/orders/{pedidoId}", ct);
        JsonElement corpoAntes = await antes.Content.ReadFromJsonAsync<JsonElement>(ct);
        corpoAntes.GetProperty("status").GetString().Should().Be("Pending");

        await client.PostAsync($"/api/v1/orders/{pedidoId}/cancel", null, ct);

        HttpResponseMessage depois = await client.GetAsync($"/api/v1/orders/{pedidoId}", ct);
        JsonElement corpoDepois = await depois.Content.ReadFromJsonAsync<JsonElement>(ct);

        corpoDepois.GetProperty("status").GetString().Should().Be("Cancelled", "o cache foi invalidado");
    }

    [Fact]
    public async Task ComPedidoPago_Retorna204()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        Guid pedidoId = await SemearAsync(ct, OrderStatus.Paid);
        using HttpClient client = factory.CreateClient();

        HttpResponseMessage resposta = await client.PostAsync($"/api/v1/orders/{pedidoId}/cancel", null, ct);

        resposta.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task ComPedidoEnviado_Retorna409()
    {
        // A transição proibida: a mercadoria já saiu, e o que existe daí em diante é devolução.
        CancellationToken ct = TestContext.Current.CancellationToken;
        Guid pedidoId = await SemearAsync(ct, OrderStatus.Shipped);
        using HttpClient client = factory.CreateClient();

        HttpResponseMessage resposta = await client.PostAsync($"/api/v1/orders/{pedidoId}/cancel", null, ct);

        resposta.StatusCode.Should().Be(HttpStatusCode.Conflict);

        JsonElement problema = await resposta.Content.ReadFromJsonAsync<JsonElement>(ct);
        problema.GetProperty("code").GetString().Should().Be("Order.TransicaoInvalida");
    }

    [Fact]
    public async Task CancelarDuasVezes_SegundaRetorna409()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        Guid pedidoId = await SemearAsync(ct);
        using HttpClient client = factory.CreateClient();

        HttpResponseMessage primeira = await client.PostAsync($"/api/v1/orders/{pedidoId}/cancel", null, ct);
        HttpResponseMessage segunda = await client.PostAsync($"/api/v1/orders/{pedidoId}/cancel", null, ct);

        primeira.StatusCode.Should().Be(HttpStatusCode.NoContent);
        segunda.StatusCode.Should().Be(HttpStatusCode.Conflict, "cancelar de novo estornaria em dobro");
    }

    [Fact]
    public async Task ComPedidoInexistente_Retorna404()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        using HttpClient client = factory.CreateClient();

        HttpResponseMessage resposta = await client.PostAsync(
            $"/api/v1/orders/{Guid.CreateVersion7()}/cancel",
            null,
            ct);

        resposta.StatusCode.Should().Be(HttpStatusCode.NotFound);

        JsonElement problema = await resposta.Content.ReadFromJsonAsync<JsonElement>(ct);
        problema.GetProperty("code").GetString().Should().Be("Order.NaoEncontrado");
    }

    [Fact]
    public async Task OCancelamentoGravaOEventoNoOutbox()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        Guid pedidoId = await SemearAsync(ct);
        using HttpClient client = factory.CreateClient();

        await client.PostAsync($"/api/v1/orders/{pedidoId}/cancel", null, ct);

        await factory.ComEscopoAsync(async contexto =>
        {
            List<string> conteudos = await Task.FromResult(
                contexto.OutboxMessages
                    .Where(m => m.Type == "CleanStart.Domain.Orders.Events.OrderCancelledEvent")
                    .Select(m => m.Content)
                    .ToList());

            // O evento entrou na mesma transação do cancelamento — o padrão outbox atravessando a pilha.
            conteudos.Should().Contain(c => c.Contains(pedidoId.ToString(), StringComparison.Ordinal));
        });
    }

    /// <summary>Gera um CPF válido e distinto — ver o motivo em <c>CriarPedidoTests</c>.</summary>
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
