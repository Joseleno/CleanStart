using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CleanStart.Domain.Customers;
using CleanStart.Domain.Orders;
using CleanStart.Domain.ValueObjects;

namespace CleanStart.Api.FunctionalTests.Orders;

/// <summary>
/// <c>GET /api/v1/orders</c> exercitado por HTTP.
/// </summary>
public sealed class ListarPedidosTests(CleanStartApiFactory factory) : IClassFixture<CleanStartApiFactory>
{
    private const string Rota = "/api/v1/orders";

    /// <summary>
    /// Cria um cliente com <paramref name="quantidade"/> pedidos e devolve a identidade dele.
    /// </summary>
    private async Task<Guid> SemearAsync(int quantidade, CancellationToken ct)
    {
        Guid clienteId = Guid.Empty;

        await factory.ComEscopoAsync(async contexto =>
        {
            Customer cliente = Customer.Register(
                "João da Silva",
                Email.Of($"joao.{Guid.CreateVersion7():N}@example.com").Value,
                Document.Of(DocumentoNovo()).Value,
                DateTimeOffset.UtcNow).Value;

            contexto.Add(cliente);

            for (int i = 0; i < quantidade; i++)
            {
                contexto.Add(Order.Place(
                    cliente.Id,
                    [(ProductId.New(), 1, Money.Of(10m, "BRL").Value)],
                    DateTimeOffset.UtcNow).Value);
            }

            await contexto.SaveChangesAsync(ct);
            clienteId = cliente.Id.Value;
        });

        return clienteId;
    }

    [Fact]
    public async Task SemFiltro_Retorna200ComItensECursor()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await SemearAsync(3, ct);
        using HttpClient client = factory.CreateClient();

        HttpResponseMessage resposta = await client.GetAsync($"{Rota}?tamanho=2", ct);

        resposta.StatusCode.Should().Be(HttpStatusCode.OK);

        JsonElement corpo = await resposta.Content.ReadFromJsonAsync<JsonElement>(ct);

        corpo.GetProperty("itens").GetArrayLength().Should().Be(2, "o tamanho pedido é respeitado");
        corpo.GetProperty("proximoCursor").GetString().Should().NotBeNullOrWhiteSpace("há mais páginas");
    }

    [Fact]
    public async Task NavegandoPeloCursor_PercorreTodasAsPaginasSemRepetir()
    {
        // O ciclo que um cliente de verdade faz: pede a primeira página, usa o cursor devolvido, repete até o
        // cursor vir nulo. É a prova de que o contrato de paginação funciona ponta a ponta.
        CancellationToken ct = TestContext.Current.CancellationToken;
        Guid clienteId = await SemearAsync(5, ct);
        using HttpClient client = factory.CreateClient();

        HashSet<Guid> vistos = [];
        string? cursor = null;

        for (int pagina = 0; pagina < 20; pagina++)
        {
            string url = cursor is null
                ? $"{Rota}?tamanho=2"
                : $"{Rota}?tamanho=2&cursor={Uri.EscapeDataString(cursor)}";

            HttpResponseMessage resposta = await client.GetAsync(url, ct);
            resposta.StatusCode.Should().Be(HttpStatusCode.OK);

            JsonElement corpo = await resposta.Content.ReadFromJsonAsync<JsonElement>(ct);

            foreach (JsonElement item in corpo.GetProperty("itens").EnumerateArray())
            {
                if (item.GetProperty("customerId").GetGuid() == clienteId)
                {
                    Guid id = item.GetProperty("id").GetGuid();
                    vistos.Add(id).Should().BeTrue($"o pedido {id} não pode vir em duas páginas");
                }
            }

            cursor = corpo.GetProperty("proximoCursor").GetString();

            if (cursor is null)
            {
                break;
            }
        }

        vistos.Should().HaveCount(5, "todos os pedidos do cliente devem ter sido visitados");
    }

    [Fact]
    public async Task UltimaPagina_TrazCursorNulo()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await SemearAsync(2, ct);
        using HttpClient client = factory.CreateClient();

        // Tamanho maior que o total: cabe tudo numa página, então não há próxima.
        HttpResponseMessage resposta = await client.GetAsync($"{Rota}?tamanho={ListOrdersLimite}", ct);

        JsonElement corpo = await resposta.Content.ReadFromJsonAsync<JsonElement>(ct);

        corpo.GetProperty("proximoCursor").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task ComCursorMalformado_Retorna400()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        using HttpClient client = factory.CreateClient();

        HttpResponseMessage resposta = await client.GetAsync($"{Rota}?cursor=isto-nao-e-cursor", ct);

        // Cursor ruim é entrada do usuário — copiada errada, truncada, de uma versão anterior da API.
        resposta.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        JsonElement problema = await resposta.Content.ReadFromJsonAsync<JsonElement>(ct);
        problema.GetProperty("code").GetString().Should().Be("Order.CursorInvalido");
    }

    [Fact]
    public async Task ComStatusInvalido_Retorna400DoBinding()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        using HttpClient client = factory.CreateClient();

        HttpResponseMessage resposta = await client.GetAsync($"{Rota}?status=NaoExiste", ct);

        resposta.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task FiltrandoPorStatus_TrazSoOsCorrespondentes()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await SemearAsync(2, ct);
        using HttpClient client = factory.CreateClient();

        HttpResponseMessage resposta = await client.GetAsync($"{Rota}?status=Pending&tamanho=50", ct);

        JsonElement corpo = await resposta.Content.ReadFromJsonAsync<JsonElement>(ct);

        foreach (JsonElement item in corpo.GetProperty("itens").EnumerateArray())
        {
            item.GetProperty("status").GetString().Should().Be("Pending");
        }
    }

    [Fact]
    public async Task TamanhoAcimaDoTeto_ELimitado()
    {
        // Sem teto, um único parâmetro derruba a memória do servidor.
        CancellationToken ct = TestContext.Current.CancellationToken;
        await SemearAsync(3, ct);
        using HttpClient client = factory.CreateClient();

        HttpResponseMessage resposta = await client.GetAsync($"{Rota}?tamanho=100000", ct);

        resposta.StatusCode.Should().Be(HttpStatusCode.OK, "o tamanho é limitado, não rejeitado");

        JsonElement corpo = await resposta.Content.ReadFromJsonAsync<JsonElement>(ct);
        corpo.GetProperty("itens").GetArrayLength().Should().BeLessThanOrEqualTo(ListOrdersLimite);
    }

    private const int ListOrdersLimite = 100;

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
