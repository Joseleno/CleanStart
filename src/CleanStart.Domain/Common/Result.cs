using CleanStart.Domain.Errors;

namespace CleanStart.Domain.Common;

/// <summary>
/// Resultado de uma operação que pode falhar por regra de negócio.
/// </summary>
/// <remarks>
/// <para>
/// Erro de negócio é resultado esperado, não acidente: pedido sem item e moeda incompatível são
/// respostas previstas da regra, e sinalizá-las com exception usa controle de fluxo excepcional para
/// o caminho normal. Exception aqui fica reservada a falha de infraestrutura e a bug.
/// </para>
/// <para>
/// Vive no <b>Domain</b> porque é o domínio quem o produz — as factories de entidade e de value object
/// retornam <see cref="Result{TValue}"/>. Um tipo não pode morar numa camada acima de quem o cria.
/// A Application consome; se precisar de mais, estende por método de extensão.
/// </para>
/// </remarks>
public class Result
{
    protected Result(bool isSuccess, Error error)
    {
        // Esta é a invariante que faz o tipo valer: sucesso-com-erro e falha-sem-erro são estados
        // contraditórios, e permitir qualquer um deles obrigaria todo consumidor a desconfiar do par.
        if (isSuccess && error != Error.None)
        {
            throw new ArgumentException("Um resultado de sucesso não pode carregar erro.", nameof(error));
        }

        if (!isSuccess && error == Error.None)
        {
            throw new ArgumentException("Um resultado de falha precisa carregar um erro.", nameof(error));
        }

        IsSuccess = isSuccess;
        Error = error;
    }

    /// <summary>A operação foi concluída.</summary>
    public bool IsSuccess { get; }

    /// <summary>A operação falhou. Complemento de <see cref="IsSuccess"/>.</summary>
    public bool IsFailure => !IsSuccess;

    /// <summary>
    /// A falha, ou <see cref="Error.None"/> em caso de sucesso — nunca nulo.
    /// </summary>
    public Error Error { get; }

    /// <summary>Cria um resultado de sucesso.</summary>
    public static Result Success() => new(true, Error.None);

    /// <summary>Cria um resultado de falha.</summary>
    public static Result Failure(Error error) => new(false, error);

    /// <summary>Cria um resultado de sucesso que carrega um valor.</summary>
    public static Result<TValue> Success<TValue>(TValue value) => new(value, true, Error.None);

    /// <summary>Cria um resultado de falha tipado.</summary>
    public static Result<TValue> Failure<TValue>(Error error) => new(default, false, error);
}

/// <summary>
/// Resultado de uma operação que, em caso de sucesso, produz um valor.
/// </summary>
/// <typeparam name="TValue">Tipo do valor produzido.</typeparam>
public sealed class Result<TValue> : Result
{
    private readonly TValue? _value;

    internal Result(TValue? value, bool isSuccess, Error error)
        : base(isSuccess, error)
    {
        _value = value;
    }

    /// <summary>
    /// O valor produzido.
    /// </summary>
    /// <exception cref="InvalidOperationException">Se o resultado for falha.</exception>
    /// <remarks>
    /// Lança em vez de devolver <c>default</c> de propósito: ler o valor de um resultado que falhou é
    /// bug de quem chama — esqueceu de checar <see cref="Result.IsFailure"/> — e bug deve aparecer alto,
    /// não virar um <c>null</c> que se propaga silenciosamente. Para tratar os dois casos de uma vez,
    /// use <see cref="Match{TResult}"/>.
    /// </remarks>
    public TValue Value => IsSuccess
        ? _value!
        : throw new InvalidOperationException(
            $"Não se lê o valor de um resultado de falha. Erro: {Error}");

    /// <summary>
    /// Converte o resultado num único valor, tratando sucesso e falha.
    /// </summary>
    /// <remarks>
    /// É o caminho preferido para consumir um <see cref="Result{TValue}"/>: obriga a tratar a falha,
    /// enquanto <c>if (IsSuccess)</c> permite esquecer o <c>else</c>. Na Api, é o que traduz o resultado
    /// em resposta HTTP.
    /// </remarks>
    public TResult Match<TResult>(
        Func<TValue, TResult> onSuccess,
        Func<Error, TResult> onFailure)
    {
        ArgumentNullException.ThrowIfNull(onSuccess);
        ArgumentNullException.ThrowIfNull(onFailure);

        return IsSuccess ? onSuccess(_value!) : onFailure(Error);
    }

    /// <summary>
    /// Converte implicitamente um valor em resultado de sucesso, para encurtar o <c>return</c> das factories.
    /// </summary>
    public static implicit operator Result<TValue>(TValue value) => Success(value);
}
