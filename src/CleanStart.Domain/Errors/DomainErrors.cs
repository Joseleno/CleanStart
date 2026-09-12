using CleanStart.Domain.Orders;

namespace CleanStart.Domain.Errors;

/// <summary>
/// Catálogo dos erros de negócio, agrupados por agregado.
/// </summary>
/// <remarks>
/// <para>
/// Centralizar em vez de construir <c>new Error("...", "...")</c> no ponto da falha resolve dois
/// problemas: o mesmo erro deixa de ganhar código diferente em dois lugares, e o teste pode afirmar
/// <c>resultado.Error.Should().Be(DomainErrors.General.ValorNaoPositivo)</c> em vez de comparar string —
/// asserção que sobrevive a reescrever a mensagem.
/// </para>
/// <para>
/// O grupo <c>General</c> guarda o que não pertence a um agregado só. Os grupos por agregado
/// (<c>Order</c>, <c>Customer</c>) entram com eles, nas T1.3 e T1.4.
/// </para>
/// </remarks>
public static class DomainErrors
{
    /// <summary>Erros aplicáveis a mais de um agregado.</summary>
    public static class General
    {
        /// <summary>Texto obrigatório ausente ou em branco.</summary>
        public static Error TextoObrigatorio(string campo) => Error.Validation(
            "General.TextoObrigatorio",
            $"O campo '{campo}' é obrigatório.");

        /// <summary>Valor numérico que deveria ser maior que zero.</summary>
        public static Error ValorNaoPositivo(string campo) => Error.Validation(
            "General.ValorNaoPositivo",
            $"O campo '{campo}' deve ser maior que zero.");
    }

    /// <summary>Erros do value object <c>Money</c>.</summary>
    public static class Money
    {
        /// <summary>Código de moeda fora do formato ISO-4217 (três letras).</summary>
        public static Error MoedaInvalida(string moeda) => Error.Validation(
            "Money.MoedaInvalida",
            $"'{moeda}' não é um código de moeda válido: esperadas três letras (ex.: BRL).");

        /// <summary>Operação aritmética entre valores de moedas diferentes.</summary>
        /// <remarks>
        /// É <see cref="ErrorType.Conflict"/> e não <c>Validation</c>: cada operando é válido isoladamente —
        /// o que não se sustenta é a combinação. A Api traduz isso em 409, não em 400.
        /// </remarks>
        public static Error MoedasIncompativeis(string esquerda, string direita) => Error.Conflict(
            "Money.MoedasIncompativeis",
            $"Não é possível operar valores em moedas diferentes: {esquerda} e {direita}.");
    }

    /// <summary>Erros do value object <c>Email</c>.</summary>
    public static class Email
    {
        /// <summary>Endereço fora de um formato aceitável.</summary>
        public static Error Invalido(string valor) => Error.Validation(
            "Email.Invalido",
            $"'{valor}' não é um endereço de e-mail válido.");
    }

    /// <summary>Erros do agregado <c>Order</c>.</summary>
    public static class Order
    {
        /// <summary>Tentativa de criar pedido sem nenhum item.</summary>
        public static Error SemItens() => Error.Validation(
            "Order.SemItens",
            "Um pedido precisa de ao menos um item.");

        /// <summary>Quantidade de item menor ou igual a zero.</summary>
        public static Error QuantidadeInvalida(int quantidade) => Error.Validation(
            "Order.QuantidadeInvalida",
            $"A quantidade do item deve ser maior que zero, mas foi {quantidade}.");

        /// <summary>Transição de estado que a regra não permite.</summary>
        /// <remarks>
        /// <see cref="ErrorType.Conflict"/> porque o pedido existe e a operação é bem formada — o que
        /// impede é o estado atual. A Api traduz em 409.
        /// </remarks>
        public static Error TransicaoInvalida(OrderStatus de, OrderStatus para) => Error.Conflict(
            "Order.TransicaoInvalida",
            $"Não é possível mudar um pedido de '{de}' para '{para}'.");

        /// <summary>Itens do pedido em moedas diferentes entre si.</summary>
        public static Error ItensEmMoedasDiferentes() => Error.Validation(
            "Order.ItensEmMoedasDiferentes",
            "Todos os itens de um pedido devem usar a mesma moeda.");
    }

    /// <summary>Erros do agregado <c>Customer</c>.</summary>
    public static class Customer
    {
        /// <summary>Nome ausente, em branco ou fora do tamanho aceitável.</summary>
        public static Error NomeInvalido() => Error.Validation(
            "Customer.NomeInvalido",
            "O nome do cliente é obrigatório e deve ter entre 2 e 200 caracteres.");

        /// <summary>Cliente não encontrado pela identidade informada.</summary>
        public static Error NaoEncontrado(Guid id) => Error.NotFound(
            "Customer.NaoEncontrado",
            $"Não existe cliente com a identidade '{id}'.");

        /// <summary>Exclusão de um cliente que já estava excluído.</summary>
        public static Error JaExcluido() => Error.Conflict(
            "Customer.JaExcluido",
            "O cliente já está excluído.");
    }

    /// <summary>Erros do value object <c>Document</c>.</summary>
    public static class Document
    {
        /// <summary>Quantidade de dígitos que não corresponde a CPF (11) nem a CNPJ (14).</summary>
        public static Error TamanhoInvalido(string valor) => Error.Validation(
            "Document.TamanhoInvalido",
            $"'{valor}' não tem a quantidade de dígitos de um CPF (11) nem de um CNPJ (14).");

        /// <summary>Dígito verificador que não confere.</summary>
        public static Error DigitoVerificadorInvalido() => Error.Validation(
            "Document.DigitoVerificadorInvalido",
            "O dígito verificador do documento não confere.");
    }
}
