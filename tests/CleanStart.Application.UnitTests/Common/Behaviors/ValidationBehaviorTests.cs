using CleanStart.Application.Common.Behaviors;
using CleanStart.Application.Common.Messaging;
using CleanStart.Domain.Common;
using FluentValidation;

namespace CleanStart.Application.UnitTests.Common.Behaviors;

/// <summary>
/// Agregação de falhas do <see cref="ValidationBehavior{TMessage,TResponse}"/>.
/// </summary>
public sealed class ValidationBehaviorTests
{
    private sealed record CriarClienteCommand(string Nome, string Email) : ICommand<int>;

    private sealed class NomeValidator : AbstractValidator<CriarClienteCommand>
    {
        public NomeValidator() => RuleFor(comando => comando.Nome).NotEmpty().WithMessage("Nome é obrigatório.");
    }

    private sealed class EmailValidator : AbstractValidator<CriarClienteCommand>
    {
        public EmailValidator() =>
            RuleFor(comando => comando.Email).NotEmpty().WithMessage("Email é obrigatório.");
    }

    private static ValueTask<Result<int>> Proximo(CriarClienteCommand _, CancellationToken __) =>
        ValueTask.FromResult(Result.Success(1));

    [Fact]
    public async Task SemValidator_PassaDireto()
    {
        // Validação é opt-in: mensagem sem validator não é bloqueada.
        ValidationBehavior<CriarClienteCommand, Result<int>> behavior = new([]);

        Result<int> resposta = await behavior.Handle(
            new CriarClienteCommand("", ""),
            Proximo,
            TestContext.Current.CancellationToken);

        resposta.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task ComMensagemValida_ChamaOHandler()
    {
        ValidationBehavior<CriarClienteCommand, Result<int>> behavior = new([new NomeValidator()]);

        Result<int> resposta = await behavior.Handle(
            new CriarClienteCommand("João", "joao@example.com"),
            Proximo,
            TestContext.Current.CancellationToken);

        resposta.Value.Should().Be(1);
    }

    [Fact]
    public async Task ComUmaFalha_Lanca()
    {
        ValidationBehavior<CriarClienteCommand, Result<int>> behavior = new([new NomeValidator()]);

        Func<Task> acao = async () => await behavior.Handle(
            new CriarClienteCommand("", "joao@example.com"),
            Proximo,
            TestContext.Current.CancellationToken);

        await acao.Should().ThrowAsync<ValidationException>();
    }

    [Fact]
    public async Task ComVariosValidators_AgregaTodasAsFalhas()
    {
        // O comportamento que dá nome ao behavior: quem preenche formulário precisa ver os dois campos
        // errados de uma vez, não descobrir um por submit. É também o motivo de o Result do domínio carregar
        // um Error único — a agregação é trabalho daqui.
        ValidationBehavior<CriarClienteCommand, Result<int>> behavior =
            new([new NomeValidator(), new EmailValidator()]);

        Func<Task> acao = async () => await behavior.Handle(
            new CriarClienteCommand("", ""),
            Proximo,
            TestContext.Current.CancellationToken);

        ValidationException excecao = (await acao.Should().ThrowAsync<ValidationException>()).Which;

        excecao.Errors.Should().HaveCount(2);
        excecao.Errors.Select(falha => falha.ErrorMessage)
            .Should().Contain("Nome é obrigatório.").And.Contain("Email é obrigatório.");
    }

    [Fact]
    public async Task ComFalha_NaoChamaOHandler()
    {
        ValidationBehavior<CriarClienteCommand, Result<int>> behavior = new([new NomeValidator()]);
        bool handlerRodou = false;

        Func<Task> acao = async () => await behavior.Handle(
            new CriarClienteCommand("", ""),
            (_, _) =>
            {
                handlerRodou = true;
                return ValueTask.FromResult(Result.Success(1));
            },
            TestContext.Current.CancellationToken);

        await acao.Should().ThrowAsync<ValidationException>();
        handlerRodou.Should().BeFalse("mensagem malformada não deve chegar ao domínio");
    }
}
