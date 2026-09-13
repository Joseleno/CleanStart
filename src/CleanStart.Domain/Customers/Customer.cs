using CleanStart.Domain.Common;
using CleanStart.Domain.Errors;
using CleanStart.Domain.ValueObjects;

namespace CleanStart.Domain.Customers;

/// <summary>
/// Cliente: quem faz pedidos.
/// </summary>
/// <remarks>
/// <para>
/// Deliberadamente simples. Existe para dar realismo ao exemplo — um pedido precisa de alguém que o faça —
/// e não para modelar um cadastro completo. Endereço, telefone, histórico e preferências ficariam no
/// caminho do que o kit quer demonstrar.
/// </para>
/// <para>
/// É raiz de agregado própria, separada de <c>Order</c>: cliente e pedido têm ciclos de vida independentes
/// e são alterados em transações distintas. O pedido guarda apenas a <see cref="CustomerId"/>, não uma
/// referência ao objeto — agregado não aponta para agregado, aponta para identidade.
/// </para>
/// </remarks>
public sealed class Customer : AggregateRoot<CustomerId>, IAuditable, ISoftDeletable
{
    private const int TamanhoMinimoNome = 2;
    private const int TamanhoMaximoNome = 200;

    private Customer(
        CustomerId id,
        string name,
        Email email,
        Document document,
        DateTimeOffset createdAt)
        : base(id)
    {
        Name = name;
        Email = email;
        Document = document;
        CreatedAt = createdAt;
    }

    /// <summary>Nome do cliente.</summary>
    public string Name { get; private set; }

    /// <summary>E-mail, já validado em forma.</summary>
    public Email Email { get; private set; }

    /// <summary>CPF ou CNPJ, com dígito verificador conferido.</summary>
    public Document Document { get; }

    /// <inheritdoc />
    public DateTimeOffset CreatedAt { get; }

    /// <inheritdoc />
    public DateTimeOffset? UpdatedAt { get; private set; }

    /// <inheritdoc />
    /// <remarks>Preenchido pelo interceptor de auditoria, na Infrastructure — não pela regra de negócio.</remarks>
    public Guid? CreatedBy { get; private set; }

    /// <inheritdoc />
    public Guid? UpdatedBy { get; private set; }

    /// <inheritdoc />
    public bool IsDeleted { get; private set; }

    /// <inheritdoc />
    public DateTimeOffset? DeletedAt { get; private set; }

    /// <summary>
    /// Cadastra um cliente.
    /// </summary>
    /// <remarks>
    /// Recebe <see cref="ValueObjects.Email"/> e <see cref="ValueObjects.Document"/> já construídos, em vez
    /// de <c>string</c>: quem chama passa pelas factories deles e trata a falha de cada um, então aqui só
    /// resta validar o que é do cliente — o nome. É a vantagem de tipar o dado na fronteira em vez de
    /// revalidar em cada camada.
    /// </remarks>
    public static Result<Customer> Register(
        string name,
        Email email,
        Document document,
        DateTimeOffset agora)
    {
        ArgumentNullException.ThrowIfNull(email);
        ArgumentNullException.ThrowIfNull(document);

        if (!NomeEValido(name))
        {
            return Result.Failure<Customer>(DomainErrors.Customer.NomeInvalido());
        }

        return new Customer(CustomerId.New(), name.Trim(), email, document, agora);
    }

    /// <summary>
    /// Altera o nome.
    /// </summary>
    public Result ChangeName(string name, DateTimeOffset agora)
    {
        if (!NomeEValido(name))
        {
            return Result.Failure(DomainErrors.Customer.NomeInvalido());
        }

        Name = name.Trim();
        UpdatedAt = agora;

        return Result.Success();
    }

    /// <summary>
    /// Altera o e-mail.
    /// </summary>
    /// <remarks>
    /// O <see cref="Document"/> não tem equivalente: CPF não muda. Se mudou, é outra pessoa — e a correção
    /// de um documento digitado errado é caso de exceção administrativa, não de método de domínio.
    /// </remarks>
    public Result ChangeEmail(Email email, DateTimeOffset agora)
    {
        ArgumentNullException.ThrowIfNull(email);

        Email = email;
        UpdatedAt = agora;

        return Result.Success();
    }

    /// <summary>
    /// Marca o cliente como excluído.
    /// </summary>
    /// <remarks>
    /// Exclusão lógica porque pedido antigo referencia o cliente: apagar a linha deixaria o histórico
    /// apontando para o vazio. Um filtro global na Infrastructure esconde os excluídos das consultas.
    /// </remarks>
    public Result Delete(DateTimeOffset agora)
    {
        if (IsDeleted)
        {
            return Result.Failure(DomainErrors.Customer.JaExcluido());
        }

        IsDeleted = true;
        DeletedAt = agora;
        UpdatedAt = agora;

        return Result.Success();
    }

    private static bool NomeEValido(string? name) =>
        !string.IsNullOrWhiteSpace(name)
        && name.Trim().Length >= TamanhoMinimoNome
        && name.Trim().Length <= TamanhoMaximoNome;
}
