using CleanStart.Application.Common.Abstractions;
using CleanStart.Domain.Common;
using CleanStart.Domain.Customers;
using CleanStart.Domain.Orders;
using CleanStart.Domain.Orders.Events;
using CleanStart.Domain.ValueObjects;
using CleanStart.Infrastructure.Configuration;
using CleanStart.Infrastructure.Persistence;
using CleanStart.Infrastructure.Persistence.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace CleanStart.Infrastructure.IntegrationTests.Persistence;

/// <summary>
/// Exercita o despachante contra um PostgreSQL real.
/// </summary>
/// <remarks>
/// <para>
/// <b>Precisa ser contra o banco de verdade.</b> O que este componente tem de arriscado é justamente o que não
/// existe fora do PostgreSQL: o <c>FOR UPDATE SKIP LOCKED</c>, o SQL cru com os nomes de coluna em snake_case, e
/// o <c>now()</c> do servidor decidindo o que já está elegível. Um provider em memória aprovaria tudo isso sem
/// executar nada.
/// </para>
/// <para>
/// Os testes desta classe compartilham o banco e podem rodar junto com os das outras. Por isso cada um filtra
/// pelas mensagens do próprio pedido, nunca "as pendentes" em geral.
/// </para>
/// </remarks>
public sealed class OutboxProcessorTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>
{
    /// <summary>Publisher que conta as entregas e, se configurado, falha.</summary>
    private sealed class PublisherDeTeste : IOutboxPublisher
    {
        public List<IDomainEvent> Entregues { get; } = [];

        public Exception? FalhaProgramada { get; set; }

        public Task PublishAsync(IDomainEvent domainEvent, CancellationToken cancellationToken = default)
        {
            if (FalhaProgramada is not null)
            {
                return Task.FromException(FalhaProgramada);
            }

            Entregues.Add(domainEvent);
            return Task.CompletedTask;
        }
    }

    private static Customer ClienteNovo() => Customer.Register(
        "Maria Souza",
        Email.Of("maria@example.com").Value,
        Document.Of(DocumentoNovo()).Value,
        PostgresFixture.Agora).Value;

    /// <summary>
    /// Gera um CPF válido e distinto a cada chamada.
    /// </summary>
    /// <remarks>
    /// O índice de documento é único e os testes compartilham a base: um literal fixo faria um teste derrubar o
    /// outro conforme a ordem de execução. E o dígito verificador precisa ser calculado — o <c>Document</c>
    /// valida de verdade, então número inventado é rejeitado no construtor, não no banco.
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

    private static Order PedidoDe(CustomerId clienteId) => Order.Place(
        clienteId,
        [(ProductId.New(), 1, Money.Of(25m, "BRL").Value)],
        PostgresFixture.Agora).Value;

    private static OutboxProcessor Construir(
        AppDbContext contexto,
        IOutboxPublisher publisher,
        DateTimeOffset agora,
        OutboxOptions? opcoes = null)
    {
        IDateTimeProvider clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(agora);

        return new OutboxProcessor(
            contexto,
            publisher,
            clock,
            Options.Create(opcoes ?? new OutboxOptions()),
            NullLogger<OutboxProcessor>.Instance);
    }

    /// <summary>Grava um pedido e devolve a mensagem de outbox que o interceptor criou.</summary>
    private async Task<(OrderId PedidoId, Guid MensagemId)> SemearPedidoAsync(CancellationToken ct)
    {
        Customer cliente = ClienteNovo();
        Order pedido = PedidoDe(cliente.Id);

        await using AppDbContext contexto = fixture.CriarContexto();
        contexto.Add(cliente);
        contexto.Add(pedido);
        await contexto.SaveChangesAsync(ct);

        // Filtra por Type no SQL e pelo conteúdo em MEMÓRIA: Content é jsonb, e o Contains traduzido vira o
        // operador LIKE, que o PostgreSQL não define para jsonb (42883). Mesma armadilha anotada em
        // PersistenciaTests. Os testes compartilham a base, então o filtro pelo id do pedido é o que impede um
        // teste de pegar a mensagem de outro.
        List<OutboxMessage> candidatas = await contexto.OutboxMessages
            .AsNoTracking()
            .Where(mensagem => mensagem.Type == "order-placed")
            .ToListAsync(ct);

        Guid mensagemId = candidatas
            .Single(mensagem => mensagem.Content.Contains(
                pedido.Id.Value.ToString(),
                StringComparison.OrdinalIgnoreCase))
            .Id;

        return (pedido.Id, mensagemId);
    }

