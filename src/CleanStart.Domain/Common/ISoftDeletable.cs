namespace CleanStart.Domain.Common;

/// <summary>
/// Entidade que é marcada como excluída em vez de apagada da tabela.
/// </summary>
/// <remarks>
/// Um filtro global de query na Infrastructure esconde os excluídos, de modo que nenhum caso de uso
/// precise lembrar do <c>WHERE IsDeleted = false</c> — e é exatamente por isso que o marcador importa:
/// quem esquece o filtro não erra por pouco, erra mostrando dado que deveria estar invisível.
/// </remarks>
public interface ISoftDeletable
{
    /// <summary>A entidade foi excluída logicamente.</summary>
    bool IsDeleted { get; }

    /// <summary>Quando foi excluída, ou nulo se não foi.</summary>
    DateTimeOffset? DeletedAt { get; }
}
