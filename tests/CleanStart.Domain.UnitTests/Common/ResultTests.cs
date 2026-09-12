using CleanStart.Domain.Common;
using CleanStart.Domain.Errors;

namespace CleanStart.Domain.UnitTests.Common;

/// <summary>
/// Contrato do <see cref="Result"/> e do <see cref="Result{TValue}"/>, incluindo as invariantes que
/// impedem estados contraditórios.
/// </summary>
public sealed class ResultTests
{
    private static readonly Error ErroQualquer = Error.Validation("Teste.Invalido", "Inválido para teste.");

    [Fact]
    public void Success_SemValor_EhSucessoSemErro()
    {
        var resultado = Result.Success();

        resultado.IsSuccess.Should().BeTrue();
        resultado.IsFailure.Should().BeFalse();
        resultado.Error.Should().Be(Error.None);
    }

    [Fact]
    public void Failure_SemValor_EhFalhaComErro()
    {
        var resultado = Result.Failure(ErroQualquer);

        resultado.IsFailure.Should().BeTrue();
        resultado.IsSuccess.Should().BeFalse();
        resultado.Error.Should().Be(ErroQualquer);
    }

    [Fact]
    public void Success_ComValor_ExpoeOValor()
    {
        var resultado = Result.Success(42);

        resultado.IsSuccess.Should().BeTrue();
        resultado.Value.Should().Be(42);
    }

    [Fact]
    public void Failure_ComValor_NaoExpoeOValor()
    {
        var resultado = Result.Failure<int>(ErroQualquer);

        resultado.IsFailure.Should().BeTrue();

        // Ler o valor de uma falha é bug de quem chama — esqueceu de checar IsFailure. Lançar faz o bug
        // aparecer aqui; devolver default(int) o deixaria viajar como um 0 plausível.
        Action lerValor = () => _ = resultado.Value;

        lerValor.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Failure_ComErroNone_Lanca()
    {
        // Falha sem erro é estado contraditório: o consumidor perguntaria "o que deu errado?" e receberia
        // Error.None. Barrado na construção para que nenhum Result inválido exista.
        Action criar = () => _ = Result.Failure(Error.None);

        criar.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Match_EmSucesso_ExecutaOnSuccess()
    {
        var resultado = Result.Success(10);

        string saida = resultado.Match(
            onSuccess: valor => $"valor {valor}",
            onFailure: erro => $"erro {erro.Code}");

        saida.Should().Be("valor 10");
    }

    [Fact]
    public void Match_EmFalha_ExecutaOnFailure()
    {
        var resultado = Result.Failure<int>(ErroQualquer);

        string saida = resultado.Match(
            onSuccess: valor => $"valor {valor}",
            onFailure: erro => $"erro {erro.Code}");

        saida.Should().Be("erro Teste.Invalido");
    }

    [Fact]
    public void ConversaoImplicita_DeValor_ViraSucesso()
    {
        // Açúcar que encurta o `return` das factories de domínio: `return novoPedido;` em vez de
        // `return Result.Success(novoPedido);`.
        Result<string> resultado = "pronto";

        resultado.IsSuccess.Should().BeTrue();
        resultado.Value.Should().Be("pronto");
    }
}
