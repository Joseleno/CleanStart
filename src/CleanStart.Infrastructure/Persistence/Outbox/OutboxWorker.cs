using CleanStart.Infrastructure.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CleanStart.Infrastructure.Persistence.Outbox;

/// <summary>
/// Acorda de tempos em tempos e manda o <see cref="OutboxProcessor"/> trabalhar.
/// </summary>
/// <remarks>
/// <para>
/// <b>Só o laço mora aqui.</b> Toda a decisão — o que reservar, o que publicar, quando desistir — está no
/// processor. A divisão não é cerimônia: testar um <c>BackgroundService</c> obriga a orquestrar
/// <c>StartAsync</c>/<c>StopAsync</c> e a esperar temporizador, e um teste que espera relógio é um teste que
/// falha sozinho de vez em quando. Com a decisão num tipo próprio, o teste chama um método e assere.
/// </para>
/// <para>
/// <b>Escopo novo a cada ciclo.</b> Um <c>BackgroundService</c> é singleton, e o <c>AppDbContext</c> é scoped —
/// guardá-lo num campo o manteria vivo pela vida do processo, acumulando entidades rastreadas indefinidamente.
/// Um escopo por ciclo dá o mesmo tempo de vida que uma requisição HTTP tem.
/// </para>
/// <para>
/// <b><see cref="PeriodicTimer"/> e não <c>Task.Delay</c>.</b> O <c>Delay</c> só começa a contar depois que o
/// trabalho termina, então o intervalo real vira "intervalo mais duração" e o ritmo escorrega conforme a carga.
/// O <c>PeriodicTimer</c> conta a partir do tique anterior.
/// </para>
/// </remarks>
internal sealed class OutboxWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<OutboxOptions> options,
    ILogger<OutboxWorker> logger) : BackgroundService
{
    private readonly OutboxOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using PeriodicTimer timer = new(TimeSpan.FromSeconds(_options.PollingIntervalSeconds));

        long ciclo = 0;

        // WaitForNextTickAsync devolve false no cancelamento, então o desligamento sai pelo while — sem
        // precisar de catch para tratar o que é funcionamento normal.
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            ciclo++;

            // O try/catch fica DENTRO do laço, e é o que mantém o despachante vivo: uma exception que escapa
            // do ExecuteAsync de um BackgroundService derruba o host inteiro (StopHost é o padrão). O banco
            // ficar indisponível por um minuto não pode matar a aplicação — o ciclo seguinte tenta de novo.
            try
            {
                await using AsyncServiceScope escopo = scopeFactory.CreateAsyncScope();
                OutboxProcessor processador = escopo.ServiceProvider.GetRequiredService<OutboxProcessor>();

                await processador.ProcessarLoteAsync(stoppingToken);

                // A limpeza é manutenção, não caminho quente: rodar a cada tique gastaria uma consulta a cada
                // poucos segundos para quase sempre não apagar nada.
                if (ciclo % _options.CleanupEveryCycles == 0)
                {
                    await processador.AplicarRetencaoAsync(stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Desligamento pedido no meio do ciclo: sair em silêncio é o comportamento correto.
                break;
            }
            catch (Exception excecao)
            {
                OutboxLogs.CicloFalhou(logger, excecao);
            }
        }
    }
}
