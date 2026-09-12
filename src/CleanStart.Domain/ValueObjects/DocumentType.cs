namespace CleanStart.Domain.ValueObjects;

/// <summary>
/// Natureza de um <see cref="Document"/>.
/// </summary>
public enum DocumentType
{
    /// <summary>Pessoa física — 11 dígitos.</summary>
    Cpf = 1,

    /// <summary>Pessoa jurídica — 14 dígitos.</summary>
    Cnpj = 2,
}
