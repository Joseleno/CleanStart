using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using CleanStart.Domain.Customers;
using CleanStart.Domain.Orders;
using CleanStart.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;

namespace CleanStart.Api.FunctionalTests;

/// <summary>
/// Exercita a autenticação, os cabeçalhos de segurança e o endpoint de token de desenvolvimento.
/// </summary>
/// <remarks>
/// <para>
/// <b>Estes testes existem porque os outros passam.</b> Os 31 testes de pedido usam um cliente autenticado, e
/// continuariam passando se a autorização fosse removida dos endpoints — um token a mais no cabeçalho não
/// incomoda quem não o exige. O que prova que a proteção está ligada é a requisição <b>sem</b> token receber 401,
/// e é isso que está aqui.
/// </para>
/// <para>
/// Vale contra o token ausente e contra o token errado, que falham por motivos diferentes: o primeiro não chega a
/// ser validado, o segundo é rejeitado pela assinatura.
/// </para>
/// </remarks>
public sealed class SegurancaTests(CleanStartApiFactory factory) : IClassFixture<CleanStartApiFactory>
{
    private const string RotaDePedidos = "/api/v1/orders";

    [Fact]
    public async Task SemToken_Retorna401()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        using HttpClient client = factory.CreateClient();

        HttpResponseMessage resposta = await client.GetAsync(RotaDePedidos, ct);

        resposta.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ComTokenInvalido_Retorna401()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        using HttpClient client = factory.CreateClient();

        // Assinado com outra chave — a forma é de um JWT, o conteúdo não confere. É o caso que prova que a
        // validação de assinatura está ligada: sem ela, qualquer um emitiria o próprio token.
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJzdWIiOiJmYWxzbyJ9.assinatura-invalida");

        HttpResponseMessage resposta = await client.GetAsync(RotaDePedidos, ct);

        resposta.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task SemToken_ACriacaoDePedidoTambemERecusada()
    {
        // Confere que a proteção alcança a escrita, e não só a leitura: é no POST que o dano de um acesso
        // indevido seria permanente.
        CancellationToken ct = TestContext.Current.CancellationToken;
        using HttpClient client = factory.CreateClient();

        HttpResponseMessage resposta = await client.PostAsJsonAsync(
            RotaDePedidos,
            new { customerId = Guid.CreateVersion7(), currency = "BRL", items = Array.Empty<object>() },
            ct);

        resposta.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task OsHealthChecks_ContinuamAbertos()
    {
        // Exigir token no health check quebraria o orquestrador: o Kubernetes não se autentica, e a instância
        // saudável seria marcada como morta e reiniciada em laço.
        CancellationToken ct = TestContext.Current.CancellationToken;
        using HttpClient client = factory.CreateClient();

        HttpResponseMessage live = await client.GetAsync("/health/live", ct);
        HttpResponseMessage ready = await client.GetAsync("/health/ready", ct);

        live.StatusCode.Should().Be(HttpStatusCode.OK);
        ready.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task TodaResposta_TrazOsCabecalhosDeSeguranca()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        using HttpClient client = factory.CreateClient();

        // Numa resposta 401, de propósito: os cabeçalhos precisam valer também para o que o pipeline recusa,
        // e não só para o caminho feliz.
        HttpResponseMessage resposta = await client.GetAsync(RotaDePedidos, ct);

        resposta.Headers.GetValues("X-Content-Type-Options").Should().Contain("nosniff");
        resposta.Headers.GetValues("X-Frame-Options").Should().Contain("DENY");
        resposta.Headers.GetValues("Referrer-Policy").Should().Contain("no-referrer");
        resposta.Headers.Should().Contain(cabecalho => cabecalho.Key == "Content-Security-Policy");
    }

    [Fact]
    public async Task OTokenDeDesenvolvimento_AbreOsEndpointsProtegidos()
    {
        // O caminho que quem clona o repositório percorre: pede um token, usa, funciona. Se ele quebrar, a
        // primeira experiência com o kit é um 401 sem explicação.
        CancellationToken ct = TestContext.Current.CancellationToken;
        using HttpClient client = factory.CreateClient();

        HttpResponseMessage emissao = await client.PostAsJsonAsync(
            "/api/v1/dev/token",
            new { usuarioId = (Guid?)null, nome = "exemplo" },
            ct);

        emissao.StatusCode.Should().Be(HttpStatusCode.OK);

        JsonElement corpo = await emissao.Content.ReadFromJsonAsync<JsonElement>(ct);
        string token = corpo.GetProperty("token").GetString()!;

        token.Should().NotBeNullOrWhiteSpace();

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        HttpResponseMessage comToken = await client.GetAsync(RotaDePedidos, ct);

        comToken.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task OUsuarioDoToken_EGravadoComoAutorDoPedido()
    {
        // Fecha o ciclo que a T6.2 abriu: até aqui a auditoria gravava autoria nula porque ninguém autenticava.
        // O claim do token precisa chegar ao AuditableInterceptor — e nada no caminho o converte em silêncio.
        CancellationToken ct = TestContext.Current.CancellationToken;
        var usuarioId = Guid.CreateVersion7();

        Guid clienteId = Guid.Empty;

        await factory.ComEscopoAsync(async contexto =>
        {
            Customer cliente = Customer.Register(
                "Cliente do teste de auditoria",
                Email.Of($"auditoria.{Guid.CreateVersion7():N}@example.com").Value,
                Document.Of(DocumentoNovo()).Value,
                DateTimeOffset.UtcNow).Value;

            contexto.Add(cliente);
            await contexto.SaveChangesAsync(ct);

            clienteId = cliente.Id.Value;
        });

        using HttpClient client = factory.CreateClientAutenticado(usuarioId);

        HttpResponseMessage resposta = await client.PostAsJsonAsync(
            RotaDePedidos,
            new
            {
                customerId = clienteId,
                currency = "BRL",
                items = new[] { new { productId = Guid.CreateVersion7(), quantity = 1, unitPrice = 10m } },
            },
            ct);

        resposta.StatusCode.Should().Be(HttpStatusCode.Created);

        JsonElement corpo = await resposta.Content.ReadFromJsonAsync<JsonElement>(ct);
        Guid pedidoId = corpo.GetProperty("id").GetGuid();

        await factory.ComEscopoAsync(async contexto =>
        {
            OrderId id = new(pedidoId);

            Guid? autor = await contexto.Orders
                .Where(pedido => pedido.Id == id)
                .Select(pedido => pedido.CreatedBy)
                .SingleAsync(ct);

            autor.Should().Be(usuarioId, "o claim do token precisa chegar até a auditoria");
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