    private async Task<OutboxMessage> LerAsync(Guid mensagemId, CancellationToken ct)
    {
        await using AppDbContext contexto = fixture.CriarContexto();

        return await contexto.OutboxMessages.AsNoTracking().SingleAsync(m => m.Id == mensagemId, ct);
    }

    [Fact]
    public async Task MensagemPendente_EPublicadaEMarcadaComoProcessada()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        (OrderId pedidoId, Guid mensagemId) = await SemearPedidoAsync(ct);

        PublisherDeTeste publisher = new();

        await using (AppDbContext contexto = fixture.CriarContexto())
        {
            OutboxProcessor processador = Construir(contexto, publisher, PostgresFixture.Agora);
            await processador.ProcessarLoteAsync(ct);
        }

        // O lote pode conter mensagens semeadas por outros testes da classe — a base é compartilhada. O que
        // importa é que o evento DESTE pedido saiu, e como o tipo original, não como JSON.
        publisher.Entregues.OfType<OrderPlacedEvent>()
            .Should().ContainSingle(evento => evento.OrderId == pedidoId);

        OutboxMessage depois = await LerAsync(mensagemId, ct);
        depois.ProcessedOn.Should().NotBeNull("a mensagem saiu");
        depois.Error.Should().BeNull();
        depois.Attempts.Should().Be(1, "a tentativa é contabilizada mesmo quando dá certo");
    }

    [Fact]
    public async Task MensagemJaProcessada_NaoEPublicadaDeNovo()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        (OrderId pedidoId, Guid mensagemId) = await SemearPedidoAsync(ct);

        PublisherDeTeste publisher = new();

        // Dois ciclos seguidos. O segundo não deve reencontrar a mensagem: é o que garante que um worker
        // rodando a cada poucos segundos não republique tudo indefinidamente.
        for (int ciclo = 0; ciclo < 2; ciclo++)
        {
            await using AppDbContext contexto = fixture.CriarContexto();
            OutboxProcessor processador = Construir(contexto, publisher, PostgresFixture.Agora);
            await processador.ProcessarLoteAsync(ct);
        }

        // Conta só as entregas deste pedido: a base é compartilhada com os demais testes da classe.
        publisher.Entregues.OfType<OrderPlacedEvent>()
            .Count(evento => evento.OrderId == pedidoId)
            .Should().Be(1, "processar duas vezes não duplica a entrega");

        OutboxMessage depois = await LerAsync(mensagemId, ct);
        depois.Attempts.Should().Be(1);
    }

    [Fact]
    public async Task FalhaAoPublicar_GravaOErroEAgendaNovaTentativa()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        (_, Guid mensagemId) = await SemearPedidoAsync(ct);

        PublisherDeTeste publisher = new() { FalhaProgramada = new InvalidOperationException("broker fora do ar") };

        await using (AppDbContext contexto = fixture.CriarContexto())
        {
            OutboxProcessor processador = Construir(contexto, publisher, PostgresFixture.Agora);
            await processador.ProcessarLoteAsync(ct);
        }

        OutboxMessage depois = await LerAsync(mensagemId, ct);

        depois.ProcessedOn.Should().BeNull("não saiu, então continua pendente");
        depois.Error.Should().Contain("broker fora do ar", "o motivo fica na tabela, não só no log");
        depois.Attempts.Should().Be(1);

        // O atraso é 10s (base) mais até 20% de variação — a asserção é sobre o intervalo, e não sobre um
        // instante exato, porque a variação aleatória é deliberada (evita que todas as mensagens voltem juntas).
        depois.NextAttemptOn.Should().BeOnOrAfter(PostgresFixture.Agora.AddSeconds(10));
        depois.NextAttemptOn.Should().BeBefore(PostgresFixture.Agora.AddSeconds(12.1));
    }

    [Fact]
    public async Task MensagemEmBackoff_NaoELidaAntesDoPrazo()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        (_, Guid mensagemId) = await SemearPedidoAsync(ct);

        // Empurra a próxima tentativa para o futuro, como uma falha teria feito.
        await using (AppDbContext preparo = fixture.CriarContexto())
        {
            OutboxMessage mensagem = await preparo.OutboxMessages.SingleAsync(m => m.Id == mensagemId, ct);
            mensagem.Attempts = 1;
            mensagem.NextAttemptOn = DateTimeOffset.UtcNow.AddMinutes(30);
            await preparo.SaveChangesAsync(ct);
        }

        PublisherDeTeste publisher = new();

        await using (AppDbContext contexto = fixture.CriarContexto())
        {
            OutboxProcessor processador = Construir(contexto, publisher, PostgresFixture.Agora);
            await processador.ProcessarLoteAsync(ct);
        }

        publisher.Entregues.Should().BeEmpty("o prazo do backoff ainda não venceu");

        OutboxMessage depois = await LerAsync(mensagemId, ct);
        depois.Attempts.Should().Be(1, "nem sequer foi tentada");
    }

    [Fact]
    public async Task TentativasEsgotadas_ParamDeSerLidas()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        (_, Guid mensagemId) = await SemearPedidoAsync(ct);

        OutboxOptions opcoes = new() { MaxAttempts = 3 };

        await using (AppDbContext preparo = fixture.CriarContexto())
        {
            OutboxMessage mensagem = await preparo.OutboxMessages.SingleAsync(m => m.Id == mensagemId, ct);
            mensagem.Attempts = 3;
            mensagem.NextAttemptOn = PostgresFixture.Agora.AddDays(-1);
            mensagem.Error = "falhou demais";
            await preparo.SaveChangesAsync(ct);
        }

        PublisherDeTeste publisher = new();

        await using (AppDbContext contexto = fixture.CriarContexto())
        {
            OutboxProcessor processador = Construir(contexto, publisher, PostgresFixture.Agora, opcoes);
            await processador.ProcessarLoteAsync(ct);
        }

        // É o dead-letter deste projeto: a mensagem deixa de satisfazer o filtro e fica na tabela com o erro.
        publisher.Entregues.Should().BeEmpty("esgotou as tentativas");

        OutboxMessage depois = await LerAsync(mensagemId, ct);
        depois.ProcessedOn.Should().BeNull("continua pendente, e é assim que se encontra o que morreu");
        depois.Attempts.Should().Be(3, "não é tentada de novo");
    }

    [Fact]
    public async Task TipoDesconhecido_NaoDerrubaOLote()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        (_, Guid mensagemBoa) = await SemearPedidoAsync(ct);

        // Uma mensagem de um evento que não existe mais no código, como aconteceria depois de uma remoção.
        var mensagemOrfa = Guid.CreateVersion7();

        await using (AppDbContext preparo = fixture.CriarContexto())
        {
            preparo.OutboxMessages.Add(new OutboxMessage
            {
                Id = mensagemOrfa,
                Type = "evento-que-nao-existe-mais",
                Content = """{"algo":1}""",
                OccurredOn = PostgresFixture.Agora.AddMinutes(-1),
                NextAttemptOn = PostgresFixture.Agora.AddMinutes(-1),
            });
            await preparo.SaveChangesAsync(ct);
        }

        PublisherDeTeste publisher = new();

        await using (AppDbContext contexto = fixture.CriarContexto())
        {
            OutboxProcessor processador = Construir(contexto, publisher, PostgresFixture.Agora);
            await processador.ProcessarLoteAsync(ct);
        }

        // A órfã é contabilizada como falha e sai do caminho pelas tentativas; a boa passa no mesmo lote.
        OutboxMessage boa = await LerAsync(mensagemBoa, ct);
        boa.ProcessedOn.Should().NotBeNull("a mensagem válida do lote não pode ser bloqueada pela órfã");

        OutboxMessage orfa = await LerAsync(mensagemOrfa, ct);
        orfa.ProcessedOn.Should().BeNull();
        orfa.Error.Should().Contain("desconhecido");
    }

    [Fact]
    public async Task Retencao_RemoveProcessadaAntigaEPreservaAsDemais()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        OutboxOptions opcoes = new() { ProcessedRetentionHours = 24 };
        DateTimeOffset agora = DateTimeOffset.UtcNow;

        var antiga = Guid.CreateVersion7();
        var recente = Guid.CreateVersion7();
        var pendenteAntiga = Guid.CreateVersion7();

        await using (AppDbContext preparo = fixture.CriarContexto())
        {
            // Processada há três dias: passou da janela.
            preparo.OutboxMessages.Add(MensagemDeTeste(antiga, agora.AddDays(-3), processadaEm: agora.AddDays(-3)));

            // Processada há uma hora: ainda dentro da janela.
            preparo.OutboxMessages.Add(MensagemDeTeste(recente, agora.AddHours(-1), processadaEm: agora.AddHours(-1)));

            // Velha, mas NUNCA despachada — a retenção não pode tocá-la, ou o evento se perde para sempre.
            preparo.OutboxMessages.Add(MensagemDeTeste(pendenteAntiga, agora.AddDays(-3), processadaEm: null));

            await preparo.SaveChangesAsync(ct);
        }

        await using (AppDbContext contexto = fixture.CriarContexto())
        {
            OutboxProcessor processador = Construir(contexto, new PublisherDeTeste(), agora, opcoes);
            await processador.AplicarRetencaoAsync(ct);
        }

        await using AppDbContext leitura = fixture.CriarContexto();
        List<Guid> restantes = await leitura.OutboxMessages
            .AsNoTracking()
            .Where(m => m.Id == antiga || m.Id == recente || m.Id == pendenteAntiga)
            .Select(m => m.Id)
            .ToListAsync(ct);

        restantes.Should().NotContain(antiga, "passou da janela de retenção");
        restantes.Should().Contain(recente, "ainda está dentro da janela");
        restantes.Should().Contain(pendenteAntiga, "a retenção nunca apaga o que não foi despachado");
    }

    [Fact]
    public async Task ConsultaDePendentes_UsaOIndiceParcial()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;

        await using AppDbContext contexto = fixture.CriarContexto();

        // O PostgreSQL só usa um índice parcial quando consegue PROVAR que a consulta implica o predicado dele.
        // Basta o WHERE divergir do filtro do índice — uma condição a mais, um parâmetro no lugar do literal —
        // para o planejador cair em Seq Scan, sem erro nenhum: o despachante continua correto e fica lento
        // conforme a tabela cresce. Este teste é o que transforma essa regressão silenciosa em falha visível.
        //
        // Nota: o planejador escolhe Seq Scan em tabela pequena, e com razão. O ENABLE_SEQSCAN desligado força
        // a comparação a ser "o índice é usável?", que é a pergunta real, e não "vale a pena agora?".
        await contexto.Database.ExecuteSqlRawAsync("SET LOCAL enable_seqscan = off", ct);

        List<string> plano = await contexto.Database
            .SqlQuery<string>($"""
                EXPLAIN SELECT id
                          FROM outbox_messages
                         WHERE processed_on IS NULL
                           AND attempts < 5
                           AND next_attempt_on <= now()
                         ORDER BY next_attempt_on, occurred_on
                         LIMIT 20
                """)
            .ToListAsync(ct);

        string planoCompleto = string.Join(Environment.NewLine, plano);

        planoCompleto.Should().Contain(
            "ix_outbox_messages_pendentes",
            "a consulta do despachante precisa casar com o filtro do índice parcial");
    }

    private static OutboxMessage MensagemDeTeste(Guid id, DateTimeOffset ocorridoEm, DateTimeOffset? processadaEm) =>
        new()
        {
            Id = id,
            Type = "order-placed",
            Content = """{"teste":true}""",
            OccurredOn = ocorridoEm,
            NextAttemptOn = ocorridoEm,
            ProcessedOn = processadaEm,
        };
}
