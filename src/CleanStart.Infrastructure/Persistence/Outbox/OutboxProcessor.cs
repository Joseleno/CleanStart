using System.Text.Json;
using CleanStart.Application.Common.Abstractions;
using CleanStart.Domain.Common;
using CleanStart.Infrastructure.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CleanStart.Infrastructure.Persistence.Outbox;

/// <summary>
/// Lê as mensagens pendentes, publica cada uma e registra o resultado. Um ciclo por chamada.
/// </summary>
/// <remarks>
/// <para>
/// <b>Separado do <see cref="OutboxWorker"/> de propósito.</b> Aqui está a decisão — o que reservar, o que
/// publicar, quando desistir; lá está apenas o laço que chama isto de tempos em tempos. A separação é o que
/// torna o comportamento testável: o teste chama <see cref="ProcessarLoteAsync"/> e assere o resultado, sem
/// orquestrar <c>StartAsync</c>/<c>StopAsync</c> nem esperar temporizador.
/// </para>
/// <para>
/// <b>São duas transações, com a publicação entre elas — e a razão é sutil.</b> Com
/// <c>EnableRetryOnFailure</c> ligado (e ele está), toda transação explícita roda dentro de um
/// <c>ExecutionStrategy</c>, que <b>repete o delegate inteiro</b> quando encontra falha transiente. Se a
/// publicação estivesse lá dentro, uma falha de rede no <c>SaveChanges</c> republicaria mensagens que já haviam
/// saído — várias vezes, sem exception e sem log. Aqui as duas transações contêm apenas trabalho de banco:
/// repeti-las é inofensivo.
/// </para>
/// <list type="number">
///   <item><b>Reserva</b> — seleciona o lote com <c>FOR UPDATE SKIP LOCKED</c> e o marca como em andamento.</item>
///   <item><b>Publicação</b> — fora de qualquer transação, uma mensagem por vez.</item>
///   <item><b>Registro</b> — grava quem saiu e quem falhou, com o atraso da próxima tentativa.</item>
/// </list>
/// <para>
/// <b>A entrega é at-least-once.</b> Se o processo cair entre a publicação e o registro, a mensagem sai de novo
/// quando a reserva expirar. É o contrato do padrão: quem reage ao evento precisa tolerar recebê-lo duas vezes.
/// </para>
/// </remarks>
internal sealed class OutboxProcessor(
    AppDbContext contexto,
    IOutboxPublisher publisher,
    IDateTimeProvider clock,
    IOptions<OutboxOptions> options,
    ILogger<OutboxProcessor> logger)
{
    /// <remarks>
    /// <c>Web</c> para casar com o que o <c>DomainEventInterceptor</c> usou ao gravar. Duas configurações
    /// diferentes nas duas pontas produziriam um JSON que se grava e não se lê de volta.
    /// </remarks>
    private static readonly JsonSerializerOptions OpcoesDeSerializacao = new(JsonSerializerDefaults.Web);

    private readonly OutboxOptions _options = options.Value;

    /// <summary>
    /// Executa um ciclo completo e devolve quantas mensagens foram despachadas com sucesso.
    /// </summary>
    public async Task<int> ProcessarLoteAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<OutboxMessage> lote = await ReservarLoteAsync(cancellationToken);

        if (lote.Count == 0)
        {
            return 0;
        }

        OutboxLogs.LoteReservado(logger, lote.Count);

        List<Guid> entregues = [];
        List<(Guid Id, string Erro)> falhas = [];

        foreach (OutboxMessage mensagem in lote)
        {
            // try/catch por mensagem, nunca em volta do laço: no laço, a primeira falha abortaria o resto do
            // lote, as demais não teriam a tentativa contabilizada, e voltariam para sempre atrás da mesma
            // mensagem envenenada.
            try
            {
                IDomainEvent? evento = Desserializar(mensagem);

                if (evento is null)
                {
                    // Tipo que não existe mais no código. Contabiliza como falha para que ela esgote as
                    // tentativas e saia do caminho, em vez de ser relida para sempre.
                    OutboxLogs.TipoDesconhecido(logger, mensagem.Id, mensagem.Type);
                    falhas.Add((mensagem.Id, $"Tipo desconhecido: '{mensagem.Type}'."));
                    continue;
                }

                await publisher.PublishAsync(evento, cancellationToken);
                entregues.Add(mensagem.Id);
            }
            catch (Exception excecao) when (excecao is not OperationCanceledException)
            {
                OutboxLogs.FalhaAoDespachar(
                    logger, mensagem.Id, mensagem.Type, mensagem.Attempts, _options.MaxAttempts, excecao);

                falhas.Add((mensagem.Id, excecao.Message));
            }
        }

        await RegistrarResultadoAsync(entregues, falhas);

        if (entregues.Count > 0)
        {
            OutboxLogs.LoteDespachado(logger, entregues.Count);
        }

        return entregues.Count;
    }

    /// <summary>
    /// Seleciona o lote pendente e o reserva, para que outra instância não o pegue.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b><c>FOR UPDATE SKIP LOCKED</c></b> é o que permite N instâncias lerem a mesma tabela sem coordenação
    /// externa: cada uma trava as linhas que pegou e as demais <i>pulam</i> essas linhas em vez de esperar por
    /// elas. Sem isso, todas as instâncias leriam o mesmo lote e publicariam tudo em duplicidade — não de vez em
    /// quando, mas em toda iteração.
    /// </para>
    /// <para>
    /// <b>A reserva (incrementar a tentativa e empurrar a próxima) acontece já aqui</b>, antes de publicar. O
    /// lock do <c>SKIP LOCKED</c> morre no commit desta transação, e entre ela e o registro final existe uma
    /// janela em que outra instância veria a mensagem de novo. Empurrar <c>NextAttemptOn</c> fecha essa janela.
    /// Diferente de um campo "em processamento": se este processo morrer agora, ninguém precisa limpar nada — a
    /// mensagem simplesmente volta a ser elegível quando o prazo vencer.
    /// </para>
    /// <para>
    /// <b>Só os identificadores vêm por SQL cru.</b> Materializar <c>OutboxMessage</c> direto do SELECT exigiria
    /// repetir aqui os nomes de coluna em snake_case, e a próxima renomeação no mapeamento quebraria isto em
    /// runtime. Com os ids em mãos, o carregamento é LINQ comum, pelo mapeamento de verdade.
    /// </para>
    /// <para>
    /// O <c>WHERE</c> repete literalmente o filtro do índice parcial de pendentes: o PostgreSQL só usa um índice
    /// parcial quando consegue provar que a consulta implica o predicado dele.
    /// </para>
    /// </remarks>
    private async Task<IReadOnlyList<OutboxMessage>> ReservarLoteAsync(CancellationToken cancellationToken)
    {
        IExecutionStrategy estrategia = contexto.Database.CreateExecutionStrategy();

        return await estrategia.ExecuteAsync(async () =>
        {
            await using IDbContextTransaction transacao =
                await contexto.Database.BeginTransactionAsync(cancellationToken);

            // now() do banco, não o relógio da aplicação: quem gravou o NextAttemptOn inicial foi outro
            // processo, e duas máquinas com relógios diferentes comparando a mesma coluna fariam mensagens
            // voltarem cedo numa e ficarem paradas noutra.
            List<Guid> ids = await contexto.Database
                .SqlQuery<Guid>($"""
                    SELECT id
                      FROM outbox_messages
                     WHERE processed_on IS NULL
                       AND attempts < {_options.MaxAttempts}
                       AND next_attempt_on <= now()
                     ORDER BY next_attempt_on, occurred_on
                     LIMIT {_options.BatchSize}
                       FOR UPDATE SKIP LOCKED
                    """)
                .ToListAsync(cancellationToken);

            if (ids.Count == 0)
            {
                await transacao.CommitAsync(cancellationToken);
                return [];
            }

            List<OutboxMessage> mensagens = await contexto.OutboxMessages
                .Where(mensagem => ids.Contains(mensagem.Id))
                .ToListAsync(cancellationToken);

            DateTimeOffset agora = clock.UtcNow;

            foreach (OutboxMessage mensagem in mensagens)
            {
                mensagem.Attempts++;
                mensagem.NextAttemptOn = agora + AtrasoDe(mensagem.Attempts);
            }

            await contexto.SaveChangesAsync(cancellationToken);
            await transacao.CommitAsync(cancellationToken);

            return mensagens;
        });
    }

    /// <summary>
    /// Marca as entregues e grava o erro das que falharam.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Sem <c>CancellationToken</c>, de propósito.</b> Chega aqui depois de as mensagens já terem saído: se o
    /// desligamento interrompesse esta gravação, elas seriam publicadas de novo no próximo arranque sem
    /// necessidade. O trabalho externo já foi feito, e registrar que foi custa milissegundos.
    /// </para>
    /// <para>
    /// O <c>ProcessedOn</c> é escrito <b>depois</b> da publicação. Na ordem inversa, uma falha de publicação
    /// deixaria a mensagem marcada como entregue sem nunca ter saído — perda silenciosa, que é o defeito que o
    /// outbox existe para evitar.
    /// </para>
    /// </remarks>
    private async Task RegistrarResultadoAsync(List<Guid> entregues, List<(Guid Id, string Erro)> falhas)
    {
        DateTimeOffset agora = clock.UtcNow;
        List<OutboxMessage> rastreadas =
        [
            .. contexto.ChangeTracker.Entries<OutboxMessage>().Select(entrada => entrada.Entity)
        ];

        foreach (OutboxMessage mensagem in rastreadas)
        {
            if (entregues.Contains(mensagem.Id))
            {
                mensagem.ProcessedOn = agora;
                mensagem.Error = null;
                continue;
            }

            (Guid Id, string Erro) falha = falhas.FirstOrDefault(item => item.Id == mensagem.Id);

            if (falha.Id != Guid.Empty)
            {
                mensagem.Error = falha.Erro;
            }
        }

        // O aviso de esgotamento sai FORA do delegate retentável: a estratégia de retry reexecuta o corpo
        // inteiro numa falha transiente, e um log lá dentro apareceria uma vez por tentativa. Vale para
        // qualquer efeito que não seja gravação — log, métrica, contador.
        foreach (OutboxMessage esgotada in rastreadas.Where(mensagem =>
                     mensagem.ProcessedOn is null && mensagem.Attempts >= _options.MaxAttempts))
        {
            OutboxLogs.TentativasEsgotadas(logger, esgotada.Id, esgotada.Type, _options.MaxAttempts);
        }

        IExecutionStrategy estrategia = contexto.Database.CreateExecutionStrategy();

        // Só a gravação fica dentro do delegate — repeti-la é inofensivo, porque as alterações já estão no
        // change tracker e o SaveChanges é idempotente em relação a elas.
        await estrategia.ExecuteAsync(() => contexto.SaveChangesAsync(CancellationToken.None));
    }

    /// <summary>
    /// Remove as mensagens já despachadas que passaram da janela de retenção. Devolve quantas saíram.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Sem isto a tabela cresce para sempre</b>, e nada falha: ela apenas fica mais lenta ao longo de meses,
    /// que é o pior modo de falha possível — ninguém percebe até o índice deixar de caber em memória. Mensagem
    /// despachada é histórico útil por alguns dias, não para sempre.
    /// </para>
    /// <para>
    /// <b>Só toca o que já saiu</b> (<c>processed_on IS NOT NULL</c>): o conjunto é disjunto do que a reserva
    /// seleciona, então não há como esta limpeza competir com um despacho em andamento.
    /// </para>
    /// <para>
    /// <b>Em lotes e com ordem determinística, e isso importa.</b> Duas instâncias apagando ao mesmo tempo, sem
    /// ordem definida, travariam as mesmas linhas em ordens diferentes — que é a receita de um deadlock. O
    /// <c>SKIP LOCKED</c> resolve as duas coisas: define que cada uma leva um conjunto distinto e evita a espera.
    /// O limite por execução impede que uma remoção grande segure a transação por muito tempo.
    /// </para>
    /// </remarks>
    public async Task<int> AplicarRetencaoAsync(CancellationToken cancellationToken = default)
    {
        DateTimeOffset corte = clock.UtcNow.AddHours(-_options.ProcessedRetentionHours);
        IExecutionStrategy estrategia = contexto.Database.CreateExecutionStrategy();

        int removidas = await estrategia.ExecuteAsync(async () => await contexto.Database.ExecuteSqlAsync($"""
            DELETE FROM outbox_messages
             WHERE id IN (
                   SELECT id
                     FROM outbox_messages
                    WHERE processed_on IS NOT NULL
                      AND processed_on < {corte}
                    ORDER BY processed_on
                    LIMIT {_options.BatchSize}
                      FOR UPDATE SKIP LOCKED
                   )
            """, cancellationToken));

        if (removidas > 0)
        {
            OutboxLogs.RetencaoAplicada(logger, removidas);
        }

        return removidas;
    }

    /// <summary>
    /// Quanto esperar antes da próxima tentativa.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Dobra a cada tentativa até um teto: dar o mesmo intervalo sempre martela um destino que já está em
    /// dificuldade, e crescer sem limite transformaria a oitava tentativa em dias.
    /// </para>
    /// <para>
    /// <b>A variação aleatória não é enfeite.</b> Quando o destino cai, todas as mensagens do lote falham no
    /// mesmo instante; sem ela, todas voltariam juntas — em cima do serviço que acabou de se recuperar. Espalhar
    /// as retentativas é o que evita que a recuperação seja derrubada pela própria fila.
    /// </para>
    /// </remarks>
    private TimeSpan AtrasoDe(int tentativa)
    {
        double baseSegundos = _options.BaseRetryDelaySeconds * Math.Pow(2, Math.Max(0, tentativa - 1));
        double limitado = Math.Min(baseSegundos, _options.MaxRetryDelaySeconds);
        double variacao = Random.Shared.NextDouble() * limitado * 0.2;

        return TimeSpan.FromSeconds(limitado + variacao);
    }

    /// <summary>
    /// Traduz a mensagem de volta para o evento, ou <c>null</c> se o tipo não existir mais.
    /// </summary>
    private static IDomainEvent? Desserializar(OutboxMessage mensagem)
    {
        Type? tipo = OutboxEventTypes.TipoDe(mensagem.Type);

        return tipo is null
            ? null
            : JsonSerializer.Deserialize(mensagem.Content, tipo, OpcoesDeSerializacao) as IDomainEvent;
    }
}
