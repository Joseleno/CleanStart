using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CleanStart.Domain.Customers;
using CleanStart.Domain.Orders;
using CleanStart.Domain.ValueObjects;

namespace CleanStart.Api.FunctionalTests.Orders;

/// <summary>
/// <c>GET /api/v1/orders/{id}</c> exercitado por HTTP.
/// </summary>
public sealed class BuscarPedidoTests(CleanStartApiFactory factory) : IClassFixture<CleanStartApiFactory>
{
    private async Task<Guid> SemearPedidoAsync(CancellationToken ct)
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

            contexto.Add(cliente);
            contexto.Add(pedido);
            await contexto.SaveChangesAsync(ct);

            pedidoId = pedido.Id.Value;
        });

        return pedidoId;
    }

    [Fact]
    public async Task ComPedidoExistente_Retorna200ComOsItens()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        Guid pedidoId = await SemearPedidoAsync(ct);
        using HttpClient client = factory.CreateClient();

        HttpResponseMessage resposta = await client.GetAsync($"/api/v1/orders/{pedidoId}", ct);

        resposta.StatusCode.Should().Be(HttpStatusCode.OK);

        JsonElement corpo = await resposta.Content.ReadFromJsonAsync<JsonElement>(ct);

        corpo.GetProperty("id").GetGuid().Should().Be(pedidoId);
        corpo.GetProperty("status").GetString().Should().Be("Pending");
        corpo.GetProperty("total").GetDecimal().Should().Be(21.00m, "2 × 10,50");
        corpo.GetProperty("items").GetArrayLength().Should().Be(1);
    }

    [Fact]
    public async Task ComPedidoInexistente_Retorna404()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        using HttpClient client = factory.CreateClient();

        HttpResponseMessage resposta = await client.GetAsync($"/api/v1/orders/{Guid.CreateVersion7()}", ct);

        resposta.StatusCode.Should().Be(HttpStatusCode.NotFound);

        JsonElement problema = await resposta.Content.ReadFromJsonAsync<JsonElement>(ct);
        problema.GetProperty("code").GetString().Should().Be("Order.NaoEncontrado");
    }

    [Fact]
    public async Task ComIdMalformado_Retorna404DoRoteamento()
    {
        // A restrição `:guid` na rota rejeita antes de qualquer código rodar — o handler nem é chamado.
        CancellationToken ct = TestContext.Current.CancellationToken;
        using HttpClient client = factory.CreateClient();

        HttpResponseMessage resposta = await client.GetAsync("/api/v1/orders/nao-e-um-guid", ct);

        resposta.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task DuasLeituras_DevolvemOMesmoResultado()
    {
        // A segunda vem do cache (a query implementa ICacheable). O teste não consegue distinguir a origem por
        // HTTP — e não deveria: o que importa é o resultado ser o mesmo. Que o cache foi consultado está coberto
        // no teste de unidade do CachingBehavior.
        CancellationToken ct = TestContext.Current.CancellationToken;
        Guid pedidoId = await SemearPedidoAsync(ct);
        using HttpClient client = factory.CreateClient();

        HttpResponseMessage primeira = await client.GetAsync($"/api/v1/orders/{pedidoId}", ct);
        HttpResponseMessage segunda = await client.GetAsync($"/api/v1/orders/{pedidoId}", ct);

        primeira.StatusCode.Should().Be(HttpStatusCode.OK);
        segunda.StatusCode.Should().Be(HttpStatusCode.OK);

        string corpo1 = await primeira.Content.ReadAsStringAsync(ct);
        string corpo2 = await segunda.Content.ReadAsStringAsync(ct);

        corpo2.Should().Be(corpo1);
    }

    [Fact]
    public async Task PedidoRecemCriado_ELegivelPeloLocationDaCriacao()
    {
        // Fecha o ciclo: cria pelo POST e lê pelo caminho que a própria resposta indicou. É o que um cliente de
        // verdade faz, e o que prova que o Location aponta para algo que existe.
        CancellationToken ct = TestContext.Current.CancellationToken;
        using HttpClient client = factory.CreateClient();

        Guid clienteId = Guid.Empty;

        await factory.ComEscopoAsync(async contexto =>
        {
            Customer cliente = Customer.Register(
                "Maria Souza",
                Email.Of($"maria.{Guid.CreateVersion7():N}@example.com").Value,
                Document.Of(DocumentoNovo()).Value,
                DateTimeOffset.UtcNow).Value;

            contexto.Add(cliente);
            await contexto.SaveChangesAsync(ct);
            clienteId = cliente.Id.Value;
        });

        object novoPedido = new
        {
            customerId = clienteId,
            currency = "BRL",
            items = new[] { new { productId = Guid.CreateVersion7(), quantity = 3, unitPrice = 7.00m } },
        };

        HttpResponseMessage criacao = await client.PostAsJsonAsync("/api/v1/orders", novoPedido, ct);
        criacao.StatusCode.Should().Be(HttpStatusCode.Created);

        string caminho = criacao.Headers.Location!.ToString();

        HttpResponseMessage leitura = await client.GetAsync(caminho, ct);

        leitura.StatusCode.Should().Be(HttpStatusCode.OK);

        JsonElement corpo = await leitura.Content.ReadFromJsonAsync<JsonElement>(ct);
        corpo.GetProperty("total").GetDecimal().Should().Be(21.00m, "3 × 7,00");
    }

    /// <summary>Gera um CPF válido e distinto — ver o motivo em <c>CriarPedidoTests</c>.</summary>
    private static string DocumentoNovo()
    {
        int[] digitos = new int[11];

        for (int i = 0; i < 9; i++)
        {
            digitos[i] = Random.Shared.Next(0, 10);
        }

        digitos[9] = CalcularDigito(digitos, quantidade: 9, pesoInicial: 10);
        digitos[10] = CalcularDigito(digitos, quantidade: 10, pesoInicial: 11);

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
