using CleanStart.Domain.Common;
using CleanStart.Domain.Errors;
using CleanStart.Domain.ValueObjects;

namespace CleanStart.Domain.UnitTests.ValueObjects;

/// <summary>
/// Validação de CPF e CNPJ por dígito verificador.
/// </summary>
public sealed class DocumentTests
{
    // CPFs e CNPJs com DV correto, gerados pelo algoritmo oficial. São fictícios: o que importa é que o
    // dígito verificador fecha, que é exatamente o que está sob teste.
    [Theory]
    [InlineData("529.982.247-25")]
    [InlineData("52998224725")]
    [InlineData("111.444.777-35")]
    public void Of_ComCpfValido_RetornaSucesso(string entrada)
    {
        Result<Document> resultado = Document.Of(entrada);

        resultado.IsSuccess.Should().BeTrue($"'{entrada}' é um CPF com dígito verificador correto");
        resultado.Value.Type.Should().Be(DocumentType.Cpf);
    }

    [Theory]
    [InlineData("11.222.333/0001-81")]
    [InlineData("11222333000181")]
    public void Of_ComCnpjValido_RetornaSucesso(string entrada)
    {
        Result<Document> resultado = Document.Of(entrada);

        resultado.IsSuccess.Should().BeTrue($"'{entrada}' é um CNPJ com dígito verificador correto");
        resultado.Value.Type.Should().Be(DocumentType.Cnpj);
    }

    [Theory]
    [InlineData("529.982.247-26")]  // último dígito alterado
    [InlineData("529.982.247-15")]  // primeiro dígito alterado
    public void Of_ComCpfDeDigitoErrado_RetornaFalha(string entrada)
    {
        Result<Document> resultado = Document.Of(entrada);

        resultado.IsFailure.Should().BeTrue();
        resultado.Error.Should().Be(DomainErrors.Document.DigitoVerificadorInvalido());
    }

    [Fact]
    public void Of_ComCnpjDeDigitoErrado_RetornaFalha()
    {
        Result<Document> resultado = Document.Of("11.222.333/0001-82");

        resultado.IsFailure.Should().BeTrue();
    }

    [Theory]
    [InlineData("111.111.111-11")]
    [InlineData("000.000.000-00")]
    [InlineData("11111111111111")]
    public void Of_ComDigitosTodosIguais_RetornaFalha(string entrada)
    {
        // Estes passam no cálculo do módulo 11 mas não são documentos reais. É o caso que a validação
        // apenas de formato deixa escapar.
        Result<Document> resultado = Document.Of(entrada);

        resultado.IsFailure.Should().BeTrue($"'{entrada}' é sequência repetida, não documento");
    }

    [Theory]
    [InlineData("123")]
    [InlineData("5299822472")]        // 10 dígitos
    [InlineData("112223330001811")]   // 15 dígitos
    public void Of_ComQuantidadeDeDigitosInvalida_RetornaFalha(string entrada)
    {
        Result<Document> resultado = Document.Of(entrada);

        resultado.IsFailure.Should().BeTrue();
        resultado.Error.Code.Should().Be("Document.TamanhoInvalido");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Of_ComTextoVazio_RetornaFalha(string? entrada)
    {
        Result<Document> resultado = Document.Of(entrada!);

        resultado.IsFailure.Should().BeTrue();
    }

    [Fact]
    public void Of_GuardaSomenteDigitos()
    {
        Result<Document> resultado = Document.Of("529.982.247-25");

        // A máscara é apresentação e não pertence ao dado: guardar as duas formas produziria cadastro
        // duplicado do mesmo documento.
        resultado.Value.Value.Should().Be("52998224725");
    }

    [Fact]
    public void Formatted_AplicaMascaraDeCpf()
    {
        Document documento = Document.Of("52998224725").Value;

        documento.Formatted.Should().Be("529.982.247-25");
    }

    [Fact]
    public void Formatted_AplicaMascaraDeCnpj()
    {
        Document documento = Document.Of("11222333000181").Value;

        documento.Formatted.Should().Be("11.222.333/0001-81");
    }

    [Fact]
    public void Equals_MesmoDocumentoComEsemMascara_SaoIguais()
    {
        Document comMascara = Document.Of("529.982.247-25").Value;
        Document semMascara = Document.Of("52998224725").Value;

        comMascara.Should().Be(semMascara);
    }
}
