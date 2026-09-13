using System.Net;
using CleanStart.Application.Common.Abstractions;
using CleanStart.Infrastructure.Http;
using Microsoft.Extensions.Logging.Abstractions;

namespace CleanStart.Infrastructure.IntegrationTests.Http;

/// <summary>
/// Exercita o cliente externo de exemplo — o que ele faz quando o outro lado não coopera.
/// </summary>
/// <remarks>
/// <para>
/// <b>Sem rede e sem container.</b> O <c>HttpMessageHandler</c> é substituído por um que responde o que o teste
/// mandar. É o único ponto do projeto em que substituir a implementação é o certo: o objetivo aqui é justamente
/// provocar falhas que um serviço real não produziria sob encomenda.
/// </para>
/// <para>
/// O que estes testes protegem é a promessa da interface: <b>falha de rede vira <c>null</c></b>, não exception.
/// Se alguém trocar isso por um <c>throw</c>, todo caso de uso que consultar cotação passa a precisar de
/// <c>try/catch</c> — e o que era "seguir sem a cotação" vira erro 500 para quem chamou a API.
/// </para>
/// </remarks>
public sealed class ExchangeRateClientTests
{
    /// <summary>Responde sempre a mesma coisa, e conta quantas vezes foi chamado.</summary>
    private sealed class HandlerDeTeste(Func<HttpRequestMessage, HttpResponseMessage> resposta)
        : HttpMessageHandler
    {
        public int Chamadas { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Chamadas++;
            return Task.FromResult(resposta(request));
        }
    }

    private static ExchangeRateClient Construir(HandlerDeTeste handler) =>
        new(
            new HttpClient(handler) { BaseAddress = new Uri("https://api.exemplo.invalid/") },
            NullLogger<ExchangeRateClient>.Instance);

    [Fact]
    public async Task ComRespostaValida_DevolveACotacao()
    {
        HandlerDeTeste handler = new(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"rates":{"USD":5.42}}""", System.Text.Encoding.UTF8, "application/json"),
        });

        ExchangeRateClient cliente = Construir(handler);

        decimal? cotacao = await cliente.ObterCotacaoAsync("BRL", "USD", TestContext.Current.CancellationToken);

        cotacao.Should().Be(5.42m);
    }

    [Fact]
    public async Task ComServicoForaDoAr_DevolveNuloEmVezDeLancar()
    {
        // 503 é o caso que o circuit breaker existe para conter. Aqui o pipeline de resiliência não está
        // montado — o teste cobre o contrato do cliente, não a política — e o que importa é o desfecho: quem
        // chamou recebe "sem cotação", e não uma exception para tratar.
        HandlerDeTeste handler = new(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));

        ExchangeRateClient cliente = Construir(handler);

        decimal? cotacao = await cliente.ObterCotacaoAsync("BRL", "USD", TestContext.Current.CancellationToken);

        cotacao.Should().BeNull();
    }

    [Fact]
    public async Task ComMoedaAusenteNaResposta_DevolveNulo()
    {
        // O serviço respondeu, mas não trouxe a moeda pedida. Não é falha de rede nem defeito de contrato —
        // é ausência de dado, e o desfecho para quem chamou é o mesmo.
        HandlerDeTeste handler = new(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"rates":{}}""", System.Text.Encoding.UTF8, "application/json"),
        });

        ExchangeRateClient cliente = Construir(handler);

        decimal? cotacao = await cliente.ObterCotacaoAsync("BRL", "JPY", TestContext.Current.CancellationToken);

        cotacao.Should().BeNull();
    }

    [Fact]
    public async Task AMoedaVaiEscapadaNaQuery()
    {
        // Interpolar parâmetro na URL sem escapar é como se constrói uma vulnerabilidade de injeção — aqui numa
        // API externa, com o valor vindo de quem chamou.
        string? urlChamada = null;

        HandlerDeTeste handler = new(requisicao =>
        {
            urlChamada = requisicao.RequestUri?.ToString();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"rates":{}}""", System.Text.Encoding.UTF8, "application/json"),
            };
        });

        ExchangeRateClient cliente = Construir(handler);

        await cliente.ObterCotacaoAsync("BRL", "US&D&extra=1", TestContext.Current.CancellationToken);

        urlChamada.Should().NotBeNull();

        // O & escapado é o que importa: sem isso, "US&D&extra=1" viraria um parâmetro a mais na query — o valor
        // de quem chamou passaria a controlar a requisição. (O Uri normaliza %20 de volta para espaço ao
        // formatar, então espaço não serve para provar escape; o & serve, porque muda a estrutura da URL.)
        urlChamada.Should().Contain("%26").And.NotContain("&extra=1");
    }
}
