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

    /// <summary>Nome curto do evento (por exemplo <c>order-placed</c>), usado para desserializar no consumo.</summary>
    /// <remarks>
    /// Guardado como texto, não como <c>Type</c>: o consumidor pode ser outro processo. E é um nome próprio, não
    /// o nome da classe — o mapa e o motivo estão em <see cref="OutboxEventTypes"/>.
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

    /// <summary>Quantas vezes o despacho já foi tentado.</summary>
    /// <remarks>
    /// <para>
    /// Sem contador não existe backoff: o despachante não teria como distinguir uma mensagem nova de outra que
    /// falhou cinquenta vezes, e reprocessaria a que está morta a cada ciclo, para sempre.
    /// </para>
    /// <para>
    /// <b>Também é o mecanismo de dead-letter</b>, e ele é a <i>ausência</i> de uma condição: quando o contador
    /// alcança o máximo, a mensagem simplesmente deixa de satisfazer o filtro da consulta e para de ser lida. Ela
    /// continua aqui, com o <see cref="Error"/> dizendo por quê. Em volume real isto viraria uma tabela
    /// <c>outbox_dead_letters</c> separada — mantém a tabela quente pequena e permite retenção independente —,
    /// mas aqui o custo seria uma tabela a mais para o leitor atravessar, sem ensinar nada que a cláusula
    /// <c>WHERE</c> não ensine.
    /// </para>
    /// <para>
    /// O máximo vive em <c>OutboxOptions</c>, nunca aqui nem no filtro parcial do índice: gravado no schema,
    /// mudar de cinco para três tentativas viraria uma migration.
    /// </para>
    /// </remarks>
    public int Attempts { get; set; }

    /// <summary>A partir de quando esta mensagem pode ser tentada de novo.</summary>
    /// <remarks>
    /// <para>
    /// <b>Nasce igual a <see cref="OccurredOn"/> e nunca é nulo</b> — uma mensagem que jamais falhou já está
    /// pronta. O campo nulo seria a modelagem mais óbvia, e é justamente a que não funciona: exigiria
    /// <c>NULLS FIRST</c> no índice, porque o padrão do PostgreSQL em ordem ascendente é <c>NULLS LAST</c> e
    /// mensagens novas ficariam atrás de todas as que estão em retry — prioridade invertida. E <c>NULLS FIRST</c>
    /// não é expressável pela Fluent API do EF Core: exigiria SQL cru na configuração, quebrando a convenção de
    /// que todo o mapeamento deste projeto é Fluent API.
    /// </para>
    /// <para>
    /// Não dá para derivar o backoff de <see cref="OccurredOn"/>: aquele é o instante do fato de negócio, copiado
    /// do evento, e não muda quando uma tentativa falha.
    /// </para>
    /// </remarks>
    public DateTimeOffset NextAttemptOn { get; set; }
}
