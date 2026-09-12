using CleanStart.Domain.Common;

namespace CleanStart.Domain.UnitTests.Common;

/// <summary>
/// Acúmulo e descarte de domain events em <see cref="AggregateRoot{TId}"/>.
/// </summary>
public sealed class AggregateRootTests
{
    private sealed record AlgoAconteceu(DateTimeOffset OccurredOn) : IDomainEvent;

    private sealed class Pedido(Guid id) : AggregateRoot<Guid>(id)
    {
        public void FazerAlgo() => RaiseDomainEvent(new AlgoAconteceu(DateTimeOffset.UtcNow));
    }

    [Fact]
    public void NovoAgregado_NaoTemEventos()
    {
        Pedido pedido = new(Guid.NewGuid());

        pedido.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void RaiseDomainEvent_AcumulaNaOrdem()
    {
        Pedido pedido = new(Guid.NewGuid());

        pedido.FazerAlgo();
        pedido.FazerAlgo();

        pedido.DomainEvents.Should().HaveCount(2);
        pedido.DomainEvents.Should().AllBeOfType<AlgoAconteceu>();
    }

    [Fact]
    public void ClearDomainEvents_DescartaTudo()
    {
        Pedido pedido = new(Guid.NewGuid());
        pedido.FazerAlgo();

        pedido.ClearDomainEvents();

        // Sem o descarte depois do despacho, um segundo SaveChanges na mesma instância republicaria os
        // mesmos eventos — e um evento publicado duas vezes é um e-mail enviado duas vezes.
        pedido.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void DomainEvents_NaoPermiteAlteracaoPorFora()
    {
        Pedido pedido = new(Guid.NewGuid());
        pedido.FazerAlgo();

        // A coleção exposta não é a List interna disfarçada: forçar um cast e tentar mutar falha, em vez
        // de devolver a quem chama o controle de uma invariante que pertence ao agregado.
        // (ReadOnlyCollection implementa ICollection explicitamente e lança em Add/Clear.)
        Action mutar = () => ((ICollection<IDomainEvent>)pedido.DomainEvents).Clear();

        mutar.Should().Throw<NotSupportedException>();
        pedido.DomainEvents.Should().HaveCount(1);
    }

    [Fact]
    public void RaiseDomainEvent_ComEventoNulo_Lanca()
    {
        AgregadoQuePublicaNulo agregado = new(Guid.NewGuid());

        Action publicar = agregado.PublicarNulo;

        publicar.Should().Throw<ArgumentNullException>();
    }

    private sealed class AgregadoQuePublicaNulo(Guid id) : AggregateRoot<Guid>(id)
    {
        public void PublicarNulo() => RaiseDomainEvent(null!);
    }
}
