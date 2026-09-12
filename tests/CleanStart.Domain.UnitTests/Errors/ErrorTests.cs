using CleanStart.Domain.Errors;

namespace CleanStart.Domain.UnitTests.Errors;

/// <summary>
/// Contrato do <see cref="Error"/>: igualdade estrutural e classificação por <see cref="ErrorType"/>.
/// </summary>
public sealed class ErrorTests
{
    [Fact]
    public void Errors_ComMesmoCodigoMensagemETipo_SaoIguais()
    {
        var um = Error.Validation("Order.SemItens", "O pedido precisa de ao menos um item.");
        var outro = Error.Validation("Order.SemItens", "O pedido precisa de ao menos um item.");

        // Igualdade estrutural é o que permite ao teste afirmar
        // `resultado.Error.Should().Be(DomainErrors.Order.SemItens)` em vez de comparar string solta.
        um.Should().Be(outro);
    }

    [Fact]
    public void Errors_ComCodigosDiferentes_NaoSaoIguais()
    {
        var um = Error.Validation("Order.SemItens", "mesma mensagem");
        var outro = Error.Validation("Order.Invalido", "mesma mensagem");

        um.Should().NotBe(outro);
    }

    [Theory]
    [InlineData(ErrorType.Validation)]
    [InlineData(ErrorType.NotFound)]
    [InlineData(ErrorType.Conflict)]
    [InlineData(ErrorType.Failure)]
    public void Factory_ClassificaComOTipoCorreto(ErrorType tipoEsperado)
    {
        // O tipo é o que a Api traduz em status HTTP na Fase 4; errar a classificação aqui vira resposta
        // com o status errado lá.
        Error erro = tipoEsperado switch
        {
            ErrorType.Validation => Error.Validation("C", "m"),
            ErrorType.NotFound => Error.NotFound("C", "m"),
            ErrorType.Conflict => Error.Conflict("C", "m"),
            ErrorType.Failure => Error.Failure("C", "m"),
            _ => throw new ArgumentOutOfRangeException(nameof(tipoEsperado)),
        };

        erro.Type.Should().Be(tipoEsperado);
    }

    [Fact]
    public void None_TemCodigoEMensagemVazios()
    {
        Error.None.Code.Should().BeEmpty();
        Error.None.Message.Should().BeEmpty();
    }

    [Fact]
    public void ToString_TrazCodigoEMensagem()
    {
        var erro = Error.NotFound("Order.NaoEncontrado", "Pedido não encontrado.");

        erro.ToString().Should().Be("Order.NaoEncontrado: Pedido não encontrado.");
    }

    [Fact]
    public void DomainErrors_General_NomeiaOCampoNaMensagem()
    {
        Error erro = DomainErrors.General.TextoObrigatorio("nome");

        erro.Type.Should().Be(ErrorType.Validation);
        erro.Message.Should().Contain("nome");
    }
}
