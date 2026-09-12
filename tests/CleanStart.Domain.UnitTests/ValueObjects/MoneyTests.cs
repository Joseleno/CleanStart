using CleanStart.Domain.Common;
using CleanStart.Domain.Errors;
using CleanStart.Domain.ValueObjects;

namespace CleanStart.Domain.UnitTests.ValueObjects;

/// <summary>
/// Criação e aritmética de <see cref="Money"/>, incluindo a recusa de operar moedas diferentes.
/// </summary>
public sealed class MoneyTests
{
    [Fact]
    public void Add_ComMoedasDiferentes_RetornaFalhaENaoLanca()
    {
        // Este é o critério de aceite da T1.2: a operação impossível se comunica por Result, não por
        // exception. Exception aqui obrigaria todo chamador a um try/catch para um caso previsível.
        Money dezReais = Money.Of(10m, "BRL").Value;
        Money cincoDolares = Money.Of(5m, "USD").Value;

        Result<Money> resultado = dezReais.Add(cincoDolares);

        resultado.IsFailure.Should().BeTrue();
        resultado.Error.Should().Be(DomainErrors.Money.MoedasIncompativeis("BRL", "USD"));
        resultado.Error.Type.Should().Be(ErrorType.Conflict);
    }

    [Fact]
    public void Add_ComMesmaMoeda_Soma()
    {
        Money dez = Money.Of(10m, "BRL").Value;
        Money cinco = Money.Of(5m, "BRL").Value;

        Result<Money> resultado = dez.Add(cinco);

        resultado.IsSuccess.Should().BeTrue();
        resultado.Value.Amount.Should().Be(15m);
        resultado.Value.Currency.Should().Be("BRL");
    }

    [Fact]
    public void Subtract_ComMoedasDiferentes_RetornaFalha()
    {
        Money reais = Money.Of(10m, "BRL").Value;
        Money dolares = Money.Of(5m, "USD").Value;

        reais.Subtract(dolares).IsFailure.Should().BeTrue();
    }

    [Fact]
    public void Subtract_PodeResultarEmNegativo()
    {
        Money cinco = Money.Of(5m, "BRL").Value;
        Money dez = Money.Of(10m, "BRL").Value;

        Result<Money> resultado = cinco.Subtract(dez);

        // Consequência de Money aceitar negativo: saldo devedor é representável sem um segundo tipo.
        resultado.IsSuccess.Should().BeTrue();
        resultado.Value.Amount.Should().Be(-5m);
    }

    [Fact]
    public void Multiply_EscalaMantendoAMoeda()
    {
        Money dez = Money.Of(10m, "BRL").Value;

        Money resultado = dez.Multiply(3m);

        // Multiply não devolve Result porque não há como falhar — e assinatura que não pode falhar não
        // finge que pode.
        resultado.Amount.Should().Be(30m);
        resultado.Currency.Should().Be("BRL");
    }

    [Fact]
    public void Zero_CriaValorZeradoNaMoeda()
    {
        Result<Money> resultado = Money.Zero("BRL");

        resultado.IsSuccess.Should().BeTrue();
        resultado.Value.Amount.Should().Be(0m);
    }

    [Theory]
    [InlineData(-50)]
    [InlineData(0)]
    [InlineData(1234.56)]
    public void Of_AceitaQualquerQuantia(decimal quantia)
    {
        // Estorno e desconto são quantias negativas legítimas. Restringir aqui exigiria um segundo tipo
        // para representá-los; quem precisa de "só positivo" valida no seu agregado.
        Money.Of(quantia, "BRL").IsSuccess.Should().BeTrue();
    }

    [Theory]
    [InlineData("brl", "BRL")]
    [InlineData("  usd  ", "USD")]
    public void Of_NormalizaAMoeda(string entrada, string esperada)
    {
        Result<Money> resultado = Money.Of(10m, entrada);

        resultado.IsSuccess.Should().BeTrue();
        resultado.Value.Currency.Should().Be(esperada);
    }

    [Theory]
    [InlineData("BR")]      // curta
    [InlineData("BRLL")]    // longa
    [InlineData("BR1")]     // com dígito
    [InlineData("")]
    [InlineData(null)]
    public void Of_ComMoedaInvalida_RetornaFalha(string? moeda)
    {
        Money.Of(10m, moeda!).IsFailure.Should().BeTrue();
    }

    [Fact]
    public void Equals_ComMesmaQuantiaEMoeda_SaoIguais()
    {
        Money um = Money.Of(10m, "BRL").Value;
        Money outro = Money.Of(10m, "BRL").Value;

        um.Should().Be(outro);
    }

    [Fact]
    public void Equals_MesmaQuantiaEmMoedasDiferentes_NaoSaoIguais()
    {
        Money reais = Money.Of(10m, "BRL").Value;
        Money dolares = Money.Of(10m, "USD").Value;

        // A razão de Money existir: sem a moeda na igualdade, dez reais valeriam dez dólares.
        reais.Should().NotBe(dolares);
    }

    [Fact]
    public void Add_NaoAlteraOsOperandos()
    {
        Money dez = Money.Of(10m, "BRL").Value;
        Money cinco = Money.Of(5m, "BRL").Value;

        _ = dez.Add(cinco);

        // Imutabilidade: a operação produz um novo valor em vez de mutar o original, que é o que permite
        // compartilhar a instância sem medo.
        dez.Amount.Should().Be(10m);
        cinco.Amount.Should().Be(5m);
    }
}
