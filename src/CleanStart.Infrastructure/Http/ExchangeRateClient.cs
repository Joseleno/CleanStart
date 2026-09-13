using System.Net.Http.Json;
using System.Text.Json;
using CleanStart.Application.Common.Abstractions;
using Microsoft.Extensions.Logging;

namespace CleanStart.Infrastructure.Http;

/// <summary>
/// Cliente HTTP tipado de exemplo, com a política de resiliência aplicada no registro.
/// </summary>
/// <remarks>
/// <para>
/// <b>Repare no que esta classe não tem:</b> nenhum <c>try/catch</c> de retry, nenhum contador de falhas, nenhum
/// <c>Task.Delay</c> entre tentativas. Retry, circuit breaker e timeout são um <i>handler</i> no pipeline do
/// <c>HttpClient</c>, montado uma vez em <c>AddResiliencia</c>. Misturar isso ao código de chamada é o erro
/// comum: cada cliente novo reimplementa a política, e elas divergem sem que ninguém perceba.
/// </para>
/// <para>
/// <b>Cliente tipado, e não <c>HttpClient</c> injetado direto.</b> O <c>IHttpClientFactory</c> cuida do tempo de
/// vida do handler — criar <c>HttpClient</c> a cada chamada esgota sockets, e guardá-lo para sempre ignora
/// mudança de DNS. O tipado ainda dá nome ao cliente, que é como a configuração o encontra.
/// </para>
/// <para>
/// <b>Traduz falha em <c>null</c>, e só falha de rede.</b> Um JSON inesperado <b>não</b> é tratado aqui: se o
/// contrato mudou, isso é defeito e precisa aparecer, não ser convertido em "sem cotação".
/// </para>
/// </remarks>
internal sealed partial class ExchangeRateClient(HttpClient http, ILogger<ExchangeRateClient> logger)
    : IExchangeRateClient
{
    private static readonly JsonSerializerOptions Opcoes = new(JsonSerializerDefaults.Web);

    public async Task<decimal?> ObterCotacaoAsync(
        string origem,
        string destino,
        CancellationToken cancellationToken = default)
    {
        try
        {
            CotacaoResponse? resposta = await http.GetFromJsonAsync<CotacaoResponse>(
                $"latest?base={Uri.EscapeDataString(origem)}&symbols={Uri.EscapeDataString(destino)}",
                Opcoes,
                cancellationToken);

            // TryGetValue e não GetValueOrDefault: o dicionário é de `decimal`, e o valor padrão dele é ZERO,
            // não nulo. Com GetValueOrDefault, uma moeda ausente na resposta viraria "a cotação é 0" — que o
            // chamador usaria como número de verdade, multiplicando o valor do pedido por zero. Ausência tem de
            // se parecer com ausência.
            return resposta is not null && resposta.Rates.TryGetValue(destino.ToUpperInvariant(), out decimal taxa)
                ? taxa
                : null;
        }
        catch (HttpRequestException excecao)
        {
            // Chegou aqui depois de o pipeline de resiliência ter esgotado as tentativas, ou com o circuito
            // aberto. Não há mais o que tentar: quem chamou decide como seguir sem a cotação.
            ServicoIndisponivel(logger, origem, destino, excecao);
            return null;
        }
        catch (TaskCanceledException excecao) when (!cancellationToken.IsCancellationRequested)
        {
            // Timeout, e não cancelamento de quem chamou — a distinção está no token. Sem a cláusula `when`,
            // este catch engoliria também o desligamento da aplicação, transformando parada em "sem cotação".
            TempoEsgotado(logger, origem, destino, excecao);
            return null;
        }
    }

    private sealed record CotacaoResponse(IReadOnlyDictionary<string, decimal> Rates);

    [LoggerMessage(
        EventId = 4000,
        Level = LogLevel.Warning,
        Message = "Cotação {Origem}->{Destino} indisponível: o serviço externo não respondeu")]
    private static partial void ServicoIndisponivel(
        ILogger logger,
        string origem,
        string destino,
        Exception excecao);

    [LoggerMessage(
        EventId = 4001,
        Level = LogLevel.Warning,
        Message = "Cotação {Origem}->{Destino} indisponível: tempo esgotado")]
    private static partial void TempoEsgotado(ILogger logger, string origem, string destino, Exception excecao);
}
