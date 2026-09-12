using CleanStart.Domain.Common;
using CleanStart.Domain.Errors;

namespace CleanStart.Domain.ValueObjects;

/// <summary>
/// Valor monetário: uma quantia e a moeda em que ela é expressa.
/// </summary>
/// <remarks>
/// <para>
/// Existe para que quantia e moeda nunca se separem. Um <c>decimal</c> solto atravessa a aplicação sem
/// dizer em que moeda está, e é assim que dez reais viram dez dólares numa soma — erro que o compilador
/// não pega porque os dois são <c>decimal</c>.
/// </para>
/// <para>
/// Aceita valor negativo de propósito: estorno, desconto e saldo devedor são quantias negativas legítimas.
/// Quem precisa de "apenas positivo" impõe isso no seu próprio contexto — o preço de um item de pedido é
/// validado pelo <c>OrderItem</c>, não pelo <c>Money</c>.
/// </para>
/// </remarks>
public sealed class Money : ValueObject
{
    private Money(decimal amount, string currency)
    {
        Amount = amount;
        Currency = currency;
    }

    /// <summary>A quantia. Pode ser negativa.</summary>
    public decimal Amount { get; }

    /// <summary>Código da moeda, três letras maiúsculas (ex.: <c>BRL</c>).</summary>
    public string Currency { get; }

    /// <summary>
    /// Cria um valor monetário.
    /// </summary>
    /// <remarks>
    /// O código da moeda é normalizado para maiúsculas: <c>"brl"</c> e <c>"BRL"</c> são a mesma moeda, e
    /// recusar a primeira forma não protegeria ninguém de nada.
    /// <para>
    /// A validação é de <b>forma</b> (três letras), não de existência: conferir contra a lista ISO-4217
    /// completa exigiria manter a tabela de moedas do mundo dentro de um kit de exemplo.
    /// </para>
    /// </remarks>
    public static Result<Money> Of(decimal amount, string currency)
    {
        if (string.IsNullOrWhiteSpace(currency))
        {
            return Result.Failure<Money>(DomainErrors.General.TextoObrigatorio(nameof(currency)));
        }

        string normalizada = currency.Trim().ToUpperInvariant();

        if (normalizada.Length != 3 || !normalizada.All(char.IsAsciiLetterUpper))
        {
            return Result.Failure<Money>(DomainErrors.Money.MoedaInvalida(currency));
        }

        return new Money(amount, normalizada);
    }

    /// <summary>Zero na moeda indicada.</summary>
    public static Result<Money> Zero(string currency) => Of(0m, currency);

    /// <summary>
    /// Soma dois valores da mesma moeda.
    /// </summary>
    /// <remarks>
    /// Retorna <see cref="Result{TValue}"/> em vez de lançar porque somar moedas diferentes é uma
    /// tentativa previsível — entrada de usuário, dado importado, carrinho multimoeda — e não um bug de
    /// programação. Falha esperada se comunica por resultado; exception fica para o que não deveria
    /// acontecer nunca.
    /// </remarks>
    public Result<Money> Add(Money other)
    {
        ArgumentNullException.ThrowIfNull(other);

        if (Currency != other.Currency)
        {
            return Result.Failure<Money>(
                DomainErrors.Money.MoedasIncompativeis(Currency, other.Currency));
        }

        return new Money(Amount + other.Amount, Currency);
    }

    /// <summary>Subtrai um valor da mesma moeda.</summary>
    public Result<Money> Subtract(Money other)
    {
        ArgumentNullException.ThrowIfNull(other);

        if (Currency != other.Currency)
        {
            return Result.Failure<Money>(
                DomainErrors.Money.MoedasIncompativeis(Currency, other.Currency));
        }

        return new Money(Amount - other.Amount, Currency);
    }

    /// <summary>
    /// Multiplica a quantia por um fator.
    /// </summary>
    /// <remarks>
    /// Não retorna <c>Result</c> porque não há como falhar: multiplicar por qualquer <c>decimal</c> produz
    /// um valor monetário válido na mesma moeda. Assinatura que não pode falhar não finge que pode.
    /// </remarks>
    public Money Multiply(decimal factor) => new(Amount * factor, Currency);

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Amount;
        yield return Currency;
    }

    public override string ToString() => $"{Amount:0.00} {Currency}";
}
