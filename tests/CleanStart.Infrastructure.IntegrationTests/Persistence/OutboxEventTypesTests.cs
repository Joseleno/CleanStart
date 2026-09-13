using System.Reflection;
using CleanStart.Domain.Common;
using CleanStart.Infrastructure.Persistence.Outbox;

namespace CleanStart.Infrastructure.IntegrationTests.Persistence;

/// <summary>
/// Verifica que todo domain event tem nome curto registrado, e que o mapa é coerente nos dois sentidos.
/// </summary>
/// <remarks>
/// <para>
/// <b>Mora aqui e não em <c>ArchitectureTests</c></b> — que seria o lugar natural — porque
/// <c>OutboxEventTypes</c> é <c>internal</c> à Infrastructure, e só este projeto e o de testes funcionais
/// recebem <c>InternalsVisibleTo</c>. Abrir o tipo só para o teste alcançá-lo seria afrouxar encapsulamento de
/// produção por conveniência de teste. Não precisa de banco: é reflexão pura.
/// </para>
/// <para>
/// O que estes testes previnem é um esquecimento silencioso de leitura e barulhento de escrita: quem cria um
/// evento novo e não o registra só descobre quando a gravação falhar. Melhor descobrir aqui.
/// </para>
/// </remarks>
public sealed class OutboxEventTypesTests
{
    private static readonly Assembly Domain = typeof(CleanStart.Domain.AssemblyMarker).Assembly;

    [Fact]
    public void TodoDomainEvent_TemNomeCurtoRegistrado()
    {
        List<Type> eventos =
        [
            .. Domain.GetTypes()
                .Where(tipo => typeof(IDomainEvent).IsAssignableFrom(tipo))
                .Where(tipo => tipo is { IsInterface: false, IsAbstract: false })
        ];

        // Guarda contra o teste virar vacuidade: se o filtro parar de encontrar eventos, ele passaria sem
        // verificar nada — e continuaria verde no dia em que um evento novo nascesse sem registro.
        eventos.Should().NotBeEmpty("o teste precisa ter eventos para inspecionar");

        List<Type> semRegistro = [.. eventos.Except(OutboxEventTypes.Registrados)];

        semRegistro.Should().BeEmpty(
            "todo domain event precisa de uma linha em OutboxEventTypes para poder ser despachado; " +
            $"faltam: {string.Join(", ", semRegistro.Select(tipo => tipo.Name))}");
    }

    [Fact]
    public void OMapa_ResolveNosDoisSentidos()
    {
        // Escrita e leitura usam dicionários distintos, e um derivado do outro. Se a derivação quebrar, uma
        // mensagem passaria a ser gravada com um nome que ninguém consegue ler de volta — e o sintoma seria
        // evento que nunca chega, sem erro nenhum.
        foreach (Type evento in OutboxEventTypes.Registrados)
        {
            string nome = OutboxEventTypes.NomeDe(evento);

            OutboxEventTypes.TipoDe(nome).Should().Be(evento, $"'{nome}' precisa voltar para {evento.Name}");
        }
    }

    [Fact]
    public void EventoNaoRegistrado_FalhaAoGravar()
    {
        // Falha explícita, e não silenciosa: gravar uma mensagem cujo nome o despachante não sabe interpretar
        // produziria uma linha que nunca sai da fila e não acusa nada.
        Action gravar = () => OutboxEventTypes.NomeDe(typeof(EventoDeMentira));

        gravar.Should().Throw<InvalidOperationException>()
            .WithMessage("*não está registrado*");
    }

    [Fact]
    public void NomeDesconhecidoNaLeitura_DevolveNuloEmVezDeLancar()
    {
        // O lado da leitura é deliberadamente mais tolerante que o da escrita: uma mensagem antiga, de um evento
        // que já não existe no código, não pode derrubar o despachante — ela travaria a fila inteira atrás de si.
        OutboxEventTypes.TipoDe("evento-que-nunca-existiu").Should().BeNull();
    }

    private sealed record EventoDeMentira(DateTimeOffset OccurredOn) : IDomainEvent;
}
