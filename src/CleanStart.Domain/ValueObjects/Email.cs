using CleanStart.Domain.Common;
using CleanStart.Domain.Errors;

namespace CleanStart.Domain.ValueObjects;

/// <summary>
/// Endereço de e-mail válido em forma.
/// </summary>
/// <remarks>
/// <para>
/// O tipo serve para que uma função que recebe <c>Email</c> não precise revalidar: se a instância existe,
/// passou pela checagem. É o ganho de substituir <c>string</c> por value object — a validação acontece uma
/// vez, na fronteira, em vez de espalhada em cada uso.
/// </para>
/// <para>
/// <b>A validação é intencionalmente modesta.</b> A gramática real de endereços (RFC 5322) admite coisas
/// como <c>"a b"@example.com</c> e comentários entre parênteses; regex que tenta cobri-la fica ilegível e
/// ainda erra. Mais importante: <b>nenhuma validação sintática prova que o endereço existe</b> — só o envio
/// prova. Então aqui se verifica o que dá para verificar com honestidade (um <c>@</c>, algo antes, um
/// domínio com ponto depois) e a confirmação de verdade fica para o fluxo de confirmação por e-mail.
/// </para>
/// </remarks>
public sealed class Email : ValueObject
{
    /// <summary>Limite prático de tamanho, conforme RFC 5321.</summary>
    private const int TamanhoMaximo = 254;

    private Email(string value) => Value = value;

    /// <summary>O endereço, normalizado em minúsculas.</summary>
    public string Value { get; }

    /// <summary>
    /// Cria um e-mail a partir do texto informado.
    /// </summary>
    /// <remarks>
    /// Normaliza para minúsculas porque a parte do domínio é insensível a caixa e, na prática, a parte
    /// local também é nos provedores reais — guardar <c>Joao@x.com</c> e <c>joao@x.com</c> como endereços
    /// distintos produziria cadastro duplicado do mesmo usuário.
    /// </remarks>
    public static Result<Email> Of(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Result.Failure<Email>(DomainErrors.General.TextoObrigatorio(nameof(Email)));
        }

        string normalizado = value.Trim().ToLowerInvariant();

        if (normalizado.Length > TamanhoMaximo || !TemFormaDeEndereco(normalizado))
        {
            return Result.Failure<Email>(DomainErrors.Email.Invalido(value));
        }

        return new Email(normalizado);
    }

    /// <summary>
    /// Verifica a forma mínima: <c>local@dominio.tld</c>, com um único <c>@</c> e sem espaço.
    /// </summary>
    private static bool TemFormaDeEndereco(string valor)
    {
        if (valor.Any(char.IsWhiteSpace))
        {
            return false;
        }

        string[] partes = valor.Split('@');

        if (partes.Length != 2)
        {
            return false;
        }

        (string local, string dominio) = (partes[0], partes[1]);

        if (local.Length == 0 || dominio.Length == 0)
        {
            return false;
        }

        // O domínio precisa de um ponto com conteúdo dos dois lados: "joao@com" e "joao@x." não são
        // endereços roteáveis.
        int ultimoPonto = dominio.LastIndexOf('.');

        return ultimoPonto > 0
            && ultimoPonto < dominio.Length - 1
            && !dominio.Contains("..", StringComparison.Ordinal);
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Value;
    }

    public override string ToString() => Value;
}
