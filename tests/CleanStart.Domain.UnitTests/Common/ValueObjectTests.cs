using CleanStart.Domain.Common;

namespace CleanStart.Domain.UnitTests.Common;

/// <summary>
/// Igualdade estrutural de <see cref="ValueObject"/>: o conteúdo é a identidade.
/// </summary>
public sealed class ValueObjectTests
{
    // Dublês locais: testar a base com um value object de verdade (Money) acoplaria este teste às
    // regras dele. O que está sob teste aqui é a igualdade da base, nada mais.
    private sealed class Dinheiro(decimal valor, string moeda) : ValueObject
    {
        public decimal Valor { get; } = valor;

        public string Moeda { get; } = moeda;

        protected override IEnumerable<object?> GetEqualityComponents()
        {
            yield return Valor;
            yield return Moeda;
        }
    }

    /// <summary>Mesma forma que <see cref="Dinheiro"/>, para provar que o tipo conta na igualdade.</summary>
    private sealed class Quantidade(decimal valor, string moeda) : ValueObject
    {
        public decimal Valor { get; } = valor;

        public string Moeda { get; } = moeda;

        protected override IEnumerable<object?> GetEqualityComponents()
        {
            yield return Valor;
            yield return Moeda;
        }
    }

    [Fact]
    public void Equals_ComMesmosComponentes_SaoIguais()
    {
        Dinheiro um = new(10m, "BRL");
        Dinheiro outro = new(10m, "BRL");

        um.Should().Be(outro);
        (um == outro).Should().BeTrue();
        (um != outro).Should().BeFalse();
    }

    [Fact]
    public void Equals_ComComponenteDiferente_NaoSaoIguais()
    {
        Dinheiro dezReais = new(10m, "BRL");
        Dinheiro dezDolares = new(10m, "USD");
        Dinheiro cincoReais = new(5m, "BRL");

        dezReais.Should().NotBe(dezDolares);
        dezReais.Should().NotBe(cincoReais);
    }

    [Fact]
    public void Equals_EntreTiposDiferentesComMesmaForma_NaoSaoIguais()
    {
        // A armadilha que a checagem de GetType() evita: sem ela, qualquer par de value objects com os
        // mesmos componentes passaria por igual, e um Dinheiro valeria uma Quantidade.
        Dinheiro dinheiro = new(10m, "BRL");
        Quantidade quantidade = new(10m, "BRL");

        dinheiro.Equals(quantidade).Should().BeFalse();
    }

    [Fact]
    public void GetHashCode_ComMesmosComponentes_EIgual()
    {
        Dinheiro um = new(10m, "BRL");
        Dinheiro outro = new(10m, "BRL");

        // Sem isto, dois value objects iguais ocupariam entradas distintas num Dictionary ou HashSet —
        // o contrato de GetHashCode é o que faz a igualdade funcionar em coleção.
        um.GetHashCode().Should().Be(outro.GetHashCode());
    }

    [Fact]
    public void Equals_ComNulo_NaoEIgual()
    {
        Dinheiro dinheiro = new(10m, "BRL");

        dinheiro.Equals(null).Should().BeFalse();
        (dinheiro == null).Should().BeFalse();
        (null == dinheiro).Should().BeFalse();
    }

    [Fact]
    public void Equals_DoisNulos_SaoIguais()
    {
        Dinheiro? um = null;
        Dinheiro? outro = null;

        (um == outro).Should().BeTrue();
    }
}
