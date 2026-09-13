using FluentValidation;

namespace CleanStart.Application.Orders.PlaceOrder;

/// <summary>
/// Valida a <b>forma</b> do <see cref="PlaceOrderCommand"/>.
/// </summary>
/// <remarks>
/// <para>
/// A divisão de trabalho com o domínio é a parte que importa aqui. Este validator recusa o que é malformado —
/// guid vazio, lista vazia, moeda fora do formato, quantidade negativa. O <c>Order.Place</c> recusa o que
/// viola a <b>regra</b> — itens em moedas diferentes, por exemplo.
/// </para>
/// <para>
/// Há sobreposição aparente: os dois checam quantidade maior que zero. Isso é intencional, não duplicação
/// descuidada. A verificação daqui existe para devolver ao cliente todas as falhas de formulário de uma vez,
/// com o caminho do campo (<c>Items[2].Quantity</c>); a do domínio existe porque a entidade **não pode**
/// depender de ninguém tê-la validado antes — ela é chamada por jobs, por seeds, por outro caso de uso.
/// Remover a do domínio deixaria a invariante sem guardião; remover a daqui pioraria a mensagem de erro.
/// </para>
/// <para>
/// O que <b>não</b> está aqui: nada que precise consultar o banco. "O cliente existe?" é pergunta para o
/// handler, que tem repositório — validator que consulta banco esconde I/O num lugar onde ninguém espera, e
/// roda antes de a transação abrir.
/// </para>
/// </remarks>
public sealed class PlaceOrderValidator : AbstractValidator<PlaceOrderCommand>
{
    public PlaceOrderValidator()
    {
        RuleFor(comando => comando.CustomerId)
            .NotEmpty()
            .WithMessage("O cliente do pedido é obrigatório.");

        RuleFor(comando => comando.Currency)
            .NotEmpty()
            .WithMessage("A moeda é obrigatória.")
            .Length(3)
            .WithMessage("A moeda deve ter três letras (ex.: BRL).");

        RuleFor(comando => comando.Items)
            .NotEmpty()
            .WithMessage("O pedido precisa de ao menos um item.");

        RuleForEach(comando => comando.Items).ChildRules(item =>
        {
            item.RuleFor(i => i.ProductId)
                .NotEmpty()
                .WithMessage("O produto do item é obrigatório.");

            item.RuleFor(i => i.Quantity)
                .GreaterThan(0)
                .WithMessage("A quantidade do item deve ser maior que zero.");

            item.RuleFor(i => i.UnitPrice)
                .GreaterThan(0)
                .WithMessage("O preço unitário deve ser maior que zero.");
        });
    }
}
