namespace CleanStart.Domain.Orders;

/// <summary>
/// Identidade de um <see cref="Order"/>.
/// </summary>
/// <remarks>
/// <para>
/// Existe para que o compilador recuse <c>BuscarPedido(customerId)</c>. Com <c>Guid</c> cru em toda
/// assinatura, trocar dois argumentos da mesma chamada compila, passa no teste que usa o mesmo id para
/// tudo, e falha em produção — é a classe de bug que identidade tipada elimina de vez.
/// </para>
/// <para>
/// É <c>record struct</c>: um <c>Guid</c> embrulhado não deveria custar uma alocação no heap, e a
/// igualdade estrutural vem de graça.
/// </para>
/// </remarks>
/// <param name="Value">O valor subjacente.</param>
public readonly record struct OrderId(Guid Value) : IComparable<OrderId>
{
    /// <summary>
    /// Ordena pela identidade subjacente.
    /// </summary>
    /// <remarks>
    /// Existe para a paginação por keyset, que precisa de <c>ORDER BY ... , id DESC</c> e da comparação
    /// <c>id &lt; :cursor</c> para desempatar datas iguais. Com UUID v7 a ordem da identidade é a ordem de
    /// criação, então ordenar por ela é ordenar cronologicamente — o que torna o desempate previsível em vez de
    /// arbitrário.
    /// </remarks>
    public int CompareTo(OrderId other) => Value.CompareTo(other.Value);

    public static bool operator <(OrderId left, OrderId right) => left.CompareTo(right) < 0;

    public static bool operator >(OrderId left, OrderId right) => left.CompareTo(right) > 0;

    public static bool operator <=(OrderId left, OrderId right) => left.CompareTo(right) <= 0;

    public static bool operator >=(OrderId left, OrderId right) => left.CompareTo(right) >= 0;

    /// <summary>Gera uma identidade nova.</summary>
    /// <remarks>
    /// UUID v7 e não v4: o v7 embute timestamp no prefixo, então as chaves nascem ordenadas no tempo.
    /// Em índice B-tree do PostgreSQL isso faz as inserções caírem sempre na mesma extremidade, em vez de
    /// espalhar página por página como o v4 — a diferença aparece em tabela grande.
    /// </remarks>
    public static OrderId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString();
}
