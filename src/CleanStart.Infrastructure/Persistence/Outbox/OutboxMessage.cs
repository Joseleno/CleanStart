namespace CleanStart.Infrastructure.Persistence.Outbox;

/// <summary>
/// Um domain event serializado, aguardando despacho.
/// </summary>
/// <remarks>
/// <para>
/// <b>Por que a tabela existe.</b> Gravar no banco e publicar numa fila são dois sistemas distintos, e não há
/// transação que cubra os dois: se o processo cai entre o commit e o publish, o fato aconteceu e ninguém foi
/// avisado; se publica antes do commit, avisa de um fato que a transação vai desfazer. O outbox resolve gravando
/// o evento **na mesma transação** do dado — e um processo separado o despacha depois, relendo a tabela.
/// </para>
/// <para>
/// Vive na Infrastructure e não no Domain: é mecanismo de entrega, não conceito de negócio. O domínio levanta
/// <c>IDomainEvent</c> e não sabe que existe fila.
/// </para>
/// <para>
/// É uma classe de persistência, sem regra: por isso tem setters e construtor sem parâmetros, ao contrário das
/// entidades de domínio. A regra de arquitetura que exige setter privado só alcança quem herda de
/// <c>Entity&lt;TId&gt;</c>, e esta classe deliberadamente não herda.
/// </para>
/// </remarks>
internal sealed class OutboxMessage
{
    /// <summary>Identidade da mensagem.</summary>
    public Guid Id { get; set; }

    /// <summary>Nome do tipo do evento, usado para desserializar no consumo.</summary>
    /// <remarks>
    /// Guardado como texto, não como <c>Type</c>: o consumidor pode ser outro processo, e o nome assembly-qualified
    /// amarraria a mensagem à versão do assembly que a gravou.
    /// </remarks>
    public string Type { get; set; } = string.Empty;

    /// <summary>O evento serializado em JSON.</summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>Quando o evento ocorreu.</summary>
    public DateTimeOffset OccurredOn { get; set; }

    /// <summary>Quando foi despachado, ou nulo se ainda está pendente.</summary>
    /// <remarks>
    /// Nulo é o sinal de "pendente" — é por esta coluna que o despachante encontra o que falta enviar.
    /// </remarks>
    public DateTimeOffset? ProcessedOn { get; set; }

    /// <summary>Erro do último despacho, se houve.</summary>
    /// <remarks>
    /// Guardar o erro em vez de só logar permite responder "por que este evento não saiu?" consultando a tabela,
    /// meses depois, sem depender de log retido.
    /// </remarks>
    public string? Error { get; set; }
}
