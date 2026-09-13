using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using CleanStart.Domain.Customers;
using CleanStart.Domain.Orders;
using CleanStart.Domain.ValueObjects;
using CleanStart.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CleanStart.Api.FunctionalTests.Orders;

/// <summary>
/// <c>POST /api/v1/orders</c> exercitado por HTTP, contra PostgreSQL e Redis reais.
/// </summary>
/// <remarks>
/// Cobre o que os testes das camadas de baixo não alcançam: serialização, binding, o pipeline de middlewares e a
/// tradução de <c>Result</c> em status HTTP.
/// </remarks>
public sealed class CriarPedidoTests(CleanStartApiFactory factory) : IClassFixture<CleanStartApiFactory>
{
    private const string Rota = "/api/v1/orders";

    /// <summary>
    /// Cadastra um cliente e devolve a identidade dele.
    /// </summary>
    /// <remarks>
    /// Pelo <c>AppDbContext</c> e não por SQL cru: o teste passa a depender do modelo, não do nome das colunas —
    /// e um mapeamento errado quebra aqui em vez de passar despercebido.
    /// <para>
    /// Documento único por chamada, porque o índice único do banco é real e os testes compartilham a base.
    /// </para>
    /// </remarks>
    private async Task<Guid> CadastrarClienteAsync(string documento)
    {
        Guid id = Guid.Empty;

        await factory.ComEscopoAsync(async contexto =>
        {
            Customer cliente = Customer.Register(
                "João da Silva",
                Email.Of($"joao.{Guid.CreateVersion7():N}@example.com").Value,
                Document.Of(documento).Value,
                DateTimeOffset.UtcNow).Value;

            contexto.Add(cliente);
            await contexto.SaveChangesAsync(TestContext.Current.CancellationToken);

            id = cliente.Id.Value;
        });

        return id;
    }

    private static object Pedido(Guid clienteId, string moeda = "BRL", int quantidade = 2, decimal preco = 10.50m) =>
        new
        {
            customerId = clienteId,
            currency = moeda,
            items = new[] { new { productId = Guid.CreateVersion7(), quantity = quantidade, unitPrice = preco } },
        };

    [Fact]
    public async Task ComDadosValidos_Retorna201ComLocationEOPedido()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        Guid clienteId = await CadastrarClienteAsync("529.982.247-25");
        using HttpClient client = factory.CreateClient();

        HttpResponseMessage resposta = await client.PostAsJsonAsync(Rota, Pedido(clienteId), ct);

        resposta.StatusCode.Should().Be(HttpStatusCode.Created);
        resposta.Headers.Location.Should().NotBeNull("201 precisa dizer onde o recurso criado pode ser lido");

        JsonElement corpo = await resposta.Content.ReadFromJsonAsync<JsonElement>(ct);

