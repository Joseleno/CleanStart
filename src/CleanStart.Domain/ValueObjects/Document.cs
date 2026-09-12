using CleanStart.Domain.Common;
using CleanStart.Domain.Errors;

namespace CleanStart.Domain.ValueObjects;

/// <summary>
/// CPF ou CNPJ com dígito verificador conferido.
/// </summary>
/// <remarks>
/// <para>
/// Guarda somente os dígitos e formata na saída. Duas razões: <c>529.982.247-25</c> e <c>52998224725</c>
/// são o mesmo documento, e guardá-los em formas diferentes produziria cadastro duplicado; e a máscara é
/// apresentação, que não pertence ao dado.
/// </para>
/// <para>
/// O dígito verificador é calculado de verdade, não apenas o formato. Validar só o tamanho aceitaria
/// <c>111.111.111-11</c> — e num kit de referência isso ensinaria a validação errada a quem está aprendendo.
/// </para>
/// </remarks>
public sealed class Document : ValueObject
{
    private const int TamanhoCpf = 11;
    private const int TamanhoCnpj = 14;

    /// <summary>Pesos do primeiro dígito verificador do CNPJ (12 posições).</summary>
    private static readonly int[] PesosCnpjPrimeiroDigito = [5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2];

    /// <summary>Pesos do segundo dígito verificador do CNPJ (13 posições — inclui o primeiro DV).</summary>
    private static readonly int[] PesosCnpjSegundoDigito = [6, 5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2];

    private Document(string value, DocumentType type)
    {
        Value = value;
        Type = type;
    }

    /// <summary>Somente os dígitos, sem máscara.</summary>
    public string Value { get; }

    /// <summary>Se é CPF ou CNPJ, inferido pela quantidade de dígitos.</summary>
    public DocumentType Type { get; }

    /// <summary>O documento com a máscara usual.</summary>
    public string Formatted => Type == DocumentType.Cpf
        ? $"{Value[..3]}.{Value[3..6]}.{Value[6..9]}-{Value[9..]}"
        : $"{Value[..2]}.{Value[2..5]}.{Value[5..8]}/{Value[8..12]}-{Value[12..]}";

    /// <summary>
    /// Cria um documento a partir de texto com ou sem máscara.
    /// </summary>
    public static Result<Document> Of(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Result.Failure<Document>(DomainErrors.General.TextoObrigatorio(nameof(Document)));
        }

        string digitos = new(value.Where(char.IsAsciiDigit).ToArray());

        DocumentType tipo = digitos.Length switch
        {
            TamanhoCpf => DocumentType.Cpf,
            TamanhoCnpj => DocumentType.Cnpj,
            _ => default,
        };

        if (tipo == default)
        {
            return Result.Failure<Document>(DomainErrors.Document.TamanhoInvalido(value));
        }

        bool valido = tipo == DocumentType.Cpf
            ? CpfEValido(digitos)
            : CnpjEValido(digitos);

        if (!valido)
        {
            return Result.Failure<Document>(DomainErrors.Document.DigitoVerificadorInvalido());
        }

        return new Document(digitos, tipo);
    }

    private static bool CpfEValido(string digitos)
    {
        // Sequências de dígito repetido passam no cálculo do DV (111.111.111-11 tem DV consistente) mas
        // não são documentos reais. Precisam ser rejeitadas à parte — é o caso que a validação só de
        // formato deixa passar.
        if (TodosOsDigitosIguais(digitos))
        {
            return false;
        }

        int primeiroDigito = CalcularDigito(digitos[..9], pesoInicial: 10);
        int segundoDigito = CalcularDigito(digitos[..10], pesoInicial: 11);

        return digitos[9] == ComoChar(primeiroDigito)
            && digitos[10] == ComoChar(segundoDigito);
    }

    private static bool CnpjEValido(string digitos)
    {
        if (TodosOsDigitosIguais(digitos))
        {
            return false;
        }

        int primeiroDigito = CalcularDigitoCnpj(digitos[..12], PesosCnpjPrimeiroDigito);
        int segundoDigito = CalcularDigitoCnpj(digitos[..13], PesosCnpjSegundoDigito);

        return digitos[12] == ComoChar(primeiroDigito)
            && digitos[13] == ComoChar(segundoDigito);
    }

    /// <summary>
    /// Dígito verificador do CPF: soma ponderada com pesos decrescentes, módulo 11.
    /// </summary>
    private static int CalcularDigito(string baseDigitos, int pesoInicial)
    {
        int soma = 0;
        int peso = pesoInicial;

        foreach (char digito in baseDigitos)
        {
            soma += (digito - '0') * peso;
            peso--;
        }

        int resto = soma % 11;

        // Resto 0 e 1 produzem dígito 0 — é a regra, não um atalho.
        return resto < 2 ? 0 : 11 - resto;
    }

    /// <summary>
    /// Dígito verificador do CNPJ: soma ponderada pela tabela de pesos do dígito, módulo 11.
    /// </summary>
    /// <remarks>
    /// Os pesos vêm em tabela explícita, uma por dígito, em vez de serem derivados por deslocamento de uma
    /// tabela só. A versão calculada é mais curta e bem menos conferível: quem lê precisa simular o índice
    /// de cabeça para saber que peso cai em que posição. Aqui basta comparar a tabela com a especificação.
    /// </remarks>
    private static int CalcularDigitoCnpj(string baseDigitos, int[] pesos)
    {
        int soma = 0;

        for (int i = 0; i < baseDigitos.Length; i++)
        {
            soma += (baseDigitos[i] - '0') * pesos[i];
        }

        int resto = soma % 11;

        return resto < 2 ? 0 : 11 - resto;
    }

    private static bool TodosOsDigitosIguais(string digitos) =>
        digitos.All(digito => digito == digitos[0]);

    private static char ComoChar(int digito) => (char)('0' + digito);

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Value;
    }

    public override string ToString() => Formatted;
}
