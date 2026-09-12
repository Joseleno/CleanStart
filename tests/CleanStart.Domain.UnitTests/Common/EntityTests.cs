using CleanStart.Domain.Common;

namespace CleanStart.Domain.UnitTests.Common;

/// <summary>
/// Igualdade por identidade de <see cref="Entity{TId}"/>: o id é a identidade, o conteúdo não conta.
/// </summary>
public sealed class EntityTests
{
    private sealed class Pedido(Guid id, string descricao) : Entity<Guid>(id)
    {
        public string Descricao { get; } = descricao;
    }

    /// <summary>Outra entidade com o mesmo tipo de id, para provar que o tipo conta.</summary>
    private sealed class Cliente(Guid id) : Entity<Guid>(id);

    [Fact]
    public void Equals_ComMesmoId_SaoIguais()
    {
        var id = Guid.NewGuid();
        var um = new Pedido(id, "primeira descrição");
        var outro = new Pedido(id, "descrição completamente diferente");

        // O conteúdo divergir não importa: é a mesma entidade em dois estados. É isso que distingue
        // entidade de value object.
        um.Should().Be(outro);
        (um == outro).Should().BeTrue();
    }

    [Fact]
    public void Equals_ComIdsDiferentes_NaoSaoIguais()
    {
        var um = new Pedido(Guid.NewGuid(), "mesma descrição");
        var outro = new Pedido(Guid.NewGuid(), "mesma descrição");

        um.Should().NotBe(outro);
    }

    [Fact]
    public void Equals_EntreTiposDiferentesComMesmoId_NaoSaoIguais()
    {
        var id = Guid.NewGuid();
        var pedido = new Pedido(id, "qualquer");
        var cliente = new Cliente(id);

        // Um Order e um Customer podem ter o mesmo Guid sem qualquer relação — a colisão é possível
        // quando os ids vêm de fontes distintas, e sem a checagem de tipo viraria igualdade.
        pedido.Equals(cliente).Should().BeFalse();
    }

    [Fact]
    public void GetHashCode_ComMesmoId_EIgual()
    {
        var id = Guid.NewGuid();
        var um = new Pedido(id, "uma");
        var outro = new Pedido(id, "outra");

        um.GetHashCode().Should().Be(outro.GetHashCode());
    }

    [Fact]
    public void GetHashCode_EntreTiposDiferentesComMesmoId_Difere()
    {
        var id = Guid.NewGuid();
        var pedido = new Pedido(id, "qualquer");
        var cliente = new Cliente(id);

        // Consequência de GetHashCode combinar o tipo: sem isso, entidades de tipos diferentes com o
        // mesmo id cairiam no mesmo bucket, que é colisão inútil num dicionário misto.
        pedido.GetHashCode().Should().NotBe(cliente.GetHashCode());
    }

    [Fact]
    public void Equals_ComNulo_NaoEIgual()
    {
        var pedido = new Pedido(Guid.NewGuid(), "qualquer");

        pedido.Equals(null).Should().BeFalse();
        (pedido == null).Should().BeFalse();
    }

    [Fact]
    public void Construtor_ComIdNulo_Lanca()
    {
        // Entidade sem identidade não é entidade. Exception e não Result: id nulo é bug de programação,
        // não regra de negócio que o usuário possa violar.
        Action criar = () => _ = new EntidadeDeIdReferencia(null!);

        criar.Should().Throw<ArgumentNullException>();
    }

    private sealed class EntidadeDeIdReferencia(string id) : Entity<string>(id);
}