        corpo.GetProperty("customerId").GetGuid().Should().Be(clienteId);
        corpo.GetProperty("status").GetString().Should().Be("Pending");
        corpo.GetProperty("currency").GetString().Should().Be("BRL");
        corpo.GetProperty("total").GetDecimal().Should().Be(21.00m, "2 × 10,50");
        corpo.GetProperty("items").GetArrayLength().Should().Be(1);
    }

    [Fact]
    public async Task ComDadosValidos_PersisteOPedidoEAMensagemDeOutbox()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        Guid clienteId = await CadastrarClienteAsync("111.444.777-35");
        using HttpClient client = factory.CreateClient();

        HttpResponseMessage resposta = await client.PostAsJsonAsync(Rota, Pedido(clienteId), ct);
        JsonElement corpo = await resposta.Content.ReadFromJsonAsync<JsonElement>(ct);
        Guid pedidoId = corpo.GetProperty("id").GetGuid();

        await factory.ComEscopoAsync(async contexto =>
        {
            // Compara a identidade tipada inteira, não `o.Id.Value`: OrderId é mapeado por conversor de valor, e
            // navegar até a propriedade interna dentro da expressão compila mas não traduz para SQL — o mesmo
            // tropeço que o Document já tinha rendido nos repositórios.
            OrderId id = new(pedidoId);

            bool existe = await contexto.Orders.AnyAsync(o => o.Id == id, ct);
            existe.Should().BeTrue("o pedido está no banco de verdade — não basta a resposta 201");

            // E o evento foi gravado na mesma transação: é o padrão outbox atravessando a pilha inteira.
            List<string> conteudos = await contexto.OutboxMessages
                .Where(m => m.Type == "CleanStart.Domain.Orders.Events.OrderPlacedEvent")
                .Select(m => m.Content)
                .ToListAsync(ct);

            conteudos.Should().Contain(c => c.Contains(pedidoId.ToString(), StringComparison.Ordinal));
        });
    }

    [Theory]
    [InlineData(0, "quantidade zero")]
    [InlineData(-1, "quantidade negativa")]
    public async Task ComQuantidadeInvalida_Retorna400(int quantidade, string motivo)
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        Guid clienteId = await CadastrarClienteAsync(DocumentoNovo());
        using HttpClient client = factory.CreateClient();

        HttpResponseMessage resposta = await client.PostAsJsonAsync(
            Rota,
            Pedido(clienteId, quantidade: quantidade),
            ct);

        resposta.StatusCode.Should().Be(HttpStatusCode.BadRequest, motivo);

        JsonElement problema = await resposta.Content.ReadFromJsonAsync<JsonElement>(ct);

        // O caminho do campo vem na resposta — é o que permite ao formulário destacar o item errado.
        problema.GetProperty("errors").GetProperty("Items[0].Quantity").GetArrayLength().Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task SemItens_Retorna400()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        Guid clienteId = await CadastrarClienteAsync(DocumentoNovo());
        using HttpClient client = factory.CreateClient();

        object semItens = new { customerId = clienteId, currency = "BRL", items = Array.Empty<object>() };

        HttpResponseMessage resposta = await client.PostAsJsonAsync(Rota, semItens, ct);

        resposta.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        JsonElement problema = await resposta.Content.ReadFromJsonAsync<JsonElement>(ct);
        problema.GetProperty("errors").TryGetProperty("Items", out _).Should().BeTrue();
    }

    [Fact]
    public async Task ComClienteInexistente_Retorna404ComCodigoDoErro()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        using HttpClient client = factory.CreateClient();

        HttpResponseMessage resposta = await client.PostAsJsonAsync(
            Rota,
            Pedido(Guid.CreateVersion7()),
            ct);

        resposta.StatusCode.Should().Be(HttpStatusCode.NotFound);
        resposta.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");

        JsonElement problema = await resposta.Content.ReadFromJsonAsync<JsonElement>(ct);

        // O código é contrato estável, e um cliente pode ramificar nele — a mensagem é para humano.
        problema.GetProperty("code").GetString().Should().Be("Customer.NaoEncontrado");
        problema.GetProperty("correlationId").GetString().Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task ComMoedaInvalida_Retorna400()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        Guid clienteId = await CadastrarClienteAsync(DocumentoNovo());
        using HttpClient client = factory.CreateClient();

        HttpResponseMessage resposta = await client.PostAsJsonAsync(Rota, Pedido(clienteId, moeda: "BRLL"), ct);

        resposta.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Theory]
    [InlineData("", "corpo vazio")]
    [InlineData("{ isso nao e json }", "JSON malformado")]
    public async Task ComCorpoInvalido_Retorna400ENao500(string corpo, string motivo)
    {
        // O binding do ASP.NET lança BadHttpRequestException antes de o endpoint ser chamado. Sem o catch
        // específico no middleware, isto viraria 500 — dizendo ao cliente que o problema é do servidor.
        CancellationToken ct = TestContext.Current.CancellationToken;
        using HttpClient client = factory.CreateClient();

        using StringContent conteudo = new(corpo, Encoding.UTF8, "application/json");

        HttpResponseMessage resposta = await client.PostAsync(Rota, conteudo, ct);

        resposta.StatusCode.Should().Be(HttpStatusCode.BadRequest, motivo);
    }

    [Fact]
    public async Task RespostaDeErro_TrazOCorrelationIdRecebido()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        using HttpClient client = factory.CreateClient();

        using HttpRequestMessage requisicao = new(HttpMethod.Post, Rota)
        {
            Content = JsonContent.Create(Pedido(Guid.CreateVersion7())),
        };
        requisicao.Headers.Add("X-Correlation-Id", "teste-correlacao-123");

        HttpResponseMessage resposta = await client.SendAsync(requisicao, ct);

        // O id atravessa: entra pelo cabeçalho, volta no cabeçalho e aparece no corpo do erro. É o que permite
        // ao usuário relatar o problema e alguém encontrar a requisição exata no log.
        resposta.Headers.GetValues("X-Correlation-Id").Should().Contain("teste-correlacao-123");

        JsonElement problema = await resposta.Content.ReadFromJsonAsync<JsonElement>(ct);
        problema.GetProperty("correlationId").GetString().Should().Be("teste-correlacao-123");
    }

    [Fact]
    public async Task HealthLive_Responde200SemTocarDependencia()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        using HttpClient client = factory.CreateClient();

        HttpResponseMessage resposta = await client.GetAsync("/health/live", ct);

        resposta.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task HealthReady_Responde200ComBancoECacheNoAr()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        using HttpClient client = factory.CreateClient();

        HttpResponseMessage resposta = await client.GetAsync("/health/ready", ct);

        // Com os containers de pé, o ready passa. Sem banco ele responderia 503 — a distinção que impede o
        // orquestrador de reiniciar pods quando o problema está no banco.
        resposta.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    /// <summary>
    /// Gera um CPF válido e distinto a cada chamada.
    /// </summary>
    /// <remarks>
    /// O índice único de documento é real e os testes compartilham a base: documento fixo faria um teste
    /// derrubar o outro conforme a ordem de execução. Os dígitos verificadores são calculados, porque o domínio
    /// os confere — inventar número faz o cadastro falhar por um motivo que nada tem a ver com o que se testa.
    /// </remarks>
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
