namespace CleanStart.Domain.Orders;

/// <summary>
/// Situação de um <see cref="Order"/>.
/// </summary>
/// <remarks>
/// As transições permitidas são <c>Pending → Paid → Shipped</c>, com <c>Cancelled</c> alcançável de
/// <c>Pending</c> e <c>Paid</c> — nunca de <c>Shipped</c>. A regra mora no <see cref="Order"/>, não aqui:
/// enum guarda o estado, não decide quem pode mudar para quem.
/// <para>
/// Os valores são explícitos porque o enum é persistido. Deixar o compilador numerar faz com que inserir
/// um membro no meio renumere os seguintes e reinterprete silenciosamente as linhas já gravadas.
/// </para>
/// </remarks>
public enum OrderStatus
{
    /// <summary>Criado, aguardando pagamento.</summary>
    Pending = 1,

    /// <summary>Pago, aguardando envio.</summary>
    Paid = 2,

    /// <summary>Enviado. Estado final — não se cancela o que já saiu.</summary>
    Shipped = 3,

    /// <summary>Cancelado. Estado final.</summary>
    Cancelled = 4,
}
