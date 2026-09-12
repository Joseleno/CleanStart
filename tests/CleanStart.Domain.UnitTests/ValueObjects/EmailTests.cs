using CleanStart.Domain.Common;
using CleanStart.Domain.ValueObjects;

namespace CleanStart.Domain.UnitTests.ValueObjects;

/// <summary>
/// Criação de <see cref="Email"/>: o que a validação de forma aceita e o que ela recusa.
/// </summary>
public sealed class EmailTests
{
    [Theory]
    [InlineData("joao@example.com")]
    [InlineData("joao.silva@example.com")]
    [InlineData("joao+tag@example.com")]
    [InlineData("joao@sub.example.com.br")]
    public void Of_ComEnderecoValido_RetornaSucesso(string entrada)
    {
        Email.Of(entrada).IsSuccess.Should().BeTrue($"'{entrada}' tem forma de endereço");
    }

    [Theory]
    [InlineData("sem-arroba.com")]
    [InlineData("@example.com")]          // sem parte local
    [InlineData("joao@")]                 // sem domínio
    [InlineData("joao@com")]              // domínio sem ponto
    [InlineData("joao@example.")]         // termina em ponto
    [InlineData("joao@@example.com")]     // dois arrobas
    [InlineData("joao@exa..mple.com")]    // pontos consecutivos
    [InlineData("joao silva@example.com")] // com espaço
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Of_ComEnderecoInvalido_RetornaFalha(string? entrada)
    {
        Email.Of(entrada!).IsFailure.Should().BeTrue($"'{entrada}' não tem forma de endereço");
    }

    [Theory]
    [InlineData("JOAO@EXAMPLE.COM", "joao@example.com")]
    [InlineData("  Joao@Example.com  ", "joao@example.com")]
    public void Of_NormalizaParaMinusculas(string entrada, string esperado)
    {
        Result<Email> resultado = Email.Of(entrada);

        // Sem normalizar, Joao@x.com e joao@x.com seriam endereços distintos — e o mesmo usuário
        // conseguiria se cadastrar duas vezes.
        resultado.Value.Value.Should().Be(esperado);
    }

    [Fact]
    public void Of_ComEnderecoAcimaDoLimite_RetornaFalha()
    {
        // RFC 5321 limita o endereço a 254 caracteres; acima disso não é entregável.
        string longo = new string('a', 250) + "@example.com";

        Email.Of(longo).IsFailure.Should().BeTrue();
    }

    [Fact]
    public void Equals_IgnorandoCaixa_SaoIguais()
    {
        Email um = Email.Of("joao@example.com").Value;
        Email outro = Email.Of("JOAO@example.com").Value;

        um.Should().Be(outro);
    }
}
