using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CleanStart.Domain.Customers;
using CleanStart.Domain.Orders;
using CleanStart.Domain.ValueObjects;

namespace CleanStart.Api.FunctionalTests.Orders;

/// <summary>
/// <c>POST /api/v1/orders/{id}/pay</c> exercitado por HTTP.
/// </summary>
public sealed class PagarPedidoTests(CleanStartApiFactory factory) : IClassFixture<CleanStartApiFactory>
{
    /// <summary>
    /// Cria um cliente e um pedido no estado indicado e devolve a identidade do pedido.
    /// </summary>
    /// <param name="clienteExcluido">
    /// Quando verdadeiro, o cliente é excluído (soft delete) depois de criado — o cenário que prova que o
    /// filtro global esconde o cliente do repositório e o pagamento é recusado.
    /// </param>
    private async Task<Guid> SemearAsync(
        CancellationToken ct,
        OrderStatus estado = OrderStatus.Pending,
        bool clienteExcluido = false)
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

            if (estado == OrderStatus.Cancelled)
            {
                pedido.Cancel(DateTimeOffset.UtcNow);
            }

            if (clienteExcluido)
            {
                cliente.Delete(DateTimeOffset.UtcNow);
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
        using HttpClient client = factory.CreateClientAutenticado();

        HttpResponseMessage resposta = await client.PostAsync($"/api/v1/orders/{pedidoId}/pay", null, ct);

        resposta.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task DepoisDePagar_ALeituraMostraPaid()
    {
        // O ciclo completo pelo HTTP: se a invalidação de cache não funcionasse, a leitura continuaria
        // devolvendo "Pending" por até cinco minutos — sem erro nenhum.
        CancellationToken ct = TestContext.Current.CancellationToken;
        Guid pedidoId = await SemearAsync(ct);
        using HttpClient client = factory.CreateClientAutenticado();

        // Lê antes, para que o pedido entre no cache com o estado antigo.
        await client.GetAsync($"/api/v1/orders/{pedidoId}", ct);

        await client.PostAsync($"/api/v1/orders/{pedidoId}/pay", null, ct);

        using var corpo = JsonDocument.Parse(
            await client.GetStringAsync($"/api/v1/orders/{pedidoId}", ct));

        corpo.RootElement.GetProperty("status").GetString()
            .Should().Be("Paid", "a invalidação precisa ter derrubado a entrada antiga");
    }

    [Fact]
    public async Task ComPedidoInexistente_Retorna404()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        using HttpClient client = factory.CreateClientAutenticado();

        HttpResponseMessage resposta = await client.PostAsync(
            $"/api/v1/orders/{Guid.CreateVersion7()}/pay", null, ct);

        resposta.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ComPedidoJaPago_Retorna409()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        Guid pedidoId = await SemearAsync(ct, OrderStatus.Paid);
        using HttpClient client = factory.CreateClientAutenticado();

        HttpResponseMessage resposta = await client.PostAsync($"/api/v1/orders/{pedidoId}/pay", null, ct);

        resposta.StatusCode.Should().Be(HttpStatusCode.Conflict);

        JsonElement problema = await resposta.Content.ReadFromJsonAsync<JsonElement>(ct);
        problema.GetProperty("code").GetString().Should().Be("Order.TransicaoInvalida");
    }

    [Fact]
    public async Task ComClienteExcluido_Retorna404()
    {
        // O soft delete exercitado por um caminho da aplicação, e não só pelo interceptor: o cliente existe na
        // tabela, mas o filtro global o esconde — então o pagamento é recusado como se ele não existisse.
        CancellationToken ct = TestContext.Current.CancellationToken;
        Guid pedidoId = await SemearAsync(ct, clienteExcluido: true);
        using HttpClient client = factory.CreateClientAutenticado();

        HttpResponseMessage resposta = await client.PostAsync($"/api/v1/orders/{pedidoId}/pay", null, ct);

        resposta.StatusCode.Should().Be(HttpStatusCode.NotFound);

        JsonElement problema = await resposta.Content.ReadFromJsonAsync<JsonElement>(ct);
        problema.GetProperty("code").GetString().Should().Be("Customer.NaoEncontrado");
    }

    [Fact]
    public async Task SemAutenticacao_Retorna401()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        using HttpClient client = factory.CreateClient();

        HttpResponseMessage resposta = await client.PostAsync(
            $"/api/v1/orders/{Guid.CreateVersion7()}/pay", null, ct);

        resposta.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
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
