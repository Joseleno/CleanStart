using CleanStart.Domain.Common;

namespace CleanStart.Application.Common.Abstractions;

/// <summary>
/// Entrega um domain event que já foi gravado no outbox ao seu destino.
/// </summary>
/// <remarks>
/// <para>
/// <b>Esta é a fronteira do padrão outbox.</b> Do lado de cá, o evento já está persistido e o commit já
/// aconteceu — o fato é definitivo. Do lado de lá está o mundo externo: um broker, um webhook, um serviço de
/// e-mail. O despachante chama esta interface e, conforme ela lance ou não, decide entre marcar a mensagem como
/// entregue ou agendar nova tentativa.
/// </para>
/// <para>
/// <b>Não é o Mediator, e isso é decisão registrada.</b> Publicar in-process pelo Mediator desfaria a razão de a
/// tabela existir — e, feito dentro da transação do lote, faria um handler gravar no mesmo escopo transacional do
/// despachante: bastaria uma mensagem posterior falhar para o rollback desfazer, sem erro nenhum, o efeito de um
/// handler que já tinha dado certo.
/// </para>
/// <para>
/// <b>A entrega é at-least-once.</b> Se o processo cair entre a publicação e a marcação, a mensagem é publicada
/// de novo no ciclo seguinte — não há como evitar isso sem uma transação que cubra o banco e o destino ao mesmo
/// tempo, que é justamente o que não existe. Quem implementa esta interface, e quem reage ao evento, precisa
/// tolerar receber o mesmo evento duas vezes: o efeito é que deve acontecer uma vez só, não a entrega.
/// </para>
/// <para>
/// Recebe o evento já desserializado, não o JSON: traduzir o que está gravado na tabela de volta para um tipo é
/// assunto de quem conhece a tabela, e essa camada não a conhece.
/// </para>
/// </remarks>
public interface IOutboxPublisher
{
    /// <summary>
    /// Entrega o evento ao destino.
    /// </summary>
    /// <remarks>
    /// Lançar é o sinal de falha, e é deliberado: o despachante precisa da exception para gravar o motivo junto
    /// da mensagem. Um retorno booleano diria que falhou, mas não por quê — e "por que este evento não saiu?"
    /// é a pergunta que se faz meses depois, quando o log já rodou.
    /// </remarks>
    /// <param name="domainEvent">O evento, já desserializado a partir da mensagem.</param>
    /// <param name="cancellationToken">Cancelamento, propagado a partir do desligamento do despachante.</param>
    Task PublishAsync(IDomainEvent domainEvent, CancellationToken cancellationToken = default);
}
